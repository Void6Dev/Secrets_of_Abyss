using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ObjectData;

namespace SoA.Common.Utils
{
    // Режим, в котором очередной мазок мышью применяется к маске
    public enum SelectionMode
    {
        Replace,
        Add,
        Erase
    }

    // Что выкинуть из постройки при экспорте
    [Flags]
    public enum StructureFilter
    {
        None = 0,
        SkipWalls = 1,
        SkipLiquids = 2,
        SkipGrass = 4,
        SkipCobwebs = 8,
        SkipVines = 16,
        SkipLightSources = 32
    }

    // Клиентская маска выделения для StructureWand: набор произвольных клеток,
    // который набирается прямоугольными мазками мыши. Состояние статическое —
    // жезл, рендерер и HUD смотрят на одну и ту же маску; сбрасывается при выходе из мира.
    public static class StructureSelection
    {
        private static readonly HashSet<Point> _cells = new();

        private static Point _dragAnchor;
        private static Point _dragCursor;
        private static Rectangle _bounds;
        private static bool _boundsDirty = true;

        public static SelectionMode Mode { get; private set; } = SelectionMode.Replace;
        public static StructureFilter Filters { get; private set; } = StructureFilter.None;
        public static bool Dragging { get; private set; }

        public static int Count => _cells.Count;
        public static IReadOnlySet<Point> Cells => _cells;

        // Растёт при любом изменении маски — рендерер по нему пересобирает геометрию
        public static int Version { get; private set; }

        // Габаритный прямоугольник маски; пустой, если ничего не выделено
        public static Rectangle Bounds
        {
            get
            {
                if (_boundsDirty)
                {
                    _bounds = ComputeBounds();
                    _boundsDirty = false;
                }
                return _bounds;
            }
        }

        public static bool OverLimit
            => Bounds.Width > StructureIO.MaxWidth || Bounds.Height > StructureIO.MaxHeight;

        public static bool Contains(int x, int y) => _cells.Contains(new Point(x, y));

        public static void CycleMode() => Mode = (SelectionMode)(((int)Mode + 1) % 3);

        public static void SetMode(SelectionMode mode) => Mode = mode;

        public static void ToggleFilter(StructureFilter filter) => Filters ^= filter;

        public static bool HasFilter(StructureFilter filter) => (Filters & filter) != 0;

        public static void Clear()
        {
            _cells.Clear();
            Dragging = false;
            Touch();
        }

        // --- Протяжка мышью ---

        public static void BeginDrag(Point tile)
        {
            _dragAnchor = tile;
            _dragCursor = tile;
            Dragging = true;
        }

        public static void UpdateDrag(Point tile)
        {
            if (Dragging)
                _dragCursor = tile;
        }

        public static void CancelDrag() => Dragging = false;

        // Прямоугольник текущего мазка. Тянется в сторону курсора, но упершись
        // в максимальный размер постройки — перестаёт расти (clamped = упёрся).
        public static Rectangle DragArea(out bool clamped)
            => RectFromDrag(_dragAnchor, _dragCursor, out clamped);

        public static void CommitDrag()
        {
            if (!Dragging)
                return;

            Dragging = false;
            Rectangle area = RectFromDrag(_dragAnchor, _dragCursor, out _);

            if (Mode == SelectionMode.Replace)
                _cells.Clear();

            if (Mode == SelectionMode.Erase)
                RemoveArea(area);
            else
                AddArea(area);

            Touch();
        }

        internal static Rectangle RectFromDrag(Point anchor, Point cursor, out bool clamped)
        {
            int spanX = cursor.X - anchor.X; // знак = направление растягивания
            int spanY = cursor.Y - anchor.Y;
            int w = Math.Abs(spanX) + 1;
            int h = Math.Abs(spanY) + 1;

            clamped = w > StructureIO.MaxWidth || h > StructureIO.MaxHeight;
            w = Math.Min(w, StructureIO.MaxWidth);
            h = Math.Min(h, StructureIO.MaxHeight);

            int left = spanX >= 0 ? anchor.X : anchor.X - (w - 1);
            int top = spanY >= 0 ? anchor.Y : anchor.Y - (h - 1);
            return new Rectangle(left, top, w, h);
        }

