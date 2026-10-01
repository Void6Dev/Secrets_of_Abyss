using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;

namespace SoA.Content.Projectiles
{
    // Аквапила в руках. Удержание, валку деревьев и положение у руки ведёт ванильный
    // ИИ сверла; здесь — отрисовка, урон по линии полотна и вся «жизнь» пилы:
    //  - запуск: мотор взрёвывает — сильная тряска, клуб пара, цепь разгоняется;
    //  - холостой ход: мелкая дрожь, выхлоп паром (в воде — пузырями), вода бежит по цепи
    //    лентой и срывается с кончика брызгами по касательной;
    //  - пиление: сильная тряска, искры и стружка из самого тайла;
    //  - разгон: пока пила зажата, в баке растёт давление (в контакте — втрое быстрее),
    //    зубья всё сильнее светятся голубым. На пике — гидроперегрузка: выброс пара,
    //    полотно продолжается астральной проекцией шины — режет врагов по всей длине
    //    и упирается в блоки. Отпустил пилу — давление сброшено;
    //  - резкий взмах пилой оставляет шлейф голубых копий, удар по врагу — вспышку силуэта.
    // В воде привод работает на полную: лента воды толще, брызг больше.
    // Лист кадров цепи — вертикальный, ChainFrames кадров одинаковой высоты
    internal class AquasawProjectile : ModProjectile
    {
        // Спрайт лежит рядом с кодом в папке владельца, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/Aquasaw/AquasawProjectile";
        private const string GlowTexturePath = "SoA/Content/Projectiles/Aquasaw/AquasawProjectile_Glow";

        // Лист 58×104: 4 кадра по 26 px (24 px картинки + 2 px отступа). Высота листа обязана
        // делиться на число кадров — игра режет его поровну
        private const int ChainFrames = 4;
        private const int StartFrameTicks = 6;      // цепь на запуске ещё не раскрутилась
        private const int IdleFrameTicks = 3;
        private const int CuttingFrameTicks = 1;

        #region Точки спрайта

        // Пиксели кадра. Спрайт горизонтальный: хват — перемычка задней ручки-петли,
        // полотно — шина от выхода из корпуса до кончика
        private static readonly Vector2 GripPixel = new(7f, 14f);
        private static readonly Vector2 BladeStartPixel = new(30f, 14f);
        private static readonly Vector2 TipPixel = new(56f, 14f);
        private static readonly Vector2 TankPixel = new(16f, 13f);
        private static readonly Vector2 ExhaustPixel = new(12f, 7f);
        private static readonly Vector2 ChainBottomStartPixel = new(31f, 20f);

        // Линия цепи вокруг шины, по ходу её движения: поверху вперёд, вокруг кончика, понизу назад
        private static readonly Vector2[] ChainLoopPixels =
        {
            new(30f, 9f), new(38f, 9f), new(46f, 9f), new(52f, 9.5f), new(55.5f, 11f),
            new(57f, 14f), new(55.5f, 17.5f), new(52f, 20f), new(46f, 20f), new(38f, 20f), new(31f, 20f),
        };

        // Куски шины для астральной проекции: отрезок ровно в два шага зубьев (8 px каждый) —
        // повторяется без видимого стыка, и скруглённый кончик. BarAxisRow — ось шины внутри кусков
        private static readonly Rectangle BarSegmentSource = new(32, 6, 16, 18);
        private static readonly Rectangle BarTipSource = new(48, 6, 10, 18);
        private static readonly Vector2 ProjectionStartPixel = new(52f, 14f);
        private const float BarAxisRow = 8f;

        private static readonly float SpriteAxisAngle = (TipPixel - GripPixel).ToRotation();

        #endregion

        #region Настройки

        private const float BladeHitWidth = 18f;
        private const int ContactSamples = 6;
        private const int CuttingGraceUpdates = 10;  // сколько обновлений «режет» держится после контакта
        private const float WetTargetDamageMultiplier = 1.25f;

        // Тряска
        private const int StartupTicks = 18;
        private const float StartupShake = 2.6f;
        private const float IdleShake = 0.6f;
        private const float CuttingShake = 2.2f;
        private const float OverdriveShake = 3f;
        private const float CuttingRotationJitter = 0.05f;

