using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Projectiles
{
    // Выпад Королевского трезубца (стандартная схема копья из ExampleMod).
    // Спрайт — ванильный Trident (placeholder до собственного арта).
    public class RoyalTridentProjectile : ModProjectile
    {
        private const float HoldoutRangeMin = 24f;
        private const float HoldoutRangeMax = 104f;
        private const float WetTargetDamageMultiplier = 1.25f;

        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Trident;

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 18;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.scale = 1.2f;
            Projectile.ownerHitCheck = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            int duration = player.itemAnimationMax;
            player.heldProj = Projectile.whoAmI;

            if (Projectile.timeLeft > duration)
                Projectile.timeLeft = duration;

            Projectile.velocity = Vector2.Normalize(Projectile.velocity);

            // Выпад вперёд первую половину анимации, возврат — вторую
            float halfDuration = duration * 0.5f;
            float progress = Projectile.timeLeft < halfDuration
                ? Projectile.timeLeft / halfDuration
                : (duration - Projectile.timeLeft) / halfDuration;

            Projectile.Center = player.MountedCenter + Vector2.SmoothStep(
                Projectile.velocity * HoldoutRangeMin, Projectile.velocity * HoldoutRangeMax, progress);

            if (Projectile.spriteDirection == -1)
                Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.ToRadians(45f);
            else
                Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.ToRadians(135f);

            // Водяная пыль на острие
            if (Main.rand.NextBool(3))
            {
                Vector2 tip = player.MountedCenter + Projectile.velocity * (HoldoutRangeMax * progress + 10f);
                Dust d = Dust.NewDustPerfect(tip, DustID.Water,
                    Projectile.velocity * Main.rand.NextFloat(1f, 3f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.8f, 1.3f);
            }

            return false; // ванильный ИИ не нужен
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (target.wet)
                modifiers.FinalDamage *= WetTargetDamageMultiplier;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Королевский укол выбивает из врага фонтан пузырей
            for (int i = 0; i < 10; i++)
            {
                Dust bubble = Dust.NewDustDirect(target.position, target.width, target.height,
                    DustID.Water, Main.rand.NextFloat(-2f, 2f), -Main.rand.NextFloat(1f, 4f));
                bubble.noGravity = true;
                bubble.scale = Main.rand.NextFloat(1f, 1.5f);
            }
        }

    }
}
