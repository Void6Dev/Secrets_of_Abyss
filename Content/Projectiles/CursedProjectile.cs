using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Projectiles
{
    public class CursedProjectile : ModProjectile
    {
        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.aiStyle = 0; // Используем собственное поведение
            Projectile.DamageType = DamageClass.Magic;
            Projectile.friendly = true;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 600;
            Projectile.light = 0.5f; // Легкое свечение (опционально)
        }

        public override void AI()
        {
            // Поиск ближайшего врага
            NPC target = FindClosestNPC(600f);

            if (target != null)
            {
                // Вектор направления к врагу
                Vector2 direction = target.Center - Projectile.Center;
                direction.Normalize(); // Нормализация вектора

                // Обновляем скорость и направление снаряда
                float speed = 10f; // Скорость снаряда
                Projectile.velocity = (Projectile.velocity * 0.95f) + (direction * speed * 0.05f);

                // Опционально: добавить эффект вращения или поворота
                Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            }
        }

        private NPC FindClosestNPC(float maxDistance)
        {
            NPC closestNPC = null;
            float closestDistance = maxDistance;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && !npc.friendly && !npc.dontTakeDamage)
                {
                    float distance = Vector2.Distance(Projectile.Center, npc.Center);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestNPC = npc;
                    }
                }
            }

            return closestNPC;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Рисование спрайта снаряда
            return true;
        }
    }
}
