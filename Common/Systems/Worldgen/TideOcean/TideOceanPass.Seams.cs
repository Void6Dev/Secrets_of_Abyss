using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Nature;

namespace SoA.Common.Systems.TideOcean
{
    // Стык биома с ванильным миром со стороны чужой породы
    public partial class TideOceanPass
    {
        private const int CaveRoundingReach = 8;       // полоса за следом биома, где скругляются пещеры
        private const int CaveRoundingPasses = 4;      // проходов заполнения: столько тайлов срезается с угла
        private const int CaveFillNeighbours = 5;      // из 8 соседей твёрдых, чтобы клетка заполнилась
        private const int CaveRoundingWaterMargin = 2; // выше зеркала воды не трогаем: там поверхность

        // Оболочка со стороны суши ниже первой печати перекладывается печатным камнем.
        // Иначе зону за печатью открывали сбоку, подкопом из пещер джунглей.
        // Идёт после смешивания пород на стыке: оно подмешивает в оболочку ванильный
        // камень, и прочную полосу нужно класть уже поверх него. Верх зоны I не трогаем —
        // туда и так открыт путь с поверхности моря
        private void HardenInlandShell()
        {
            ushort sealstoneType = (ushort)ModContent.TileType<Sealstone_tile>();
            int fromGy = Math.Max(_gWater, _gPortBottom - ShellThickness);
            int toGy = Math.Min(_grid.Height - 1, _gAbyssBottom + ShellThickness);
            int hardened = 0;

            for (int gy = fromGy; gy <= toGy; gy++)
            {
                int y = _grid.ToWorldY(gy);
                if (y < 20 || y >= Main.maxTilesY - 20)
                    continue;

                int edge = InlandEdgeAt(gy);
                for (int gx = edge - ShellThickness + 1; gx <= edge; gx++)
                {
                    int x = _grid.ToWorldX(gx);
                    if (gx < 0 || x < 12 || x >= Main.maxTilesX - 12 || InsideTempleOuter(gx, gy))
                        continue;

                    // Стена остаётся своей: меняется только блок
                    Tile tile = Main.tile[x, y];
                    ushort wall = tile.WallType;
                    tile.ResetToType(sealstoneType);
                    tile.LiquidAmount = 0;
                    tile.WallType = wall;
                    hardened++;
                }
            }

            Log($"оболочка со стороны суши: печатный камень, строки {_topY + fromGy}..{_topY + toGy}, тайлов {hardened}");
        }

        // Монолит штампуется поверх ванильных пещер, и пещера упирается в него
        // плоским срезом с прямыми углами — сразу видно, что её отрезали. Здесь
        // в полосе за следом биома вогнутые углы пещер заполняются породой соседей
        // по правилу большинства, и тупик становится скруглённым. Проход только
        // добавляет твёрдое и только за следом — герметичность оболочки не страдает
        private void RoundOffNeighbourCaves()
        {
            List<Point> band = CollectNeighbourBand();
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();

            var toFill = new List<(Point Cell, ushort Type)>();
            int filledTotal = 0;

            // Кандидаты собираются по снимку прохода, а заполняются после: иначе
            // заполнение сразу влияет на соседей и углы съедаются с перекосом по обходу
            for (int pass = 0; pass < CaveRoundingPasses; pass++)
            {
                toFill.Clear();
                foreach (Point cell in band)
                {
                    if (TryPickCaveFill(cell.X, cell.Y, tidestoneType, tidesandType, out ushort fillType))
                        toFill.Add((cell, fillType));
                }

                if (toFill.Count == 0)
                    break;

                foreach ((Point cell, ushort fillType) in toFill)
                {
                    Tile tile = Main.tile[cell.X, cell.Y];
                    tile.ResetToType(fillType);
                    tile.LiquidAmount = 0;
                }
                filledTotal += toFill.Count;
            }

            Log($"стык с сушей: в полосе {band.Count} клеток, скруглено {filledTotal} тайлов пещер");
        }

