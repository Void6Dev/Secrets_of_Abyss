using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SoA.Common.UI;
using Terraria;
using Terraria.GameContent;

namespace SoA.Common.Graphics.SandFormation
{
    // Отрисовка сборки. Всё рисуется квадами из MagicPixel по целым пикселям —
    // никаких мягких блобов и размытия: зерно должно оставаться зерном.
    //
    // Батч не свапается: свечение делается цветом с A = 0. Батч Терарии
    // premultiplied (BlendState.AlphaBlend), поэтому такой цвет складывается
    // с фоном, то есть работает как аддитив, но без второго Begin/End.
    public static class SandFormationRenderer
    {
        // Спрайт проявляется под уже севшими зёрнами — стык не читается
        private const float SpriteFadeFrom = 0.86f;
        private const float SpriteFadeTo = 0.99f;

        // Последние зёрна гаснут ровно тогда, когда спрайт уже целый
        private const float GrainFadeFrom = 0.965f;

        private const int BurstGrains = 16;
        private const float MaxTrailLength = 3f;

        private static Texture2D Pixel => TextureAssets.MagicPixel.Value;

        // MagicPixel НЕ размером 1x1 — это узкая длинная текстура. Без явного
        // источника квад растягивается на всю её высоту (зерно превращается в
        // полосу во весь экран). Ванильный код по той же причине везде передаёт
        // сюда Rectangle(0, 0, 1, 1)
        private static readonly Rectangle PixelSource = new(0, 0, 1, 1);

        public static void Draw(SpriteBatch spriteBatch, SandFormationEffect effect, Vector2 drawPos,
            Color light, float rotation, Vector2 origin, float scale)
        {
            Texture2D texture = effect.Texture;
            if (texture == null)
                return;

            float progress = effect.FormationProgress;
            float spriteAlpha = SandFormationEffect.SmoothStep(
                SandFormationEffect.Remap(progress, SpriteFadeFrom, SpriteFadeTo));

            // Силуэт ещё не снят (первый кадр) или текстуру не прочитать —
            // показываем обычный спрайт, чтобы предмет не пропал совсем
            if (!effect.IsReady)
            {
                DrawSprite(texture, drawPos, light * progress, rotation, origin, scale);
                return;
            }

            // Центр спрайта в экранных координатах: origin у владельца может быть
            // каким угодно (рукоять копья), а зёрна живут в системе центра текстуры
            Vector2 anchor = drawPos + (texture.Size() * 0.5f - origin).RotatedBy(rotation) * scale;

            float grainAlpha = 1f - SandFormationEffect.SmoothStep(
                SandFormationEffect.Remap(progress, GrainFadeFrom, 1f));

            if (effect.Settings.BackgroundGlow && effect.Glow > 0.25f)
                DrawBackgroundGlow(effect, anchor, scale);

            DrawSprite(texture, drawPos, light * spriteAlpha, rotation, origin, scale);

            DrawGrains(effect, anchor, rotation, scale, grainAlpha);

            if (effect.BurstTimer > 0f)
                DrawBurst(effect, anchor, rotation, scale, texture, drawPos, origin);

            if (SandFormationEffect.Debug)
                DrawDebug(spriteBatch, effect, anchor, rotation, scale);
        }

        private static void DrawSprite(Texture2D texture, Vector2 drawPos, Color color,
            float rotation, Vector2 origin, float scale)
        {
            if (color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0)
                return;
            Main.EntitySpriteDraw(texture, drawPos, null, color, rotation, origin, scale,
                SpriteEffects.None, 0);
        }

