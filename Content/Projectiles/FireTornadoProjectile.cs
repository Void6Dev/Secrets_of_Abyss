using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Огненное торнадо Scythe of Fire Storm: летит по траектории запуска,
    // притягивает и жжёт врагов, плавно затухает; об блоки не проходит.
    // Коснувшись земли, едет по ней в сторону полёта; стена гасит вихрь.
    // Коснувшись воды, превращается в паровой гейзер: не жжёт,
    // зато мощно подбрасывает врагов и живёт меньше.
    // ai[0]: сила заряда 0..1 — масштаб вихря и радиус притяжения.
    // ai[1]: 0 — полёт, ±1 — едет по земле в эту сторону.
    public class FireTornadoProjectile : ModProjectile
    {
        private const int Lifetime = 300;
        private const int WallFadeTicks = 45; // остаток жизни после удара в стену
        private const int SteamLifetimeCap = 200;   // пар живёт меньше огня
        private const float SteamLiftMultiplier = 2.2f;
        private const float SteamMaxLiftSpeed = 8f;
        private const float SteamPullRadiusFactor = 0.8f;

        private bool _steam = false;
        private const int HitCooldownTicks = 12;
        private const float CruiseSpeed = 1.4f;   // крейсерский дрейф после броска
        private const float LaunchDecay = 0.94f;  // торможение стартового рывка
        private const float BasePullRadius = 210f;
        private const float PullStrength = 0.55f;
        private const float CoreSpinStrength = 0.35f; // закрутка внутри воронки
        private const float CoreLift = 0.45f;         // подъёмная сила против гравитации
        private const float MaxLiftSpeed = 3.5f;      // потолок скорости подъёма
        private const float MaxPulledSpeed = 9f;
        private const int BaseVortexWidth = 130;
        private const int BaseVortexHeight = 190;
        private const float BaseDrawWidth = 190f;
        private const float BaseDrawHeight = 270f;

        private float ChargeRatio => Projectile.ai[0];
        private ref float GroundDir => ref Projectile.ai[1];
        private float Scale => MathHelper.Lerp(0.65f, 1.15f, ChargeRatio);
        private float LifeProgress => 1f - Projectile.timeLeft / (float)Lifetime;

        // Воронка «стоит» на нижней кромке хитбокса: центр визуала и зоны урона выше точки касания
        private Vector2 VortexCenter => Projectile.Bottom - new Vector2(0f, BaseVortexHeight * Scale / 2f);

        public override void SetDefaults()
        {
            // Физический хитбокс маленький: им вихрь цепляется за тайлы,
            // а зона урона задаётся отдельно в Colliding
            Projectile.width = 36;
            Projectile.height = 36;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Lifetime;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = HitCooldownTicks;
        }

        public override void AI()
        {
            if (!_steam && TouchesWater())
                ConvertToSteam();

            if (GroundDir != 0f)
            {
                // Едет по земле в сторону полёта; гравитация прижимает и ведёт по склонам вниз
                Projectile.velocity.X = GroundDir * CruiseSpeed;
                Projectile.velocity.Y += 0.5f;
            }
            else if (Projectile.velocity.Length() > CruiseSpeed)
            {
                // Резкий бросок быстро гаснет до медленного дрейфа:
                // иначе торнадо убегает от собственного притяжения
                Projectile.velocity *= LaunchDecay;
            }

            PullEnemies();
            SpawnVortexDust();
            if (_steam)
                SpawnSteamPuffs();

            float glow = 1f - LifeProgress * 0.7f;
            if (_steam)
                Lighting.AddLight(VortexCenter, 0.35f * glow, 0.55f * glow, 0.7f * glow);
            else
                Lighting.AddLight(VortexCenter, 1.4f * glow, 0.6f * glow, 0.1f * glow);
        }

        private bool TouchesWater()
        {
            if (Projectile.wet && !Projectile.lavaWet && !Projectile.honeyWet)
                return true;

            Point bottom = Projectile.Bottom.ToTileCoordinates();
            Tile tile = Framing.GetTileSafely(bottom.X, bottom.Y);
            return tile.LiquidAmount > 32 && tile.LiquidType == LiquidID.Water;
        }

        private void ConvertToSteam()
        {
            _steam = true;
            if (Projectile.timeLeft > SteamLifetimeCap)
                Projectile.timeLeft = SteamLifetimeCap;
            Projectile.netUpdate = true;

            SoundEngine.PlaySound(SoundID.SplashWeak with { Volume = 1f, Pitch = -0.4f }, Projectile.Center);
            SoundEngine.PlaySound(SoundID.Item34 with { Volume = 0.6f, Pitch = 0.5f }, Projectile.Center);

            if (Main.netMode == NetmodeID.Server)
                return;

            // Всплеск у самого устья: тело гейзера рисует шейдер,
            // поэтому пыль лишь подчёркивает удар о воду
            for (int i = 0; i < 24; i++)
            {
                Vector2 pos = Projectile.Bottom + new Vector2(
                    Main.rand.NextFloatDirection() * BaseVortexWidth * Scale * 0.4f,
                    Main.rand.NextFloat(-8f, 8f));
                Dust steam = Dust.NewDustPerfect(pos, Main.rand.NextBool(3) ? DustID.Water : DustID.Smoke);
                steam.color = Color.White;
                steam.velocity = new Vector2(Main.rand.NextFloatDirection() * 2.2f, -Main.rand.NextFloat(2.5f, 6f));
                steam.scale = Main.rand.NextFloat(1.2f, 2f);
                steam.alpha = 60;
                steam.noGravity = Main.rand.NextBool();
            }
        }

        private void PullEnemies()
        {
            float pullRadius = BasePullRadius * Scale * (_steam ? SteamPullRadiusFactor : 1f);
            float lift = _steam ? CoreLift * SteamLiftMultiplier : CoreLift;
            float maxLift = _steam ? SteamMaxLiftSpeed : MaxLiftSpeed;
            float coreRadius = BaseVortexWidth * Scale * 0.5f;
            float fade = 1f - LifeProgress * 0.5f; // затухающий вихрь тянет слабее

            foreach (NPC npc in Main.npc)
            {
                if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.knockBackResist <= 0f)
                    continue;

                float dist = Vector2.Distance(npc.Center, VortexCenter);
                if (dist > pullRadius)
                    continue;

                float grip = fade * npc.knockBackResist;
                Vector2 toCenter = (VortexCenter - npc.Center).SafeNormalize(Vector2.Zero);

                // Радиальное притяжение, у края слабее
                float pullT = 1f - dist / pullRadius;
                if (dist > 20f)
                    npc.velocity += toCenter * PullStrength * (0.6f + pullT) * grip;

                if (dist < coreRadius)
                {
                    // Отрыв от земли: без него наземных врагов держит трение
                    npc.velocity.Y -= lift * grip;
                    if (npc.velocity.Y < -maxLift)
                        npc.velocity.Y = -maxLift;

                    // Закрутка по спирали и перенос вместе с торнадо
                    Vector2 tangent = new(-toCenter.Y, toCenter.X);
                    npc.velocity += tangent * CoreSpinStrength * grip;
                    npc.position += Projectile.velocity * 0.6f;
                }

                if (npc.velocity.Length() > MaxPulledSpeed)
                    npc.velocity *= 0.94f;
            }
        }

        private void SpawnVortexDust()
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            float vortexHeight = BaseVortexHeight * Scale;
            float dustFade = 1f - LifeProgress;
            int perTick = dustFade > 0.4f ? 3 : 1;

            // Тело гейзера рисует шейдер — пыль оставляем лишь редкими клубами-обрывками
            if (_steam)
                perTick = Main.rand.NextBool() ? 1 : 0;

            float spin = Main.GameUpdateCount * 0.25f;
            for (int i = 0; i < perTick; i++)
            {
                float h = Main.rand.NextFloat(); // 0 — низ воронки, 1 — верх
                float radius = MathHelper.Lerp(10f, 64f, h) * Scale;
                float angle = spin * (2.2f - h) + i * MathHelper.TwoPi / 3f;
                float offY = MathHelper.Lerp(vortexHeight / 2f - 10f, -vortexHeight / 2f + 10f, h);

                Vector2 dustPos = VortexCenter + new Vector2((float)Math.Cos(angle) * radius, offY);
                Dust d;
                if (_steam)
                {
                    d = Dust.NewDustPerfect(dustPos, Main.rand.NextBool(3) ? DustID.Water : DustID.Smoke);
                    d.color = Color.White;
                    d.alpha = 50;
                    d.velocity = new Vector2(-(float)Math.Sin(angle) * 1.6f + Projectile.velocity.X * 0.4f, -2.6f);
                }
                else
                {
                    d = Dust.NewDustPerfect(dustPos, Main.rand.NextBool(4) ? DustID.InfernoFork : DustID.Torch);
                    d.velocity = new Vector2(-(float)Math.Sin(angle) * 2.2f + Projectile.velocity.X * 0.4f, -1.6f);
                }
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1.1f, 2f) * (0.5f + 0.5f * dustFade);
            }

            if (Main.rand.NextBool(4))
            {
                Dust smoke = Dust.NewDustDirect(Projectile.position, Projectile.width, 20, DustID.Smoke);
                if (_steam)
                    smoke.color = Color.White;
                smoke.velocity = new Vector2(Main.rand.NextFloatDirection() * 0.5f, -Main.rand.NextFloat(2f, 4f));
                smoke.alpha = 130;
            }
        }

        // Клубящийся султан пара над колонной: пухлые облака всходят с макушки
        // шейдерной колонны, расходятся вширь и тают — то, чего не даёт плоский квад
        private void SpawnSteamPuffs()
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            float vortexHeight = BaseVortexHeight * Scale;
            float crownWidth = BaseDrawWidth * Scale * 0.5f;
            Vector2 crown = VortexCenter - new Vector2(0f, vortexHeight * 0.5f);
            float puffFade = 0.4f + 0.6f * (1f - LifeProgress); // к концу жизни пара меньше

            int puffs = Main.rand.NextBool() ? 2 : 1;
            for (int i = 0; i < puffs; i++)
            {
                Vector2 spawn = crown + new Vector2(
                    Main.rand.NextFloatDirection() * crownWidth,
                    Main.rand.NextFloat(-10f, 18f));
                // Пухлые белые облака расходятся вверх-вширь
                Dust cloud = Dust.NewDustPerfect(spawn, DustID.Cloud);
                cloud.velocity = new Vector2(
                    Main.rand.NextFloatDirection() * 1.6f + Projectile.velocity.X * 0.3f,
                    -Main.rand.NextFloat(1.5f, 4f));
                cloud.scale = Main.rand.NextFloat(1.3f, 2.3f) * puffFade;
                cloud.alpha = 90;
                cloud.noGravity = true;
                cloud.color = Color.Lerp(Color.White, new Color(200, 220, 235), Main.rand.NextFloat());
            }

            // Редкие серые клубы дыма добавляют глубины султану
            if (Main.rand.NextBool(3))
            {
                Vector2 spawn = crown + new Vector2(Main.rand.NextFloatDirection() * crownWidth * 0.6f,
                    Main.rand.NextFloat(-4f, 12f));
                Dust smoke = Dust.NewDustPerfect(spawn, DustID.Smoke);
                smoke.velocity = new Vector2(Main.rand.NextFloatDirection() * 1f, -Main.rand.NextFloat(2f, 4.5f));
                smoke.scale = Main.rand.NextFloat(1.2f, 2f) * puffFade;
                smoke.alpha = 120;
                smoke.noGravity = true;
                smoke.color = new Color(180, 190, 200);
            }
        }

        // Зона урона — вся воронка, а не маленький физический хитбокс
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            int w = (int)(BaseVortexWidth * Scale);
            int h = (int)(BaseVortexHeight * Scale);
            Vector2 center = VortexCenter;
            Rectangle vortex = new((int)(center.X - w / 2f), (int)(center.Y - h / 2f), w, h);
            return vortex.Intersects(targetHitbox);
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Стена по ходу движения — вихрь догорает на месте
            if (Projectile.velocity.X != oldVelocity.X)
            {
                Projectile.velocity = Vector2.Zero;
                GroundDir = 0f;
                if (Projectile.timeLeft > WallFadeTicks)
                    Projectile.timeLeft = WallFadeTicks;
                Projectile.netUpdate = true;
                return false;
            }

            if (Projectile.velocity.Y != oldVelocity.Y)
            {
                // Коснулся земли — дальше едет по ней в сторону полёта
                if (oldVelocity.Y > 0f && GroundDir == 0f)
                {
                    GroundDir = oldVelocity.X != 0f
                        ? Math.Sign(oldVelocity.X)
                        : Main.player[Projectile.owner].direction;
                    Projectile.netUpdate = true;
                }
                Projectile.velocity.Y = 0f;
            }
            return false;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Пар ошпаривает, но не поджигает
            if (!_steam)
                target.AddBuff(ModContent.BuffType<HellFireDebuff>(), 120);
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(_steam);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _steam = reader.ReadBoolean();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D quadTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/BeamDistortion").Value;
            Texture2D noiseTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/WaveNoise").Value;
            Vector2 pos = VortexCenter - Main.screenPosition;

            // AlphaBlend (premultiplied): и огненная воронка, и паровой гейзер
            // рисуются процедурным шейдером на одном и том же кваде
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            var shader = GameShaders.Misc[_steam ? "SoA:SteamGeyser" : "SoA:FireTornado"];
            shader.UseOpacity(1f);
            shader.Shader.Parameters["uProgress"]?.SetValue(LifeProgress);
            shader.Apply();
            Main.graphics.GraphicsDevice.Textures[1] = noiseTex;
            Main.graphics.GraphicsDevice.SamplerStates[1] = SamplerState.LinearWrap;

            // Лёгкий наклон по ходу полёта
            float tilt = MathHelper.Clamp(Projectile.velocity.X * 0.02f, -0.18f, 0.18f);
            Main.EntitySpriteDraw(quadTex, pos, null, Color.White, tilt, quadTex.Size() / 2f,
                new Vector2(BaseDrawWidth * Scale / quadTex.Width, BaseDrawHeight * Scale / quadTex.Height),
                SpriteEffects.None, 0);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
