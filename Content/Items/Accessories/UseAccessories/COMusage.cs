using Terraria;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using System;
using SoA.Content.Projectiles;
using SoA.Content.Buffs;

namespace SoA.Content.Items.Accessories.UseAccessories
{
    public class COMusage : ModPlayer
    {
        private const float BurstCooldownSeconds = 5f;
        private const int BurstShotCount = 5;
        private const int TicksBetweenShots = 6;
        private const int BurstBaseDamage = 100;

        public bool CurseOfMe;
        private float lastLaserTime = -BurstCooldownSeconds - 1f;
        private int burstShotsLeft = 0;
        private int burstTimer = 0;
        private int burstIndex = 0;
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

            if (currentTime - lastLaserTime >= BurstCooldownSeconds)
            {
                burstShotsLeft = BurstShotCount;
                burstTimer = 0;
                burstIndex = 0;
                lastLaserTime = currentTime;
                // CoMDebuff — визуальный индикатор перезарядки залпа, эффекта не имеет
                Player.AddBuff(ModContent.BuffType<CoMDebuff>(), (int)(BurstCooldownSeconds * 60));
            }
        }

        public override void PostUpdate()
        {
            // Снаряды спавнит только клиент-владелец, дальше их синхронизирует сам tML
            if (Player.whoAmI != Main.myPlayer)
                return;

            if (burstShotsLeft > 0)
            {
                burstTimer++;

                if (burstTimer >= TicksBetweenShots)
                {
                    FireCursedProjectileBurst(burstIndex);

                    burstIndex++;
                    burstShotsLeft--;
                    burstTimer = 0;
                }
            }
        }

        private void FireCursedProjectileBurst(int index)
        {
            NPC target = FindClosestNPC(600f);

            float angleStep = MathHelper.TwoPi / 5f; // 5 снарядов по кругу
            float angle = angleStep * index;

            // 👉 clockwise вращение
            Vector2 offset = angle.ToRotationVector2();

            Vector2 spawnPos = Player.Center + offset * 20f;

            Vector2 direction;

            if (target != null)
            {
                direction = target.Center - spawnPos;
                direction.Normalize();
            }
            else
            {
                direction = offset; // если нет цели — стреляем по кругу
            }

            // ApplyTo учитывает и аддитивные, и мультипликативные бонусы магического урона
            int damage = (int)Player.GetTotalDamage(DamageClass.Magic).ApplyTo(BurstBaseDamage);

            Projectile.NewProjectile(
                Player.GetSource_Accessory(null),
                spawnPos,
                direction * 10f,
                ModContent.ProjectileType<CursedProjectile>(),
                damage,
                5f,
                Player.whoAmI
            );

            for (int i = 0; i < 8; i++)
            {
                Dust d = Dust.NewDustDirect(spawnPos - new Vector2(4), 8, 8, Terraria.ID.DustID.CursedTorch,
                    direction.X * Main.rand.NextFloat(2f, 6f), direction.Y * Main.rand.NextFloat(2f, 6f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1f, 1.5f);
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
