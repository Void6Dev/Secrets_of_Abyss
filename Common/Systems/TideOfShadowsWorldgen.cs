using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using System.Collections.Generic;
using SoA.Content.Tiles;
using Terraria.IO;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Nature;

namespace SoA.Common.Systems
{
    public class TideOfShadowsWorldgen : ModSystem
    {
        public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
        {
            int pilesIndex = tasks.FindIndex(genpass => genpass.Name.Equals("Piles"));
            if (pilesIndex != -1)
            {
                tasks.Insert(pilesIndex + 1, new TOSWorldgen_PilesPass("SoA Piles", 100f));
            }
        }
    }

    public class TOSWorldgen_PilesPass : GenPass
    {
        public TOSWorldgen_PilesPass(string name, float loadWeight) : base(name, loadWeight)
        {
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "Generating piles...";

            // Определите типы тайлов
            int[] tileTypes = { ModContent.TileType<Tidesand_tile>() };

            // Спавн обломков рядом со спауном
            for (int k = 0; k < 15; k++)
            {
                bool success = false;
                int attempts = 0;

                while (!success)
                {
                    attempts++;
                    if (attempts > 1000)
                    {
                        break;
                    }
                    int x = WorldGen.genRand.Next(Main.maxTilesX / 2 - 40, Main.maxTilesX / 2 + 40);
                    int y = WorldGen.genRand.Next((int)GenVars.worldSurfaceLow, (int)GenVars.worldSurfaceHigh);
                    int tileType = WorldGen.genRand.Next(tileTypes);
                    int placeStyle = WorldGen.genRand.Next(6); // Используйте корректное количество стилей размещения

                    // Проверьте, если тайл уже существует в этой позиции
                    if (Main.tile[x, y].TileType == tileType)
                    {
                        continue;
                    }

                    // Разместите тайл
                    WorldGen.PlaceTile(x, y, tileType, mute: true, style: placeStyle);
                    success = Main.tile[x, y].TileType == tileType;
                }
            }
        }
    }
}
