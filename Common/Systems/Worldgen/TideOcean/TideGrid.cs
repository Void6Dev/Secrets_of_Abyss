using System;
using Terraria;
using Terraria.ID;
using SoA.Common.Utils;

namespace SoA.Common.Systems.TideOcean
{
    // Растровый буфер биома. Вся резьба идёт по нему и только в самом конце
    // однократно переносится в тайлы мира. Так сглаживание, эрозия и проверка
    // герметичности работают по цельной картинке, а не по уже испорченным тайлам.
    //
    // Система координат: gx растёт от края мира вглубь суши, gy — вниз.
    // Мировые координаты: x = OriginX + gx * Dir, y = OriginY + gy.
    public sealed class TideGrid
    {
        public const byte Untouched = 255; // ванильный рельеф, не трогаем
        public const byte Water = 0;
        public const byte Stone = 1;       // Tidestone
        public const byte Sand = 2;        // Tidesand
        public const byte Air = 3;         // воздушный карман

        public readonly int Width;
        public readonly int Height;
        public readonly int OriginX;
        public readonly int OriginY;
        public readonly int Dir;

        private readonly byte[] _cells;
        private byte[] _scratch;

        public TideGrid(int width, int height, int originX, int originY, int dir)
        {
            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;
            Dir = dir;
            _cells = new byte[width * height];
            _cells.AsSpan().Fill(Untouched);
        }

        public bool InBounds(int gx, int gy) => gx >= 0 && gy >= 0 && gx < Width && gy < Height;

        public int ToWorldX(int gx) => OriginX + gx * Dir;

        public int ToWorldY(int gy) => OriginY + gy;

        public byte Get(int gx, int gy) => InBounds(gx, gy) ? _cells[gy * Width + gx] : Untouched;

        public void Set(int gx, int gy, byte value)
        {
            if (InBounds(gx, gy))
                _cells[gy * Width + gx] = value;
        }

        // Untouched считается твёрдым: снаружи следа биома лежит обычная порода
        public static bool IsSolid(byte cell) => cell == Stone || cell == Sand || cell == Untouched;

        // Диск с рваным краем. Радиус модулируется низкочастотным шумом,
        // поэтому пятна не читаются как окружности даже до сглаживания
        // footprintOnly не даёт писать в ячейки за следом биома. Колонны и сталактиты
        // строятся от пола до свода, а стенка чаши с глубиной завалена вглубь суши —
        // без этой проверки верхушка колонны оставляла камень посреди ванильных джунглей
        public void Disc(float cx, float cy, float radius, byte value, int channel, float roughness = 0.3f,
            bool footprintOnly = false)
        {
            if (radius < 0.5f)
                return;

            int span = (int)MathF.Ceiling(radius * (1f + roughness)) + 1;
            int icx = (int)MathF.Round(cx);
            int icy = (int)MathF.Round(cy);

            for (int dy = -span; dy <= span; dy++)
            {
                for (int dx = -span; dx <= span; dx++)
                {
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > radius * (1f + roughness))
                        continue;

                    int tx = icx + dx;
                    int ty = icy + dy;
                    if (footprintOnly && Get(tx, ty) == Untouched)
                        continue;

                    float wobble = TideNoise.Fbm(tx * 0.09f, ty * 0.09f, channel, 2) - 0.5f;
                    if (distance <= radius * (1f + wobble * 2f * roughness))
                        Set(tx, ty, value);
                }
            }
        }

