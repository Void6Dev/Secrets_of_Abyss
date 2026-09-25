using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI.Chat;

namespace SoA.Common.UI
{
    // Примитивы самодельного интерфейса инструмента построек: всё рисуется из
    // MagicPixel и шрифта, отдельных спрайтов под иконки пока нет.
    public static class SoAHudDraw
    {
        public const int BorderThickness = 2;

        // Палитра по мокапу инструмента
        public static readonly Color PanelFill = new(24, 27, 52, 235);
        public static readonly Color PanelBorder = new(72, 84, 150);
        public static readonly Color ButtonFill = new(40, 45, 84);
        public static readonly Color ButtonHoverFill = new(56, 63, 110);
        public static readonly Color ActiveFill = new(232, 193, 74);
        public static readonly Color ActiveBorder = new(255, 226, 130);
        public static readonly Color TitleText = new(244, 205, 96);
        public static readonly Color BodyText = new(198, 204, 228);
        public static readonly Color DimText = new(140, 148, 180);
        public static readonly Color SaveFill = new(64, 148, 72);
        public static readonly Color SaveBorder = new(126, 214, 130);
        public static readonly Color CancelFill = new(56, 68, 138);
        public static readonly Color CancelBorder = new(110, 130, 210);
        public static readonly Color CloseFill = new(176, 52, 52);
        public static readonly Color CloseBorder = new(240, 120, 120);

        private static Texture2D Pixel => TextureAssets.MagicPixel.Value;

        // --- Текст ---

        public static void Text(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale = 1f)
            => ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, text,
                position, color, 0f, Vector2.Zero, new Vector2(scale));

