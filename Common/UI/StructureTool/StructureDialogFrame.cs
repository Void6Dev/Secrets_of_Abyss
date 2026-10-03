using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace SoA.Common.UI
{
    // Общая рамка окон инструмента построек: панель по центру экрана, плашка
    // заголовка сверху и красный крестик закрытия в углу
    public static class StructureDialogFrame
    {
        public const int Padding = 24;
        public const int ContentTop = 30;      // от верха панели: под плашкой заголовка
        public const int ButtonHeight = 36;
        public const int FooterGap = 12;       // между содержимым и нижними кнопками

        private const int MaxWidth = 880;
        private const int MaxHeight = 520;
        private const int ScreenMargin = 40;
        private const int CloseButtonSize = 26;
        private const int TitleBarHeight = 34;

        public static Rectangle PanelBounds()
        {
            int width = Math.Min(MaxWidth, Main.screenWidth - ScreenMargin);
            int height = Math.Min(MaxHeight, Main.screenHeight - ScreenMargin * 2);
            return new Rectangle((Main.screenWidth - width) / 2, (Main.screenHeight - height) / 2, width, height);
        }

        public static Rectangle CloseButtonBounds(Rectangle panel)
            => new(panel.Right - CloseButtonSize / 2 - 8, panel.Y - CloseButtonSize / 2, CloseButtonSize, CloseButtonSize);

        public static int ButtonTop(Rectangle panel) => panel.Bottom - Padding - ButtonHeight;

        public static void Draw(SpriteBatch spriteBatch, Rectangle panel, string title, Point mouse)
        {
            SoAHudDraw.Panel(spriteBatch, panel, SoAHudDraw.PanelFill, SoAHudDraw.PanelBorder);

            int titleWidth = (int)SoAHudDraw.Measure(title).X + 60;
            var titleBar = new Rectangle(panel.X + (panel.Width - titleWidth) / 2, panel.Y - 16, titleWidth, TitleBarHeight);
            SoAHudDraw.Panel(spriteBatch, titleBar, SoAHudDraw.CancelFill, SoAHudDraw.CancelBorder);
            SoAHudDraw.TextCentered(spriteBatch, title, titleBar, Color.White);

            Rectangle close = CloseButtonBounds(panel);
            bool hovered = close.Contains(mouse);
            SoAHudDraw.Panel(spriteBatch, close, hovered ? SoAHudDraw.CloseBorder : SoAHudDraw.CloseFill,
                SoAHudDraw.CloseBorder);
            SoAHudDraw.Cross(spriteBatch, close.Center.ToVector2(), 12, 2.5f, Color.White);
        }

        // Обрезает строку с многоточием, чтобы влезла в ширину
        public static string Ellipsize(string text, float maxWidth, float scale)
        {
            if (string.IsNullOrEmpty(text) || SoAHudDraw.Measure(text, scale).X <= maxWidth)
                return text;

            const string Ellipsis = "...";   // у шрифта Terraria может не быть символа «…»
            int length = text.Length;
            while (length > 0 && SoAHudDraw.Measure(text[..length] + Ellipsis, scale).X > maxWidth)
                length--;
            return text[..length] + Ellipsis;
        }
    }
}
