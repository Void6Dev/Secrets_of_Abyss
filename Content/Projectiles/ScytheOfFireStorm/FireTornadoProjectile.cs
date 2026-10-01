using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Utils;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Огненное торнадо Scythe of Fire Storm: летит по траектории запуска,
    // притягивает и жжёт врагов, плавно затухает; об блоки не проходит.
    // Коснувшись земли, едет по ней в сторону полёта, уступы до двух блоков перешагивает;
    // стена гасит вихрь. Коснувшись воды, превращается в паровой гейзер: не жжёт,
    // зато мощно подбрасывает врагов и живёт меньше.
    // Взмах Косы сквозь вихрь подпитывает его (Feed): жизнь продлевается, вихрь
    // разворачивается к курсору. Размер от подпитки не растёт.
    // ai[0]: сила заряда 0..1 — масштаб вихря и радиус притяжения.
    // ai[1]: 0 — полёт, ±1 — едет по земле в эту сторону.
    public class FireTornadoProjectile : ModProjectile
    {
        // Спрайт лежит рядом с кодом в папке владельца, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/ScytheOfFireStorm/FireTornadoProjectile";

        public const int PhysicalSize = 36;

        private const int Lifetime = 300;
        private const int WallFadeTicks = 45; // остаток жизни после удара в стену
        private const float MaxStepUp = 34f;  // уступ, который вихрь на земле перешагивает
        private const int SteamLifetimeCap = 200;   // пар живёт меньше огня
        private const float SteamLiftMultiplier = 2.2f;
        private const float SteamMaxLiftSpeed = 8f;
        private const float SteamPullRadiusFactor = 0.8f;

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

        // Общий множитель размера на всех ступенях заряда: тело, зона урона, притяжение
        private const float SizeScale = 0.65f;
        private const int BirthTicks = 14;          // вихрь вырастает из взмаха, а не появляется целым
        private const int FeedLifeBonus = 90;
        private const float FeedLaunchSpeed = 7f;

        // Огненные ленты вокруг воронки
        private const int RibbonStrands = 2;
        private const float RibbonHeat = 0.4f;
        private const int RibbonPoints = 28;
        private const float RibbonTurns = 1.6f;
        private const float RibbonSpinSpeed = 4.5f;
        private const float RibbonWidth = 10f;
        private const float RibbonOrbit = 1.12f;   // ленты вьются чуть снаружи силуэта, а не внутри тела
        private const float RibbonFromH = 0.06f;   // у самой земли и над устьем тела нет — ленты там не нужны
        private const float RibbonToH = 0.9f;

        private static readonly Color FlameGlow = new(255, 150, 50, 0);
        private static readonly Color AshColor = new(60, 45, 40);

        private bool _steam;
        // Счётчик подпиток приходит по сети; его рост — сигнал всем клиентам показать вспышку
        private byte _feedCount;
        private byte _shownFeedCount;

        private float ChargeRatio => Projectile.ai[0];
        private ref float GroundDir => ref Projectile.ai[1];
        private ref float Age => ref Projectile.localAI[0];

        private float Birth => SoAEasing.BackOut(Math.Min(Age / BirthTicks, 1f));
        private float Scale => MathHelper.Lerp(0.65f, 1.15f, ChargeRatio) * SizeScale * Birth;
        private float LifeProgress => 1f - Projectile.timeLeft / (float)Lifetime;

        public bool CanBeFed => !_steam;

        // Воронка «стоит» на нижней кромке хитбокса: центр визуала и зоны урона выше точки касания
        private Vector2 VortexCenter => Projectile.Bottom - new Vector2(0f, BaseVortexHeight * Scale / 2f);

        public override void SetDefaults()
        {
            // Физический хитбокс маленький: им вихрь цепляется за тайлы,
            // а зона урона задаётся отдельно в Colliding
            Projectile.width = PhysicalSize;
            Projectile.height = PhysicalSize;
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
            Age++;
            if (_feedCount != _shownFeedCount)
            {
                _shownFeedCount = _feedCount;
                FeedEffects();
            }

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
            else
                SpawnFireDetails();

            float glow = 1f - LifeProgress * 0.7f;
            if (_steam)
                Lighting.AddLight(VortexCenter, 0.35f * glow, 0.55f * glow, 0.7f * glow);
            else
                Lighting.AddLight(VortexCenter, 1.4f * glow, 0.6f * glow, 0.1f * glow);
        }

        #region Вода

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

        #endregion

        #region Подпитка

        // Вся воронка: зона урона и цель подпитки взмахом Косы
        public Rectangle VortexArea
        {
            get
            {
                int w = (int)(BaseVortexWidth * Scale);
                int h = (int)(BaseVortexHeight * Scale);
                Vector2 center = VortexCenter;
                return new Rectangle((int)(center.X - w / 2f), (int)(center.Y - h / 2f), w, h);
            }
        }

        // Только у владельца (его взмах); остальные узнают по netUpdate и счётчику подпиток
        public void Feed(Vector2 swingDirection)
        {
            Projectile.timeLeft = Math.Min(Projectile.timeLeft + FeedLifeBonus, Lifetime);

            // Взмах задаёт новый курс: по земле — сторону, в полёте — новый бросок
            if (GroundDir != 0f)
                GroundDir = swingDirection.X >= 0f ? 1f : -1f;
            else
                Projectile.velocity = swingDirection.SafeNormalize(Vector2.UnitX) * FeedLaunchSpeed;

            _feedCount++;
            Projectile.netUpdate = true;
        }

        private void FeedEffects()
        {
            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.7f, Pitch = 0.2f }, VortexCenter);
            SoundEngine.PlaySound(SoundID.Item34 with { Volume = 0.8f, Pitch = -0.3f }, VortexCenter);
            SoAParticles.SpawnGlow(VortexCenter, Vector2.Zero, FlameGlow, 40f, 150f, 16);
            SoAParticles.AddLight(VortexCenter, new Color(255, 140, 40), 2.5f, 20);

            // Языки пламени срываются с воронки по кругу
            for (int i = 0; i < 20; i++)
            {
                float angle = MathHelper.TwoPi * i / 20f;
                Vector2 velocity = angle.ToRotationVector2() * Main.rand.NextFloat(3f, 7f) - Vector2.UnitY * 2f;
                SoAParticles.SpawnStreak(VortexCenter, velocity, FlameGlow, 2.2f, 0.1f, 22);
            }
        }

        #endregion

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

        #region Частицы

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

        // Угли с макушки и пепел, который вихрь поднимает с земли
        private void SpawnFireDetails()
        {
            if (Main.dedServ)
                return;

            float fade = 1f - LifeProgress;
            float vortexHeight = BaseVortexHeight * Scale;
            Vector2 crown = VortexCenter - new Vector2(0f, vortexHeight * 0.45f);

            if (Main.rand.NextFloat() < 0.4f * fade)
            {
                Vector2 from = crown + new Vector2(Main.rand.NextFloat(-50f, 50f) * Scale, 0f);
                Vector2 velocity = new(Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat(2f, 5f));
                SoAParticles.SpawnStreak(from, velocity, FlameGlow, 1.8f, 0.04f, 30, 2.4f);
            }

            if (GroundDir != 0f && Main.rand.NextFloat() < 0.5f * fade)
            {
                Vector2 from = Projectile.Bottom + new Vector2(Main.rand.NextFloat(-40f, 40f) * Scale, -4f);
                Vector2 velocity = new(Main.rand.NextFloat(-1.5f, 1.5f), -Main.rand.NextFloat(0.5f, 1.5f));
                SoAParticles.SpawnSmoke(from, velocity, AshColor, 10f, 34f * Scale, 0.35f, 45);
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

        #endregion

        #region Урон и столкновения

        // Зона урона — вся воронка, а не маленький физический хитбокс
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) => VortexArea.Intersects(targetHitbox);

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            bool hitFloor = Projectile.velocity.Y != oldVelocity.Y && oldVelocity.Y > 0f;
            bool hitWall = Projectile.velocity.X != oldVelocity.X;

            // Коснулся земли — дальше едет по ней в сторону полёта
            if (hitFloor && GroundDir == 0f)
            {
                GroundDir = oldVelocity.X != 0f
                    ? Math.Sign(oldVelocity.X)
                    : Main.player[Projectile.owner].direction;
                Projectile.netUpdate = true;
            }

            // Ступенька или угол склона — перешагнуть; стена по ходу — вихрь догорает на месте
            if (hitWall && !SoAPhysics.TryStepUp(Projectile, oldVelocity, MaxStepUp))
            {
                Projectile.velocity = Vector2.Zero;
                GroundDir = 0f;
                if (Projectile.timeLeft > WallFadeTicks)
                    Projectile.timeLeft = WallFadeTicks;
                Projectile.netUpdate = true;
                return false;
            }

            if (Projectile.velocity.Y != oldVelocity.Y)
                Projectile.velocity.Y = 0f;
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
            writer.Write(_feedCount);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _steam = reader.ReadBoolean();
            _feedCount = reader.ReadByte();
        }

        #endregion

        #region Отрисовка

        public override bool PreDraw(ref Color lightColor)
        {
            if (!_steam)
            {
                DrawGroundGlow();
                DrawRibbons(front: false);
            }

            Texture2D quadTex = SoAVfx.Quad;
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

            Main.EntitySpriteDraw(quadTex, pos, null, Color.White, Tilt, quadTex.Size() / 2f,
                new Vector2(BaseDrawWidth * Scale / quadTex.Width, BaseDrawHeight * Scale / quadTex.Height),
                SpriteEffects.None, 0);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            if (!_steam)
                DrawRibbons(front: true);
            return false;
        }

        // Лёгкий наклон по ходу полёта
        private float Tilt => MathHelper.Clamp(Projectile.velocity.X * 0.02f, -0.18f, 0.18f);

        // Раскалённое пятно под вихрем, пока он едет по земле
        private void DrawGroundGlow()
        {
            if (GroundDir == 0f)
                return;

            float fade = 1f - LifeProgress;
            float size = BaseVortexWidth * Scale * 1.4f;
            SoAVfx.DrawTintedGlow(Main.spriteBatch, Projectile.Bottom, new Vector2(size, size * 0.2f),
                FlameGlow * (0.7f * fade));
        }

        // Огненные ленты спиралью вокруг воронки. Дальняя половина витка рисуется под телом
        // вихря и тусклее, ближняя — поверх: спираль читается объёмной
        private void DrawRibbons(bool front)
        {
            float fade = (1f - LifeProgress) * Birth;
            float height = BaseDrawHeight * Scale; // по высоте квада шейдера, а не зоны урона
            float time = Main.GlobalTimeWrappedHourly * RibbonSpinSpeed;
            Vector2 center = VortexCenter;
            Span<Vector2> run = stackalloc Vector2[RibbonPoints];

            for (int strand = 0; strand < RibbonStrands; strand++)
            {
                float phase = MathHelper.TwoPi * strand / RibbonStrands;
                int count = 0;
                float runStartH = 0f;

                for (int i = 0; i <= RibbonPoints; i++)
                {
                    bool end = i == RibbonPoints;
                    float h = MathHelper.Lerp(RibbonFromH, RibbonToH, i / (float)(RibbonPoints - 1));
                    float angle = time + phase + h * RibbonTurns * MathHelper.TwoPi;
                    bool isFront = Math.Sin(angle) > 0f;

                    if (!end && isFront == front)
                    {
                        if (count == 0)
                            runStartH = h;
                        float radius = FunnelHalfWidth(h) * RibbonOrbit;
                        var offset = new Vector2((float)Math.Cos(angle) * radius, height * (0.5f - h));
                        run[count++] = center + offset.RotatedBy(Tilt);
                        continue;
                    }

                    // Кусок спирали с одной стороны кончился — рисуем его отдельной лентой
                    if (count >= 2)
                        DrawRibbonRun(run[..count], runStartH, h, fade * (front ? 1f : 0.5f));
                    count = 0;
                }
            }
        }

        // Полуширина воронки на высоте h (0 — земля, 1 — устье) в пикселях мира: тот же профиль,
        // что halfWidth в TornadoPS (там y = 1 - h и доля от ширины квада)
        private float FunnelHalfWidth(float h)
            => MathHelper.Lerp(0.46f, 0.10f, (float)Math.Pow(1f - h, 1.1)) * BaseDrawWidth * Scale;

        private void DrawRibbonRun(ReadOnlySpan<Vector2> points, float fromH, float toH, float strength)
        {
            float width = RibbonWidth * Scale;
            SoATrail.Draw(points,
                progress =>
                {
                    // Лента тоньше к макушке и сходит на нет на концах куска
                    float h = MathHelper.Lerp(fromH, toH, progress);
                    return width * (1f - 0.5f * h) * (float)Math.Sin(MathHelper.Pi * progress);
                },
                progress => TrailStyle.MagmaBody(MathHelper.Lerp(fromH, toH, progress) * 0.8f, RibbonHeat) * strength,
                TrailStyle.Magma);
        }

        #endregion
    }
}