        // --- Дополнение маски до целых мультитайлов ---

        // Половина сундука или двери сохранилась бы битым кадром, поэтому перед
        // экспортом расширяем маску на все клетки затронутых многотайловых объектов.
        // Возвращает, сколько клеток добавилось.
        public static int ExpandToWholeObjects()
        {
            var extra = new HashSet<Point>();

            foreach (Point cell in _cells)
            {
                if (!WorldGen.InWorld(cell.X, cell.Y, 1))
                    continue;

                Tile tile = Main.tile[cell.X, cell.Y];
                if (!tile.HasTile || !TryGetObjectBounds(cell.X, cell.Y, tile, out Rectangle obj))
                    continue;

                for (int x = obj.Left; x < obj.Right; x++)
                {
                    for (int y = obj.Top; y < obj.Bottom; y++)
                    {
                        var p = new Point(x, y);
                        if (!_cells.Contains(p) && WorldGen.InWorld(x, y, 1))
                            extra.Add(p);
                    }
                }
            }

            foreach (Point p in extra)
                _cells.Add(p);

            if (extra.Count > 0)
                Touch();
            return extra.Count;
        }

        // Восстанавливает верхний левый угол многотайлового объекта по кадру тайла.
        // Одиночные тайлы и растения (без TileObjectData) объектами не считаются.
        private static bool TryGetObjectBounds(int x, int y, Tile tile, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;

            TileObjectData data = TileObjectData.GetTileData(tile);
            if (data == null || data.CoordinateHeights == null)
                return false;
            if (data.Width <= 1 && data.Height <= 1)
                return false;

            int stepX = data.CoordinateWidth + data.CoordinatePadding;
            if (stepX <= 0)
                return false;

            int fullWidth = data.Width * stepX;
            int subX = tile.TileFrameX % fullWidth / stepX;

            // Ряды объекта могут иметь разную высоту (двери, платформы), поэтому
            // ищем ряд накоплением высот, а не делением
            int fullHeight = 0;
            for (int row = 0; row < data.Height; row++)
                fullHeight += data.CoordinateHeights[row] + data.CoordinatePadding;
            if (fullHeight <= 0)
                return false;

            int remainder = tile.TileFrameY % fullHeight;
            int subY = data.Height - 1;
            int accumulated = 0;
            for (int row = 0; row < data.Height; row++)
            {
                accumulated += data.CoordinateHeights[row] + data.CoordinatePadding;
                if (remainder < accumulated)
                {
                    subY = row;
                    break;
                }
            }

            bounds = new Rectangle(x - subX, y - subY, data.Width, data.Height);
            return true;
        }

        // --- Внутреннее ---

        private static void AddArea(Rectangle area)
        {
            for (int x = area.Left; x < area.Right; x++)
                for (int y = area.Top; y < area.Bottom; y++)
                    if (WorldGen.InWorld(x, y, 1))
                        _cells.Add(new Point(x, y));
        }

        private static void RemoveArea(Rectangle area)
        {
            for (int x = area.Left; x < area.Right; x++)
                for (int y = area.Top; y < area.Bottom; y++)
                    _cells.Remove(new Point(x, y));
        }

        private static void Touch()
        {
            _boundsDirty = true;
            Version++;
        }

        private static Rectangle ComputeBounds()
        {
            if (_cells.Count == 0)
                return Rectangle.Empty;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (Point c in _cells)
            {
                if (c.X < minX) minX = c.X;
                if (c.Y < minY) minY = c.Y;
                if (c.X > maxX) maxX = c.X;
                if (c.Y > maxY) maxY = c.Y;
            }
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}
