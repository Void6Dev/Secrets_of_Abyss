using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace SoA.Common.Utils
{
    // Экспорт/размещение построек. Формат — TagCompound (.str):
    // легенды типов тайлов и стен хранятся по именам для модовых типов,
    // поэтому файл переживает пересборку мода и смену внутренних ID.
    // Ограничения v1: содержимое сундуков и провода не сохраняются.
    public static class StructureIO
    {
        public const int MaxWidth = 250;
        public const int MaxHeight = 200;

        public static string ExportDirectory => Path.Combine(Main.SavePath, "SoAStructures");

        // --- Экспорт области мира в файл на диске ---
        public static string Export(Rectangle tileArea, string name)
        {
            int w = tileArea.Width, h = tileArea.Height;
            var tileLegend = new List<string>();
            var wallLegend = new List<string>();
            var legendIndex = new Dictionary<string, int>();
            var wallIndex = new Dictionary<string, int>();

            int[] tType = new int[w * h];
            int[] frameX = new int[w * h];
            int[] frameY = new int[w * h];
            int[] wall = new int[w * h];
            int[] liquid = new int[w * h];
            int[] slope = new int[w * h];
            int[] paint = new int[w * h];

            for (int dx = 0; dx < w; dx++)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    int idx = dx * h + dy;
                    Tile tile = Main.tile[tileArea.X + dx, tileArea.Y + dy];

                    tType[idx] = tile.HasTile
                        ? LegendId(TileKey(tile.TileType), tileLegend, legendIndex)
                        : -1;
                    frameX[idx] = tile.TileFrameX;
                    frameY[idx] = tile.TileFrameY;
                    wall[idx] = tile.WallType > 0
                        ? LegendId(WallKey(tile.WallType), wallLegend, wallIndex)
                        : -1;
                    liquid[idx] = (tile.LiquidType << 8) | tile.LiquidAmount;
                    slope[idx] = (int)tile.Slope | (tile.IsHalfBlock ? 8 : 0);
                    paint[idx] = PackPaint(tile);
                }
            }

            var tag = new TagCompound
            {
                ["version"] = 2,
                ["w"] = w,
                ["h"] = h,
                ["tileLegend"] = tileLegend,
                ["wallLegend"] = wallLegend,
                ["t"] = tType,
                ["fx"] = frameX,
                ["fy"] = frameY,
                ["wl"] = wall,
                ["lq"] = liquid,
                ["sl"] = slope,
                ["pt"] = paint,
            };

            Directory.CreateDirectory(ExportDirectory);
            string path = Path.Combine(ExportDirectory, name + ".str");
            TagIO.ToFile(tag, path);
            return path;
        }

        // Размеры сохранённой постройки без её размещения (для поиска площадки)
        public static bool TryGetSize(Mod mod, string name, out int w, out int h)
        {
            w = h = 0;
            TagCompound tag = LoadTag(mod, name);
            if (tag == null)
                return false;
            w = tag.GetInt("w");
            h = tag.GetInt("h");
            return w > 0 && h > 0;
        }

        // --- Размещение: сначала ищем файл внутри мода (Assets/Structures/имя.str),
        // затем на диске в папке экспорта — удобно тестировать без пересборки.
        // mirror = отразить постройку по горизонтали (для зеркальной стороны мира) ---
        public static bool Place(Mod mod, string name, int topLeftX, int topLeftY, bool mirror = false)
        {
            TagCompound tag = LoadTag(mod, name);
            if (tag == null)
                return false;

            int w = tag.GetInt("w");
            int h = tag.GetInt("h");
            var tileLegend = ResolveTileLegend(tag.GetList<string>("tileLegend"));
            var wallLegend = ResolveWallLegend(tag.GetList<string>("wallLegend"));
            int[] tType = tag.GetIntArray("t");
            int[] frameX = tag.GetIntArray("fx");
            int[] frameY = tag.GetIntArray("fy");
            int[] wall = tag.GetIntArray("wl");
            int[] liquid = tag.GetIntArray("lq");
            int[] slope = tag.GetIntArray("sl");
            int[] paint = tag.GetIntArray("pt");    // v1-файлы без покраски → пустой массив
            bool hasPaint = paint.Length == w * h;

            for (int dx = 0; dx < w; dx++)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    // Зеркалим положение колонки; кадры и склоны отражаем ниже,
                    // чтобы многотайловые объекты и сундуки собрались правильно
                    int destDx = mirror ? w - 1 - dx : dx;
                    int x = topLeftX + destDx, y = topLeftY + dy;
                    if (!WorldGen.InWorld(x, y, 10))
                        continue;

                    int idx = dx * h + dy;
                    Tile tile = Main.tile[x, y];
                    tile.ClearTile();

                    int typeIdx = tType[idx];
                    if (typeIdx >= 0 && tileLegend[typeIdx] >= 0)
                    {
                        ushort type = (ushort)tileLegend[typeIdx];
                        short fx = (short)frameX[idx];
                        int sl = slope[idx] & 7;

                        if (mirror)
                        {
                            fx = MirrorFrameX(type, fx);
                            sl = MirrorSlope(sl);
                        }

                        tile.ResetToType(type);
                        tile.TileFrameX = fx;
                        tile.TileFrameY = (short)frameY[idx];
                        tile.Slope = (SlopeType)sl;
                        tile.IsHalfBlock = (slope[idx] & 8) != 0;
                    }

                    int wallIdx = wall[idx];
                    tile.WallType = wallIdx >= 0 && wallLegend[wallIdx] >= 0
                        ? (ushort)wallLegend[wallIdx]
                        : WallID.None;

                    tile.LiquidType = liquid[idx] >> 8;
                    tile.LiquidAmount = (byte)(liquid[idx] & 0xFF);

                    if (hasPaint)
                        UnpackPaint(tile, paint[idx]);
                }
            }

            WorldGen.RangeFrame(topLeftX - 1, topLeftY - 1, topLeftX + w + 1, topLeftY + h + 1);
            return true;
        }

        // Покраска в один int: цвет блока | цвет стены << 8 | покрытия (illuminant/echo)
        private static int PackPaint(Tile tile)
            => tile.TileColor
             | (tile.WallColor << 8)
             | (tile.IsTileFullbright ? 1 << 16 : 0)
             | (tile.IsWallFullbright ? 1 << 17 : 0)
             | (tile.IsTileInvisible ? 1 << 18 : 0)
             | (tile.IsWallInvisible ? 1 << 19 : 0);

        private static void UnpackPaint(Tile tile, int packed)
        {
            tile.TileColor = (byte)(packed & 0xFF);
            tile.WallColor = (byte)(packed >> 8 & 0xFF);
            tile.IsTileFullbright = (packed & 1 << 16) != 0;
            tile.IsWallFullbright = (packed & 1 << 17) != 0;
            tile.IsTileInvisible = (packed & 1 << 18) != 0;
            tile.IsWallInvisible = (packed & 1 << 19) != 0;
        }

        // Горизонтальный обмен склонов: Down/Up-Left <-> Down/Up-Right (сырые id 1..4)
        private static int MirrorSlope(int slope) => slope switch
        {
            1 => 2,
            2 => 1,
            3 => 4,
            4 => 3,
            _ => slope
        };

        // Разворачивает горизонтальные подкадры многотайлового объекта, сохраняя
        // блок стиля. Одно-тайловые и merge-тайлы кадр не меняют — их переставит RangeFrame
        private static short MirrorFrameX(int type, short frameX)
        {
            TileObjectData data = TileObjectData.GetTileData(type, 0);
            if (data == null || data.Width <= 1)
                return frameX;

            int step = data.CoordinateWidth + data.CoordinatePadding;
            if (step <= 0)
                return frameX;

            int fullWidth = data.Width * step;
            int styleBlock = frameX / fullWidth;
            int column = frameX % fullWidth / step;
            int mirrored = data.Width - 1 - column;
            return (short)(styleBlock * fullWidth + mirrored * step);
        }

        private static TagCompound LoadTag(Mod mod, string name)
        {
            string modPath = "Assets/Structures/" + name + ".str";
            if (mod.FileExists(modPath))
            {
                using Stream stream = mod.GetFileStream(modPath);
                return TagIO.FromStream(stream);
            }

            string diskPath = Path.Combine(ExportDirectory, name + ".str");
            return File.Exists(diskPath) ? TagIO.FromFile(diskPath) : null;
        }

        // --- Легенды ---

        private static int LegendId(string key, List<string> legend, Dictionary<string, int> index)
        {
            if (index.TryGetValue(key, out int id))
                return id;
            id = legend.Count;
            legend.Add(key);
            index[key] = id;
            return id;
        }

        private static string TileKey(int type)
            => type < TileID.Count ? "v:" + type : "m:" + TileLoader.GetTile(type).FullName;

        private static string WallKey(int type)
            => type < WallID.Count ? "v:" + type : "m:" + WallLoader.GetWall(type).FullName;

        private static int[] ResolveTileLegend(IList<string> legend)
        {
            int[] result = new int[legend.Count];
            for (int i = 0; i < legend.Count; i++)
            {
                string key = legend[i];
                if (key.StartsWith("v:"))
                    result[i] = int.Parse(key[2..]);
                else if (ModContent.TryFind(key[2..], out ModTile modTile))
                    result[i] = modTile.Type;
                else
                    result[i] = -1; // модовый тайл исчез — клетка останется пустой
            }
            return result;
        }

        private static int[] ResolveWallLegend(IList<string> legend)
        {
            int[] result = new int[legend.Count];
            for (int i = 0; i < legend.Count; i++)
            {
                string key = legend[i];
                if (key.StartsWith("v:"))
                    result[i] = int.Parse(key[2..]);
                else if (ModContent.TryFind(key[2..], out ModWall modWall))
                    result[i] = modWall.Type;
                else
                    result[i] = -1;
            }
            return result;
        }
    }
}
