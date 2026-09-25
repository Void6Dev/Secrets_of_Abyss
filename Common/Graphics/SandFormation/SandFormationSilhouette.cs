using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Common.Graphics.SandFormation
{
    // Точки посадки зёрен, снятые с альфа-канала спрайта. Ничего про конкретный
    // предмет не знает: меч, кирка, зелье, самоцвет — всё раскладывается одинаково.
    //
    // Текстура читается ОДИН раз на спрайт и кладётся в кеш: GetData дорогая и на
    // GPU-ресурсе, дёргать её в кадре нельзя. Вызывать только из апдейта (не из
    // отрисовки) — во время draw текстура может быть привязана к сэмплеру.
    public sealed class SandFormationSilhouette
    {
        // Больше точек, чем зёрен, всё равно не нужно: по ним только выбирают цель
        private const int MaxPoints = 1024;
        private const byte AlphaThreshold = 100;

        private static readonly Dictionary<Texture2D, SandFormationSilhouette> Cache = new();
        private static readonly SandFormationSilhouette Empty = new();

        // Координаты относительно центра спрайта, в пикселях спрайта
        public Vector2[] Points { get; private set; } = Array.Empty<Vector2>();

        // Цвет спрайта в этих точках — по нему зерно перекрашивается перед подменой
        // песка настоящим спрайтом, чтобы стык не читался
        public Color[] Colors { get; private set; } = Array.Empty<Color>();

        // Точки по контуру и наружные нормали в них: кайма и финальная вспышка
        public Vector2[] EdgePoints { get; private set; } = Array.Empty<Vector2>();
        public Vector2[] EdgeNormals { get; private set; } = Array.Empty<Vector2>();

        public bool IsEmpty => Points.Length == 0;

        public static SandFormationSilhouette Get(Texture2D texture)
        {
            if (texture == null || texture.Width <= 0 || texture.Height <= 0)
                return Empty;

            if (Cache.TryGetValue(texture, out SandFormationSilhouette cached))
                return cached;

            SandFormationSilhouette built = Build(texture);
            Cache[texture] = built;
            return built;
        }

        public static void ClearCache() => Cache.Clear();

        private static SandFormationSilhouette Build(Texture2D texture)
        {
            int width = texture.Width;
            int height = texture.Height;

            Color[] pixels = new Color[width * height];
            try
            {
                texture.GetData(pixels);
            }
            catch (Exception)
            {
                // Нестандартный формат поверхности или занятый ресурс: сборка просто
                // не запустится, вместо неё вызывающий увидит обычный спрайт
                return Empty;
            }

            // Шаг выборки: с крупного спрайта берём каждый второй-третий пиксель,
            // чтобы точек назначения было около MaxPoints
            int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(width * height / (double)MaxPoints)));

            var points = new List<Vector2>(MaxPoints);
            var colors = new List<Color>(MaxPoints);
            var edgePoints = new List<Vector2>(MaxPoints / 2);
            var edgeNormals = new List<Vector2>(MaxPoints / 2);

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    if (!IsSolid(pixels, width, height, x, y))
                        continue;

                    // +0.5 — центр пикселя: иначе силуэт съезжает на полпикселя вверх-влево
                    var local = new Vector2(x - width * 0.5f + 0.5f, y - height * 0.5f + 0.5f);
                    points.Add(local);
                    colors.Add(pixels[y * width + x]);

                    Vector2 outward = OutwardNormal(pixels, width, height, x, y, step);
                    if (outward != Vector2.Zero)
                    {
                        edgePoints.Add(local);
                        edgeNormals.Add(Vector2.Normalize(outward));
                    }
                }
            }

            return new SandFormationSilhouette
            {
                Points = points.ToArray(),
                Colors = colors.ToArray(),
                EdgePoints = edgePoints.ToArray(),
                EdgeNormals = edgeNormals.ToArray(),
            };
        }

        private static bool IsSolid(Color[] pixels, int width, int height, int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                return false;
            return pixels[y * width + x].A > AlphaThreshold;
        }

        // Сумма направлений на прозрачных соседей: нулевой вектор — точка внутри силуэта
        private static Vector2 OutwardNormal(Color[] pixels, int width, int height, int x, int y, int step)
        {
            Vector2 outward = Vector2.Zero;

            if (!IsSolid(pixels, width, height, x - step, y))
                outward.X -= 1f;
            if (!IsSolid(pixels, width, height, x + step, y))
                outward.X += 1f;
            if (!IsSolid(pixels, width, height, x, y - step))
                outward.Y -= 1f;
            if (!IsSolid(pixels, width, height, x, y + step))
                outward.Y += 1f;

            return outward;
        }
    }
}
