using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Graphics
{
    // Белые силуэты спрайтов для вспышек «попадание / полный заряд»: тинт обычного спрайта
    // только затемняет, а вспышке нужен чистый белый по форме. Строится при первой отрисовке
    // (главный поток) и живёт до выгрузки мода
    public class SilhouetteCache : ModSystem
    {
        private static readonly Dictionary<(Texture2D Source, string Mask), Texture2D> Cache = new();

        public static Texture2D Get(Texture2D source) => Get(source, "", static _ => true);

        // Силуэт только тех пикселей, что проходят include: например, лезвие без рукояти —
        // своя маска свечения без отдельного спрайта. mask — имя маски в кэше
        public static Texture2D Get(Texture2D source, string mask, Func<Color, bool> include)
        {
            if (Cache.TryGetValue((source, mask), out Texture2D silhouette) && !silhouette.IsDisposed)
                return silhouette;

            var pixels = new Color[source.Width * source.Height];
            source.GetData(pixels);
            // Премультиплицированный белый: полупрозрачный край остаётся мягким
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = include(pixels[i]) ? Color.White * (pixels[i].A / 255f) : Color.Transparent;

            silhouette = new Texture2D(source.GraphicsDevice, source.Width, source.Height);
            silhouette.SetData(pixels);
            Cache[(source, mask)] = silhouette;
            return silhouette;
        }

        public override void Unload()
        {
            Main.QueueMainThreadAction(() =>
            {
                foreach (Texture2D silhouette in Cache.Values)
                    silhouette.Dispose();
                Cache.Clear();
            });
        }
    }
}
