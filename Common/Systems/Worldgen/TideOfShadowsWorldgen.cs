using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using SoA.Common.Systems.JungleLake;
using SoA.Common.Systems.TideOcean;

namespace SoA.Common.Systems
{
    // Регистрирует генпассы мода в строгом порядке. Каждый следующий работает
    // по готовому рельефу предыдущего, поэтому переставлять их нельзя:
    // лепка биома → галеон → арка-ориентир → озеро в джунглях → деревня на озере
    public class TideOfShadowsWorldgen : ModSystem
    {
        public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
        {
            int cleanupIndex = tasks.FindIndex(genpass => genpass.Name.Equals("Final Cleanup"));
            if (cleanupIndex == -1)
                return;

            tasks.Insert(cleanupIndex + 1, new TideOceanPass("SoA Tide of Shadows Ocean", 320f));
            tasks.Insert(cleanupIndex + 2, new SunkenShipPass("SoA Sunken Ship", 180f, Mod));
            tasks.Insert(cleanupIndex + 3, new CoralArchPass("SoA Coral Arch", 60f));
            tasks.Insert(cleanupIndex + 4, new JungleLakePass("SoA Jungle Lake", 120f));
            tasks.Insert(cleanupIndex + 5, new FishingVillagePass("SoA Fishing Village", 90f, Mod));
        }
    }
}
