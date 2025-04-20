using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Microsoft.Xna.Framework;
using System;
using SoA.Content.Projectiles;
using SoA.Content.Buffs;

namespace SoA.Content.Items.Accessories.UseAccessories
{
    public class COMusage : ModPlayer
    {
        public bool CurseOfMe;
        private float lastLaserTime = -7f; 

        public override void ResetEffects()
        {
            CurseOfMe = false;
        }

        public override void OnHitByNPC(NPC npc, Player.HurtInfo hurtInfo)
        {
            base.OnHitByNPC(npc, hurtInfo);
            if (CurseOfMe)
            {
                HandleLaserCooldown();
            }
        }

        public override void OnHitByProjectile(Projectile proj, Player.HurtInfo hurtInfo)
        {
            base.OnHitByProjectile(proj, hurtInfo);
            if (CurseOfMe)
            {
                HandleLaserCooldown();
            }
        }

        private void HandleLaserCooldown()
        {
            float currentTime = Main.GameUpdateCount / 60f; 
            if (currentTime - lastLaserTime >= 5f) 
            {
                int numProjectiles = 5; 
                    for (int i = 0; i < numProjectiles; i++)
                    {
                        FireCursedProjectile();
                    }
                    lastLaserTime = currentTime; 
            }
            {
                float remainingTime = 5f - (currentTime - lastLaserTime);
                int buffDuration = (int)(remainingTime * 60);
                Player.AddBuff(ModContent.BuffType<CoM_debaff>(), buffDuration);
            }
        }

        private void FireCursedProjectile()
        {
            NPC target = FindClosestNPC(600f);
            if (target != null)
            {
                Vector2 direction = target.Center - Player.Center;
                direction.Normalize();

                int damage = (int)(100 * Player.GetDamage(DamageClass.Magic).Multiplicative);
                Projectile.NewProjectile(Player.GetSource_Accessory(null), Player.Center, direction * 10f, ModContent.ProjectileType<CursedProjectile>(), damage, 5f, Player.whoAmI);
            }
        }

        private NPC FindClosestNPC(float maxDetectDistance)
        {
            NPC closestNPC = null;
            float closestDistance = maxDetectDistance;

            foreach (NPC npc in Main.npc)
            {
                if (npc.active && !npc.friendly && npc.lifeMax > 5 && !npc.dontTakeDamage)
                {
                    float distance = Vector2.Distance(Player.Center, npc.Center);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestNPC = npc;
                    }
                }
            }

            return closestNPC;
        }

        public override void UpdateBadLifeRegen()
        {
            if (CurseOfMe)
            {
                Player.lifeRegen = Math.Min(Player.lifeRegen, -3); 
            }
        }
    }
}
