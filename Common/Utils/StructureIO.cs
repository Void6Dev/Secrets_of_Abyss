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
    // v3: маска клеток — постройка может быть любой формы, невыделенные клетки
    // внутри габаритного прямоугольника при размещении не трогаются вообще.
    // v4: кадры стен ("wf") и флаг "framed" — только для превью, размещение их не требует.
    // Ограничения: содержимое сундуков и провода не сохраняются.
    public static class StructureIO
    {
        public const int MaxWidth = 250;
        public const int MaxHeight = 200;
        public const string Extension = ".str";
        public const string ModFolder = "Assets/Structures/";

        private const int FormatVersion = 4;

        public static string ExportDirectory => Path.Combine(Main.SavePath, "SoAStructures");

        // Assets/Structures в исходниках мода: есть только на машине автора
        public static string SourceDirectory(Mod mod)
            => Path.Combine(Main.SavePath, "ModSources", mod.Name, "Assets", "Structures");

        public static string FilePath(string directory, string name) => Path.Combine(directory, name + Extension);

        // --- Снимок области мира ---
        // mask == null — берём весь прямоугольник; иначе только перечисленные
        // клетки (координаты мира), остальные помечаются как «не часть постройки».
        // filters — что выкинуть из постройки (жидкости, стены, трава...).
        public static StructureData Capture(Rectangle tileArea, StructureFilter filters = StructureFilter.None,
            IReadOnlySet<Point> mask = null)
        {
            var data = new StructureData(tileArea.Width, tileArea.Height)
            {
                Filters = filters,
                TilesFramed = true,
                WallFrames = new int[tileArea.Width * tileArea.Height]
            };

            bool skipLiquids = (filters & StructureFilter.SkipLiquids) != 0;
            bool skipWalls = (filters & StructureFilter.SkipWalls) != 0;
            bool anyOutside = false;
            byte[] maskData = new byte[data.CellCount];

            for (int dx = 0; dx < data.Width; dx++)
            {
                for (int dy = 0; dy < data.Height; dy++)
                {
                    int idx = data.Index(dx, dy);
                    int x = tileArea.X + dx, y = tileArea.Y + dy;

                    data.TileTypes[idx] = StructureData.NoType;
                    data.WallTypes[idx] = StructureData.NoType;

                    bool inStructure = WorldGen.InWorld(x, y, 1)
                        && (mask == null || mask.Contains(new Point(x, y)));
                    if (!inStructure)
                    {
                        anyOutside = true;
                        continue;
                    }
                    maskData[idx] = 1;

                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile && !StructureFilters.Skips(tile.TileType, filters))
                    {
                        data.TileTypes[idx] = tile.TileType;
                        data.FrameX[idx] = tile.TileFrameX;
                        data.FrameY[idx] = tile.TileFrameY;
                        data.Slopes[idx] = (byte)((int)tile.Slope | (tile.IsHalfBlock ? 8 : 0));
                        data.HasChests |= Main.tileContainer[tile.TileType];
                    }

                    if (tile.WallType != WallID.None && !skipWalls)
                    {
                        data.WallTypes[idx] = tile.WallType;
                        data.WallFrames[idx] = (tile.WallFrameX & 0xFFFF) | (tile.WallFrameY << 16);
                    }

                    if (!skipLiquids)
                        data.Liquids[idx] = (tile.LiquidType << 8) | tile.LiquidAmount;

                    data.Paint[idx] = PackPaint(tile);
                    data.HasWires |= tile.RedWire || tile.BlueWire || tile.GreenWire || tile.YellowWire
                        || tile.HasActuator;
                }
            }

            if (mask != null || anyOutside)
                data.Mask = maskData;
            return data;
        }

        // --- Запись на диск ---

        public static void Write(StructureData data, string path)
        {
            var tileLegend = new List<string>();
            var wallLegend = new List<string>();
            var tileIndex = new Dictionary<int, int>();
            var wallIndex = new Dictionary<int, int>();

            int count = data.CellCount;
            int[] tType = new int[count];
            int[] frameX = new int[count];
            int[] frameY = new int[count];
            int[] wall = new int[count];
            int[] slope = new int[count];

            for (int i = 0; i < count; i++)
            {
                int type = data.TileTypes[i];
                tType[i] = type == StructureData.NoType ? -1 : LegendId(type, TileKey, tileLegend, tileIndex);
                frameX[i] = data.FrameX[i];
                frameY[i] = data.FrameY[i];
                slope[i] = data.Slopes[i];

                int wallType = data.WallTypes[i];
                wall[i] = wallType == StructureData.NoType ? -1 : LegendId(wallType, WallKey, wallLegend, wallIndex);
            }

            var tag = new TagCompound
            {
                ["version"] = FormatVersion,
                ["w"] = data.Width,
                ["h"] = data.Height,
                ["filters"] = (int)data.Filters,
                ["framed"] = data.TilesFramed,
                ["tileLegend"] = tileLegend,
                ["wallLegend"] = wallLegend,
                ["t"] = tType,
                ["fx"] = frameX,
                ["fy"] = frameY,
                ["wl"] = wall,
                ["lq"] = data.Liquids,
                ["sl"] = slope,
                ["pt"] = data.Paint,
            };

            if (data.WallFrames != null)
                tag["wf"] = data.WallFrames;
            if (data.Mask != null)
                tag["mask"] = data.Mask;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            TagIO.ToFile(tag, path);
        }

        public static string Export(Rectangle tileArea, string name,
            StructureFilter filters = StructureFilter.None, IReadOnlySet<Point> mask = null)
        {
            string path = FilePath(ExportDirectory, name);
            Write(Capture(tileArea, filters, mask), path);
            return path;
        }

        // --- Чтение ---

        // Сначала файл внутри мода (Assets/Structures/имя.str), затем папка экспорта —
        // удобно тестировать без пересборки
        public static StructureData Load(Mod mod, string name)
        {
            string modPath = ModFolder + name + Extension;
            if (mod.FileExists(modPath))
                return LoadModFile(mod, modPath);

            string diskPath = FilePath(ExportDirectory, name);
            return File.Exists(diskPath) ? LoadFile(diskPath) : null;
        }

        public static StructureData LoadModFile(Mod mod, string modPath)
        {
            using Stream stream = mod.GetFileStream(modPath);
            return FromTag(TagIO.FromStream(stream));
        }

        public static StructureData LoadFile(string path) => FromTag(TagIO.FromFile(path));

        // null — файл повреждён (размеры массивов не сходятся с w*h)
        public static StructureData FromTag(TagCompound tag)
        {
            int w = tag.GetInt("w");
            int h = tag.GetInt("h");
            if (w <= 0 || h <= 0)
                return null;

            int count = w * h;
            int[] tType = tag.GetIntArray("t");
            int[] frameX = tag.GetIntArray("fx");
            int[] frameY = tag.GetIntArray("fy");
            int[] wall = tag.GetIntArray("wl");
            int[] liquid = tag.GetIntArray("lq");
            int[] slope = tag.GetIntArray("sl");
            if (tType.Length != count || frameX.Length != count || frameY.Length != count
                || wall.Length != count || liquid.Length != count || slope.Length != count)
                return null;

            int[] tileLegend = ResolveTileLegend(tag.GetList<string>("tileLegend"), out int missingTiles);
            int[] wallLegend = ResolveWallLegend(tag.GetList<string>("wallLegend"), out int missingWalls);

            var data = new StructureData(w, h)
            {
                Filters = (StructureFilter)tag.GetInt("filters"),
                TilesFramed = tag.GetBool("framed"),
                MissingTypes = missingTiles + missingWalls
            };

            int[] paint = tag.GetIntArray("pt");     // v1-файлы без покраски → пустой массив
            int[] wallFrames = tag.GetIntArray("wf");
            byte[] mask = tag.ContainsKey("mask") ? tag.GetByteArray("mask") : null;
            if (wallFrames.Length == count)
                data.WallFrames = wallFrames;
            if (mask != null && mask.Length == count)  // до v3 постройки прямоугольные
                data.Mask = mask;

            for (int i = 0; i < count; i++)
            {
                int typeIdx = tType[i];
                data.TileTypes[i] = typeIdx >= 0 && typeIdx < tileLegend.Length ? tileLegend[typeIdx] : StructureData.NoType;
                data.FrameX[i] = (short)frameX[i];
                data.FrameY[i] = (short)frameY[i];
                data.Slopes[i] = (byte)slope[i];

                int wallIdx = wall[i];
                data.WallTypes[i] = wallIdx >= 0 && wallIdx < wallLegend.Length ? wallLegend[wallIdx] : StructureData.NoType;
                data.Liquids[i] = liquid[i];
                if (paint.Length == count)
                    data.Paint[i] = paint[i];

                if (data.TileTypes[i] != StructureData.NoType)
                    data.HasChests |= Main.tileContainer[data.TileTypes[i]];
            }

            return data;
        }

        // Размеры сохранённой постройки без её размещения (для поиска площадки)
        public static bool TryGetSize(Mod mod, string name, out int w, out int h)
        {
            StructureData data = Load(mod, name);
            w = data?.Width ?? 0;
            h = data?.Height ?? 0;
            return w > 0 && h > 0;
        }

        // --- Размещение ---
        // mirror = отразить постройку по горизонтали (для зеркальной стороны мира)

        public static bool Place(Mod mod, string name, int topLeftX, int topLeftY, bool mirror = false)
        {
            StructureData data = Load(mod, name);
            if (data == null)
                return false;

            Place(data, topLeftX, topLeftY, mirror);
            return true;
        }

        public static void Place(StructureData data, int topLeftX, int topLeftY, bool mirror = false)
        {
            int w = data.Width, h = data.Height;

            for (int dx = 0; dx < w; dx++)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    int idx = data.Index(dx, dy);
                    if (!data.InStructure(idx))
                        continue;   // клетка вне выделения — оставляем мир как есть

                    // Зеркалим положение колонки; кадры и склоны отражаем ниже,
                    // чтобы многотайловые объекты и сундуки собрались правильно
                    int destDx = mirror ? w - 1 - dx : dx;
                    int x = topLeftX + destDx, y = topLeftY + dy;
                    if (!WorldGen.InWorld(x, y, 10))
                        continue;

                    Tile tile = Main.tile[x, y];
                    tile.ClearTile();

                    int type = data.TileTypes[idx];
                    if (type != StructureData.NoType)
                    {
                        short fx = data.FrameX[idx];
                        int sl = data.Slopes[idx] & 7;

                        if (mirror)
                        {
                            fx = MirrorFrameX(type, fx);
                            sl = MirrorSlope(sl);
                        }

                        tile.ResetToType((ushort)type);
                        tile.TileFrameX = fx;
                        tile.TileFrameY = data.FrameY[idx];
                        tile.Slope = (SlopeType)sl;
                        tile.IsHalfBlock = (data.Slopes[idx] & 8) != 0;
                    }

                    int wallType = data.WallTypes[idx];
                    tile.WallType = wallType != StructureData.NoType ? (ushort)wallType : WallID.None;

                    tile.LiquidType = data.Liquids[idx] >> 8;
                    tile.LiquidAmount = (byte)(data.Liquids[idx] & 0xFF);

                    UnpackPaint(tile, data.Paint[idx]);
                }
            }

            WorldGen.RangeFrame(topLeftX - 1, topLeftY - 1, topLeftX + w + 1, topLeftY + h + 1);
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

        // --- Легенды ---

        private static int LegendId(int type, System.Func<int, string> keyOf, List<string> legend,
            Dictionary<int, int> index)
        {
            if (index.TryGetValue(type, out int id))
                return id;
            id = legend.Count;
            legend.Add(keyOf(type));
            index[type] = id;
            return id;
        }

        private static string TileKey(int type)
            => type < TileID.Count ? "v:" + type : "m:" + TileLoader.GetTile(type).FullName;

        private static string WallKey(int type)
            => type < WallID.Count ? "v:" + type : "m:" + WallLoader.GetWall(type).FullName;

        // Ключи легенды: "v:<id>" — ванильный по номеру (так пишет экспорт),
        // "n:<имя из TileID/WallID>" — ванильный по имени (так пишут постройки,
        // собранные скриптом: имя не спутать), "m:<Мод/Тайл>" — модовый
        private static int[] ResolveTileLegend(IList<string> legend, out int missing)
        {
            missing = 0;
            int[] result = new int[legend.Count];
            for (int i = 0; i < legend.Count; i++)
            {
                string key = legend[i];
                if (key.StartsWith("v:"))
                    result[i] = int.Parse(key[2..]);
                else if (key.StartsWith("n:"))
                    result[i] = TileID.Search.TryGetId(key[2..], out int id) ? id : -1;
                else if (ModContent.TryFind(key[2..], out ModTile modTile))
                    result[i] = modTile.Type;
                else
                    result[i] = -1; // модовый тайл исчез — клетка останется пустой

                if (result[i] < 0)
                    missing++;
            }
            return result;
        }

        private static int[] ResolveWallLegend(IList<string> legend, out int missing)
        {
            missing = 0;
            int[] result = new int[legend.Count];
            for (int i = 0; i < legend.Count; i++)
            {
                string key = legend[i];
                if (key.StartsWith("v:"))
                    result[i] = int.Parse(key[2..]);
                else if (key.StartsWith("n:"))
                    result[i] = WallID.Search.TryGetId(key[2..], out int id) ? id : -1;
                else if (ModContent.TryFind(key[2..], out ModWall modWall))
                    result[i] = modWall.Type;
                else
                    result[i] = -1;

                if (result[i] < 0)
                    missing++;
            }
            return result;
        }
    }
}