        public static void TextCentered(SpriteBatch spriteBatch, string text, Rectangle area, Color color, float scale = 1f)
        {
            Vector2 size = Measure(text, scale);
            var position = new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + (area.Height - size.Y) / 2f);
            Text(spriteBatch, text, position, color, scale);
        }

        public static Vector2 Measure(string text, float scale = 1f)
            => FontAssets.MouseText.Value.MeasureString(text) * scale;

        // --- Плашки и рамки ---

        public static void Fill(SpriteBatch spriteBatch, Rectangle area, Color color)
            => spriteBatch.Draw(Pixel, area, color);

        public static void Frame(SpriteBatch spriteBatch, Rectangle area, Color color, int thickness = BorderThickness)
        {
            spriteBatch.Draw(Pixel, new Rectangle(area.X, area.Y, area.Width, thickness), color);
            spriteBatch.Draw(Pixel, new Rectangle(area.X, area.Bottom - thickness, area.Width, thickness), color);
            spriteBatch.Draw(Pixel, new Rectangle(area.X, area.Y, thickness, area.Height), color);
            spriteBatch.Draw(Pixel, new Rectangle(area.Right - thickness, area.Y, thickness, area.Height), color);
        }

        public static void Panel(SpriteBatch spriteBatch, Rectangle area, Color fill, Color border)
        {
            Fill(spriteBatch, area, fill);
            Frame(spriteBatch, area, border);
        }

        // Пунктирная рамка — иконка режима выделения
        public static void DashedFrame(SpriteBatch spriteBatch, Rectangle area, Color color,
            int dash = 4, int gap = 3, int thickness = 2)
        {
            int step = dash + gap;

            for (int x = area.X; x < area.Right; x += step)
            {
                int width = Math.Min(dash, area.Right - x);
                spriteBatch.Draw(Pixel, new Rectangle(x, area.Y, width, thickness), color);
                spriteBatch.Draw(Pixel, new Rectangle(x, area.Bottom - thickness, width, thickness), color);
            }

            for (int y = area.Y; y < area.Bottom; y += step)
            {
                int height = Math.Min(dash, area.Bottom - y);
                spriteBatch.Draw(Pixel, new Rectangle(area.X, y, thickness, height), color);
                spriteBatch.Draw(Pixel, new Rectangle(area.Right - thickness, y, thickness, height), color);
            }
        }

        // --- Фигуры ---

        public static void Line(SpriteBatch spriteBatch, Vector2 from, Vector2 to, float thickness, Color color)
        {
            Vector2 delta = to - from;
            float rotation = MathF.Atan2(delta.Y, delta.X);
            spriteBatch.Draw(Pixel, from, new Rectangle(0, 0, 1, 1), color, rotation,
                new Vector2(0f, 0.5f), new Vector2(delta.Length(), thickness), SpriteEffects.None, 0f);
        }

        public static void Plus(SpriteBatch spriteBatch, Vector2 center, int size, int thickness, Color color)
        {
            int half = size / 2;
            spriteBatch.Draw(Pixel, new Rectangle((int)center.X - half, (int)center.Y - thickness / 2, size, thickness), color);
            spriteBatch.Draw(Pixel, new Rectangle((int)center.X - thickness / 2, (int)center.Y - half, thickness, size), color);
        }

        public static void Cross(SpriteBatch spriteBatch, Vector2 center, int size, float thickness, Color color)
        {
            float half = size / 2f;
            Line(spriteBatch, center + new Vector2(-half, -half), center + new Vector2(half, half), thickness, color);
            Line(spriteBatch, center + new Vector2(half, -half), center + new Vector2(-half, half), thickness, color);
        }

        public static void CheckMark(SpriteBatch spriteBatch, Rectangle box, Color color)
        {
            var low = new Vector2(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f);
            var corner = new Vector2(box.X + box.Width * 0.44f, box.Y + box.Height * 0.74f);
            var high = new Vector2(box.X + box.Width * 0.78f, box.Y + box.Height * 0.26f);
            Line(spriteBatch, low, corner, 2.5f, color);
            Line(spriteBatch, corner, high, 2.5f, color);
        }

        public static void Circle(SpriteBatch spriteBatch, Vector2 center, int radius, Color color, int ringThickness = 0)
        {
            int innerRadius = radius - ringThickness;

            for (int dy = -radius; dy <= radius; dy++)
            {
                int half = (int)MathF.Sqrt(radius * radius - dy * dy);
                int y = (int)center.Y + dy;

                int innerSquared = innerRadius * innerRadius - dy * dy;
                if (ringThickness <= 0 || innerSquared <= 0)
                {
                    spriteBatch.Draw(Pixel, new Rectangle((int)center.X - half, y, half * 2 + 1, 1), color);
                    continue;
                }

                int innerHalf = (int)MathF.Sqrt(innerSquared);
                int segment = half - innerHalf + 1;
                spriteBatch.Draw(Pixel, new Rectangle((int)center.X - half, y, segment, 1), color);
                spriteBatch.Draw(Pixel, new Rectangle((int)center.X + innerHalf, y, segment, 1), color);
            }
        }

        // Курсор-стрелка: заглушка под иконку режима выделения
        public static void CursorArrow(SpriteBatch spriteBatch, Vector2 tip, float size, Color color, Color outline)
        {
            for (int row = 0; row < (int)size; row++)
            {
                int width = Math.Max(1, (int)(row * 0.62f));
                spriteBatch.Draw(Pixel, new Rectangle((int)tip.X, (int)tip.Y + row, width, 1), color);
            }
            Line(spriteBatch, tip, tip + new Vector2(0f, size), 1f, outline);
            Line(spriteBatch, tip, tip + new Vector2(size * 0.62f, size), 1f, outline);
        }

        // Глиф мыши для строк управления в тултипе; highlightLeft — подсветить ЛКМ
        public static void MouseGlyph(SpriteBatch spriteBatch, Rectangle area, bool highlightLeft, Color body, Color highlight)
        {
            Panel(spriteBatch, area, body, highlight * 0.5f);

            int buttonHeight = area.Height / 2 - 2;
            int buttonWidth = area.Width / 2 - 2;
            var button = highlightLeft
                ? new Rectangle(area.X + 2, area.Y + 2, buttonWidth, buttonHeight)
                : new Rectangle(area.Right - 2 - buttonWidth, area.Y + 2, buttonWidth, buttonHeight);
            Fill(spriteBatch, button, highlight);
        }

        // Карандаш у поля ввода имени
        public static void Pencil(SpriteBatch spriteBatch, Rectangle area, Color color)
        {
            var tip = new Vector2(area.X, area.Bottom);
            var end = new Vector2(area.Right, area.Y);
            Line(spriteBatch, tip, end, 2.5f, color);
            Line(spriteBatch, tip, tip + new Vector2(area.Width * 0.28f, -area.Height * 0.1f), 2f, color);
        }

        // Жезл на кнопке сохранения: палочка с ромбовидным навершием
        public static void Wand(SpriteBatch spriteBatch, Rectangle area, Color shaft, Color gem)
        {
            var bottom = new Vector2(area.X + area.Width * 0.28f, area.Bottom - 2);
            var top = new Vector2(area.Right - area.Width * 0.3f, area.Y + area.Height * 0.34f);
            Line(spriteBatch, bottom, top, 3f, shaft);

            int gemSize = Math.Max(4, area.Width / 4);
            for (int row = -gemSize; row <= gemSize; row++)
            {
                int width = gemSize - Math.Abs(row);
                if (width <= 0)
                    continue;
                spriteBatch.Draw(Pixel, new Rectangle((int)top.X - width, (int)top.Y + row, width * 2, 1), gem);
            }
        }
    }
}
