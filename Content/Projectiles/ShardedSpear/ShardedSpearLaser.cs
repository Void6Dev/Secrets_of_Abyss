using System;
using ReLogic.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.Enums;
using Terraria.ModLoader;
using Terraria.Audio;
using Terraria.GameContent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Graphics.Shaders;
using Terraria.Graphics.CameraModifiers;

namespace SoA.Content.Projectiles
{
    // Держатель копья: зарядка, наведение, спавн луча и вся отрисовка копья
    public class ShardedSpearHoldout : ModProjectile
    {
        public override string Texture => "SoA/Content/Items/Weapons/ShardedSpear";

        private const int MaxCharge = 90;
        private const int ManaDrainInterval = 20;
        private const int ManaDrainAmount = 3;
        private const int SpearFrames = 8;
        private const float SpearScale = 1.7f;

        private int _charge;
        private int _manaTimer;
        private int _beamIndex = -1;
        private float _fireFlash;
        private Vector2 _aim;
        private SlotId _loopSlot;

        private static readonly SoundStyle StartupSound =
            new("SoA/Content/Sounds/ShardedSpear_startup");
        private static readonly SoundStyle LoopSound =
            SoundID.Item15 with { IsLooped = true, Volume = 1.45f, Pitch = -0.2f };

        private float Power => _charge / (float)MaxCharge;
        private bool Charged => _charge >= MaxCharge;

