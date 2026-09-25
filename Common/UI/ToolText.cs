using Terraria.Localization;

namespace SoA.Common.UI
{
    // Доступ к подписям интерфейса инструмента построек. Ключи лежат в
    // Localization/*.hjson под Mods.SoA.StructureTool, многострочные значения
    // приходят одной строкой с переводами строк.
    public static class ToolText
    {
        private const string Prefix = "Mods.SoA.StructureTool.";

        public static string Get(string key) => Language.GetTextValue(Prefix + key);

        public static string Get(string key, params object[] args)
            => Language.GetTextValue(Prefix + key, args);

        public static string[] Lines(string key) => Get(key).Split('\n');
    }
}
