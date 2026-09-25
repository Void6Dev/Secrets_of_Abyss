using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Вертикальный тулбар инструмента: три режима выделения и кнопка сохранения,
    // с тултипом-подсказкой по наведению. Иконки нарисованы примитивами — когда
    // появятся спрайты, менять надо только методы DrawIcon.
    public static class StructureToolbar
    {
        private const int MarginX = 20;
        private const int ButtonSize = 44;
        private const int ButtonGap = 6;
        private const int DividerGap = 16;
        private const int StripPadding = 6;
        private const int SaveButtonIndex = 3;
        private const int ButtonCount = 4;

        private const int TooltipWidth = 310;
        private const int TooltipPadding = 12;
        private const int TooltipLineHeight = 20;
        private const int GlyphWidth = 14;
        private const int GlyphHeight = 20;

        // Ключ локализации кнопки и есть ли у неё строка про ПКМ
        private static readonly (string Key, bool HasRightClick)[] Tools =
        {
            ("Select", true),
            ("Add", true),
            ("Erase", true),
            ("Save", false)
        };

        private static int _hovered = -1;

        public static Rectangle Bounds()
        {
            int height = 3 * (ButtonSize + ButtonGap) + DividerGap + ButtonSize;
            int top = (Main.screenHeight - height) / 2;
            return new Rectangle(MarginX - StripPadding, top - StripPadding,
                ButtonSize + StripPadding * 2, height + StripPadding * 2);
        }

        private static Rectangle ButtonBounds(int index)
        {
            Rectangle strip = Bounds();
            int y = strip.Y + StripPadding + index * (ButtonSize + ButtonGap);
            if (index >= SaveButtonIndex)
                y += DividerGap;
            return new Rectangle(strip.X + StripPadding, y, ButtonSize, ButtonSize);
        }

        public static void ClearHover() => _hovered = -1;

        public static void HandleInput(in UiInput input)
        {
            _hovered = -1;
            for (int i = 0; i < ButtonCount; i++)
            {
                if (!ButtonBounds(i).Contains(input.Mouse))
                    continue;
                _hovered = i;
                break;
            }

            if (_hovered < 0)
                return;

            Main.LocalPlayer.mouseInterface = true;
            if (!input.LeftClick)
                return;

            if (_hovered == SaveButtonIndex)
            {
                StructureSaveDialog.Open();
                return;
            }

            StructureSelection.SetMode((SelectionMode)_hovered);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            Rectangle strip = Bounds();
            SoAHudDraw.Panel(spriteBatch, strip, SoAHudDraw.PanelFill * 0.85f, SoAHudDraw.PanelBorder);

            Rectangle divider = ButtonBounds(SaveButtonIndex);
            SoAHudDraw.Fill(spriteBatch,
                new Rectangle(strip.X + StripPadding, divider.Y - DividerGap / 2 - 1, ButtonSize, 2),
                SoAHudDraw.PanelBorder);

            for (int i = 0; i < ButtonCount; i++)
                DrawButton(spriteBatch, i);

            if (_hovered >= 0)
                DrawTooltip(spriteBatch, _hovered);
        }

        private static void DrawButton(SpriteBatch spriteBatch, int index)
        {
            Rectangle button = ButtonBounds(index);
            bool active = index < SaveButtonIndex && (int)StructureSelection.Mode == index;
            bool hovered = _hovered == index;

            Color fill = active
                ? SoAHudDraw.ActiveFill
                : hovered ? SoAHudDraw.ButtonHoverFill : SoAHudDraw.ButtonFill;
            Color border = active ? SoAHudDraw.ActiveBorder : SoAHudDraw.PanelBorder;
            SoAHudDraw.Panel(spriteBatch, button, fill, border);

            Color icon = active ? new Color(28, 30, 58) : SoAHudDraw.BodyText;
            DrawIcon(spriteBatch, index, button, icon);
        }

        private static readonly string[] IconFiles = { "Select", "Add", "Erase", "Save" };

        private static void DrawIcon(SpriteBatch spriteBatch, int index, Rectangle button, Color color)
        {
            var box = new Rectangle(button.X + 10, button.Y + 10, button.Width - 20, button.Height - 20);

            // Нарисованный спрайт берём как есть, без подкраски: цвет в нём уже свой.
            // Тонируется только заглушка, чтобы читалась на золотом фоне активной кнопки
            if (DevIcons.TryDraw(spriteBatch, IconFiles[index], box, Color.White))
                return;

            switch (index)
            {
                case 0:
                    SoAHudDraw.DashedFrame(spriteBatch, box, color);
                    SoAHudDraw.CursorArrow(spriteBatch, new Vector2(box.Right - 4, box.Bottom - 6), 11f,
                        color, SoAHudDraw.PanelFill);
                    break;
                case 1:
                    SoAHudDraw.DashedFrame(spriteBatch, box, color);
                    SoAHudDraw.Plus(spriteBatch, box.Center.ToVector2(), 12, 3, color);
                    break;
                case 2:
                    SoAHudDraw.DashedFrame(spriteBatch, box, color);
                    SoAHudDraw.Cross(spriteBatch, box.Center.ToVector2(), 12, 3f, new Color(232, 76, 76));
                    break;
                default:
                    SoAHudDraw.Wand(spriteBatch, box, color, new Color(120, 220, 150));
                    break;
            }
        }

        private static void DrawTooltip(SpriteBatch spriteBatch, int index)
        {
            (string key, bool hasRightClick) = Tools[index];
            string[] description = ToolText.Lines($"Toolbar.{key}.Description");
            int controlCount = hasRightClick ? 2 : 1;
            Rectangle button = ButtonBounds(index);

            int height = TooltipPadding * 2 + TooltipLineHeight
                + description.Length * TooltipLineHeight + 6
                + controlCount * (GlyphHeight + 4);
            var panel = new Rectangle(Bounds().Right + 10, button.Y - StripPadding, TooltipWidth, height);
            if (panel.Bottom > Main.screenHeight - 20)
                panel.Y = Main.screenHeight - 20 - panel.Height;

            SoAHudDraw.Panel(spriteBatch, panel, SoAHudDraw.PanelFill, SoAHudDraw.PanelBorder);

            int textX = panel.X + TooltipPadding;
            int y = panel.Y + TooltipPadding;

            SoAHudDraw.Text(spriteBatch, ToolText.Get($"Toolbar.{key}.Title"), new Vector2(textX, y),
                SoAHudDraw.TitleText, 0.95f);
            y += TooltipLineHeight + 2;

            foreach (string line in description)
            {
                SoAHudDraw.Text(spriteBatch, line, new Vector2(textX, y), SoAHudDraw.BodyText, 0.85f);
                y += TooltipLineHeight;
            }

            y += 6;
            DrawControlLine(spriteBatch, textX, ref y, key, leftButton: true);
            if (hasRightClick)
                DrawControlLine(spriteBatch, textX, ref y, key, leftButton: false);
        }

        private static void DrawControlLine(SpriteBatch spriteBatch, int x, ref int y, string key, bool leftButton)
        {
            var glyph = new Rectangle(x, y, GlyphWidth, GlyphHeight);
            if (!DevIcons.TryDraw(spriteBatch, leftButton ? "MouseLeft" : "MouseRight", glyph, Color.White))
                SoAHudDraw.MouseGlyph(spriteBatch, glyph, leftButton,
                    SoAHudDraw.ButtonFill, new Color(140, 220, 140));

            string text = ToolText.Get($"Toolbar.{key}.{(leftButton ? "LeftClick" : "RightClick")}");
            SoAHudDraw.Text(spriteBatch, text, new Vector2(x + GlyphWidth + 8, y), SoAHudDraw.BodyText, 0.85f);
            y += GlyphHeight + 4;
        }
    }
}
