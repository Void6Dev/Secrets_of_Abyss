using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Окно сохранения постройки. Слева — имя, куда сохранить, галочки игнорируемых
    // тайлов и сводка; справа — интерактивное превью ровно того, что уйдёт в файл.
    public static class StructureSaveDialog
    {
        private const int LeftColumnWidth = 300;
        private const int ColumnGap = 24;
        private const int MaxNameLength = 48;
        private const int FieldHeight = 34;
        private const int TargetRowY = 98;
        private const int FiltersLabelY = 132;
        private const int FiltersTop = 156;
        private const int FilterColumnWidth = 150;
        private const int FilterRowHeight = 28;
        private const int StatsTop = 248;
        private const int StatsLineHeight = 20;
        private const int PreviewFooterHeight = 46;
        private const int CancelButtonWidth = 140;
        private const int ButtonGap = 10;

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

        private static readonly StructureViewport _preview = new();

        private static bool _open;
        private static string _name = string.Empty;
        private static bool _fieldFocused;
        private static Rectangle _area;
        private static StructureStats _stats;
        private static bool _sourcesAvailable;
        private static bool _saveToSources;     // запоминается между открытиями
        private static string _checkedPath;
        private static bool _fileExists;

        public static bool IsOpen => _open;

        private static Mod Mod => ModContent.GetInstance<StructureToolUi>().Mod;

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
            _name = StructureSelection.SourceName ?? "struct_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _area = StructureSelection.Bounds;
            _sourcesAvailable = Directory.Exists(StructureIO.SourceDirectory(Mod));
            _checkedPath = null;
            StructureToolUi.NotifyModalOpened();
            Recapture(resetView: true);
        }

        public static void Close()
        {
            if (!_open)
                return;

            _open = false;
            _fieldFocused = false;
            _preview.Release();
            StructureToolUi.NotifyDialogClosed();
        }

        // Превью и сводка показывают ровно то, что уйдёт в файл с текущими галочками
        private static void Recapture(bool resetView)
        {
            StructureData data = StructureIO.Capture(_area, StructureSelection.Filters, StructureSelection.Cells);
            _stats = data.ComputeStats();
            _preview.Show(data, resetView);
        }

        private static string TargetDirectory => _saveToSources && _sourcesAvailable
            ? StructureIO.SourceDirectory(Mod)
            : StructureIO.ExportDirectory;

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

            Rectangle panel = StructureDialogFrame.PanelBounds();
            Rectangle preview = PreviewBounds(panel);
            _preview.HandleInput(input, preview);

            if (!input.LeftClick)
                return;

            if (StructureDialogFrame.CloseButtonBounds(panel).Contains(input.Mouse)
                || CancelButtonBounds(panel).Contains(input.Mouse))
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

            // Двигать превью можно, не теряя фокус с поля имени
            if (preview.Contains(input.Mouse))
                return;

            if (_sourcesAvailable && TargetRowBounds(panel).Contains(input.Mouse))
            {
                _saveToSources = !_saveToSources;
                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }

            for (int i = 0; i < Filters.Length; i++)
            {
                if (!FilterRowBounds(panel, i).Contains(input.Mouse))
                    continue;

                StructureSelection.ToggleFilter(Filters[i].Flag);
                SoundEngine.PlaySound(SoundID.MenuTick);
                Recapture(resetView: false);
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

            string path = StructureIO.FilePath(TargetDirectory, name);
            StructureData data = StructureIO.Capture(_area, StructureSelection.Filters, StructureSelection.Cells);
            try
            {
                StructureIO.Write(data, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Main.NewText(ToolText.Get("Chat.SaveFailed", exception.Message), Color.OrangeRed);
                return;
            }

            StructureSelection.SetSourceName(name);
            bool toSources = _saveToSources && _sourcesAvailable;
            Main.NewText(ToolText.Get(toSources ? "Chat.SavedToSources" : "Chat.Saved",
                _area.Width, _area.Height, StructureSelection.Count, path), Color.LightGreen);
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

        // Проверка диска — только когда имя или папка сменились, а не каждый кадр
        private static void RefreshFileExists()
        {
            string name = SanitizeFileName(_name);
            string path = name.Length == 0 ? string.Empty : StructureIO.FilePath(TargetDirectory, name);
            if (path == _checkedPath)
                return;

            _checkedPath = path;
            _fileExists = path.Length > 0 && File.Exists(path);
        }

        // --- Раскладка ---

        private static int LeftX(Rectangle panel) => panel.X + StructureDialogFrame.Padding;

        private static Rectangle FieldBounds(Rectangle panel)
            => new(LeftX(panel), panel.Y + 54, LeftColumnWidth, FieldHeight);

        private static Rectangle TargetRowBounds(Rectangle panel)
            => new(LeftX(panel), panel.Y + TargetRowY, LeftColumnWidth, SoAHudDraw.CheckboxSize + 6);

        private static Rectangle FilterRowBounds(Rectangle panel, int index)
            => new(LeftX(panel) + index % 2 * FilterColumnWidth,
                panel.Y + FiltersTop + index / 2 * FilterRowHeight,
                FilterColumnWidth - 6, SoAHudDraw.CheckboxSize + 6);

        private static Rectangle PreviewBounds(Rectangle panel)
        {
            int x = LeftX(panel) + LeftColumnWidth + ColumnGap;
            int y = panel.Y + StructureDialogFrame.ContentTop;
            return new Rectangle(x, y, panel.Right - StructureDialogFrame.Padding - x,
                panel.Bottom - StructureDialogFrame.Padding - PreviewFooterHeight - y);
        }

        private static Rectangle CancelButtonBounds(Rectangle panel)
            => new(LeftX(panel), StructureDialogFrame.ButtonTop(panel), CancelButtonWidth, StructureDialogFrame.ButtonHeight);

        private static Rectangle SaveButtonBounds(Rectangle panel)
            => new(LeftX(panel) + CancelButtonWidth + ButtonGap, StructureDialogFrame.ButtonTop(panel),
                LeftColumnWidth - CancelButtonWidth - ButtonGap, StructureDialogFrame.ButtonHeight);

        // --- Отрисовка ---

        public static void Draw(SpriteBatch spriteBatch)
        {
            Rectangle panel = StructureDialogFrame.PanelBounds();
            Point mouse = Main.MouseScreen.ToPoint();
            RefreshFileExists();

            StructureDialogFrame.Draw(spriteBatch, panel, ToolText.Get("Dialog.Title"), mouse);
            DrawNameField(spriteBatch, panel);
            DrawTarget(spriteBatch, panel, mouse);
            DrawFilters(spriteBatch, panel, mouse);
            DrawStats(spriteBatch, panel);
            DrawPreview(spriteBatch, panel);

            SoAHudDraw.Button(spriteBatch, CancelButtonBounds(panel), ToolText.Get("Dialog.Cancel"),
                SoAHudDraw.CancelFill, SoAHudDraw.CancelBorder, CancelButtonBounds(panel).Contains(mouse));
            SoAHudDraw.Button(spriteBatch, SaveButtonBounds(panel),
                ToolText.Get(_fileExists ? "Dialog.Overwrite" : "Dialog.Save"),
                SoAHudDraw.SaveFill, SoAHudDraw.SaveBorder, SaveButtonBounds(panel).Contains(mouse));
        }

        private static void DrawNameField(SpriteBatch spriteBatch, Rectangle panel)
        {
            SoAHudDraw.Text(spriteBatch, ToolText.Get("Dialog.NameLabel"),
                new Vector2(LeftX(panel), panel.Y + StructureDialogFrame.ContentTop), SoAHudDraw.BodyText, 0.9f);

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

        // Галочка «в исходники мода» есть, только если исходники лежат на этой машине
        private static void DrawTarget(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
        {
            Rectangle row = TargetRowBounds(panel);
            if (_sourcesAvailable)
            {
                SoAHudDraw.Checkbox(spriteBatch, row, ToolText.Get("Dialog.ToSources"), _saveToSources,
                    row.Contains(mouse));
                return;
            }

            SoAHudDraw.Text(spriteBatch, ToolText.Get("Dialog.ToExport"), new Vector2(row.X, row.Y + 1),
                SoAHudDraw.DimText, 0.85f);
        }

        private static void DrawFilters(SpriteBatch spriteBatch, Rectangle panel, Point mouse)
        {
            SoAHudDraw.Text(spriteBatch, ToolText.Get("Dialog.FiltersLabel"),
                new Vector2(LeftX(panel), panel.Y + FiltersLabelY), SoAHudDraw.BodyText, 0.9f);

            for (int i = 0; i < Filters.Length; i++)
            {
                Rectangle row = FilterRowBounds(panel, i);
                SoAHudDraw.Checkbox(spriteBatch, row, ToolText.Get("Filters." + Filters[i].Key),
                    StructureSelection.HasFilter(Filters[i].Flag), row.Contains(mouse));
            }
        }

        private static void DrawStats(SpriteBatch spriteBatch, Rectangle panel)
        {
            var position = new Vector2(LeftX(panel), panel.Y + StatsTop);

            void Line(string text, Color color)
            {
                text = StructureDialogFrame.Ellipsize(text, LeftColumnWidth, 0.82f);
                SoAHudDraw.Text(spriteBatch, text, position, color, 0.82f);
                position.Y += StatsLineHeight;
            }

            Line(ToolText.Get("Dialog.StatsSize", _area.Width, _area.Height, _stats.Cells), SoAHudDraw.BodyText);
            Line(ToolText.Get("Dialog.StatsTiles", _stats.Tiles, _stats.TileKinds), SoAHudDraw.BodyText);
            Line(ToolText.Get("Dialog.StatsWalls", _stats.Walls, _stats.Liquids), SoAHudDraw.BodyText);

            position.Y += 6;
            StructureData data = _preview.Data;
            if (data != null && data.HasChests)
                Line(ToolText.Get("Dialog.WarnChests"), Color.Orange);
            if (data != null && data.HasWires)
                Line(ToolText.Get("Dialog.WarnWires"), Color.Orange);
            if (_fileExists)
                Line(ToolText.Get("Dialog.WarnOverwrite"), Color.Orange);
        }

        private static void DrawPreview(SpriteBatch spriteBatch, Rectangle panel)
        {
            Rectangle box = PreviewBounds(panel);
            _preview.Draw(spriteBatch, box);

            string hover = _preview.DescribeHover();
            float maxWidth = box.Width;
            if (hover != null)
                SoAHudDraw.Text(spriteBatch, StructureDialogFrame.Ellipsize(hover, maxWidth, 0.8f),
                    new Vector2(box.X, box.Bottom + 6), SoAHudDraw.BodyText, 0.8f);

            SoAHudDraw.Text(spriteBatch, StructureDialogFrame.Ellipsize(ToolText.Get("Preview.Hint"), maxWidth, 0.75f),
                new Vector2(box.X, box.Bottom + 26), SoAHudDraw.DimText, 0.75f);
        }
    }
}
