using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace SoA.Common.Utils
{
    // Режим размещения постройки жезлом: призрак за курсором, клик ставит её в мир.
    // Курсор держит нижний центр постройки — так её удобно ставить на землю.
    // Мир меняется только в одиночной игре (проверяет вызывающий код).
    public static class StructurePlacement
    {
        private static readonly List<Rectangle> _conflictRuns = new();
        private static bool _conflictsDirty = true;

        public static bool Active => Data != null;
        public static StructureData Data { get; private set; }
        public static string Name { get; private set; }
        public static bool Mirror { get; private set; }
        public static Point TopLeft { get; private set; }

        // Клетки мира, где постройка заменит или снесёт чужой тайл (тот же тайл — не считается)
        public static int ConflictCount { get; private set; }
        public static IReadOnlyList<Rectangle> ConflictRuns => _conflictRuns;   // в тайлах, высота 1

        public static Rectangle Area => Data == null
            ? Rectangle.Empty
            : new Rectangle(TopLeft.X, TopLeft.Y, Data.Width, Data.Height);

        public static void Begin(StructureData data, string name)
        {
            Data = data;
            Name = name;
            Mirror = false;
            _conflictsDirty = true;
        }

        public static void End()
        {
            Data = null;
            Name = null;
            _conflictRuns.Clear();
            ConflictCount = 0;
        }

        public static void ToggleMirror()
        {
            Mirror = !Mirror;
            _conflictsDirty = true;
        }

        public static void UpdateCursor(Point cursorTile)
        {
            if (Data == null)
                return;

            var topLeft = new Point(cursorTile.X - Data.Width / 2, cursorTile.Y - Data.Height + 1);
            if (topLeft != TopLeft)
            {
                TopLeft = topLeft;
                _conflictsDirty = true;
            }

            if (_conflictsDirty)
                RebuildConflicts();
        }

        // Ставит постройку и делает её клетки выделением — сразу можно править и пересохранять
        public static void PlaceNow()
        {
            if (Data == null)
                return;

            StructureHistory.RecordPlacement(Area);
            StructureIO.Place(Data, TopLeft.X, TopLeft.Y, Mirror);
            StructureSelection.ReplaceWithoutHistory(PlacedCells(), Name);
            _conflictsDirty = true;
        }

        // Клетка постройки (без учёта зеркала) для клетки мира внутри Area
        private static int StructureIndex(int worldX, int worldY)
        {
            int dx = worldX - TopLeft.X;
            if (Mirror)
                dx = Data.Width - 1 - dx;
            return Data.Index(dx, worldY - TopLeft.Y);
        }

        private static IEnumerable<Point> PlacedCells()
        {
            Rectangle area = Area;
            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    if (Data.InStructure(StructureIndex(x, y)) && WorldGen.InWorld(x, y, 1))
                        yield return new Point(x, y);
                }
            }
        }

        private static void RebuildConflicts()
        {
            _conflictsDirty = false;
            _conflictRuns.Clear();
            ConflictCount = 0;

            Rectangle area = Area;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int runStart = int.MinValue;
                for (int x = area.Left; x <= area.Right; x++)
                {
                    bool conflict = x < area.Right && IsConflict(x, y);
                    if (conflict)
                    {
                        ConflictCount++;
                        if (runStart == int.MinValue)
                            runStart = x;
                    }
                    else if (runStart != int.MinValue)
                    {
                        _conflictRuns.Add(new Rectangle(runStart, y, x - runStart, 1));
                        runStart = int.MinValue;
                    }
                }
            }
        }

        private static bool IsConflict(int x, int y)
        {
            if (!WorldGen.InWorld(x, y, 10))
                return false;

            int idx = StructureIndex(x, y);
            if (!Data.InStructure(idx))
                return false;

            Tile tile = Main.tile[x, y];
            return tile.HasTile && tile.TileType != Data.TileTypes[idx];
        }
    }
}
