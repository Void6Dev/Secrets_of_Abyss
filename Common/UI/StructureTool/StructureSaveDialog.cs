using System;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Диалог сохранения постройки: имя, галочки игнорируемых тайлов и превью.
    // Инструмент отладочный и только для автора, поэтому подписи заданы прямо здесь,
    // а не через локализацию.
    public static class StructureSaveDialog
    {
        private const int PanelWidth = 500;
        private const int PanelHeight = 300;
        private const int Padding = 24;
        private const int MaxNameLength = 48;

        private const int FieldWidth = 300;
        private const int FieldHeight = 34;
        private const int CheckboxSize = 18;
        private const int CheckboxColumnWidth = 170;
        private const int CheckboxRowHeight = 30;
        private const int PreviewWidth = 130;
        private const int PreviewHeight = 150;
        private const int ButtonHeight = 36;
        private const int CloseButtonSize = 26;

        // Порядок галочек в две колонки: слева направо, сверху вниз
        private static readonly (StructureFilter Flag, string Key)[] Filters =
        {
            (StructureFilter.SkipWalls, "Walls"),
            (StructureFilter.SkipLiquids, "Liquids"),
            (StructureFilter.SkipGrass, "Grass"),
            (StructureFilter.SkipCobwebs, "Cobwebs"),
            (StructureFilter.SkipVines, "Vines"),
            (StructureFilter.SkipLightSources, "LightSources")
        };

        private static bool _open;
        private static string _name = string.Empty;
        private static bool _fieldFocused;
        private static Rectangle _area;

        public static bool IsOpen => _open;

        public static void Open()
        {
            if (StructureSelection.Count == 0)
            {
                Main.NewText(ToolText.Get("Chat.NothingSelected"), Color.Orange);
                return;
            }

            int expanded = StructureSelection.ExpandToWholeObjects();
            if (expanded > 0)
                Main.NewText(ToolText.Get("Chat.Snapped", expanded), Color.LightBlue);

            if (StructureSelection.OverLimit)
            {
                Rectangle over = StructureSelection.Bounds;
                Main.NewText(ToolText.Get("Chat.OverLimit", over.Width, over.Height,
                    StructureIO.MaxWidth, StructureIO.MaxHeight), Color.Orange);
                return;
            }

            _open = true;
            _fieldFocused = true;
            _name = "struct_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _area = StructureSelection.Bounds;
            Main.clrInput();
            StructurePreview.Request(_area, StructureSelection.Cells);
        }

        public static void Close()
        {
            if (!_open)
                return;

            _open = false;
            _fieldFocused = false;
            StructurePreview.Release();
            StructureToolUi.NotifyDialogClosed();
        }

        // Набранный текст читаем в фазе отрисовки, а не обновления: буфер введённых
        // символов ванилла наполняет позже, и в UpdateUI он ещё пуст — стирание
        // работает (оно идёт по состоянию клавиш), а буквы теряются.
        // Так же это делает и собственное поле ввода tML.
        public static void ReadTypedText()
        {
            if (!_open || !_fieldFocused)
                return;

            PlayerInput.WritingText = true;
            Main.instance.HandleIME();

            string typed = Main.GetInputText(_name);
            _name = typed.Length > MaxNameLength ? typed[..MaxNameLength] : typed;

            // Флаги ванильного ввода забираем себе, иначе на то же нажатие
            // среагирует ещё и чат
            Main.inputTextEnter = false;
            Main.inputTextEscape = false;
        }

        public static void HandleInput(in UiInput input)
        {
            if (input.EscapePressed)
            {
                Cancel();
                return;
            }

            if (input.EnterPressed)
            {
                Save();
                return;
            }

            if (!input.LeftClick)
                return;

            Rectangle panel = PanelBounds();

            if (CloseButtonBounds(panel).Contains(input.Mouse) || CancelButtonBounds(panel).Contains(input.Mouse))
            {
                Cancel();
                return;
            }

            if (SaveButtonBounds(panel).Contains(input.Mouse))
            {
                Save();
                return;
            }

            if (FieldBounds(panel).Contains(input.Mouse))
            {
                _fieldFocused = true;
                Main.clrInput();
                return;
            }

            for (int i = 0; i < Filters.Length; i++)
            {
                if (!FilterRowBounds(panel, i).Contains(input.Mouse))
                    continue;

                StructureSelection.ToggleFilter(Filters[i].Flag);
                SoundEngine.PlaySound(SoundID.MenuTick);
                // Превью показывает ровно то, что уйдёт в файл
                StructurePreview.Request(_area, StructureSelection.Cells);
                return;
            }

            _fieldFocused = false;
        }

        private static void Cancel()
        {
            Close();
            SoundEngine.PlaySound(SoundID.MenuClose);
        }

        private static void Save()
        {
            string name = SanitizeFileName(_name);
            if (name.Length == 0)
            {
                Main.NewText(ToolText.Get("Chat.EnterName"), Color.Orange);
                return;
            }

            string path = StructureIO.Export(_area, name, StructureSelection.Filters, StructureSelection.Cells);
            Main.NewText(ToolText.Get("Chat.Saved", _area.Width, _area.Height, StructureSelection.Count, path),
                Color.LightGreen);
            Close();
            SoundEngine.PlaySound(SoundID.MenuOpen);
        }

        // Имя уходит в путь файла, поэтому оставляем только безопасные символы
        private static string SanitizeFileName(string raw)
        {
            var result = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    result.Append(c);
                else if (c == ' ' || c == '.')
                    result.Append('_');
            }
            return result.ToString();
        }

        // --- Раскладка ---

        public static Rectangle PanelBounds()
            => new((Main.screenWidth - PanelWidth) / 2, (Main.screenHeight - PanelHeight) / 2, PanelWidth, PanelHeight);

        private static Rectangle CloseButtonBounds(Rectangle panel)
            => new(panel.Right - CloseButtonSize / 2 - 8, panel.Y - CloseButtonSize / 2, CloseButtonSize, CloseButtonSize);

        private static Rectangle FieldBounds(Rectangle panel)
            => new(panel.X + Padding, panel.Y + 62, FieldWidth, FieldHeight);

        private static Rectangle FilterRowBounds(Rectangle panel, int index)
            => new(panel.X + Padding + index % 2 * CheckboxColumnWidth,
                panel.Y + 136 + index / 2 * CheckboxRowHeight,
                CheckboxColumnWidth - 10, CheckboxSize + 6);

        private static Rectangle CheckboxBounds(Rectangle row)
            => new(row.X, row.Y + (row.Height - CheckboxSize) / 2, CheckboxSize, CheckboxSize);

        private static Rectangle PreviewBounds(Rectangle panel)
            => new(panel.Right - Padding - PreviewWidth, panel.Y + 62, PreviewWidth, PreviewHeight);

        private static Rectangle CancelButtonBounds(Rectangle panel)
            => new(panel.X + Padding + 16, panel.Bottom - Padding - ButtonHeight, 150, ButtonHeight);

        private static Rectangle SaveButtonBounds(Rectangle panel)
            => new(panel.Right - Padding - 186, panel.Bottom - Padding - ButtonHeight, 170, ButtonHeight);

        // --- Отрисовка ---

        public static void Draw(SpriteBatch spriteBatch)
        {
            Rectangle panel = PanelBounds();
            Point mouse = Main.MouseScreen.ToPoint();

            SoAHudDraw.Panel(spriteBatch, panel, SoAHudDraw.PanelFill, SoAHudDraw.PanelBorder);
            DrawTitleBar(spriteBatch, panel, mouse);
            DrawNameField(spriteBatch, panel);
            DrawFilters(spriteBatch, panel, mouse);
            DrawPreview(spriteBatch, panel);
            DrawButtons(spriteBatch, panel, mouse);
        }

        private static void DrawTitleBar(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
        {
            string title = ToolText.Get("Dialog.Title");
            int titleWidth = (int)SoAHudDraw.Measure(title).X + 60;
            var titleBar = new Rectangle(panel.X + (panel.Width - titleWidth) / 2, panel.Y - 16, titleWidth, 34);
            SoAHudDraw.Panel(spriteBatch, titleBar, SoAHudDraw.CancelFill, SoAHudDraw.CancelBorder);
            SoAHudDraw.TextCentered(spriteBatch, title, titleBar, Color.White);

            Rectangle close = CloseButtonBounds(panel);
            bool hovered = close.Contains(mouse);
            SoAHudDraw.Panel(spriteBatch, close, hovered ? SoAHudDraw.CloseBorder : SoAHudDraw.CloseFill,
                SoAHudDraw.CloseBorder);
            SoAHudDraw.Cross(spriteBatch, close.Center.ToVector2(), 12, 2.5f, Color.White);
        }

        private static void DrawNameField(SpriteBatch spriteBatch, Rectangle panel)
        {
            SoAHudDraw.Text(spriteBatch, ToolText.Get("Dialog.NameLabel"),
                new Vector2(panel.X + Padding, panel.Y + 34), SoAHudDraw.BodyText, 0.9f);

            Rectangle field = FieldBounds(panel);
            SoAHudDraw.Panel(spriteBatch, field, new Color(16, 18, 36, 235),
                _fieldFocused ? SoAHudDraw.ActiveBorder : SoAHudDraw.PanelBorder);

            bool caret = _fieldFocused && (int)(Main.GlobalTimeWrappedHourly * 2f) % 2 == 0;
            string shown = _name + (caret ? "|" : string.Empty);
            SoAHudDraw.Text(spriteBatch, shown, new Vector2(field.X + 10, field.Y + 6),
                _name.Length == 0 ? SoAHudDraw.DimText : Color.White, 0.9f);

            var pencil = new Rectangle(field.Right - 26, field.Y + 10, 14, 14);
            if (!DevIcons.TryDraw(spriteBatch, "Pencil", pencil, Color.White))
                SoAHudDraw.Pencil(spriteBatch, pencil, SoAHudDraw.DimText);
        }

        private static void DrawFilters(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
        {
            SoAHudDraw.Text(spriteBatch, ToolText.Get("Dialog.FiltersLabel"),
                new Vector2(panel.X + Padding, panel.Y + 108), SoAHudDraw.BodyText, 0.9f);

            for (int i = 0; i < Filters.Length; i++)
            {
                Rectangle row = FilterRowBounds(panel, i);
                Rectangle box = CheckboxBounds(row);
                bool hovered = row.Contains(mouse);
                bool checked_ = StructureSelection.HasFilter(Filters[i].Flag);

                SoAHudDraw.Panel(spriteBatch, box, new Color(16, 18, 36, 235),
                    hovered ? SoAHudDraw.ActiveBorder : SoAHudDraw.PanelBorder);
                if (checked_)
                    SoAHudDraw.CheckMark(spriteBatch, box, Color.White);

                SoAHudDraw.Text(spriteBatch, ToolText.Get("Filters." + Filters[i].Key),
                    new Vector2(box.Right + 8, row.Y + 1),
                    hovered ? Color.White : SoAHudDraw.BodyText, 0.85f);
            }
        }

        private static void DrawPreview(SpriteBatch spriteBatch, Rectangle panel)
        {
            Rectangle box = PreviewBounds(panel);
            SoAHudDraw.Panel(spriteBatch, box, new Color(12, 14, 28, 235), SoAHudDraw.PanelBorder);

            Texture2D preview = StructurePreview.Texture;
            if (preview == null)
            {
                SoAHudDraw.TextCentered(spriteBatch, "...", box, SoAHudDraw.DimText, 0.9f);
            }
            else
            {
                // Вписываем превью в рамку по большей стороне, пропорции сохраняем
                var inner = new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
                float fit = MathF.Min(inner.Width / (float)preview.Width, inner.Height / (float)preview.Height);
                int width = Math.Max(1, (int)(preview.Width * fit));
                int height = Math.Max(1, (int)(preview.Height * fit));
                spriteBatch.Draw(preview, new Rectangle(
                    inner.X + (inner.Width - width) / 2, inner.Y + (inner.Height - height) / 2, width, height),
                    Color.White);
            }

            string size = $"{_area.Width}x{_area.Height}";
            SoAHudDraw.Text(spriteBatch, size,
                new Vector2(box.X + (box.Width - SoAHudDraw.Measure(size, 0.8f).X) / 2f, box.Bottom + 4),
                SoAHudDraw.DimText, 0.8f);
        }

        private static void DrawButtons(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
        {
            Rectangle cancel = CancelButtonBounds(panel);
            bool cancelHovered = cancel.Contains(mouse);
            SoAHudDraw.Panel(spriteBatch, cancel,
                cancelHovered ? SoAHudDraw.CancelBorder * 0.6f : SoAHudDraw.CancelFill, SoAHudDraw.CancelBorder);
            SoAHudDraw.TextCentered(spriteBatch, ToolText.Get("Dialog.Cancel"), cancel, Color.White, 0.95f);

            Rectangle save = SaveButtonBounds(panel);
            bool saveHovered = save.Contains(mouse);
            SoAHudDraw.Panel(spriteBatch, save,
                saveHovered ? SoAHudDraw.SaveBorder * 0.6f : SoAHudDraw.SaveFill, SoAHudDraw.SaveBorder);
            SoAHudDraw.TextCentered(spriteBatch, ToolText.Get("Dialog.Save"), save, Color.White, 0.95f);
        }
    }
}
