using System.Collections.Generic;

namespace SoA.Common.Utils
{
    // Постройка в памяти: типы уже переведены в ID текущей сессии, поэтому её можно
    // сразу рисовать и ставить. Клетки лежат колонками: индекс = x * Height + y (как в .str).
    public sealed class StructureData
    {
        public const int NoType = -1;

        public readonly int Width;
        public readonly int Height;

        public readonly int[] TileTypes;
        public readonly short[] FrameX;
        public readonly short[] FrameY;
        public readonly int[] WallTypes;
        public readonly int[] Liquids;      // тип << 8 | количество
        public readonly byte[] Slopes;      // SlopeType | 8 за полублок
        public readonly int[] Paint;

        // frameX | frameY << 16; null — файл без кадров стен (старый экспорт или скрипт)
        public int[] WallFrames;
        // null — постройка прямоугольная, иначе 0 у клеток, которые не трогаются при размещении
        public byte[] Mask;

        public StructureFilter Filters;
        // Кадры тайлов сняты с мира. У построек из скрипта блоки без кадров — их
        // расставит RangeFrame при размещении, а превью подставляет средний кадр
        public bool TilesFramed;
        // Типы из легенды, которых больше нет (модовый тайл удалён или переименован)
        public int MissingTypes;
        public bool HasChests;
        public bool HasWires;

        public StructureData(int width, int height)
        {
            Width = width;
            Height = height;
            int count = width * height;
            TileTypes = new int[count];
            FrameX = new short[count];
            FrameY = new short[count];
            WallTypes = new int[count];
            Liquids = new int[count];
            Slopes = new byte[count];
            Paint = new int[count];
        }

        public int CellCount => Width * Height;

        public int Index(int x, int y) => x * Height + y;

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool InStructure(int index) => Mask == null || Mask[index] != 0;

        public StructureStats ComputeStats()
        {
            var stats = new StructureStats();
            var tileKinds = new HashSet<int>();

            for (int i = 0; i < CellCount; i++)
            {
                if (!InStructure(i))
                    continue;

                stats.Cells++;
                if (TileTypes[i] != NoType)
                {
                    stats.Tiles++;
                    tileKinds.Add(TileTypes[i]);
                }
                if (WallTypes[i] != NoType)
                    stats.Walls++;
                if ((Liquids[i] & 0xFF) > 0)
                    stats.Liquids++;
            }

            stats.TileKinds = tileKinds.Count;
            return stats;
        }
    }

    public struct StructureStats
    {
        public int Cells;
        public int Tiles;
        public int TileKinds;
        public int Walls;
        public int Liquids;
    }
}