        // Ванильные клетки не дальше CaveRoundingReach от следа биома.
        // Дистанция считается по шахматной метрике в двух проходах по растру,
        // растянутому на полосу со всех сторон: у дна и у дальней кромки
        // след доходит до края сетки, и полоса ложится уже за ней
        private List<Point> CollectNeighbourBand()
        {
            int pad = CaveRoundingReach;
            int width = _grid.Width + pad * 2;
            int height = _grid.Height + pad * 2;
            var distance = new byte[width * height];

            for (int ey = 0; ey < height; ey++)
            {
                for (int ex = 0; ex < width; ex++)
                {
                    bool footprint = _grid.Get(ex - pad, ey - pad) != TideGrid.Untouched;
                    distance[ey * width + ex] = footprint ? (byte)0 : byte.MaxValue;
                }
            }

            for (int ey = 0; ey < height; ey++)
            {
                for (int ex = 0; ex < width; ex++)
                {
                    int best = distance[ey * width + ex];
                    if (ex > 0) best = Math.Min(best, distance[ey * width + ex - 1] + 1);
                    if (ey > 0)
                    {
                        best = Math.Min(best, distance[(ey - 1) * width + ex] + 1);
                        if (ex > 0) best = Math.Min(best, distance[(ey - 1) * width + ex - 1] + 1);
                        if (ex < width - 1) best = Math.Min(best, distance[(ey - 1) * width + ex + 1] + 1);
                    }
                    distance[ey * width + ex] = (byte)best;
                }
            }

            for (int ey = height - 1; ey >= 0; ey--)
            {
                for (int ex = width - 1; ex >= 0; ex--)
                {
                    int best = distance[ey * width + ex];
                    if (ex < width - 1) best = Math.Min(best, distance[ey * width + ex + 1] + 1);
                    if (ey < height - 1)
                    {
                        best = Math.Min(best, distance[(ey + 1) * width + ex] + 1);
                        if (ex < width - 1) best = Math.Min(best, distance[(ey + 1) * width + ex + 1] + 1);
                        if (ex > 0) best = Math.Min(best, distance[(ey + 1) * width + ex - 1] + 1);
                    }
                    distance[ey * width + ex] = (byte)best;
                }
            }

            var band = new List<Point>();
            for (int ey = 0; ey < height; ey++)
            {
                int gy = ey - pad;
                if (gy <= _gWater + CaveRoundingWaterMargin)
                    continue;

                int y = _grid.ToWorldY(gy);
                if (y < 21 || y >= Main.maxTilesY - 21)
                    continue;

                for (int ex = 0; ex < width; ex++)
                {
                    int d = distance[ey * width + ex];
                    if (d == 0 || d > pad)
                        continue;

                    int gx = ex - pad;
                    int x = _grid.ToWorldX(gx);
                    if (x < 13 || x >= Main.maxTilesX - 13 || InsideTempleOuter(gx, gy))
                        continue;

                    band.Add(new Point(x, y));
                }
            }
            return band;
        }

        // Пустая клетка заполняется, если её обступили природные породы.
        // Рядом с мебелью, растениями, платформами и постройками не трогаем ничего:
        // сундук не замуруется, а кирпич Храма или Темницы не обрастёт камнем
        private bool TryPickCaveFill(int x, int y, ushort tidestoneType, ushort tidesandType, out ushort fillType)
        {
            fillType = 0;
            if (Main.tile[x, y].HasTile)
                return false;

            Span<ushort> types = stackalloc ushort[8];
            int solid = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    Tile neighbour = Main.tile[x + dx, y + dy];
                    if (!neighbour.HasTile)
                        continue;

                    ushort type = neighbour.TileType;
                    if (!Main.tileSolid[type] || Main.tileSolidTop[type])
                        return false;
                    if (!IsRoundableRock(type, tidestoneType, tidesandType))
                        return false;

                    types[solid++] = type;
                }
            }

            if (solid < CaveFillNeighbours)
                return false;

            fillType = CaveFillTypeFor(MostCommon(types[..solid]), tidestoneType, tidesandType);
            return true;
        }

        private static bool IsRoundableRock(ushort type, ushort tidestoneType, ushort tidesandType)
            => type == tidestoneType || type == tidesandType || IsNaturalRock(type)
               || type is TileID.Grass or TileID.JungleGrass or TileID.Marble or TileID.Granite
                   or TileID.CorruptHardenedSand or TileID.CrimsonHardenedSand or TileID.HallowHardenedSand;

        // Трава без открытого воздуха не живёт, а сыпучая порода в тупике
        // обвалится при первом касании — заливаем устойчивым родственником
        private static ushort CaveFillTypeFor(ushort type, ushort tidestoneType, ushort tidesandType)
        {
            if (type == tidesandType)
                return tidestoneType;

            return type switch
            {
                TileID.Grass => TileID.Dirt,
                TileID.JungleGrass => TileID.Mud,
                _ => StableRock(type)
            };
        }

        private static ushort MostCommon(ReadOnlySpan<ushort> types)
        {
            ushort best = types[0];
            int bestCount = 0;
            for (int i = 0; i < types.Length; i++)
            {
                int count = 0;
                for (int j = 0; j < types.Length; j++)
                {
                    if (types[j] == types[i])
                        count++;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    best = types[i];
                }
            }
            return best;
        }
    }
}
