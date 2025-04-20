using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace SoA.Content.Projectiles
{
    public class ShardedSpearProjectile : ModProjectile
    {
        private int chargeTime = 180; // Время зарядки (1 секунда)
        
        public override void SetDefaults()
        {
            Projectile.damage = 90;
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = chargeTime + 10;

            Projectile.aiStyle = ProjAIStyleID.ShortSword;
        }

        public override void AI()
        {
            // Пульсация
            float progress = (float)Projectile.timeLeft / chargeTime;
            Projectile.scale = 1f + 0.3f * (1f - progress);
            
            // Частицы воды
            if (Main.rand.NextBool(3))
            {
                Dust dust = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                dust.velocity *= 0.5f;
                dust.scale = 1.2f;
            }
            
            // После зарядки создаёт луч
            if (Projectile.timeLeft == 10)
            {
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, 
                    Projectile.velocity * 10f, ModContent.ProjectileType<ShardedSpearBeam>(), Projectile.damage, Projectile.knockBack, Projectile.owner);
            }
        }
    }
    
    public class ShardedSpearBeam : ModProjectile
    {
        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 40;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 20;
        }

        
        public override void AI()
        {
            Projectile.velocity *= 1.05f;
            
            // Эффект воды
            if (Main.rand.NextBool(2))
            {
                Dust dust = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                dust.velocity *= 0.3f;
                dust.scale = 1.5f;
            }
        }
    }
}