        // Давление и перегрузка
        // Пила разгоняется, пока зажата, а в контакте — втрое быстрее
        private const int PressureIdleBuildTicks = 150;
        private const int PressureCuttingBuildTicks = 50;
        private const int OverdriveHissTicks = 16;

        // Астральная проекция шины: false — только визуал, без дальности
        private const bool ProjectionDealsDamage = true;
        private const float ProjectionLength = 96f;
        private const float ProjectionHitWidth = 18f;   // = высота проекции шины на спрайте
        private const int ProjectionGrowTicks = 8;
        private const float ProjectionRayStep = 6f;

        // Вода, пар, частицы
        private const float WaterRibbonWidth = 5.5f;
        private const float IdleWaterFlow = 0.7f;
        private const float CuttingWaterFlow = 1.1f;
        private const float WetFlowBonus = 1.35f;
        private const int IdleExhaustTicks = 12;
        private const int CuttingExhaustTicks = 5;
        private const int TileDebrisTicks = 3;
        private const int HitFlashTicks = 6;
        private const float GlowPulseSpeed = 4f;

        // Шлейф взмаха: копии появляются, когда пилу ведут быстрее этого (рад/тик)
        private const int GhostCount = 4;
        private const float GhostMinTurnSpeed = 0.06f;
        private const float GhostFullTurnSpeed = 0.25f;

        private static readonly Color WaterGlow = new(120, 205, 255, 0);
        private static readonly Color HotCore = new(225, 250, 255, 0);
        private static readonly Color SparkColor = new(190, 235, 255, 0);
        private static readonly Color SteamColor = new(190, 225, 255);
        private static readonly Color AstralViolet = new(150, 110, 255, 0);
        private static readonly Color AstralBody = new(60, 130, 255, 0);
        private const float AstralTwinOffset = 1.5f;

        #endregion

        private int _cuttingTimer;       // > 0 — полотно недавно было в контакте
        private int _hitFlash;
        private Vector2 _shakeOffset;
        private float _shakeRotation;

        private float _pressure;         // 0..1
        private bool _overdrive;
        private float _projectionGrow;          // 0..1 — проекция выдвигается и прячется плавно
        private Vector2 _projectionEnd;         // где проекция упёрлась (в блок или на полной длине)
        private bool _projectionHitWall;

        private readonly float[] _ghostRotations = new float[GhostCount];
        private float _lastRotation;
        private float _turnSpeed;
        private int _ghostCount;

