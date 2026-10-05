using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace SoA.Common.Utils
{
    // Отмена действий жезла. Каждая запись — полный снимок маски выделения, а у
    // размещения ещё и снимок мира под постройкой, поэтому записи независимы и
    // старые можно выбрасывать без порчи цепочки. Чисто клиентское, только одиночная игра.
    public static class StructureHistory
    {
        private const int MaxEntries = 40;
        private const int MaxWorldSnapshots = 8;   // снимок мира до 250x200 клеток — держим немного

        private sealed class Entry
        {
            public HashSet<Point> Cells;
            public string SourceName;
            public StructureData World;
            public Point WorldOrigin;
        }

        private static readonly LinkedList<Entry> _entries = new();
        private static int _worldSnapshots;

        public static bool CanUndo => _entries.Count > 0;

        public static void RecordSelection()
            => Push(new Entry { Cells = StructureSelection.SnapshotCells(), SourceName = StructureSelection.SourceName });

        // Вызывать до того, как постройка ляжет в мир: сохраняем то, что она перезапишет
        public static void RecordPlacement(Rectangle area)
            => Push(new Entry
            {
                Cells = StructureSelection.SnapshotCells(),
                SourceName = StructureSelection.SourceName,
                World = StructureIO.Capture(area),
                WorldOrigin = area.Location
            });

        // false — отменять нечего; restoredWorld — откатили размещение, а не только выделение
        public static bool Undo(out bool restoredWorld)
        {
            restoredWorld = false;
            if (_entries.Count == 0)
                return false;

            Entry entry = _entries.Last.Value;
            RemoveLast();

            if (entry.World != null)
            {
                StructureIO.Place(entry.World, entry.WorldOrigin.X, entry.WorldOrigin.Y);
                restoredWorld = true;
            }

            StructureSelection.RestoreCells(entry.Cells, entry.SourceName);
            return true;
        }

        public static void Clear()
        {
            _entries.Clear();
            _worldSnapshots = 0;
        }

        private static void Push(Entry entry)
        {
            _entries.AddLast(entry);
            if (entry.World != null)
                _worldSnapshots++;

            while (_entries.Count > MaxEntries || _worldSnapshots > MaxWorldSnapshots)
                RemoveFirst();
        }

        private static void RemoveFirst()
        {
            if (_entries.First.Value.World != null)
                _worldSnapshots--;
            _entries.RemoveFirst();
        }

        private static void RemoveLast()
        {
            if (_entries.Last.Value.World != null)
                _worldSnapshots--;
            _entries.RemoveLast();
        }
    }
}
