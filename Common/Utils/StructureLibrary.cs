using System;
using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader;

namespace SoA.Common.Utils
{
    // Откуда взят файл постройки. Порядок = порядок в списке библиотеки
    public enum StructureSource
    {
        Mod,        // упакован в .tmod — его видит генерация мира
        Sources,    // Assets/Structures в исходниках, попадёт в мод при следующей сборке
        Export      // папка экспорта жезла, генерация берёт его, если в моде такого нет
    }

    public readonly record struct StructureEntry(string Name, StructureSource Source, string Path);

    // Все доступные файлы построек для окна библиотеки
    public static class StructureLibrary
    {
        public static List<StructureEntry> Scan(Mod mod)
        {
            var entries = new List<StructureEntry>();

            foreach (string file in mod.GetFileNames() ?? new List<string>())
            {
                if (file.StartsWith(StructureIO.ModFolder) && file.EndsWith(StructureIO.Extension))
                    entries.Add(new StructureEntry(Path.GetFileNameWithoutExtension(file), StructureSource.Mod, file));
            }

            AddDirectory(entries, StructureIO.SourceDirectory(mod), StructureSource.Sources);
            AddDirectory(entries, StructureIO.ExportDirectory, StructureSource.Export);

            entries.Sort((a, b) =>
            {
                int bySource = a.Source.CompareTo(b.Source);
                return bySource != 0 ? bySource : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return entries;
        }

        // null — файл не читается или повреждён
        public static StructureData Load(Mod mod, StructureEntry entry)
        {
            try
            {
                return entry.Source == StructureSource.Mod
                    ? StructureIO.LoadModFile(mod, entry.Path)
                    : StructureIO.LoadFile(entry.Path);
            }
            catch (Exception exception)
            {
                mod.Logger.Warn($"Structure '{entry.Path}' failed to load: {exception.Message}");
                return null;
            }
        }

        // Папка на диске, где лежит файл; для упакованных в мод — исходники или экспорт
        public static string FolderOf(Mod mod, StructureEntry entry)
        {
            if (entry.Source != StructureSource.Mod)
                return Path.GetDirectoryName(entry.Path);

            string sources = StructureIO.SourceDirectory(mod);
            return Directory.Exists(sources) ? sources : StructureIO.ExportDirectory;
        }

        private static void AddDirectory(List<StructureEntry> entries, string directory, StructureSource source)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (string path in Directory.GetFiles(directory, "*" + StructureIO.Extension))
                entries.Add(new StructureEntry(Path.GetFileNameWithoutExtension(path), source, path));
        }
    }
}
