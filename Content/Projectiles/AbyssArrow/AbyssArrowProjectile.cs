using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using SoA.Common.Utils;

namespace SoA.Content.Projectiles.AbyssArrow
{
    public class AbyssArrowProjectile : ModProjectile
    {
        private const float HuntRange = 300f;
        private const float TurnRate = 0.06f;
        private const float LandGravity = 0.18f;
        private const float MaxFallSpeed = 16f;
        private const int GravityDelayTicks = 12;
        private const int Lifetime = 600;
        private ref float CruiseSpeed => ref Projectile.ai[0];
        private ref float AgeTicks => ref Projectile.ai[1];

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Ranged;
            Projectile.arrow = true;
            Projectile.penetrate = 1;
            Projectile.timeLeft = Lifetime;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = true;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            if (Projectile.velocity == Vector2.Zero)
                return;

            if (CruiseSpeed <= 0f)
                CruiseSpeed = Projectile.velocity.Length();

            AgeTicks++;

            if (SoACombat.IsInWater(Projectile.Center))
                UpdateUnderwater();
            else
                UpdateInAir();

            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            Lighting.AddLight(Projectile.Center, 0.1f, 0.2f, 0.4f);
            SpawnTrailDust();
        }

        // Под водой гравитации нет вообще, а вектор плавно уводится на добычу
        private void UpdateUnderwater()
        {
            NPC prey = SoACombat.FindClosestNPC(Projectile.Center, HuntRange, requireLineOfSight: true);

            if (prey != null)
            {
                Vector2 toPrey = Vector2.Normalize(prey.Center - Projectile.Center);
                Vector2 heading = Vector2.Normalize(Projectile.velocity);

                Projectile.velocity = Vector2.Normalize(Vector2.Lerp(heading, toPrey, TurnRate)) * CruiseSpeed;
            }
            else
            {
                Projectile.velocity = Vector2.Normalize(Projectile.velocity) * CruiseSpeed;
            }
        }

        private void UpdateInAir()
        {
            if (AgeTicks <= GravityDelayTicks)
                return;

            Projectile.velocity.Y += LandGravity;

            if (Projectile.velocity.Y > MaxFallSpeed)
                Projectile.velocity.Y = MaxFallSpeed;
        }

        private void SpawnTrailDust()
        {
            bool underwater = SoACombat.IsInWater(Projectile.Center);

            if (!Main.rand.NextBool(underwater ? 2 : 4))
                return;

            Dust trail = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                underwater ? DustID.Water : DustID.DungeonWater,
                -Projectile.velocity.X * 0.1f, -Projectile.velocity.Y * 0.1f);
            trail.noGravity = true;
            trail.scale = Main.rand.NextFloat(0.6f, 1.1f);
            trail.alpha = 100;
        }

        // Ванильный ИИ стрелы озвучивал удар о блок сам, кастомный — нет
        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            SoundEngine.PlaySound(SoundID.Dig, Projectile.position);
            return true;
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
