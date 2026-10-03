using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Библиотека построек: список файлов из мода, исходников и папки экспорта,
    // интерактивное превью выбранного и кнопка «Разместить» — жезл переходит
    // в режим размещения с призраком постройки за курсором.
    public static class StructureLibraryDialog
    {
        private const int ListWidth = 280;
        private const int ColumnGap = 24;
        private const int RowHeight = 30;
        private const int RowPadding = 10;
        private const int ScrollbarWidth = 4;
        private const int InfoLineHeight = 20;
        private const int InfoLines = 4;
        private const int PlaceButtonWidth = 170;
        private const int CloseButtonWidth = 130;
        private const int ButtonGap = 10;

        private static readonly StructureViewport _preview = new();

        private static bool _open;
        private static List<StructureEntry> _entries = new();
        private static int _selected = -1;
        private static int _scrollRows;
        private static bool _loadFailed;
        private static StructureStats _stats;
        private static string _lastSelectedPath;   // выбор переживает закрытие окна

        public static bool IsOpen => _open;

        private static Mod Mod => ModContent.GetInstance<StructureToolUi>().Mod;

        private static StructureEntry? Selected
            => _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;

        public static void Open()
        {
            _entries = StructureLibrary.Scan(Mod);
            _selected = _entries.FindIndex(entry => entry.Path == _lastSelectedPath);
            if (_selected < 0 && _entries.Count > 0)
                _selected = 0;
            _scrollRows = 0;

            _open = true;
            StructureToolUi.NotifyModalOpened();
            LoadSelected();
            ScrollToSelected();
            SoundEngine.PlaySound(SoundID.MenuOpen);
        }

        public static void Close()
        {
            if (!_open)
                return;

            _open = false;
            _preview.Release();
            StructureToolUi.NotifyDialogClosed();
        }

        private static void LoadSelected()
        {
            _loadFailed = false;
            if (Selected is not StructureEntry entry)
            {
                _preview.Show(null, resetView: true);
                return;
            }

            _lastSelectedPath = entry.Path;
            StructureData data = StructureLibrary.Load(Mod, entry);
            _loadFailed = data == null;
            _stats = data?.ComputeStats() ?? default;
            _preview.Show(data, resetView: true);
        }

        public static void HandleInput(in UiInput input)
        {
            if (input.EscapePressed)
            {
                Close();
                SoundEngine.PlaySound(SoundID.MenuClose);
                return;
            }

            if (input.EnterPressed)
            {
                Place();
                return;
            }

            Rectangle panel = StructureDialogFrame.PanelBounds();
            Rectangle list = ListBounds(panel);
            _preview.HandleInput(input, PreviewBounds(panel));

            if (input.Scroll != 0 && list.Contains(input.Mouse))
                _scrollRows = Math.Clamp(_scrollRows - Math.Sign(input.Scroll), 0, MaxScroll(list));

            if (!input.LeftClick)
                return;

            if (StructureDialogFrame.CloseButtonBounds(panel).Contains(input.Mouse)
                || CloseButtonBounds(panel).Contains(input.Mouse))
            {
                Close();
                SoundEngine.PlaySound(SoundID.MenuClose);
                return;
            }

            if (PlaceButtonBounds(panel).Contains(input.Mouse))
            {
                Place();
                return;
            }

            if (FolderButtonBounds(panel).Contains(input.Mouse))
            {
                OpenFolder();
                return;
            }

            int row = RowAt(list, input.Mouse);
            if (row >= 0 && row != _selected)
            {
                _selected = row;
                LoadSelected();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }

        private static void Place()
        {
            if (Selected is not StructureEntry entry || _preview.Data == null)
                return;

            StructurePlacement.Begin(_preview.Data, entry.Name);
            Close();
            SoundEngine.PlaySound(SoundID.MenuTick);
            if (Main.netMode != NetmodeID.SinglePlayer)
                Main.NewText(ToolText.Get("Chat.PlaceSingleplayerOnly"), Color.Orange);
        }

        private static void OpenFolder()
        {
            string folder = Selected is StructureEntry entry
                ? StructureLibrary.FolderOf(Mod, entry)
                : StructureIO.ExportDirectory;

            Directory.CreateDirectory(folder);
            Terraria.Utils.OpenFolder(folder);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        // --- Список ---

        private static int VisibleRows(Rectangle list) => Math.Max(1, list.Height / RowHeight);

        private static int MaxScroll(Rectangle list) => Math.Max(0, _entries.Count - VisibleRows(list));

        private static void ScrollToSelected()
        {
            Rectangle list = ListBounds(StructureDialogFrame.PanelBounds());
            int visible = VisibleRows(list);
            if (_selected >= _scrollRows + visible)
                _scrollRows = _selected - visible + 1;
            _scrollRows = Math.Clamp(_scrollRows, 0, MaxScroll(list));
        }

        private static Rectangle RowBounds(Rectangle list, int visibleIndex)
            => new(list.X + 2, list.Y + 2 + visibleIndex * RowHeight, list.Width - 4 - ScrollbarWidth - 2, RowHeight - 2);

        private static int RowAt(Rectangle list, Point mouse)
        {
            int visible = VisibleRows(list);
            for (int i = 0; i < visible && _scrollRows + i < _entries.Count; i++)
            {
                if (RowBounds(list, i).Contains(mouse))
                    return _scrollRows + i;
            }
            return -1;
        }

        // --- Раскладка ---

        private static int LeftX(Rectangle panel) => panel.X + StructureDialogFrame.Padding;

        private static int ContentBottom(Rectangle panel)
            => StructureDialogFrame.ButtonTop(panel) - StructureDialogFrame.FooterGap;

        private static Rectangle ListBounds(Rectangle panel)
        {
            int y = panel.Y + StructureDialogFrame.ContentTop;
            return new Rectangle(LeftX(panel), y, ListWidth, ContentBottom(panel) - y);
        }

        private static Rectangle PreviewBounds(Rectangle panel)
        {
            int x = LeftX(panel) + ListWidth + ColumnGap;
            int y = panel.Y + StructureDialogFrame.ContentTop;
            return new Rectangle(x, y, panel.Right - StructureDialogFrame.Padding - x,
                ContentBottom(panel) - InfoLines * InfoLineHeight - 6 - y);
        }

        private static Rectangle FolderButtonBounds(Rectangle panel)
            => new(LeftX(panel), StructureDialogFrame.ButtonTop(panel), ListWidth, StructureDialogFrame.ButtonHeight);

        private static Rectangle PlaceButtonBounds(Rectangle panel)
            => new(panel.Right - StructureDialogFrame.Padding - PlaceButtonWidth, StructureDialogFrame.ButtonTop(panel),
                PlaceButtonWidth, StructureDialogFrame.ButtonHeight);

        private static Rectangle CloseButtonBounds(Rectangle panel)
        {
            Rectangle place = PlaceButtonBounds(panel);
            return new Rectangle(place.X - ButtonGap - CloseButtonWidth, place.Y, CloseButtonWidth, place.Height);
        }

        // --- Отрисовка ---

        public static void Draw(SpriteBatch spriteBatch)
        {
            Rectangle panel = StructureDialogFrame.PanelBounds();
            Point mouse = Main.MouseScreen.ToPoint();

            StructureDialogFrame.Draw(spriteBatch, panel, ToolText.Get("Library.Title"), mouse);
            DrawList(spriteBatch, ListBounds(panel), mouse);

            Rectangle preview = PreviewBounds(panel);
            _preview.Draw(spriteBatch, preview);
            DrawInfo(spriteBatch, preview);

            Rectangle folder = FolderButtonBounds(panel);
            SoAHudDraw.Button(spriteBatch, folder, ToolText.Get("Library.OpenFolder"),
                SoAHudDraw.ButtonFill, SoAHudDraw.PanelBorder, folder.Contains(mouse));

            Rectangle close = CloseButtonBounds(panel);
            SoAHudDraw.Button(spriteBatch, close, ToolText.Get("Library.Close"),
                SoAHudDraw.CancelFill, SoAHudDraw.CancelBorder, close.Contains(mouse));

            Rectangle place = PlaceButtonBounds(panel);
            bool canPlace = _preview.Data != null;
            SoAHudDraw.Button(spriteBatch, place, ToolText.Get("Library.Place"),
                SoAHudDraw.SaveFill, SoAHudDraw.SaveBorder, canPlace && place.Contains(mouse), canPlace);
        }

        private static void DrawList(SpriteBatch spriteBatch, Rectangle list, Point mouse)
        {
            SoAHudDraw.Panel(spriteBatch, list, new Color(12, 14, 28, 235), SoAHudDraw.PanelBorder);

            if (_entries.Count == 0)
            {
                var hintPosition = new Vector2(list.X + RowPadding, list.Y + RowPadding);
                foreach (string line in ToolText.Lines("Library.Empty"))
                {
                    SoAHudDraw.Text(spriteBatch, line, hintPosition, SoAHudDraw.DimText, 0.8f);
                    hintPosition.Y += InfoLineHeight;
                }
                return;
            }

            int visible = VisibleRows(list);
            for (int i = 0; i < visible && _scrollRows + i < _entries.Count; i++)
            {
                int index = _scrollRows + i;
                StructureEntry entry = _entries[index];
                Rectangle row = RowBounds(list, i);
                bool selected = index == _selected;
                bool hovered = row.Contains(mouse);

                if (selected)
                    SoAHudDraw.Panel(spriteBatch, row, SoAHudDraw.ButtonHoverFill, SoAHudDraw.ActiveBorder);
                else if (hovered)
                    SoAHudDraw.Fill(spriteBatch, row, SoAHudDraw.ButtonFill);

                string source = SourceLabel(entry.Source);
                float sourceWidth = SoAHudDraw.Measure(source, 0.72f).X;
                SoAHudDraw.Text(spriteBatch, source,
                    new Vector2(row.Right - RowPadding - sourceWidth, row.Y + 7), SourceColor(entry.Source), 0.72f);

                float nameWidth = row.Width - RowPadding * 3 - sourceWidth;
                SoAHudDraw.Text(spriteBatch, StructureDialogFrame.Ellipsize(entry.Name, nameWidth, 0.85f),
                    new Vector2(row.X + RowPadding, row.Y + 5), selected ? Color.White : SoAHudDraw.BodyText, 0.85f);
            }

            DrawScrollbar(spriteBatch, list, visible);
        }

        private static void DrawScrollbar(SpriteBatch spriteBatch, Rectangle list, int visible)
        {
            if (_entries.Count <= visible)
                return;

            int trackHeight = list.Height - 4;
            int thumbHeight = Math.Max(16, trackHeight * visible / _entries.Count);
            int thumbY = list.Y + 2 + (trackHeight - thumbHeight) * _scrollRows / Math.Max(1, MaxScroll(list));
            SoAHudDraw.Fill(spriteBatch, new Rectangle(list.Right - ScrollbarWidth - 2, thumbY, ScrollbarWidth, thumbHeight),
                SoAHudDraw.PanelBorder);
        }

        private static void DrawInfo(SpriteBatch spriteBatch, Rectangle preview)
        {
            var position = new Vector2(preview.X, preview.Bottom + 6);
            float maxWidth = preview.Width;

            void Line(string text, Color color, float scale = 0.82f)
            {
                if (!string.IsNullOrEmpty(text))
                    SoAHudDraw.Text(spriteBatch, StructureDialogFrame.Ellipsize(text, maxWidth, scale), position, color, scale);
                position.Y += InfoLineHeight;
            }

            if (Selected is not StructureEntry entry)
                return;

            Line($"{entry.Name}  ·  {SourceLabel(entry.Source)}", SoAHudDraw.TitleText, 0.9f);

            StructureData data = _preview.Data;
            if (data == null)
            {
                Line(_loadFailed ? ToolText.Get("Library.LoadFailed") : string.Empty, Color.OrangeRed);
                return;
            }

            Line(ToolText.Get("Library.Stats", data.Width, data.Height, _stats.Cells, _stats.Tiles, _stats.Walls),
                SoAHudDraw.BodyText);

            if (data.MissingTypes > 0)
                Line(ToolText.Get("Library.MissingTypes", data.MissingTypes), Color.Orange);
            else
                Line(ToolText.Get("Preview.Hint"), SoAHudDraw.DimText, 0.75f);

            Line(_preview.DescribeHover(), SoAHudDraw.BodyText, 0.8f);
        }

        private static string SourceLabel(StructureSource source) => ToolText.Get("Library.Source." + source);

        private static Color SourceColor(StructureSource source) => source switch
        {
            StructureSource.Mod => new Color(126, 214, 130),
            StructureSource.Sources => new Color(244, 205, 96),
            _ => new Color(110, 170, 240)
        };
    }
}
