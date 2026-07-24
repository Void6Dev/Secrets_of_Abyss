using System.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace SoA.Common.Systems
{
    // Флаги убитых боссов мода: сохраняются в мире и синхронизируются в мультиплеере
    public class DownedBossSystem : ModSystem
    {
        public static bool downedKingCrab;

        public override void ClearWorld()
        {
            downedKingCrab = false;
        }

        public override void SaveWorldData(TagCompound tag)
        {
            if (downedKingCrab)
                tag["downedKingCrab"] = true;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            downedKingCrab = tag.ContainsKey("downedKingCrab");
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(downedKingCrab);
        }

        public override void NetReceive(BinaryReader reader)
        {
            downedKingCrab = reader.ReadBoolean();
        }
    }
}
