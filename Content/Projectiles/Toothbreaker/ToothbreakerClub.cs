using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Weapons;
using SoA.Content.Buffs;
using SoA.Content.Items.Weapons;

namespace SoA.Content.Projectiles
{
    // Зубодробилка в руке. Цикл замах → зарядка → удар — в ClubProjectile; здесь приливные
    // эффекты: капли стягиваются к бойку, водяной серп на ударе, полный заряд оглушает
    // и бьёт об землю ToothbreakerShockwave
    public class ToothbreakerClub : ClubProjectile
    {
        private static readonly Color TideGlow = new(120, 205, 255, 0);

        public override string Texture => Toothbreaker.TexturePath;

        // Точки спрайта 82×76: хват на нижней четверти рукояти, центр бойка
        // и середина плоской грани, которой молот бьёт вниз
        protected override Vector2 GripPixel => new(21f, 55f);
        protected override Vector2 HeadPixel => new(62f, 12f);
        protected override Vector2 FacePixel => new(78f, 19f);
        protected override float DrawScale => 1.15f;

        protected override int WindupTicks => 18;
        protected override int ChargeTicks => 36;
        protected override int SwingTicks => 18;

        protected override Color ChargeColor => TideGlow;
        protected override Color SmashDustColor => SoAVfx.TideSand;
        protected override TrailStyle? SwingTrailStyle => TrailStyle.Tide;
        protected override float SwingTrailWidth => 20f;

        protected override void OnChargeTick(Player owner)
        {
            if (Main.dedServ || FullCharge)
                return;

            Vector2 head = PixelToWorld(HeadPixel);
            Lighting.AddLight(head, new Vector3(0.15f, 0.45f, 0.6f) * Charge);
            if (Charge > 0f && Main.rand.NextBool(3))
            {
                Vector2 spawnOffset = Main.rand.NextVector2CircularEdge(30f, 30f);
                Dust d = Dust.NewDustPerfect(head + spawnOffset, DustID.Water, -spawnOffset * 0.1f);
                d.noGravity = true;
                d.scale = 0.8f + Charge * 0.6f;
            }
        }

        protected override void OnChargeStage(Player owner, int stage)
        {
            if (Main.dedServ)
                return;

            Vector2 head = PixelToWorld(HeadPixel);
            for (int i = 0; i < 14; i++)
            {
                Dust d = Dust.NewDustPerfect(head, DustID.Water, Main.rand.NextVector2Circular(3.5f, 3.5f));
                d.noGravity = true;
                d.scale = 1.3f;
            }
        }

        protected override void OnSmash(Player owner, Vector2 face)
        {
            if (FullCharge && Projectile.owner == Main.myPlayer)
            {
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), face, Vector2.Zero,
                    ModContent.ProjectileType<ToothbreakerShockwave>(),
                    (int)(Projectile.damage * ToothbreakerShockwave.DamageMult), Projectile.knockBack,
                    Projectile.owner);
            }
        }

        protected override void OnClubHit(NPC target)
        {
            if (FullCharge)
                TideStunDebuff.Apply(target);
        }
    }
}
