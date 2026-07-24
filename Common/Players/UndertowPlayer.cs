using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Common.Players
{
    // Отбойное течение: во второй фазе Король-краб тянет игрока к себе, не давая
    // бесконечно кайтить. Считается на клиенте самого игрока — движение
    // собственного персонажа авторитетно локально (в NPC.AI на сервере не поправить).
    public class UndertowPlayer : ModPlayer
    {
        private const float PullRange = 1400f;   // радиус действия течения (px)
        private const float DeadZone = 60f;      // у самого короля не дёргаем
        private const float PullAccel = 0.07f;   // мягкое подтягивание
        private const float MaxPullSpeed = 3.2f; // не разгоняем игрока против его воли

        public override void PreUpdateMovement()
        {
            if (Player.dead || Main.netMode == NetmodeID.Server)
                return;

            int type = ModContent.NPCType<King_crab>();
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.type != type || npc.ai[3] != 1f)
                    continue; // только живой король во второй фазе

                float dx = npc.Center.X - Player.Center.X;
                float distX = Math.Abs(dx);
                if (distX < DeadZone || distX > PullRange)
                    return;

                int pullDir = Math.Sign(dx);
                // Тянем к королю, но не выше порога — иначе играем против ввода игрока
                if (pullDir * Player.velocity.X < MaxPullSpeed)
                    Player.velocity.X += pullDir * PullAccel;
                return;
            }
        }
    }
}
