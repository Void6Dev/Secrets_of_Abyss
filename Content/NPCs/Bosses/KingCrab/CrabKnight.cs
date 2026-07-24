using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Прислужник Короля-краба: пока живы оба рыцаря, король неуязвим.
    // Спрайт — перекрашенный ванильный краб (placeholder до собственного арта).
    public class CrabKnight : ModNPC
    {
        private const int LungeIntervalTicks = 180;
        private const float LungeRange = 400f;
        private const int TelegraphTicks = 30;

        public override string Texture => "Terraria/Images/NPC_" + NPCID.Crab;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = Main.npcFrameCount[NPCID.Crab];
            NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
        }

        public override void SetDefaults()
        {
            NPC.width = 34;
            NPC.height = 22;
            NPC.scale = 1.35f;
            NPC.damage = 20;
            NPC.defense = 9;
            NPC.lifeMax = 220;
            NPC.knockBackResist = 0.5f;
            NPC.aiStyle = NPCAIStyleID.Fighter;
            AIType = NPCID.Crab;
            AnimationType = NPCID.Crab;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = Item.buyPrice(silver: 5);
        }

        public override Color? GetAlpha(Color drawColor)
        {
            // Стальной отлив — «рыцарская броня»
            Color armored = Color.Lerp(drawColor, new Color(120, 140, 220), 0.45f);

            // Перед выпадом краснеет с нарастающей пульсацией — телеграф атаки
            float untilLunge = LungeIntervalTicks - NPC.localAI[0];
            if (untilLunge <= TelegraphTicks && untilLunge >= 0f)
            {
                float t = 1f - untilLunge / TelegraphTicks;
                float pulse = 0.6f + 0.4f * (float)Math.Sin(Main.GameUpdateCount * 0.5f);
                return Color.Lerp(armored, new Color(255, 90, 70), t * pulse);
            }
            return armored;
        }

        public override void PostAI()
        {
            // Без короля рыцарям незачем сражаться
            if (!NPC.AnyNPCs(ModContent.NPCType<King_crab>()))
            {
                NPC.EncourageDespawn(10);
                return;
            }

            // Периодический выпад в сторону цели поверх ванильного ИИ бойца
            NPC.localAI[0]++;

            // Телеграф: песок из-под ног за полсекунды до прыжка
            float untilLunge = LungeIntervalTicks - NPC.localAI[0];
            if (untilLunge <= TelegraphTicks && untilLunge >= 0f
                && Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                Dust sand = Dust.NewDustDirect(NPC.BottomLeft - new Vector2(0f, 6f), NPC.width, 6, DustID.Sand);
                sand.velocity = new Vector2(Main.rand.NextFloatDirection() * 2f, -Main.rand.NextFloat(1f, 3f));
                sand.scale = Main.rand.NextFloat(1f, 1.5f);
            }
            if (NPC.localAI[0] >= LungeIntervalTicks && NPC.velocity.Y == 0f)
            {
                Player target = Main.player[NPC.target];
                if (target.active && !target.dead
                    && Vector2.Distance(target.Center, NPC.Center) < LungeRange)
                {
                    NPC.localAI[0] = 0f;
                    NPC.velocity = new Vector2(Math.Sign(target.Center.X - NPC.Center.X) * 6.5f, -7f);
                    SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.5f }, NPC.Center);
                    NPC.netUpdate = true;
                }
                else
                {
                    NPC.localAI[0] = LungeIntervalTicks * 0.7f;
                }
            }

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(8))
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
                d.noGravity = true;
                d.velocity *= 0.4f;
            }
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server || NPC.life > 0)
                return;

            for (int i = 0; i < 15; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.RedTorch);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 4f, -Main.rand.NextFloat(1f, 4f));
                d.noGravity = true;
            }
        }
    }
}
