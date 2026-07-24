using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace SoA.Content.Buffs
{
    public class HellFireDebuff : ModBuff
    {
        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
        }

        public override void Update(NPC npc, ref int buffIndex)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Ticks every 10 frames = 3x faster than vanilla OnFire (~30 frames)
            if (npc.buffTime[buffIndex] > 0 && npc.buffTime[buffIndex] % 10 == 0)
            {
                var hitInfo = new NPC.HitInfo()
                {
                    Damage = 4,
                    Knockback = 0f,
                    HitDirection = 0,
                    Crit = false
                };

                npc.StrikeNPC(hitInfo);
            }
        }
    }

    public class HellFireGlobalNPC : GlobalNPC
    {
        public override void PostAI(NPC npc)
        {
            if (!npc.friendly && npc.HasBuff(ModContent.BuffType<HellFireDebuff>()))
                npc.velocity *= 0.85f;
        }

        public override void DrawEffects(NPC npc, ref Color drawColor)
        {
            if (!npc.HasBuff(ModContent.BuffType<HellFireDebuff>()))
                return;

            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustDirect(npc.position, npc.width, npc.height,
                    DustID.InfernoFork, Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(0.5f, 2.5f));
                d.scale = Main.rand.NextFloat(0.6f, 1.3f);
                d.noGravity = false;
            }

            drawColor = Color.Lerp(drawColor, new Color(255, 60, 0), 0.35f);
        }
    }
}
