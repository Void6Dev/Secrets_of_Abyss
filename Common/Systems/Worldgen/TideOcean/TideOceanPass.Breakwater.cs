using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Common.Systems.TideOcean
{
    // Руины волнолома затопленного порта на рифе у края мира. Постройка ручная:
    // Assets/Structures/breakwater_ruin.str, либо папка экспорта инструмента структур —
    // оттуда её можно менять без пересборки мода. Строится для океана слева: край мира
    // слева от постройки; для правого океана отражается. Нет файла — остаётся голый риф.
    //
    // Ставится по уровню моря, а не по рифу: ряд BreakwaterWaterlineRow постройки ложится
    // ровно на зеркало воды, палуба остаётся над ней, арки — под ней. Риф там, где он выше,
    // постройка прорезает только своей кладкой (маска), в пролётах арок он остаётся
    public partial class TideOceanPass
    {
        private const string BreakwaterStructure = "breakwater_ruin";
        private const int BreakwaterEdgeGap = 4;        // столбцов от края сетки до постройки
        private const int BreakwaterWaterlineRow = 32;  // первый подводный ряд постройки, сверху. Перестроил — сверь
        private const int BreakwaterRockScan = 80;      // ниже зеркала воды породу под постройкой не ищем

        private int _breakwaterEndGx;

        private void PlaceBreakwaterRuin()
        {
            _breakwaterEndGx = 0;
            Mod mod = ModContent.GetInstance<SoA>();
            if (!StructureIO.TryGetSize(mod, BreakwaterStructure, out int width, out int height))
            {
                Log($"руин волнолома нет ({BreakwaterStructure}.str) — у края мира голый риф");
                return;
            }

            int firstGx = BreakwaterEdgeGap;
            int lastGx = firstGx + width - 1;
            if (lastGx >= _shoreGx - BeachWidth)
            {
                Log($"руины волнолома шириной {width} не влезают в море до уреза воды — пропущены");
                return;
            }

            int topY = _waterTopY - BreakwaterWaterlineRow;
            int leftX = Math.Min(_grid.ToWorldX(firstGx), _grid.ToWorldX(lastGx));
            if (!StructureIO.Place(mod, BreakwaterStructure, leftX, topY, mirror: _dir == -1))
                return;

            int bottomY = topY + height - 1;
            for (int x = leftX; x < leftX + width; x++)
            {
                RaiseFoundation(x, bottomY);
                FloodBelowWaterline(x, topY, bottomY);
            }

            _breakwaterEndGx = lastGx;
            Log($"руины волнолома {width}x{height} поставлены на {leftX},{topY}");
        }

        // Опора под сплошным низом постройки доводится до дна той же кладкой, что
        // и низ опоры. Под пролётами арок фундамента нет — там вода проходит насквозь
        private void RaiseFoundation(int x, int bottomY)
        {
            if (!WorldGen.SolidTile(x, bottomY))
                return;

            ushort pillarType = Main.tile[x, bottomY].TileType;
            for (int y = bottomY + 1; y < _waterTopY + BreakwaterRockScan && !WorldGen.SolidTile(x, y); y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ResetToType(pillarType);
                tile.LiquidAmount = 0;
            }
        }

        // Постройка собрана на суше, и ниже зеркала воды в ней остаются сухие карманы
        private void FloodBelowWaterline(int x, int topY, int bottomY)
        {
            for (int y = Math.Max(topY, _waterTopY); y <= bottomY; y++)
            {
                Tile tile = Main.tile[x, y];
                bool blocksWater = tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
                if (blocksWater || tile.LiquidAmount > 0)
                    continue;

                tile.LiquidType = LiquidID.Water;
                tile.LiquidAmount = byte.MaxValue;
            }
        }
    }
}
