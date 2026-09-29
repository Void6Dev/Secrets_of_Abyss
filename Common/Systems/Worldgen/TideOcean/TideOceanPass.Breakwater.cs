using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;
using SoA.Content.Tiles.Nature;

namespace SoA.Common.Systems.TideOcean
{
    // Руины волнолома затопленного порта на рифе у края мира. Постройка ручная:
    // Assets/Structures/breakwater_ruin.str, либо папка экспорта инструмента структур —
    // оттуда её можно менять без пересборки мода. Строится для океана слева: край мира
    // слева от постройки; для правого океана отражается. Нет файла — остаётся голый риф
    public partial class TideOceanPass
    {
        private const string BreakwaterStructure = "breakwater_ruin";
        private const int BreakwaterEdgeGap = 4;      // столбцов от края сетки до постройки
        private const int BreakwaterBuryDepth = 2;    // нижние ряды утоплены в гребень рифа
        private const int BreakwaterRockScan = 80;    // ниже зеркала воды породу под постройкой не ищем

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

            // Постройка встаёт на самую высокую точку рифа под собой: так она нигде
            // не зарывается глубже BuryDepth, а провалы под ней закрывает фундамент
            int crestY = int.MaxValue;
            for (int gx = firstGx; gx <= lastGx; gx++)
                crestY = Math.Min(crestY, RockTopWorldY(gx));
            if (crestY == int.MaxValue)
            {
                Log("под руинами волнолома не найден риф — пропущены");
                return;
            }

            int topY = crestY + BreakwaterBuryDepth - height;
            int leftX = Math.Min(_grid.ToWorldX(firstGx), _grid.ToWorldX(lastGx));
            if (!StructureIO.Place(mod, BreakwaterStructure, leftX, topY, mirror: _dir == -1))
                return;

            ushort stoneType = (ushort)ModContent.TileType<Tidestone_tile>();
            int bottomY = topY + height - 1;
            for (int x = leftX; x < leftX + width; x++)
            {
                RaiseFoundation(x, bottomY, stoneType);
                FloodBelowWaterline(x, topY, bottomY);
            }

            _breakwaterEndGx = lastGx;
            Log($"руины волнолома {width}x{height} поставлены на {leftX},{topY}");
        }

        private int RockTopWorldY(int gx)
        {
            int x = _grid.ToWorldX(gx);
            for (int y = _topY; y < _waterTopY + BreakwaterRockScan; y++)
            {
                if (WorldGen.SolidTile(x, y))
                    return y;
            }
            return int.MaxValue;
        }

        // Опора под сплошным низом постройки доводится до рифа. Под пролётами и
        // арками, где низ пустой, фундамента нет — там вода проходит насквозь
        private void RaiseFoundation(int x, int bottomY, ushort stoneType)
        {
            if (!WorldGen.SolidTile(x, bottomY))
                return;

            for (int y = bottomY + 1; y < _waterTopY + BreakwaterRockScan && !WorldGen.SolidTile(x, y); y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ResetToType(stoneType);
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