        // Порядок слоёв: орбита → хвосты → обычный песок → яркий песок → кайма.
        // Один проход по массиву на слой дороже по циклам, но дешевле по батчу:
        // квады идут группами и не перемешиваются
        private static void DrawGrains(SandFormationEffect effect, Vector2 anchor, float rotation,
            float scale, float grainAlpha)
        {
            if (grainAlpha <= 0.002f)
                return;

            SandFormationParticle[] particles = effect.Particles;

            for (int layer = 0; layer < 5; layer++)
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    ref SandFormationParticle particle = ref particles[i];

                    bool bright = particle.Brightness > 0.82f;
                    bool orbit = particle.Kind == SandFormationGrainKind.Orbit;
                    bool edge = particle.Kind == SandFormationGrainKind.Edge;

                    bool inLayer = layer switch
                    {
                        0 => orbit,
                        1 => !orbit,                       // хвосты обычных зёрен
                        2 => !orbit && !bright && !edge,
                        3 => !orbit && bright && !edge,
                        _ => edge,
                    };
                    if (!inLayer)
                        continue;

                    float alpha = grainAlpha * SandFormationEffect.Saturate(particle.Life * 6f);
                    if (orbit)
                        alpha *= 1f - effect.Completion;
                    if (alpha <= 0.002f)
                        continue;

                    Vector2 screen = ToScreen(anchor, particle.Position, rotation, scale);

                    if (layer == 1)
                    {
                        DrawTrail(in particle, anchor, rotation, scale, alpha);
                        continue;
                    }

                    Color color = GrainColor(effect, in particle);
                    DrawGrain(screen, particle.Size * scale, color * (alpha * particle.Brightness));

                    // Свечение — не размытие, а пара ярких пикселей рядом с зерном
                    if (bright && effect.Glow > 0.3f)
                        DrawSpark(screen, particle.Size * scale, color, alpha * effect.Glow);
                }
            }
        }

        // К концу сборки зерно перекрашивается в цвет своей точки спрайта:
        // подмена песка настоящей текстурой перестаёт быть заметной
        private static Color GrainColor(SandFormationEffect effect, in SandFormationParticle particle)
        {
            if (effect.Formation <= 0f)
                return particle.Color;
            return Color.Lerp(particle.Color, particle.TargetColor, effect.Formation * 0.55f);
        }

        private static void DrawTrail(in SandFormationParticle particle, Vector2 anchor, float rotation,
            float scale, float alpha)
        {
            float trailLength = MathHelper.Clamp(particle.Velocity.Length() / 120f, 0f, MaxTrailLength);
            int steps = (int)trailLength;
            if (steps <= 0)
                return;

            Color tail = SandFormationPalette.Ramp[SandFormationPalette.TrailIndex];

            for (int t = 0; t < steps; t++)
            {
                Vector2 previous = t switch
                {
                    0 => particle.Trail0,
                    1 => particle.Trail1,
                    _ => particle.Trail2,
                };

                float fade = alpha * (1f - (t + 1f) / (MaxTrailLength + 1f)) * 0.7f;
                DrawGrain(ToScreen(anchor, previous, rotation, scale), scale, tail * fade);
            }
        }

        // Формы зерна: 1x1, 2x2 и «ступенька» 3x2 + 2x1 — так пиксели читаются
        // как крупинки, а не как круглые точки
        private static void DrawGrain(Vector2 screen, float size, Color color)
        {
            if (color.A == 0 && color.R + color.G + color.B == 0)
                return;

            Vector2 pixel = Align(screen);

            if (size <= 1.4f)
            {
                Quad(pixel, 1f, 1f, color);
                return;
            }

            if (size <= 2.4f)
            {
                Quad(pixel, 2f, 2f, color);
                return;
            }

            Quad(pixel, 3f, 2f, color);
            Quad(pixel + new Vector2(1f, 2f), 2f, 1f, color);
        }

        private static void DrawSpark(Vector2 screen, float size, Color color, float intensity)
        {
            Vector2 pixel = Align(screen);
            float offset = MathHelper.Max(size, 1f) + 1f;

            // Цвет с A = 0 в premultiplied-батче складывается с фоном — это и есть свечение
            var glow = new Color(color.R, color.G, color.B, 0) * (intensity * 0.35f);

            Quad(pixel + new Vector2(-offset, 0f), 1f, 1f, glow);
            Quad(pixel + new Vector2(offset, 0f), 1f, 1f, glow);
            Quad(pixel + new Vector2(0f, -offset), 1f, 1f, glow);
            Quad(pixel + new Vector2(0f, offset), 1f, 1f, glow);
        }

        // Тёплая подложка под сборкой: редкая сетка тусклых пикселей, а не мягкий ореол
        private static void DrawBackgroundGlow(SandFormationEffect effect, Vector2 anchor, float scale)
        {
            const int Sparks = 10;
            float radius = effect.Settings.FormationRadius * (1.1f - effect.Completion * 0.6f);
            var color = new Color(0xE6, 0x9A, 0x35, 0) * (0.10f * effect.Glow);

            for (int i = 0; i < Sparks; i++)
            {
                float angle = MathHelper.TwoPi * i / Sparks + effect.Time * 0.6f;
                var local = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle) * 0.8f) * radius;
                Quad(Align(anchor + local * scale), 2f, 2f, color);
            }
        }

        // Последнее зерно легло: короткий выброс песка по контуру и золотая
        // подсветка спрайта. Именно короткий — это точка, а не взрыв
        private static void DrawBurst(SandFormationEffect effect, Vector2 anchor, float rotation,
            float scale, Texture2D texture, Vector2 drawPos, Vector2 origin)
        {
            SandFormationSilhouette silhouette = effect.Silhouette;
            float fade = effect.BurstFade;
            float travel = 1f - fade;

            if (silhouette.EdgePoints.Length > 0)
            {
                Color spark = SandFormationPalette.Ramp[6];
                var additive = new Color(spark.R, spark.G, spark.B, 0);

                for (int i = 0; i < BurstGrains; i++)
                {
                    int edge = SandFormationRandom.Index(effect.Seed, i, 40, silhouette.EdgePoints.Length);
                    float distance = SandFormationRandom.Range(effect.Seed, i, 41, 3f, 12f) * travel;

                    Vector2 local = silhouette.EdgePoints[edge] + silhouette.EdgeNormals[edge] * (1f + distance);
                    float size = (SandFormationRandom.Value(effect.Seed, i, 42) < 0.5f ? 1f : 2f) * scale;
                    DrawGrain(ToScreen(anchor, local, rotation, scale), size, additive * fade);
                }
            }

            DrawSprite(texture, drawPos, new Color(255, 226, 160, 0) * (0.35f * fade),
                rotation, origin, scale);
        }

        private static void DrawDebug(SpriteBatch spriteBatch, SandFormationEffect effect, Vector2 anchor,
            float rotation, float scale)
        {
            SandFormationParticle[] particles = effect.Particles;

            for (int i = 0; i < particles.Length; i++)
            {
                ref SandFormationParticle particle = ref particles[i];
                Quad(Align(ToScreen(anchor, particle.TargetPosition, rotation, scale)), 1f, 1f, Color.Lime * 0.5f);
                Quad(Align(ToScreen(anchor, particle.StartPosition, rotation, scale)), 1f, 1f, Color.Red * 0.35f);
            }

            DrawCircle(anchor, effect.Settings.FormationRadius * scale, Color.Cyan * 0.5f);
            DrawCircle(anchor, effect.Settings.SpreadRadius * scale, Color.Orange * 0.35f);

            string info = $"progress {effect.FormationProgress:0.000}\n"
                + $"grains {effect.ParticleCount}\n"
                + $"seed {effect.Seed}\n"
                + $"radius {effect.Settings.FormationRadius:0.#} / spread {effect.Settings.SpreadRadius:0.#}\n"
                + $"fps {Main.frameRate}";
            SoAHudDraw.Text(spriteBatch, info, anchor + new Vector2(effect.Settings.SpreadRadius * scale + 6f, -20f),
                Color.White, 0.75f);
        }

        private static void DrawCircle(Vector2 center, float radius, Color color)
        {
            const int Steps = 32;
            for (int i = 0; i < Steps; i++)
            {
                float angle = MathHelper.TwoPi * i / Steps;
                Quad(Align(center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius),
                    1f, 1f, color);
            }
        }

        private static Vector2 ToScreen(Vector2 anchor, Vector2 local, float rotation, float scale)
            => anchor + local.RotatedBy(rotation) * scale;

        // Целые экранные пиксели: без этого зёрна «плывут» и мылятся
        private static Vector2 Align(Vector2 position)
            => new((float)Math.Floor(position.X), (float)Math.Floor(position.Y));

        private static void Quad(Vector2 position, float width, float height, Color color)
            => Main.EntitySpriteDraw(Pixel, position, PixelSource, color, 0f, Vector2.Zero,
                new Vector2(MathHelper.Max(width, 1f), MathHelper.Max(height, 1f)), SpriteEffects.None, 0);
    }
}
