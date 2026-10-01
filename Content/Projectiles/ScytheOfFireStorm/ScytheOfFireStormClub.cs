using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Weapons;
using SoA.Content.Items.Weapons;

namespace SoA.Content.Projectiles
{
    // Коса огненной бури в руке. Цикл замах → зарядка → взмах — в ClubProjectile; здесь:
    //  - нагрев лезвия по трём ступеням: тёмно-красный → оранжевый → бело-жёлтый, угли и марево;
    //  - на середине взмаха с кончика лезвия срывается то, на что хватило заряда и маны:
    //    без ступени — огненный серп, дальше торнадо всё крупнее;
    //  - лезвие, прошедшее сквозь своё торнадо, подпитывает его и разворачивает к курсору
    public class ScytheOfFireStormClub : ClubProjectile
    {
        // Доля взмаха, на которой лезвие впереди игрока — отсюда и летит заряд
        private const float ReleaseAt = 0.45f;
        private const float FeedLineWidth = 24f;
        private const float CrescentSpeed = 13f;
        private const float CrescentDamageMult = 0.7f;
        private const float MinTornadoSpeed = 4.5f;
        private const float MaxTornadoSpeed = 12f;
        private const float MinTornadoDamageMult = 0.6f;
        private const float MaxTornadoDamageMult = 1.5f;

        // Сверх маны за клик: цена ступени. Не хватает маны — выходит ступень пониже
        private static readonly int[] StageManaCost = { 0, 6, 10, 14 };
        // Заряд торнадо (его размер и сила) по ступени
        private static readonly float[] StageTornadoCharge = { 0f, 0.25f, 0.6f, 1f };

        private static readonly Color Ember = new(150, 35, 10);
        private static readonly Color Flame = new(255, 120, 25);
        private static readonly Color WhiteHot = new(255, 235, 170);
        private static readonly Color HazeColor = new(70, 55, 50);

        private int _releaseStage;
        private bool _released;
        private readonly HashSet<int> _fedThisSwing = new();

        public override string Texture => "SoA/Content/Items/Weapons/ScytheOfFireStorm";

        // Спрайт 112×112: хват на нижней трети древка, стык лезвия с древком, кончик лезвия
        protected override Vector2 GripPixel => new(36f, 72f);
        protected override Vector2 HeadPixel => new(70f, 14f);
        protected override Vector2 TipPixel => new(108f, 50f);
        protected override float DrawScale => 0.85f;
        protected override float HitWidth => 30f;
        protected override bool SmashesGround => false;

        protected override int WindupTicks => 14;
        protected override int ChargeTicks => 60;
        protected override int ChargeStages => 3;
        protected override int SwingTicks => 16;
        protected override float MinDamageMult => 0.5f;
        protected override float FullDamageMult => 1f;

        protected override Color ChargeColor => HeatColor(Charge);
        protected override float RimStrength => State == ClubState.Charging ? Charge * Charge : 0f;
        protected override float OverlayStrength => State == ClubState.Smashed ? 0f : 0.15f + 0.6f * Charge;
        protected override TrailStyle? SwingTrailStyle => TrailStyle.Magma;
        protected override float SwingTrailWidth => 26f;
        protected override SoundStyle SwingSound => SoundID.Item71 with { Pitch = -0.2f };

        protected override void SafeSetDefaults() => Projectile.DamageType = DamageClass.Magic;

        // Светится только лезвие (и навершие): тёплые пиксели. Древко остаётся под светом мира
        protected override Texture2D GlowMask(Texture2D texture)
            => SilhouetteCache.Get(texture, "ScytheBlade", static c => c.R > 150);

        protected override Color SwingTrailColorAt(float progress) => TrailStyle.MagmaBody(progress, Charge);

        private static Color HeatColor(float heat)
            => heat < 0.5f
                ? Color.Lerp(Ember, Flame, heat * 2f)
                : Color.Lerp(Flame, WhiteHot, (heat - 0.5f) * 2f);

        private Vector2 RandomBladePoint() => PixelToWorld(Vector2.Lerp(HeadPixel, TipPixel, Main.rand.NextFloat()));

        #region Зарядка

        protected override void OnChargeTick(Player owner)
        {
            if (Main.dedServ || Charge <= 0f)
                return;

            Vector2 blade = RandomBladePoint();
            Lighting.AddLight(blade, HeatColor(Charge).ToVector3() * (0.4f + 0.8f * Charge));

            // Угли срываются с лезвия и уходят вверх
            if (Main.rand.NextFloat() < 0.25f + 0.6f * Charge)
            {
                var velocity = new Vector2(Main.rand.NextFloat(-0.8f, 0.8f), -Main.rand.NextFloat(1.5f, 3.5f));
                SoAParticles.SpawnStreak(blade, velocity, SoAVfx.Additive(HeatColor(Charge)), 1.5f, -0.02f, 24, 2f);
            }

            // Марево: редкие прозрачные клубы над раскалённым лезвием
            if (Main.rand.NextFloat() < 0.3f * Charge)
            {
                SoAParticles.SpawnSmoke(blade, new Vector2(0f, -Main.rand.NextFloat(0.6f, 1.2f)),
                    HazeColor, 8f, 26f, 0.12f + 0.1f * Charge, 40);
            }
        }

