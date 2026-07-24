using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace SoA.Content.Projectiles
{
    // Стрела бездны: под водой не тонет и не замедляется, светится тёмно-синим.
    // Спрайт — ванильная Frostburn Arrow (placeholder до собственного арта).
    public class AbyssArrowProjectile : ModProjectile
    {
        private const float WetTargetDamageMultiplier = 1.2f;

        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.FrostburnArrow;

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Ranged;
            Projectile.arrow = true;
            Projectile.ignoreWater = true;
            Projectile.aiStyle = ProjAIStyleID.Arrow;
            AIType = ProjectileID.WoodenArrowFriendly;
        }

        public override void AI()
        {
            // Ванильный ИИ стрелы включает гравитацию после 15 тиков (ai[0]);
            // в воде компенсируем её — стрела летит по прямой
            if (Projectile.wet && Projectile.ai[0] > 15f)
                Projectile.velocity.Y -= 0.1f;

            Lighting.AddLight(Projectile.Center, 0.1f, 0.2f, 0.4f);

            if (Main.rand.NextBool(Projectile.wet ? 2 : 4))
            {
                Dust trail = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    Projectile.wet ? DustID.Water : DustID.DungeonWater,
                    -Projectile.velocity.X * 0.1f, -Projectile.velocity.Y * 0.1f);
                trail.noGravity = true;
                trail.scale = Main.rand.NextFloat(0.6f, 1.1f);
                trail.alpha = 100;
            }
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (target.wet)
                modifiers.FinalDamage *= WetTargetDamageMultiplier;
        }

        public override void OnKill(int timeLeft)
        {
            for (int i = 0; i < 8; i++)
            {
                Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    DustID.DungeonWater, Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.8f, 1.3f);
            }
        }
    }
}
