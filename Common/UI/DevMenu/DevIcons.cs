using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace SoA.Common.UI
{
    // Необязательные иконки интерфейса инструмента. Пока png нет, всё рисуется
    // примитивами; как только файл появится в папке — он подхватится сам,
    // править код не нужно. Имена файлов перечислены в дев-меню.
    public static class DevIcons
    {
        // Путь регистрозависим — должен совпадать с папкой в проекте буква в букву
        public const string Folder = "SoA/Content/Items/DevTools/icons/";

        private static readonly Dictionary<string, Texture2D> _cache = new();

        public static void ClearCache() => _cache.Clear();

        public static Texture2D Get(string name)
        {
            if (_cache.TryGetValue(name, out Texture2D cached))
                return cached;

            string path = Folder + name;
            Texture2D texture = ModContent.HasAsset(path)
                ? ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value
                : null;

            _cache[name] = texture;
            return texture;
        }

        // true — нарисовали спрайт, false — файла нет, рисуй заглушку
        public static bool TryDraw(SpriteBatch spriteBatch, string name, Rectangle box, Color color)
        {
            Texture2D texture = Get(name);
            if (texture == null)
                return false;

            spriteBatch.Draw(texture, box, color);
            return true;
        }
    }
}
