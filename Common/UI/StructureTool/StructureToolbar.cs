using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Вертикальный тулбар инструмента: три режима выделения и размещение из библиотеки,
    // под чертой — отмена и сохранение. Тултип по наведению. Иконки берутся из
    // icons/*.png, пока файла нет — рисуются примитивами в DrawIcon.
    public static class StructureToolbar
    {
        private const int MarginX = 20;
        private const int ButtonSize = 44;
        private const int ButtonGap = 6;
        private const int DividerGap = 16;
        private const int StripPadding = 6;

        private const int PlaceButtonIndex = 3;
        private const int UndoButtonIndex = 4;
        private const int SaveButtonIndex = 5;
        private const int FirstActionIndex = UndoButtonIndex;  // черта отделяет действия от режимов
        private const int ButtonCount = 6;

        private const int TooltipWidth = 310;
        private const int TooltipPadding = 12;
        private const int TooltipLineHeight = 20;
        private const int GlyphWidth = 14;
        private const int GlyphHeight = 20;

        public static readonly Color PlaceColor = new(240, 200, 90);
        public static readonly Color LimitColor = new(255, 60, 60);

        // Ключ локализации и файл иконки совпадают; есть ли у кнопки строка про ПКМ
        private static readonly (string Key, bool HasRightClick)[] Tools =
        {
            ("Select", true),
            ("Add", true),
            ("Erase", true),
            ("Place", true),
            ("Undo", false),
            ("Save", false)
        };

        private static int _hovered = -1;

        public static Color ModeColor(SelectionMode mode) => mode switch
        {
            SelectionMode.Add => new Color(80, 220, 120),
            SelectionMode.Erase => new Color(255, 90, 90),
            _ => new Color(90, 180, 255)
        };

        public static Rectangle Bounds()
        {
            int height = ButtonCount * ButtonSize + (ButtonCount - 2) * ButtonGap + DividerGap;
            int top = (Main.screenHeight - height) / 2;
            return new Rectangle(MarginX - StripPadding, top - StripPadding,
                ButtonSize + StripPadding * 2, height + StripPadding * 2);
        }

        private static Rectangle ButtonBounds(int index)
        {
            Rectangle strip = Bounds();
            int y = strip.Y + StripPadding + index * (ButtonSize + ButtonGap);
            if (index >= FirstActionIndex)
                y += DividerGap - ButtonGap;
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

            switch (_hovered)
            {
                case PlaceButtonIndex:
                    StructureLibraryDialog.Open();
                    break;
                case UndoButtonIndex:
                    StructureToolUi.Undo();
                    break;
                case SaveButtonIndex:
                    StructureSaveDialog.Open();
                    break;
                default:
                    StructurePlacement.End();
                    StructureSelection.SetMode((SelectionMode)_hovered);
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    break;
            }
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            Rectangle strip = Bounds();
            SoAHudDraw.Panel(spriteBatch, strip, SoAHudDraw.PanelFill * 0.85f, SoAHudDraw.PanelBorder);

            Rectangle firstAction = ButtonBounds(FirstActionIndex);
            SoAHudDraw.Fill(spriteBatch,
                new Rectangle(strip.X + StripPadding, firstAction.Y - DividerGap / 2 - 1, ButtonSize, 2),
                SoAHudDraw.PanelBorder);

            for (int i = 0; i < ButtonCount; i++)
                DrawButton(spriteBatch, i);

            if (_hovered >= 0)
                DrawTooltip(spriteBatch, _hovered);
        }

        private static bool IsActive(int index)
        {
            if (index == PlaceButtonIndex)
                return StructurePlacement.Active;
            return index < PlaceButtonIndex && !StructurePlacement.Active && (int)StructureSelection.Mode == index;
        }

        private static bool IsEnabled(int index) => index != UndoButtonIndex || StructureHistory.CanUndo;

        private static void DrawButton(SpriteBatch spriteBatch, int index)
        {
            Rectangle button = ButtonBounds(index);
            bool active = IsActive(index);
            bool enabled = IsEnabled(index);
            bool hovered = _hovered == index && enabled;

            Color fill = active
                ? SoAHudDraw.ActiveFill
                : hovered ? SoAHudDraw.ButtonHoverFill : SoAHudDraw.ButtonFill;
            Color border = active ? SoAHudDraw.ActiveBorder : SoAHudDraw.PanelBorder;
            SoAHudDraw.Panel(spriteBatch, button, fill, border);

            Color icon = active ? new Color(28, 30, 58) : SoAHudDraw.BodyText;
            DrawIcon(spriteBatch, index, button, enabled ? icon : SoAHudDraw.DimText * 0.6f, enabled);
        }

        private static void DrawIcon(SpriteBatch spriteBatch, int index, Rectangle button, Color color, bool enabled)
        {
            var box = new Rectangle(button.X + 10, button.Y + 10, button.Width - 20, button.Height - 20);

            // Нарисованный спрайт берём как есть, без подкраски: цвет в нём уже свой.
            // Тонируется только заглушка, чтобы читалась на золотом фоне активной кнопки
            if (DevIcons.TryDraw(spriteBatch, Tools[index].Key, box, enabled ? Color.White : Color.White * 0.35f))
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
                case PlaceButtonIndex:
                    SoAHudDraw.Stamp(spriteBatch, box, color);
                    break;
                case UndoButtonIndex:
                    SoAHudDraw.UndoArrow(spriteBatch, box, color);
                    break;
                default:
                    SoAHudDraw.Wand(spriteBatch, box, color, new Color(120, 220, 150));
                    break;
            }
        }

        private static void DrawTooltip(SpriteBatch spriteBatch, int index)
        {
            (string key, bool hasRightClick) = Tools[index];
            string[] description = ToolText.Lines($"Toolbar.{key}.Description", UndoKeyName());
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

        // Клавиша отмены переназначается в настройках управления — показываем текущую
        private static string UndoKeyName()
        {
            List<string> keys = StructureToolUi.UndoKeybind?.GetAssignedKeys();
            return keys is { Count: > 0 } ? keys[0] : "—";
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
