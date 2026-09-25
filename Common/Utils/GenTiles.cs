using System;
using Terraria;
using Terraria.ID;

namespace SoA.Common.Utils
{
    // Низкоуровневые операции по тайлам для генпассов. Вынесены отдельно, потому что
    // одни и те же четыре строки с проверкой границ мира нужны и деревне, и арке,
    // и озеру в джунглях: расходиться они не должны
    public static class GenTiles
    {
        // Ближе к краю мира генерировать нечего: там ванильная рамка
        public const int WorldEdgeMargin = 12;

        public static bool InBounds(int x, int y)
            => x >= WorldEdgeMargin && y >= WorldEdgeMargin
               && x < Main.maxTilesX - WorldEdgeMargin && y < Main.maxTilesY - WorldEdgeMargin;

        public static void PlaceSolid(int x, int y, ushort type)
        {
            if (!InBounds(x, y))
                return;

            Tile tile = Main.tile[x, y];
            tile.ResetToType(type);
            tile.LiquidAmount = 0;
        }

        // Пустой тайл. Стену ставим только если попросили: у хижин она своя,
        // а под водой стена читается как грязное пятно
        public static void Clear(int x, int y, ushort wallType = 0)
        {
            if (!InBounds(x, y))
                return;

            Tile tile = Main.tile[x, y];
            tile.HasTile = false;
            tile.LiquidAmount = 0;
            if (wallType != 0)
                tile.WallType = wallType;
        }

        // Только фон, тайл не трогаем: стена не мешает ни плыть, ни идти
        public static void SetWall(int x, int y, ushort wallType)
        {
            if (!InBounds(x, y))
                return;

            Main.tile[x, y].WallType = wallType;
        }

        // Тайл под водой: и убрать породу, и залить, иначе в толще остаётся воздушный карман
        public static void Flood(int x, int y, int liquidType = LiquidID.Water)
        {
            if (!InBounds(x, y))
                return;

            Tile tile = Main.tile[x, y];
            tile.HasTile = false;
            tile.LiquidType = liquidType;
            tile.LiquidAmount = 255;
        }

        // Верхний твёрдый тайл настоящей земли. Ищется снизу вверх намеренно:
        // поиск сверху вниз цепляет парящие острова, и столбец под островом
        // отдаёт высоту на две сотни тайлов выше соседей
        public static int FindGroundTop(int x)
        {
            if (x < WorldEdgeMargin || x >= Main.maxTilesX - WorldEdgeMargin)
                return -1;

            // Ниже линии поверхности земля сплошная; если попали в приповерхностную
            // пещеру, опускаемся до первого твёрдого тайла
            int probeY = (int)Main.worldSurface + 20;
            int limit = Math.Min(Main.maxTilesY - 200, probeY + 160);
            while (probeY < limit && !IsSolid(x, probeY))
                probeY++;

            if (!IsSolid(x, probeY))
                return -1;

            // Вверх, пока порода не кончится: так холмы выше линии поверхности
            // тоже обрабатываются правильно
            int topY = probeY;
            while (topY > 60 && IsSolid(x, topY - 1))
                topY--;

            return topY;
        }

        public static bool IsSolid(int x, int y)
        {
            if (!InBounds(x, y))
                return false;

            Tile tile = Main.tile[x, y];
            return tile.HasTile && Main.tileSolid[tile.TileType];
        }

        // Y первого твёрдого тайла минус один, то есть уровень, на который можно встать
        public static int FindFloor(int x, int fromY, int scanBottom)
        {
            if (x < WorldEdgeMargin || x >= Main.maxTilesX - WorldEdgeMargin)
                return -1;

            for (int y = Math.Max(20, fromY); y < scanBottom; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    return y - 1;
            }
            return -1;
        }
    }
}