        protected override void OnChargeStage(Player owner, int stage)
        {
            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item20 with { Volume = 0.6f, Pitch = -0.3f + 0.2f * stage }, Projectile.Center);
            for (int i = 0; i < 8 + 5 * stage; i++)
            {
                Dust d = Dust.NewDustPerfect(RandomBladePoint(), stage == ChargeStages ? DustID.InfernoFork : DustID.Torch,
                    Main.rand.NextVector2Circular(3f, 3f) - Vector2.UnitY);
                d.noGravity = true;
                d.scale = 1f + 0.3f * stage;
            }
        }

        #endregion

        #region Взмах

        protected override void OnSwingStart(Player owner)
        {
            _released = false;
            _fedThisSwing.Clear();
            if (Projectile.owner != Main.myPlayer)
                return;

            // Ступень оплачивается при отпускании; на что не хватило маны — то и выйдет
            _releaseStage = ChargeStage;
            while (_releaseStage > 0 && !owner.CheckMana(owner.HeldItem, StageManaCost[_releaseStage], pay: true))
                _releaseStage--;
        }

        protected override void OnSwingTick(Player owner, float progress)
        {
            if (Projectile.owner != Main.myPlayer)
                return;

            if (!_released && progress >= ReleaseAt)
            {
                _released = true;
                Release(owner);
            }
            FeedTornadoes(owner);
        }

        // На середине взмаха кончик лезвия бывает уже в земле. Вихрь, рождённый в блоке,
        // первым же столкновением принимал его за стену и гас под ногами игрока —
        // поэтому идём от игрока к кончику и берём последнюю точку, где хитбокс вихря свободен
        private static Vector2 ClearSpawnPoint(Vector2 from, Vector2 to)
        {
            const int steps = 8;
            float half = FireTornadoProjectile.PhysicalSize / 2f;
            Vector2 clear = from;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
                if (Collision.SolidCollision(point - new Vector2(half), FireTornadoProjectile.PhysicalSize,
                        FireTornadoProjectile.PhysicalSize))
                {
                    break;
                }
                clear = point;
            }
            return clear;
        }

        private void Release(Player owner)
        {
            Vector2 tip = ClearSpawnPoint(owner.MountedCenter, PixelToWorld(TipPixel));
            Vector2 aim = (Main.MouseWorld - owner.MountedCenter).SafeNormalize(Vector2.UnitX * Direction);
            var source = Projectile.GetSource_FromThis();

            if (_releaseStage == 0)
            {
                Projectile.NewProjectile(source, tip, aim * CrescentSpeed, ModContent.ProjectileType<FireCrescentProjectile>(),
                    (int)(Projectile.damage * CrescentDamageMult), Projectile.knockBack * 0.5f, Projectile.owner);
                return;
            }

            KillOldestTornadoIfFull(owner);
            float tornadoCharge = StageTornadoCharge[_releaseStage];
            int index = Projectile.NewProjectile(source, tip, aim * MathHelper.Lerp(MinTornadoSpeed, MaxTornadoSpeed, tornadoCharge),
                ModContent.ProjectileType<FireTornadoProjectile>(),
                (int)(Projectile.damage * MathHelper.Lerp(MinTornadoDamageMult, MaxTornadoDamageMult, tornadoCharge)),
                Projectile.knockBack, Projectile.owner, tornadoCharge);

            // Только что рождённый вихрь тем же взмахом не подпитываем
            if (index < Main.maxProjectiles)
                _fedThisSwing.Add(Main.projectile[index].identity);

            SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.9f, Pitch = -0.2f }, tip);
            for (int i = 0; i < 10 + 4 * _releaseStage; i++)
            {
                Dust d = Dust.NewDustPerfect(tip, DustID.InfernoFork, aim.RotatedByRandom(0.5f) * Main.rand.NextFloat(2f, 7f));
                d.noGravity = true;
                d.scale = 1f + 0.3f * _releaseStage;
            }
        }

        // Лимит без блокировки косы: новый вихрь гасит самый старый
        private static void KillOldestTornadoIfFull(Player owner)
        {
            int tornadoType = ModContent.ProjectileType<FireTornadoProjectile>();
            if (owner.ownedProjectileCounts[tornadoType] < ScytheOfFireStorm.MaxActiveTornadoes)
                return;

            Projectile oldest = null;
            foreach (Projectile proj in Main.ActiveProjectiles)
            {
                if (proj.owner == owner.whoAmI && proj.type == tornadoType
                    && (oldest == null || proj.timeLeft < oldest.timeLeft))
                {
                    oldest = proj;
                }
            }
            oldest?.Kill();
        }

        private void FeedTornadoes(Player owner)
        {
            Vector2 grip = PixelToWorld(GripPixel);
            Vector2 tip = PixelToWorld(TipPixel);
            int tornadoType = ModContent.ProjectileType<FireTornadoProjectile>();

            foreach (Projectile proj in Main.ActiveProjectiles)
            {
                if (proj.owner != owner.whoAmI || proj.type != tornadoType || _fedThisSwing.Contains(proj.identity)
                    || proj.ModProjectile is not FireTornadoProjectile tornado || !tornado.CanBeFed)
                {
                    continue;
                }

                Rectangle area = tornado.VortexArea;
                float collisionPoint = 0f;
                if (!Collision.CheckAABBvLineCollision(area.TopLeft(), area.Size(), grip, tip, FeedLineWidth, ref collisionPoint))
                    continue;

                _fedThisSwing.Add(proj.identity);
                tornado.Feed(Main.MouseWorld - area.Center.ToVector2());
            }
        }

        #endregion
    }
}
