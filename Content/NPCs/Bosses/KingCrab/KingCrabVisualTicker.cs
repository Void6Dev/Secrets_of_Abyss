using Terraria;
using Terraria.ModLoader;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Тикает визуальную симуляцию Короля-краба ровно раз в игровой тик и независимо от того,
    // рисуется ли он сейчас. PostUpdateNPCs — после движения всех NPC: риг ног и клешней
    // считается от окончательной позиции тика, иначе на рывке ноги отставали бы от туши.
    // Чистая косметика: на выделенном сервере не крутится
    public class KingCrabVisualTicker : ModSystem
    {
        public override void PostUpdateNPCs()
        {
            if (Main.dedServ)
                return;

            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (npc.ModNPC is King_crab crab)
                    crab.TickVisuals();
            }
        }
    }
}