        // Отрезок с радиусом — основа тоннелей, жёлобов и трещин
        public void Capsule(float x0, float y0, float x1, float y1, float radius, byte value, int channel,
            float roughness = 0.3f, bool footprintOnly = false)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            int steps = Math.Max(1, (int)MathF.Sqrt(dx * dx + dy * dy));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(x0 + dx * t, y0 + dy * t, radius, value, channel, roughness, footprintOnly);
            }
        }

        // Клеточный автомат по правилу большинства: убирает одиночные тайлы,
        // зазубрины и прямые грани. Воздушные карманы исключены — их заливает
        // окружающая порода, поэтому карманы ставятся уже после сглаживания
        public void Smooth(int iterations, int marginX, int marginY)
        {
            _scratch ??= new byte[_cells.Length];

            for (int pass = 0; pass < iterations; pass++)
            {
                Array.Copy(_cells, _scratch, _cells.Length);

                for (int gy = marginY; gy < Height - marginY; gy++)
                {
                    for (int gx = marginX; gx < Width - marginX; gx++)
                    {
                        byte cell = _cells[gy * Width + gx];
                        if (cell == Untouched || cell == Air)
                            continue;

                        int solidNeighbours = 0;
                        int sandNeighbours = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0)
                                    continue;
                                byte neighbour = Get(gx + dx, gy + dy);
                                if (IsSolid(neighbour))
                                {
                                    solidNeighbours++;
                                    if (neighbour == Sand)
                                        sandNeighbours++;
                                }
                            }
                        }

                        if (IsSolid(cell) && solidNeighbours < 4)
                            _scratch[gy * Width + gx] = Water;
                        else if (!IsSolid(cell) && solidNeighbours > 5)
                            _scratch[gy * Width + gx] = sandNeighbours > solidNeighbours / 2 ? Sand : Stone;
                    }
                }

                Array.Copy(_scratch, _cells, _cells.Length);
            }
        }

        // Эрозия: срезает одиночные выступы и засыпает ямы в один тайл.
        // Именно этот проход убирает «блочность» и остатки прямых углов
        public void Erode(int marginX, int marginY)
        {
            _scratch ??= new byte[_cells.Length];
            Array.Copy(_cells, _scratch, _cells.Length);

            for (int gy = marginY; gy < Height - marginY; gy++)
            {
                for (int gx = marginX; gx < Width - marginX; gx++)
                {
                    byte cell = _cells[gy * Width + gx];
                    if (cell == Untouched || cell == Air)
                        continue;

                    bool up = IsSolid(Get(gx, gy - 1));
                    bool down = IsSolid(Get(gx, gy + 1));
                    bool left = IsSolid(Get(gx - 1, gy));
                    bool right = IsSolid(Get(gx + 1, gy));
                    int cardinal = (up ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);

                    if (IsSolid(cell) && cardinal <= 1)
                        _scratch[gy * Width + gx] = Water;   // торчащий зуб
                    else if (!IsSolid(cell) && cardinal >= 4)
                        _scratch[gy * Width + gx] = Stone;   // ямка в один тайл
                }
            }

            Array.Copy(_scratch, _cells, _cells.Length);
        }

        // Герметизация: любая пустота у самой границы следа биома заливается камнем.
        // Без этого океан утечёт в ванильные пещеры и биом развалится
        public void SealRim(Func<int, int> inlandEdge, int shell, int freeSurfaceGy)
        {
            // Над зеркалом воды удерживать нечего — там небо, и оболочка не нужна
            for (int gy = freeSurfaceGy; gy < Height; gy++)
            {
                int edge = inlandEdge(gy);

                for (int gx = 0; gx < Width; gx++)
                {
                    byte cell = _cells[gy * Width + gx];
                    if (cell == Untouched || IsSolid(cell))
                        continue;

                    bool atWorldEdge = gx < shell;
                    bool atInlandEdge = gx > edge - shell;
                    bool atFloor = gy > Height - shell - 1;

                    if (atWorldEdge || atInlandEdge || atFloor)
                        _cells[gy * Width + gx] = Stone;
                }
            }
        }

        // Единственный проход записи в мир
        public void Blit(ushort stoneType, ushort sandType, ushort deepWallType, ushort deepSandWallType, int wallStartGy)
        {
            for (int gy = 0; gy < Height; gy++)
            {
                int y = OriginY + gy;
                if (y < 20 || y >= Main.maxTilesY - 20)
                    continue;

                bool deep = gy >= wallStartGy;

                for (int gx = 0; gx < Width; gx++)
                {
                    byte cell = _cells[gy * Width + gx];
                    if (cell == Untouched)
                        continue;

                    int x = OriginX + gx * Dir;
                    if (x < 12 || x >= Main.maxTilesX - 12)
                        continue;

                    Tile tile = Main.tile[x, y];
                    switch (cell)
                    {
                        case Stone:
                            tile.ResetToType(stoneType);
                            tile.LiquidAmount = 0;
                            break;
                        case Sand:
                            tile.ResetToType(sandType);
                            tile.LiquidAmount = 0;
                            break;
                        case Water:
                            ClearTile(tile);
                            tile.LiquidAmount = 255;
                            tile.LiquidType = LiquidID.Water;
                            break;
                        default: // Air
                            ClearTile(tile);
                            tile.LiquidAmount = 0;
                            break;
                    }

                    // Задник берётся по породе клетки: за песком песчаный, за всем
                    // остальным каменный — иначе на срезе наноса стена спорит с блоком
                    tile.WallType = deep
                        ? (cell == Sand ? deepSandWallType : deepWallType)
                        : WallID.None;
                }
            }
        }

        private static void ClearTile(Tile tile)
        {
            tile.HasTile = false;
            tile.Slope = SlopeType.Solid;
            tile.IsHalfBlock = false;
        }
    }
}