        // Своё поле, а не localAI: ванильный ИИ сверла может распоряжаться localAI сам
        private int _age;
        private uint _lastTickProcessed;
        private bool Cutting => _cuttingTimer > 0;
        private float StartupProgress => Math.Min(_age / (float)StartupTicks, 1f);
        private Player Owner => Main.player[Projectile.owner];
        private int FacingDirection => Projectile.velocity.X >= 0f ? 1 : -1;
        private Vector2 Aim => Projectile.velocity.SafeNormalize(Vector2.UnitX * FacingDirection);
        // «Вниз» по спрайту: у кончика цепь уходит именно туда
        private Vector2 SpriteDown => Aim.RotatedBy(MathHelper.PiOver2 * FacingDirection);
        private float WaterFlow
            => ((Cutting ? CuttingWaterFlow : IdleWaterFlow) + 0.4f * _pressure) * (Owner.wet ? WetFlowBonus : 1f);
        private bool ProjectionActive => _projectionGrow > 0.01f;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = ChainFrames;
        }

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.ownerHitCheck = true;
            Projectile.extraUpdates = 1;
            Projectile.timeLeft = 300;
            Projectile.aiStyle = ProjAIStyleID.Drill;
        }

        #region Геометрия

        // Точка руки, в которую садится хват спрайта
        private Vector2 HandPosition => Owner.RotatedRelativePoint(Owner.MountedCenter);

        // Пиксель кадра → мировая точка. Без тряски — для урона и контакта,
        // с тряской — для отрисовки и частиц, чтобы они сидели на дрожащем спрайте
        private Vector2 PixelToWorld(Vector2 pixel, bool shaken = false)
        {
            var local = new Vector2(FacingDirection * (pixel.X - GripPixel.X), pixel.Y - GripPixel.Y);
            Vector2 origin = shaken ? HandPosition + _shakeOffset : HandPosition;
            return origin + local.RotatedBy(SpriteRotation(shaken ? _shakeRotation : 0f));
        }

        // Отражённый спрайт смотрит зеркально, поэтому и ось полотна у него зеркальная
        private float SpriteRotation(float jitter)
        {
            float axis = FacingDirection == 1 ? SpriteAxisAngle : MathHelper.Pi - SpriteAxisAngle;
            return Projectile.velocity.ToRotation() - axis + jitter;
        }

        #endregion

        public override void AI()
        {
            // Ванильный ИИ сверла отработал до нас: позиция у руки, направление, удержание
            if (_cuttingTimer > 0)
                _cuttingTimer--;

            bool touching = BladeContact(out Vector2 contact, out Point contactTile);
            if (touching)
                _cuttingTimer = CuttingGraceUpdates;

            // Всё дальше — раз в тик, а не на каждое из двух обновлений (extraUpdates = 1)
            if (_lastTickProcessed == Main.GameUpdateCount)
                return;
            _lastTickProcessed = Main.GameUpdateCount;

            _age++;
            if (_hitFlash > 0)
                _hitFlash--;

            UpdatePressure();
            UpdateProjection();
            UpdateShake();
            UpdateGhosts();
            AnimateChain();
            UpdateLight();
            if (Main.dedServ)
                return;

            if (_age == 1)
                StartupBurst();
            if (touching)
                ContactEffects(contact, contactTile);
            SpawnChainSpray();
            SpawnExhaust();
            if (ProjectionActive)
                SpawnProjectionSpray();
        }

        // Первая точка полотна, упёршаяся в блок или дерево. Деревья не «твёрдые»,
        // поэтому проверяем и то, что рубится топором
        private bool BladeContact(out Vector2 contact, out Point tilePos)
        {
            Vector2 start = PixelToWorld(BladeStartPixel);
            Vector2 tip = PixelToWorld(TipPixel);
            for (int i = 0; i <= ContactSamples; i++)
            {
                contact = Vector2.Lerp(start, tip, i / (float)ContactSamples);
                tilePos = contact.ToTileCoordinates();
                Tile tile = Framing.GetTileSafely(tilePos.X, tilePos.Y);
                if (tile.HasTile && (Main.tileSolid[tile.TileType] || Main.tileAxe[tile.TileType]))
                    return true;
            }
            contact = tip;
            tilePos = Point.Zero;
            return false;
        }

        #region Давление и проекция

        private void UpdatePressure()
        {
            // Разгон не сбрасывается, пока пила в руках: отпустил — снаряд пропал, давление тоже
            int buildTicks = Cutting ? PressureCuttingBuildTicks : PressureIdleBuildTicks;
            _pressure = Math.Min(_pressure + StartupProgress / buildTicks, 1f);

            if (!_overdrive && _pressure >= 1f)
            {
                _overdrive = true;
                if (!Main.dedServ)
                    OverdriveBurst();
            }

            if (_overdrive && !Main.dedServ && _age % OverdriveHissTicks == 0)
                SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.35f, Pitch = 0.4f, PitchVariance = 0.15f },
                    PixelToWorld(TipPixel));
        }

        // Проекция выдвигается из кончика и упирается в первый твёрдый блок на пути
        private void UpdateProjection()
        {
            _projectionGrow = _overdrive
                ? Math.Min(_projectionGrow + 1f / ProjectionGrowTicks, 1f)
                : Math.Max(_projectionGrow - 2f / ProjectionGrowTicks, 0f);

            Vector2 tip = PixelToWorld(TipPixel);
            float length = ProjectionLength * _projectionGrow;
            _projectionHitWall = false;
            _projectionEnd = tip + Aim * length;
            for (float d = ProjectionRayStep; d <= length; d += ProjectionRayStep)
            {
                Vector2 point = tip + Aim * d;
                Point tilePos = point.ToTileCoordinates();
                if (WorldGen.SolidTile(tilePos.X, tilePos.Y))
                {
                    _projectionEnd = point;
                    _projectionHitWall = true;
                    break;
                }
            }
        }

        #endregion

        // Запуск — рёв мотора; холостой ход — мелкая дрожь; в контакте — сильная тряска и дёрганье
        private void UpdateShake()
        {
            float strength = _overdrive ? OverdriveShake
                : Cutting ? CuttingShake
                : MathHelper.Lerp(StartupShake, IdleShake, StartupProgress);
            _shakeOffset = Main.rand.NextVector2Circular(strength, strength);

            float jitter = Cutting ? 1f : 1f - StartupProgress;
            _shakeRotation = Main.rand.NextFloatDirection() * CuttingRotationJitter * jitter;
        }

        // Резкий взмах — запоминаем прошлые повороты для шлейфа копий
        private void UpdateGhosts()
        {
            float rotation = Projectile.velocity.ToRotation();
            _turnSpeed = _age > 1 ? Math.Abs(MathHelper.WrapAngle(rotation - _lastRotation)) : 0f;
            _lastRotation = rotation;

            for (int i = GhostCount - 1; i > 0; i--)
                _ghostRotations[i] = _ghostRotations[i - 1];
            _ghostRotations[0] = SpriteRotation(0f);
            _ghostCount = Math.Min(_ghostCount + 1, GhostCount);
        }

        // Цепь раскручивается на запуске и бежит быстрее, пока пилит
        private void AnimateChain()
        {
            int frameTicks = Cutting
                ? CuttingFrameTicks
                : (int)Math.Round(MathHelper.Lerp(StartFrameTicks, IdleFrameTicks, StartupProgress));
            if (++Projectile.frameCounter >= frameTicks)
            {
                Projectile.frameCounter = 0;
                Projectile.frame = (Projectile.frame + 1) % ChainFrames;
            }
        }

        private void UpdateLight()
        {
            float pulse = 0.8f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * GlowPulseSpeed);
            float tank = Cutting ? 1.3f : pulse;
            Lighting.AddLight(PixelToWorld(TankPixel), new Vector3(0.05f, 0.35f, 0.6f) * (tank + _pressure));
            if (Cutting)
                Lighting.AddLight(PixelToWorld(TipPixel), new Vector3(0.2f, 0.5f, 0.8f) * (1f + _pressure));
            if (ProjectionActive)
                Lighting.AddLight(_projectionEnd, new Vector3(0.3f, 0.7f, 1f) * _projectionGrow);
        }

        #region Частицы

        // Мотор взрёвывает: клуб пара из выхлопа и выплеск воды с цепи
        private void StartupBurst()
        {
            Vector2 exhaust = PixelToWorld(ExhaustPixel, shaken: true);
            for (int i = 0; i < 4; i++)
            {
                Vector2 velocity = -Aim * Main.rand.NextFloat(0.5f, 1.5f) - Vector2.UnitY * Main.rand.NextFloat(0.5f, 1.5f);
                SoAParticles.SpawnSmoke(exhaust, velocity, SteamColor, 8f, 26f, 0.35f, 40);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 velocity = (Aim * 2f + SpriteDown * 3f).RotatedByRandom(0.6f) * Main.rand.NextFloat(0.8f, 1.4f);
                SoAParticles.SpawnStreak(PixelToWorld(TipPixel, shaken: true), velocity, WaterGlow, 1.6f, 0.3f, 22);
            }
        }

        // Вход в перегрузку: бак сбрасывает пар, кончик выстреливает кольцом брызг
        private void OverdriveBurst()
        {
            Vector2 tip = PixelToWorld(TipPixel, shaken: true);
            Vector2 exhaust = PixelToWorld(ExhaustPixel, shaken: true);
            SoundEngine.PlaySound(SoundID.Splash with { Volume = 0.8f, Pitch = 0.3f }, tip);
            SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.7f, Pitch = 0.1f }, tip);

            SoAParticles.SpawnGlow(tip, Vector2.Zero, HotCore, 30f, 120f, 14);
            SoAParticles.AddLight(tip, new Color(120, 220, 255), 2.5f, 18);
            for (int i = 0; i < 16; i++)
            {
                Vector2 velocity = (MathHelper.TwoPi * i / 16f).ToRotationVector2() * Main.rand.NextFloat(4f, 8f);
                SoAParticles.SpawnStreak(tip, velocity, WaterGlow, 1.8f, 0.15f, 20);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector2 velocity = -Aim * Main.rand.NextFloat(1f, 2.5f) - Vector2.UnitY * Main.rand.NextFloat(1f, 2f);
                SoAParticles.SpawnSmoke(exhaust, velocity, SteamColor, 10f, 36f, 0.4f, 45);
            }
        }

        // Цепь швыряет воду: с кончика — по касательной вперёд-вниз, с нижней ветви — назад
        private void SpawnChainSpray()
        {
            float flow = WaterFlow;
            Vector2 tip = PixelToWorld(TipPixel, shaken: true);
            int tipDrops = (int)(flow * 2f) + (Main.rand.NextFloat() < flow * 2f % 1f ? 1 : 0);
            for (int i = 0; i < tipDrops; i++)
            {
                Vector2 velocity = (SpriteDown * Main.rand.NextFloat(2.5f, 6f) + Aim * Main.rand.NextFloat(1f, 4f))
                    .RotatedByRandom(0.3f);
                SoAParticles.SpawnStreak(tip, velocity, WaterGlow, 2.2f, 0.25f, 28);
            }

            // Водяная пыль у кончика — облачко, в котором тонет цепь
            if (Main.rand.NextFloat() < 0.5f * flow)
                SoAParticles.SpawnGlow(tip, Aim * 0.8f + SpriteDown * 0.6f, WaterGlow * 0.6f, 10f, 26f, 14);

            if (Main.rand.NextFloat() < 0.5f * flow)
            {
                Vector2 velocity = (-Aim * Main.rand.NextFloat(1.5f, 3f) + SpriteDown).RotatedByRandom(0.3f);
                SoAParticles.SpawnStreak(PixelToWorld(ChainBottomStartPixel, shaken: true), velocity, WaterGlow, 1.8f, 0.3f, 22);
            }

            // Под водой привод гонит пузыри вдоль полотна
            if (Owner.wet && Main.rand.NextFloat() < 0.3f * flow)
            {
                Vector2 along = Vector2.Lerp(PixelToWorld(BladeStartPixel), PixelToWorld(TipPixel), Main.rand.NextFloat());
                SoAParticles.SpawnBubble(along, new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), -1f),
                    Main.rand.NextFloat(3f, 6f), Color.White);
            }
        }

        // Проекция: астральная пыль вдоль неё и брызги там, где она бьёт — в стену веером,
        // в воздухе — облачком
        private void SpawnProjectionSpray()
        {
            Vector2 tip = PixelToWorld(TipPixel, shaken: true);
            for (int i = 0; i < 2; i++)
            {
                Vector2 along = Vector2.Lerp(tip, _projectionEnd, Main.rand.NextFloat());
                Vector2 drift = Aim.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloatDirection() * 0.8f + Aim * 0.5f;
                Color mote = Main.rand.NextBool() ? AstralViolet : WaterGlow;
                SoAParticles.SpawnGlow(along + Main.rand.NextVector2Circular(6f, 6f), drift, mote, 5f, 12f, 18);
            }

            if (_projectionHitWall)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector2 velocity = (-Aim).RotatedByRandom(1.2f) * Main.rand.NextFloat(3f, 8f);
                    SoAParticles.SpawnStreak(_projectionEnd, velocity, WaterGlow, 1.6f, 0.3f, 20);
                }
                if (Main.rand.NextBool(3))
                    SoAParticles.SpawnSmoke(_projectionEnd, -Aim * 0.5f, SteamColor, 8f, 24f, 0.25f, 30);
            }
            else if (Main.rand.NextBool(2))
            {
                SoAParticles.SpawnSmoke(_projectionEnd, Aim * 1.5f, SteamColor, 6f, 20f, 0.2f, 26);
            }
        }

        // Выхлоп: на воздухе — облачко пара назад-вверх, в воде — цепочка пузырей
        private void SpawnExhaust()
        {
            int interval = Cutting ? CuttingExhaustTicks : IdleExhaustTicks;
            if (_age % interval != 0)
                return;

            Vector2 exhaust = PixelToWorld(ExhaustPixel, shaken: true);
            if (Owner.wet)
            {
                for (int i = 0; i < 3; i++)
                    SoAParticles.SpawnBubble(exhaust, new Vector2(Main.rand.NextFloat(-0.5f, 0.5f), -Main.rand.NextFloat(1f, 2f)),
                        Main.rand.NextFloat(3f, 7f), Color.White);
                return;
            }

            Vector2 velocity = -Aim * Main.rand.NextFloat(0.4f, 1f) - Vector2.UnitY * Main.rand.NextFloat(0.6f, 1.2f);
            SoAParticles.SpawnSmoke(exhaust, velocity, SteamColor, 6f, Cutting ? 22f : 16f, 0.25f, 34);
        }

        // Искры и стружка из того тайла, в который вгрызается полотно: щепки от дерева, крошка от камня
        private void ContactEffects(Vector2 contact, Point tilePos)
        {
            Vector2 back = (HandPosition - contact).SafeNormalize(Vector2.Zero);
            int sparks = _overdrive ? 4 : 2;
            for (int i = 0; i < sparks; i++)
            {
                Vector2 velocity = back.RotatedByRandom(0.7f) * Main.rand.NextFloat(4f, 10f);
                SoAParticles.SpawnStreak(contact, velocity, SparkColor, 1.5f, 0.3f, 16);
            }
            if (Main.rand.NextBool(4))
                SoAParticles.AddLight(contact, new Color(150, 220, 255), 1.2f, 6);

            if (_age % TileDebrisTicks == 0)
            {
                Tile tile = Framing.GetTileSafely(tilePos.X, tilePos.Y);
                int dust = WorldGen.KillTile_MakeTileDust(tilePos.X, tilePos.Y, tile);
                if (dust >= 0 && dust < Main.maxDust)
                {
                    Dust chip = Main.dust[dust];
                    chip.position = contact;
                    chip.velocity = back.RotatedByRandom(0.8f) * Main.rand.NextFloat(2f, 5f);
                }
            }
        }

        #endregion

        #region Урон

        // Бьёт всё полотно, а в перегрузке — ещё и астральная проекция на всю длину
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float collisionPoint = 0f;
            Vector2 tip = PixelToWorld(TipPixel);
            if (Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                    HandPosition, tip, BladeHitWidth, ref collisionPoint))
            {
                return true;
            }

            return ProjectionDealsDamage && ProjectionActive && Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(),
                targetHitbox.Size(), tip, _projectionEnd, ProjectionHitWidth, ref collisionPoint);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Пила питается водой: по врагам, находящимся в жидкости, урон выше
            if (target.wet)
                modifiers.FinalDamage *= WetTargetDamageMultiplier;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            _cuttingTimer = CuttingGraceUpdates;
            _hitFlash = HitFlashTicks;
            if (Main.dedServ)
                return;

            Vector2 hitPoint = Vector2.Lerp(Projectile.Center, target.Center, 0.5f);
            Vector2 back = (HandPosition - hitPoint).SafeNormalize(Vector2.Zero);
            for (int i = 0; i < 8; i++)
            {
                Vector2 velocity = back.RotatedByRandom(0.9f) * Main.rand.NextFloat(4f, 11f);
                SoAParticles.SpawnStreak(hitPoint, velocity, SparkColor, 1.6f, 0.3f, 18);
            }

            // Разрезы: длинные быстрые штрихи поперёк цели вдоль полотна
            for (int i = 0; i < 3; i++)
            {
                Vector2 slash = Aim.RotatedBy(Main.rand.NextFloatDirection() * 0.25f) * Main.rand.NextFloat(10f, 16f);
                SoAParticles.SpawnStreak(target.Center + Main.rand.NextVector2Circular(8f, 8f), slash,
                    HotCore, 1.2f, 0f, 8, 2.6f);
            }

            // По мокрой цели — фонтан брызг
            if (target.wet)
            {
                for (int i = 0; i < 10; i++)
                {
                    Vector2 velocity = new(Main.rand.NextFloat(-4f, 4f), -Main.rand.NextFloat(2f, 6f));
                    SoAParticles.SpawnStreak(target.Center, velocity, WaterGlow, 2f, 0.3f, 26);
                }
            }
        }

        #endregion

        #region Отрисовка

        // Батч не переключаем: пила держится через heldProj и рисуется внутри отрисовки игрока.
        // Свечение — цвет с A = 0, в обычном батче это уже аддитив
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Rectangle frame = tex.Frame(1, ChainFrames, 0, Projectile.frame);
            Vector2 origin = FacingDirection == 1 ? GripPixel : new Vector2(frame.Width - GripPixel.X, GripPixel.Y);
            SpriteEffects effects = FacingDirection == 1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Vector2 gfx = new(0f, Owner.gfxOffY);
            Vector2 drawPos = HandPosition + _shakeOffset + gfx - Main.screenPosition;
            float rotation = SpriteRotation(_shakeRotation);

            // Лента воды рисуется сразу в устройство и ложится под спрайты батча — выглядывает
            // из-под зубьев. Проекция — до пилы, чтобы настоящий кончик лёг поверх её начала
            DrawWaterChain(gfx);
            if (ProjectionActive)
                DrawAstralProjection(tex, frame, gfx);

            DrawGhosts(tex, frame, origin, effects, gfx);

            // Сопло всегда в водяном ореоле; с давлением он растёт и белеет
            Vector2 tipGlowPos = PixelToWorld(TipPixel, shaken: true) + gfx;
            Color tipGlow = Color.Lerp(WaterGlow, HotCore, _pressure) * ((0.35f + 0.65f * _pressure) * StartupProgress);
            SoAVfx.DrawTintedGlow(Main.spriteBatch, tipGlowPos, new Vector2(28f + 22f * _pressure), tipGlow);

            Main.EntitySpriteDraw(tex, drawPos, frame, lightColor, rotation, origin, Projectile.scale, effects, 0);

            // Окошко бака: пилит или под давлением — горит ярче
            Texture2D glow = ModContent.Request<Texture2D>(GlowTexturePath).Value;
            float pulse = 0.8f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * GlowPulseSpeed);
            float glowStrength = Cutting ? 1f : pulse * 0.85f * MathHelper.Lerp(0.5f, 1f, StartupProgress);
            Main.EntitySpriteDraw(glow, drawPos, frame, Color.White * glowStrength, rotation, origin,
                Projectile.scale, effects, 0);

            // Зубья раскаляются водой по мере давления; в перегрузке — с ореолом
            if (_pressure > 0.05f)
            {
                Texture2D teeth = TeethMask(tex);
                Color teethColor = SoAVfx.Additive(Color.Lerp(WaterGlow, HotCore, _pressure)) * (_pressure * 0.9f);
                Main.EntitySpriteDraw(teeth, drawPos, frame, teethColor, rotation, origin, Projectile.scale, effects, 0);
                if (_overdrive)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 offset = (MathHelper.PiOver2 * i).ToRotationVector2() * 2f;
                        Main.EntitySpriteDraw(teeth, drawPos + offset, frame, WaterGlow * 0.35f, rotation, origin,
                            Projectile.scale, effects, 0);
                    }
                }
            }

            // Вспышка всего силуэта при ударе
            if (_hitFlash > 0)
            {
                Texture2D silhouette = SilhouetteCache.Get(tex);
                Color flash = SoAVfx.Additive(Color.White) * (_hitFlash / (float)HitFlashTicks * 0.8f);
                Main.EntitySpriteDraw(silhouette, drawPos, frame, flash, rotation, origin, Projectile.scale, effects, 0);
            }
            return false;
        }

        // Вода бежит по цепи: струи шейдера Tide текут от головы ленты к хвосту — поверху вперёд,
        // вокруг кончика и понизу назад, ровно как движется сама цепь
        private void DrawWaterChain(Vector2 gfx)
        {
            Span<Vector2> path = stackalloc Vector2[ChainLoopPixels.Length];
            for (int i = 0; i < ChainLoopPixels.Length; i++)
                path[i] = PixelToWorld(ChainLoopPixels[i], shaken: true) + gfx;

            float flow = WaterFlow * StartupProgress;
            float width = WaterRibbonWidth * (0.7f + 0.6f * flow);
            SoATrail.Draw(path,
                progress => width * (0.4f + 0.6f * (float)Math.Sin(MathHelper.Pi * progress)),
                progress => WaterGlow * flow,
                TrailStyle.Tide);
        }

        // Астральная проекция шины: на полном разогреве полотно «продолжается» до точки,
        // где считается урон. Собирается из кусков самого спрайта — отрезок шины ровно в два
        // шага зубьев (стыков не видно) и скруглённый кончик; зубья бегут в такт настоящей цепи.
        // Призрачное тело, светящиеся зубья, по краям голубой и фиолетовый двойники
        private void DrawAstralProjection(Texture2D tex, Rectangle frame, Vector2 gfx)
        {
            Texture2D body = SilhouetteCache.Get(tex);
            Texture2D teeth = TeethMask(tex);
            Vector2 start = PixelToWorld(ProjectionStartPixel, shaken: true) + gfx;
            float length = Vector2.Distance(PixelToWorld(ProjectionStartPixel), _projectionEnd);
            float rotation = Projectile.velocity.ToRotation();
            // Пила, отражённая по горизонтали и повёрнутая на π, — это спрайт, перевёрнутый
            // по вертикали: зубья проекции остаются на тех же сторонах, что у настоящей шины
            bool flipped = FacingDirection == -1;
            SpriteEffects effects = flipped ? SpriteEffects.FlipVertically : SpriteEffects.None;
            Vector2 side = Aim.RotatedBy(MathHelper.PiOver2) * AstralTwinOffset;
            float time = Main.GlobalTimeWrappedHourly;

            float drawn = 0f;
            for (int index = 0; drawn < length - 0.5f; index++)
            {
                float remaining = length - drawn;
                bool isTip = remaining <= BarTipSource.Width;
                Rectangle piece = isTip ? BarTipSource : BarSegmentSource;
                int width = isTip
                    ? (int)Math.Ceiling(remaining)
                    : (int)Math.Min(piece.Width, remaining - BarTipSource.Width);
                if (width <= 0)
                    width = (int)Math.Ceiling(remaining);

                var source = new Rectangle(frame.X + piece.X, frame.Y + piece.Y, width, piece.Height);
                var origin = new Vector2(0f, flipped ? piece.Height - BarAxisRow : BarAxisRow);
                Vector2 pos = start + Aim * drawn - Main.screenPosition;

                // К дальнему концу проекция бледнеет; каждый кусок мерцает в своей фазе
                float flicker = 0.8f + 0.2f * (float)Math.Sin(time * 25f + index * 1.3f);
                float alpha = _projectionGrow * (1f - 0.45f * drawn / Math.Max(length, 1f)) * flicker;

                Main.EntitySpriteDraw(teeth, pos + side, source, AstralViolet * (0.35f * alpha), rotation, origin, 1f, effects, 0);
                Main.EntitySpriteDraw(teeth, pos - side, source, WaterGlow * (0.35f * alpha), rotation, origin, 1f, effects, 0);
                Main.EntitySpriteDraw(body, pos, source, AstralBody * (0.22f * alpha), rotation, origin, 1f, effects, 0);
                Main.EntitySpriteDraw(teeth, pos, source, Color.Lerp(WaterGlow, HotCore, 0.6f) * (0.95f * alpha),
                    rotation, origin, 1f, effects, 0);

                drawn += width;
            }

            // Ореол там, где проекция бьёт
            Vector2 end = _projectionEnd + gfx;
            SoAVfx.DrawTintedGlow(Main.spriteBatch, end, new Vector2(_projectionHitWall ? 46f : 30f),
                Color.Lerp(WaterGlow, AstralViolet, 0.4f) * (0.5f * _projectionGrow));
        }

        // Светлые пиксели спрайта — зубья цепи (и блик окошка, это не мешает)
        private static Texture2D TeethMask(Texture2D tex)
            => SilhouetteCache.Get(tex, "AquasawTeeth", static c => c.R > 150 && c.G > 150 && c.B > 180);

        // Шлейф взмаха: голубые копии пилы на прошлых поворотах, тем ярче, чем резче ведёшь
        private void DrawGhosts(Texture2D tex, Rectangle frame, Vector2 origin, SpriteEffects effects, Vector2 gfx)
        {
            float strength = MathHelper.Clamp((_turnSpeed - GhostMinTurnSpeed) / (GhostFullTurnSpeed - GhostMinTurnSpeed), 0f, 1f);
            if (strength <= 0f)
                return;

            Texture2D silhouette = SilhouetteCache.Get(tex);
            Vector2 drawPos = HandPosition + gfx - Main.screenPosition;
            for (int i = _ghostCount - 1; i >= 1; i--)
            {
                float fade = 1f - i / (float)GhostCount;
                Main.EntitySpriteDraw(silhouette, drawPos, frame, WaterGlow * (fade * fade * 0.45f * strength),
                    _ghostRotations[i], origin, Projectile.scale, effects, 0);
            }
        }

        #endregion
    }
}
