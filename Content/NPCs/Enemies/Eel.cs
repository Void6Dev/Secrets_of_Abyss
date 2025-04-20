using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ModLoader.Utilities;
using Microsoft.Xna.Framework;
using SoA.Content.Items.Materials;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.NPCs.Enemies
{
    internal class Eel : ModNPC
    {
        private int chargeTime = 0;
        private int chargeCooldown = 180; 
        private bool isCharging = false; // Флаг зарядки

        public override void SetStaticDefaults() {
            Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Shark]; // Используем анимацию акулы
            NPCID.Sets.NPCBestiaryDrawModifiers value = new NPCID.Sets.NPCBestiaryDrawModifiers() {
                Velocity = 1f
            };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, value);
        }

        public override void SetDefaults()
        {
            Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Shark];
            NPC.width = 26;
            NPC.height = 60;
            NPC.damage = 40;
            NPC.defense = 15;
            NPC.lifeMax = 300;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = 60f;
            NPC.knockBackResist = 0.8f;
            NPC.aiStyle = 16; // Возвращаем aiStyle для стандартного поведения акулы
            NPC.noGravity = true;
            AnimationType = NPCID.Shark; // Снова используем анимацию акулы
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            return SpawnCondition.Ocean.Chance * 0.3f;
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<AbyssScale_small>(), 4, 1, 3)); 
        }

        public override void PostDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            //
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life <= 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.Electric, 2.5f * Main.rand.NextFloatDirection(), -2.5f, 0, default(Color), 0.7f);
                }
            }
        }

        public override void AI()
        {
            Lighting.AddLight(NPC.Center, 0.3f, 0.6f, 1.2f); // Освещение

            Player target = Main.player[NPC.target]; // Цель NPC

            // Определяем направление NPC
            if (target.Center.X < NPC.Center.X)
            {
                NPC.direction = -1; // Смотрит влево
                NPC.spriteDirection = -1;
            }
            else
            {
                NPC.direction = 1; // Смотрит вправо
                NPC.spriteDirection = 1;
            }

            if (chargeCooldown > 0)
            {
                chargeCooldown--;
            }
            else if (!isCharging && Vector2.Distance(NPC.Center, target.Center) < 500f)
            {
                StartCharging();
            }

            if (isCharging)
            {
                chargeTime--;

                if (chargeTime <= 0)
                {
                    PerformChargedBite(target);
                    isCharging = false;
                    chargeCooldown = 180; // Перезарядка после атаки
                }
            }

            // Проверка на воду
            if (!NPC.wet)
            {
                // Если угорь вне воды, он не должен двигаться нормально
                NPC.velocity.Y += 0.2f; // Постепенно падает
                if (NPC.velocity.Y > 10f)
                {
                    NPC.velocity.Y = 10f; // Ограничиваем скорость падения
                }
            }
            else
            {
                // Стандартное плавание в воде, если не заряжается и не делает рывок
                if (!isCharging)
                {
                    Swim();
                }
            }
        }

        private void StartCharging()
        {
            chargeTime = 60; // Время зарядки перед рывком
            isCharging = true;
            NPC.velocity = Vector2.Zero; // Останавливаем NPC при зарядке
            NPC.netUpdate = true;
        }

        private void PerformChargedBite(Player target)
        {
            Vector2 direction = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX); // Направление к игроку

            
            float dashSpeed = 40f;
            NPC.velocity = direction * dashSpeed; // Скорость рывка

            // Дополнительная отладка для проверки направления и скорости
            Main.NewText($"Charging in direction {direction} with speed {dashSpeed}");
        }

        private void Swim()
        {
            // Плавание в стиле стандартного поведения водных NPC
            NPC.TargetClosest(faceTarget: true); // Преследует ближайшего игрока
            float speed = 5f;
            Vector2 moveDirection = (Main.player[NPC.target].Center - NPC.Center).SafeNormalize(Vector2.UnitX) * speed; // Направление движения к цели
            NPC.velocity = (NPC.velocity * 20f + moveDirection) / 21f; // Плавное движение
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            target.AddBuff(BuffID.Electrified, 300);
        }
    }
}