        // Наконечник копья в мире: позиция передней руки + половина спрайта вдоль прицела
        internal static Vector2 SpearTip(Player player, Vector2 aimDirection)
        {
            float armRotation = aimDirection.ToRotation() - MathHelper.PiOver2;
            Vector2 handPos = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRotation);
            Texture2D spearTex = ModContent.Request<Texture2D>("SoA/Content/Items/Weapons/ShardedSpear").Value;
            float frameHeight = spearTex.Height / (float)SpearFrames;
            return handPos + aimDirection * (frameHeight * SpearScale * 0.5f);
        }

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.friendly = false;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 9999;
            Projectile.aiStyle = -1;
            Projectile.penetrate = -1;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player.dead || !player.active)
            {
                StopFiring();
                Projectile.Kill();
                return;
            }

            // Плавное наведение: при стрельбе луч тяжелее доворачивается.
            // Курсор читает только владелец и рассылает направление через ai[0];
            // иначе на чужом экране копьё целилось бы в ЛОКАЛЬНЫЙ курсор, а не в курсор владельца.
            if (Projectile.owner == Main.myPlayer)
            {
                Vector2 rawAim = Main.MouseWorld - player.MountedCenter;
                Vector2 targetAim = rawAim.LengthSquared() > 0.01f ? Vector2.Normalize(rawAim) : Vector2.UnitX;
                if (_aim == Vector2.Zero) _aim = targetAim;
                Vector2 nextAim = Vector2.Normalize(Vector2.Lerp(_aim, targetAim, Charged ? 0.08f : 0.15f));
                if (Vector2.Dot(nextAim, _aim) < 0.9998f) // заметный доворот — синхронизируем
                    Projectile.netUpdate = true;
                _aim = nextAim;
                Projectile.ai[0] = _aim.ToRotation();
            }
            else
            {
                Vector2 syncedAim = Projectile.ai[0].ToRotationVector2();
                _aim = _aim == Vector2.Zero
                    ? syncedAim
                    : Vector2.Normalize(Vector2.Lerp(_aim, syncedAim, 0.3f));
            }

            Projectile.Center = player.MountedCenter;
            Projectile.rotation = _aim.ToRotation();

            // === Поворот персонажа с активированным копьём (сохранено из старой версии) ===
            player.itemTime = 2;
            player.itemAnimation = 2;
            player.direction = _aim.X >= 0f ? 1 : -1;
            float armRotation = Projectile.rotation - MathHelper.PiOver2;
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRotation);
            // === конец сохранённого блока ===

            if (!player.channel || player.mouseInterface)
            {
                StopFiring();
                Projectile.Kill();
                return;
            }

            if (_charge == 0)
                SoundEngine.PlaySound(StartupSound, player.position);
            if (_charge < MaxCharge)
                _charge++;

            if (++_manaTimer >= ManaDrainInterval)
            {
                _manaTimer = 0;
                if (!player.CheckMana(ManaDrainAmount, pay: true))
                {
                    StopFiring();
                    Projectile.Kill();
                    return;
                }
            }

            _fireFlash *= 0.93f;

            Vector2 tip = SpearTip(player, _aim);
            Lighting.AddLight(tip, 0.05f, 0.3f + Power * 0.5f, 0.8f);

            if (Main.netMode != NetmodeID.Server)
                SpawnChargeDust(tip);

            if (Charged)
                UpdateBeam(player, tip);
        }

        private void SpawnChargeDust(Vector2 tip)
        {
            // Частицы стягиваются к наконечнику, чем ближе к полному заряду — тем плотнее
            if (!Charged && Main.rand.NextFloat() < 0.35f + Power * 0.55f)
            {
                Vector2 offset = Main.rand.NextVector2Unit() * Main.rand.NextFloat(30f, 60f);
                Dust converge = Dust.NewDustPerfect(tip + offset, DustID.IceTorch);
                converge.velocity = -offset * 0.11f;
                converge.scale = 0.6f + Power * 0.8f;
                converge.noGravity = true;
                converge.noLight = true;
            }

            // Пара искр по сужающейся орбите вокруг наконечника
            if (_charge > 10 && Main.rand.NextBool(2))
            {
                float orbitAngle = Main.GameUpdateCount * 0.25f;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 orbitPos = tip + (orbitAngle + MathHelper.Pi * i).ToRotationVector2()
                        * (20f - 12f * Power);
                    Dust spark = Dust.NewDustPerfect(orbitPos, DustID.Electric);
                    spark.velocity = Vector2.Zero;
                    spark.scale = 0.4f + 0.3f * Power;
                    spark.noGravity = true;
                }
            }
        }

        private void UpdateBeam(Player player, Vector2 tip)
        {
            if (_beamIndex < 0)
            {
                int idx = Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    player.MountedCenter,
                    Vector2.Zero,
                    ModContent.ProjectileType<ShardedSpearLaserBeam>(),
                    Projectile.damage,
                    Projectile.knockBack,
                    Projectile.owner);
                if (idx < Main.maxProjectiles)
                {
                    _beamIndex = idx;
                    Main.projectile[idx].ai[1] = _aim.ToRotation();
                }

                SoundEngine.PlaySound(SoundID.Item68, player.position);
                _loopSlot = SoundEngine.PlaySound(LoopSound, player.position);
                _fireFlash = 1f;

                if (Main.myPlayer == Projectile.owner)
                    Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                        player.MountedCenter, _aim, 8f, 8f, 20, 1000f, "SoA:ShardedSpear"));

                if (Main.netMode != NetmodeID.Server)
                {
                    for (int i = 0; i < 26; i++)
                    {
                        Vector2 burstVel = (MathHelper.TwoPi * i / 26f).ToRotationVector2()
                            * Main.rand.NextFloat(3f, 8f);
                        Dust burst = Dust.NewDustPerfect(tip, DustID.IceTorch, burstVel);
                        burst.noGravity = true;
                        burst.scale = Main.rand.NextFloat(1f, 1.6f);
                        burst.fadeIn = 0.4f;
                    }
                }
            }
            else
            {
                Projectile beam = Main.projectile[_beamIndex];
                if (beam.active && beam.type == ModContent.ProjectileType<ShardedSpearLaserBeam>()
                    && beam.owner == Projectile.owner)
                {
                    beam.timeLeft = 10;
                    beam.ai[1] = _aim.ToRotation();
                }
                else
                {
                    _beamIndex = -1;
                }

                if (SoundEngine.TryGetActiveSound(_loopSlot, out var activeSound))
                    activeSound.Position = player.position;
            }
        }

        private void StopFiring()
        {
            if (_beamIndex >= 0 && _beamIndex < Main.maxProjectiles)
            {
                Projectile beam = Main.projectile[_beamIndex];
                if (beam.active && beam.type == ModContent.ProjectileType<ShardedSpearLaserBeam>())
                    beam.ai[0] = 1f; // запускаем анимацию погасания
            }
            _beamIndex = -1;

            if (SoundEngine.TryGetActiveSound(_loopSlot, out var sound))
                sound.Stop();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Player player = Main.player[Projectile.owner];
            Texture2D tex = ModContent.Request<Texture2D>("SoA/Content/Items/Weapons/ShardedSpear").Value;

            int frameHeight = tex.Height / SpearFrames;
            int frame = Math.Min((int)(Power * SpearFrames), SpearFrames - 1);
            Rectangle src = new(0, frame * frameHeight, tex.Width, frameHeight);
            Vector2 origin = new(tex.Width * 0.5f, frameHeight * 0.5f);

            // Копьё рисуется в позиции передней руки (та же логика поворота, что в AI)
            float armRotation = Projectile.rotation - MathHelper.PiOver2;
            Vector2 handPos = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, armRotation);
            Vector2 drawPos = handPos - Main.screenPosition;

            SpriteEffects fx = player.direction == -1 ? SpriteEffects.FlipVertically : SpriteEffects.None;
            float spriteRot = Projectile.rotation + MathHelper.PiOver4 * player.direction;

            Main.EntitySpriteDraw(tex, drawPos, src, lightColor, spriteRot, origin, SpearScale, fx, 0);

            // Свечение копья, нарастающее с зарядом (альфа = 255: аддитив гасит цвет линейно)
            if (Power > 0.2f)
            {
                float chargeGlow = (Power - 0.2f) / 0.8f;
                float pulse = (0.3f + 0.2f * MathF.Sin(Main.GameUpdateCount * 0.45f)) * chargeGlow;
                Color innerGlow = new Color(60, 140, 255) * pulse;
                innerGlow.A = 255;
                Color outerGlow = new Color(140, 200, 255) * (pulse * 0.5f);
                outerGlow.A = 255;

                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, null, null, null,
                    Main.GameViewMatrix.TransformationMatrix);

                Main.EntitySpriteDraw(tex, drawPos, src, innerGlow, spriteRot, origin, 1.3f, fx, 0);
                Main.EntitySpriteDraw(tex, drawPos, src, outerGlow, spriteRot, origin,
                    SpearScale + 0.1f * chargeGlow, fx, 0);

                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                    Main.GameViewMatrix.TransformationMatrix);
            }

            // Орб зарядки на наконечнике + вспышка выстрела
            if (_charge > 0)
            {
                Vector2 tipPos = SpearTip(player, _aim) - Main.screenPosition;
                float glowPulse = (0.8f + 0.2f * MathF.Sin(Main.GameUpdateCount * 0.3f)) * Power;
                Texture2D glowTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/BeamDistortion").Value;

                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, null, null, null, null,
                    Main.GameViewMatrix.TransformationMatrix);

                GameShaders.Misc["SoA:BeamGlow"].UseOpacity(glowPulse).Apply();
                Main.EntitySpriteDraw(glowTex, tipPos, null, Color.White, 0f, glowTex.Size() / 2f,
                    0.7f * Power, SpriteEffects.None, 0);

                if (_fireFlash > 0.05f)
                {
                    GameShaders.Misc["SoA:BeamGlow"].UseOpacity(_fireFlash).Apply();
                    Main.EntitySpriteDraw(glowTex, tipPos, null, Color.White, 0f, glowTex.Size() / 2f,
                        0.5f + 1.7f * _fireFlash, SpriteEffects.None, 0);
                }

                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                    Main.GameViewMatrix.TransformationMatrix);
            }

            return false;
        }
    }

    // Луч: один растянутый квад с процедурным шейдером SoA:ShardedBeam
    public class ShardedSpearLaserBeam : ModProjectile
    {
        public override string Texture => "SoA/Content/Projectiles/ShardedSpear/ShardedSpearBeamMid";

        private const float MaxLength = 1150f;
        private const float GrowPerTick = 140f;
        private const float FadeTicks = 12f;
        private const float HitWidth = 16f;
        private const float QuadWidth = 110f; // ширина квада, само свечение уже за счёт профиля

        private static readonly float[] LaserSamples = new float[3];

        private float _length;    // дистанция до препятствия
        private float _reach;     // анимированная текущая длина
        private float _grow;      // разгон ширины при появлении
        private float _waterBlend;
        private bool _hitsWall;
        private int _baseDamage;

        private Vector2 Direction => Projectile.ai[1].ToRotationVector2();
        private bool Dying => Projectile.ai[0] >= 1f;
        private float DeathFade => Dying ? Math.Max(0f, 1f - (Projectile.ai[0] - 1f) / FadeTicks) : 1f;

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 18;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 10;
            Projectile.aiStyle = -1;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player.dead || !player.active)
            {
                Projectile.Kill();
                return;
            }

            Projectile.velocity = Vector2.Zero;

            Vector2 dir = Direction;
            Vector2 tip = ShardedSpearHoldout.SpearTip(player, dir);
            Projectile.Center = tip;
            Projectile.rotation = dir.ToRotation();

            if (Dying)
            {
                Projectile.ai[0]++;
                Projectile.timeLeft = 2;
                if (Projectile.ai[0] > FadeTicks + 1f)
                    Projectile.Kill();
                return;
            }

            _grow = Math.Min(1f, _grow + 0.12f);

            // Дистанция до тайлов ванильным лазер-сканом (как у Last Prism)
            Collision.LaserScan(tip, dir, HitWidth * 0.5f, MaxLength, LaserSamples);
            _length = MaxLength;
            foreach (float sample in LaserSamples)
                _length = Math.Min(_length, sample);
            _hitsWall = _length < MaxLength - 5f;

            _reach = _reach < _length ? Math.Min(_reach + GrowPerTick, _length) : _length;

            // Свет вдоль луча одной строкой
            DelegateMethods.v3_1 = Vector3.Lerp(
                new Vector3(0.15f, 0.6f, 0.9f), new Vector3(0.5f, 0.15f, 0.95f), _waterBlend);
            Utils.PlotTileLine(tip, tip + dir * _reach, 26f, DelegateMethods.CastLight);

            // Вода вдоль луча: удвоение урона и перекраска в фиолетовый
            bool inWater = false;
            for (float t = 0f; t < _reach; t += 32f)
            {
                Point tilePos = (tip + dir * t).ToTileCoordinates();
                if (!WorldGen.InWorld(tilePos.X, tilePos.Y))
                    continue;
                Tile tile = Main.tile[tilePos.X, tilePos.Y];
                if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water)
                {
                    inWater = true;
                    break;
                }
            }
            _waterBlend = MathHelper.Lerp(_waterBlend, inWater ? 1f : 0f, 0.06f);

            if (_baseDamage == 0)
                _baseDamage = Projectile.damage;
            Projectile.damage = _waterBlend > 0.5f ? _baseDamage * 2 : _baseDamage;

            if (Main.netMode != NetmodeID.Server)
                SpawnBeamDust(tip, dir);
        }

        private void SpawnBeamDust(Vector2 tip, Vector2 dir)
        {
            Vector2 perp = dir.RotatedBy(MathHelper.PiOver2);

            // Искры, летящие вдоль луча
            if (_reach > 40f)
            {
                for (int i = 0; i < 2; i++)
                {
                    float t = Main.rand.NextFloat(16f, _reach);
                    Dust spark = Dust.NewDustPerfect(
                        tip + dir * t + perp * Main.rand.NextFloatDirection() * 7f, DustID.IceTorch);
                    spark.velocity = perp * Main.rand.NextFloatDirection() * 1.6f
                        + dir * Main.rand.NextFloat(1f, 3f);
                    spark.scale = Main.rand.NextFloat(0.5f, 0.9f);
                    spark.noGravity = true;
                    spark.noLight = true;
                }
            }

            // Брызги в точке попадания
            if (_hitsWall && _reach >= _length - 1f)
            {
                Vector2 hitPos = tip + dir * _length;
                Lighting.AddLight(hitPos, 0.1f, 1.1f, 1.4f);

                for (int i = 0; i < 2; i++)
                {
                    Dust splash = Dust.NewDustDirect(hitPos - new Vector2(6f), 12, 12, DustID.IceTorch);
                    splash.velocity = (perp * Main.rand.NextFloatDirection() * 2.5f
                        - dir * Main.rand.NextFloat(0.3f, 1.2f)) * Main.rand.NextFloat(3f, 7f);
                    splash.scale = Main.rand.NextFloat(0.6f, 1.0f);
                    splash.noGravity = true;
                    splash.fadeIn = 0.5f;
                }
            }
        }

        public override void CutTiles()
        {
            Vector2 dir = Direction;
            Vector2 tip = ShardedSpearHoldout.SpearTip(Main.player[Projectile.owner], dir);
            DelegateMethods.tilecut_0 = TileCuttingContext.AttackProjectile;
            Utils.PlotTileLine(tip, tip + dir * _reach, HitWidth, DelegateMethods.CutTiles);
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (Dying)
                return false;
            Vector2 dir = Direction;
            Vector2 tip = ShardedSpearHoldout.SpearTip(Main.player[Projectile.owner], dir);
            float unused = 0f;
            return Collision.CheckAABBvLineCollision(
                targetHitbox.TopLeft(), targetHitbox.Size(),
                tip, tip + dir * _reach, HitWidth, ref unused);
        }

        public override void OnKill(int timeLeft)
        {
            Vector2 dir = Direction;
            Vector2 pos = ShardedSpearHoldout.SpearTip(Main.player[Projectile.owner], dir)
                + dir * Math.Max(_length, 0f);
            SoundEngine.PlaySound(SoundID.Splash, pos);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (_reach < 12f)
                return false;

            Player player = Main.player[Projectile.owner];
            Vector2 dir = Direction;
            Vector2 start = ShardedSpearHoldout.SpearTip(player, dir) - Main.screenPosition;
            float fade = DeathFade;
            float widthScale = _grow * (0.5f + 0.5f * fade); // при погасании луч сужается

            Texture2D quadTex = ModContent.Request<Texture2D>("SoA/Content/Projectiles/ShardedSpear/ShardedSpearBeamMid").Value;
            Texture2D noiseTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/WaveNoise").Value;
            Texture2D starTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/BeamDistortion").Value;

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            // Весь луч — один квад: uv.y растягивается на всю длину, форму рисует шейдер
            var beamShader = GameShaders.Misc["SoA:ShardedBeam"];
            beamShader.UseOpacity(1f);
            beamShader.Shader.Parameters["uBlend"]?.SetValue(_waterBlend);
            beamShader.Shader.Parameters["uLength"]?.SetValue(_reach);
            beamShader.Shader.Parameters["uFade"]?.SetValue(fade * _grow);
            beamShader.Apply();

            Main.graphics.GraphicsDevice.Textures[1] = noiseTex;
            Main.graphics.GraphicsDevice.SamplerStates[1] = SamplerState.LinearWrap;

            Vector2 quadScale = new(QuadWidth * widthScale / quadTex.Width, _reach / quadTex.Height);
            Main.EntitySpriteDraw(quadTex, start, null, Color.White,
                Projectile.rotation - MathHelper.PiOver2,
                new Vector2(quadTex.Width / 2f, 0f), quadScale, SpriteEffects.None, 0);

            // Муззл-вспышка у наконечника
            var glowShader = GameShaders.Misc["SoA:BeamGlow"];
            glowShader.UseOpacity(0.8f * fade * _grow).Apply();
            Main.EntitySpriteDraw(starTex, start, null, Color.White, 0f, starTex.Size() / 2f,
                0.8f * _grow, SpriteEffects.None, 0);

            // Точка попадания: звезда + расширяющиеся кольца
            if (_hitsWall && _reach >= _length - 1f)
            {
                Vector2 impact = start + dir * _reach;
                float impPulse = 0.7f + 0.3f * MathF.Sin(Main.GameUpdateCount * 0.45f);

                glowShader.UseOpacity(0.55f * impPulse * fade).Apply();
                Main.EntitySpriteDraw(starTex, impact, null, Color.White, 0f, starTex.Size() / 2f,
                    0.9f, SpriteEffects.None, 0);

                glowShader.UseOpacity(0.4f * impPulse * fade).Apply();
                Main.EntitySpriteDraw(starTex, impact, null, Color.White,
                    dir.ToRotation() + MathHelper.PiOver2, starTex.Size() / 2f,
                    new Vector2(0.35f, 2.2f) * impPulse, SpriteEffects.None, 0);

                var ringShader = GameShaders.Misc["SoA:ImpactRing"];
                for (int i = 0; i < 2; i++)
                {
                    float ringProgress = (Main.GameUpdateCount / 30f + i * 0.5f) % 1f;
                    ringShader.UseOpacity((1f - ringProgress) * fade);
                    ringShader.Shader.Parameters["uProgress"]?.SetValue(ringProgress);
                    ringShader.Shader.Parameters["uBlend"]?.SetValue(_waterBlend);
                    ringShader.Apply();
                    Main.EntitySpriteDraw(starTex, impact, null, Color.White, 0f, starTex.Size() / 2f,
                        2.2f, SpriteEffects.None, 0);
                }
            }

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            return false;
        }
    }
}
