using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Common.Config;

namespace SoA.Common.UI
{
    // Полноэкранное дев-меню: список того, что нужно сделать автору — спрайты
    // с именами файлов, проверки в игре, нерешённые вопросы.
    // Открывается клавишей «ё». Сами задачи лежат в файле рядом с сейвами,
    // см. DevBoard: меню только показывает их и правит статусы.
    // Подписи самого меню — в Mods.SoA.DevMenu.
    public class DevMenu : ModSystem
    {
        private const int ContentMaxWidth = 1100;
        private const int SideMargin = 80;
        private const int TopMargin = 70;
        private const int BottomMargin = 60;
        private const int HeaderHeight = 54;
        private const int SectionGap = 22;
        private const int SectionHeaderHeight = 30;
        private const int TaskGap = 12;
        private const int CheckboxSize = 18;
        private const int TextOffset = CheckboxSize + 12;
        private const int TitleLineHeight = 21;
        private const int DetailLineHeight = 18;
        private const int ScrollStep = 48;
        private const int SuppressFramesAfterClose = 3;
        private const int ShotDelayFrames = 3;   // кадры между закрытием меню и снимком

        private const float TitleScale = 0.9f;
        private const float DetailScale = 0.8f;

        private const string TextKey = "Mods.SoA.DevMenu.";

        private static readonly Color OverlayColor = new(8, 10, 20, 238);
        private static readonly Color DetailColor = new(150, 158, 195);
        private static readonly Color PathColor = new(130, 200, 160);
        private static readonly Color DoingColor = new(240, 190, 80);
        private static readonly Color LaterColor = new(120, 128, 160);

        // Строка списка: и рисование, и клики работают по одной раскладке,
        // поэтому её больше нельзя развести между двумя методами
        private readonly struct Row
        {
            public readonly Rectangle Bounds;
            public readonly DevBoardSection Section;
            public readonly DevBoardTask Task;

            public Row(Rectangle bounds, DevBoardSection section, DevBoardTask task)
            {
                Bounds = bounds;
                Section = section;
                Task = task;
            }
        }

        public static ModKeybind ToggleKeybind { get; private set; }

        private static readonly List<Row> _rows = new();
        private static bool _open;
        private static bool _previousLeft, _previousRight;
        private static KeyboardState _previousKeys;
        private static int _scroll;
        private static int _contentHeight;
        private static int _suppressFrames;
        private static int _lockedHotbarSlot;
        private static int _shotFrames;
        private static Rectangle _counterButton;

        public static bool IsOpen => _open;

        private static bool Enabled => SoADevConfig.Instance?.EnableDevMenu == true;

        public override void Load()
        {
            ToggleKeybind = KeybindLoader.RegisterKeybind(Mod, "DevMenu", Keys.OemTilde);
        }

        public override void Unload()
        {
            ToggleKeybind = null;
            DevIcons.ClearCache();
        }

        public override void OnWorldLoad() => DevBoard.Reload();

        public override void OnWorldUnload() => _open = false;

        public static void Toggle()
        {
            if (!Enabled)
                return;

            _open = !_open;
            if (_open)
            {
                // Перечитываем каждый раз: доску мог поправить Claude, пока игра открыта
                DevBoard.Reload();
                _scroll = 0;
                _lockedHotbarSlot = Main.LocalPlayer.selectedItem;
            }
            else
            {
                _suppressFrames = SuppressFramesAfterClose;
            }

            SoundEngine.PlaySound(_open ? SoundID.MenuOpen : SoundID.MenuClose);
        }

        public override void UpdateUI(GameTime gameTime)
        {
            KeyboardState keys = Keyboard.GetState();
            bool escape = Pressed(keys, Keys.Escape);
            bool reload = Pressed(keys, Keys.F5);
            bool shot = Pressed(keys, Keys.F2);
            _previousKeys = keys;

            bool leftClick = Main.mouseLeft && !_previousLeft;
            bool rightClick = Main.mouseRight && !_previousRight;
            _previousLeft = Main.mouseLeft;
            _previousRight = Main.mouseRight;

            if (_suppressFrames > 0)
            {
                _suppressFrames--;
                Main.playerInventory = false;
            }

            if (!_open)
                return;

            if (!Enabled || Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
            {
                _open = false;
                return;
            }

            // Меню модальное: мышь и колесо не должны уходить в мир и хотбар
            Main.LocalPlayer.mouseInterface = true;
            Main.LocalPlayer.selectedItem = _lockedHotbarSlot;
            Main.playerInventory = false;

            if (escape)
            {
                Toggle();
                return;
            }

            if (shot)
            {
                // Меню закрываем сейчас, снимок делаем через пару кадров:
                // в кадре должна остаться игра, а не оверлей доски
                _shotFrames = ShotDelayFrames;
                Toggle();
                return;
            }

            if (reload)
            {
                DevBoard.Reload();
                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }

            ApplyScroll();

            if (!leftClick && !rightClick)
                return;

            Point mouse = Main.MouseScreen.ToPoint();

            // Счётчик в шапке работает кнопкой «скрыть готовые»: отдельная клавиша
            // тут не годится — игра продолжает слышать хоткеи и, например, лечит по H
            if (_counterButton.Contains(mouse))
            {
                DevBoard.HideDone = !DevBoard.HideDone;
                DevBoard.Save();
                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }

            HandleClick(mouse, leftClick ? 1 : -1);
        }

        private static bool Pressed(KeyboardState keys, Keys key)
            => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

        private static void ApplyScroll()
        {
            int delta = PlayerInput.ScrollWheelDeltaForUI;
            if (delta == 0)
                return;

            int viewport = ViewportBounds().Height;
            int maxScroll = Math.Max(0, _contentHeight - viewport);
            _scroll = Math.Clamp(_scroll - Math.Sign(delta) * ScrollStep, 0, maxScroll);
        }

        // direction: 1 — статус вперёд по кругу, -1 — назад
        private static void HandleClick(Point mouse, int direction)
        {
            foreach (Row row in _rows)
            {
                if (!row.Bounds.Contains(mouse))
                    continue;

                if (row.Task != null)
                    DevBoard.Cycle(row.Task, direction);
                else
                {
                    row.Section.Collapsed = !row.Section.Collapsed;
                    DevBoard.Save();
                }

                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }
        }

        // --- Отрисовка ---

        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            TakePendingShot();

            if (!_open || !Enabled)
                return;

            SoAHudDraw.Fill(spriteBatch, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), OverlayColor);

            Rectangle viewport = ViewportBounds();
            DrawHeader(spriteBatch, viewport);

            Point mouse = Main.MouseScreen.ToPoint();
            int y = viewport.Y - _scroll;
            _rows.Clear();

            foreach (DevBoardSection section in DevBoard.Sections)
            {
                var headerRow = new Rectangle(viewport.X, y, viewport.Width, SectionHeaderHeight);
                if (headerRow.Y >= viewport.Y && headerRow.Bottom <= viewport.Bottom)
                {
                    DrawSectionHeader(spriteBatch, section, headerRow, mouse);
                    _rows.Add(new Row(headerRow, section, null));
                }
                y += SectionHeaderHeight;

                if (!section.Collapsed)
                {
                    foreach (DevBoardTask task in section.Tasks)
                    {
                        if (DevBoard.HideDone && task.Status == DevStatus.Done)
                            continue;

                        int height = TaskHeight(task);
                        var row = new Rectangle(viewport.X, y, viewport.Width, height);

                        // Рисуем и ловим клики только по полностью видимым строкам —
                        // так ничего не наезжает на заголовок при прокрутке
                        if (row.Y >= viewport.Y && row.Bottom <= viewport.Bottom)
                        {
                            DrawTask(spriteBatch, task, row, mouse);
                            _rows.Add(new Row(row, section, task));
                        }
                        y += height + TaskGap;
                    }
                }

                y += SectionGap;
            }

            _contentHeight = y + _scroll - viewport.Y;
            DrawScrollbar(spriteBatch, viewport);
        }

        private static void TakePendingShot()
        {
            if (_shotFrames == 0)
                return;

            if (--_shotFrames > 0)
                return;

            string fileName = DevShot.Capture(out string error);
            if (fileName == null)
            {
                Main.NewText(Language.GetTextValue(TextKey + "ShotFailed", error), Color.Orange);
                return;
            }

            Point tile = Main.LocalPlayer.Center.ToTileCoordinates();
            DevBoard.AddNote($"Снимок {DateTime.Now:dd.MM HH:mm}",
                DevBoard.ShotFolderName + "/" + fileName,
                $"Игрок: x={tile.X} y={tile.Y}, мир {Main.worldName}",
                "Что не так — допиши строкой ниже прямо в файле доски.");

            Main.NewText(Language.GetTextValue(TextKey + "ShotSaved", fileName), SoAHudDraw.SaveBorder);
        }

        private static void DrawHeader(SpriteBatch spriteBatch, Rectangle viewport)
        {
            var titlePosition = new Vector2(viewport.X, viewport.Y - HeaderHeight + 6);
            SoAHudDraw.Text(spriteBatch, Language.GetTextValue(TextKey + "Title"), titlePosition, Color.White, 1.15f);

            int total = DevBoard.TotalCount;
            int done = DevBoard.DoneTotalCount;
            string counter = $"{done}/{total}";
            Vector2 counterSize = SoAHudDraw.Measure(counter, 1.15f);
            var counterPosition = new Vector2(viewport.Right - counterSize.X, titlePosition.Y);
            _counterButton = new Rectangle((int)counterPosition.X - 6, (int)counterPosition.Y,
                (int)counterSize.X + 12, (int)counterSize.Y);

            Color counterColor = DevBoard.HideDone
                ? SoAHudDraw.ActiveBorder
                : total > 0 && done == total ? SoAHudDraw.SaveBorder : SoAHudDraw.DimText;
            if (_counterButton.Contains(Main.MouseScreen.ToPoint()))
                counterColor = Color.White;
            SoAHudDraw.Text(spriteBatch, counter, counterPosition, counterColor, 1.15f);

            string hint = Language.GetTextValue(TextKey + "Hint");
            if (DevBoard.HideDone)
                hint += Language.GetTextValue(TextKey + "HiddenSuffix");
            SoAHudDraw.Text(spriteBatch, hint,
                new Vector2(viewport.X, viewport.Bottom + 14), SoAHudDraw.DimText, 0.8f);

            if (DevBoard.Error != null)
                SoAHudDraw.Text(spriteBatch, DevBoard.Error,
                    new Vector2(viewport.X, viewport.Bottom + 32), SoAHudDraw.CloseBorder, 0.8f);

            SoAHudDraw.Fill(spriteBatch, new Rectangle(viewport.X, viewport.Y - 12, viewport.Width, 2),
                SoAHudDraw.PanelBorder);
        }

        private static void DrawSectionHeader(SpriteBatch spriteBatch, DevBoardSection section, Rectangle row, Point mouse)
        {
            bool hovered = row.Contains(mouse);
            Color color = hovered ? Color.White : SoAHudDraw.TitleText;

            // Треугольник-раскрывашка: вправо — свёрнуто, вниз — раскрыто
            var arrow = new Vector2(row.X + 6, row.Y + 9);
            if (section.Collapsed)
            {
                SoAHudDraw.Line(spriteBatch, arrow, arrow + new Vector2(8f, 4f), 2f, color);
                SoAHudDraw.Line(spriteBatch, arrow + new Vector2(8f, 4f), arrow + new Vector2(0f, 8f), 2f, color);
            }
            else
            {
                SoAHudDraw.Line(spriteBatch, arrow, arrow + new Vector2(8f, 8f), 2f, color);
                SoAHudDraw.Line(spriteBatch, arrow + new Vector2(8f, 8f), arrow + new Vector2(16f, 0f), 2f, color);
            }

            SoAHudDraw.Text(spriteBatch, section.Name, new Vector2(row.X + 26, row.Y), color, 1f);

            string counter = $"{section.DoneCount}/{section.Tasks.Count}";
            SoAHudDraw.Text(spriteBatch, counter,
                new Vector2(row.Right - SoAHudDraw.Measure(counter, 0.8f).X, row.Y + 3),
                SoAHudDraw.DimText, 0.8f);
        }

        private static void DrawTask(SpriteBatch spriteBatch, DevBoardTask task, Rectangle row, Point mouse)
        {
            bool done = task.Status == DevStatus.Done;
            bool hovered = row.Contains(mouse);

            var box = new Rectangle(row.X, row.Y + 1, CheckboxSize, CheckboxSize);
            SoAHudDraw.Panel(spriteBatch, box, new Color(16, 18, 36, 235),
                hovered ? SoAHudDraw.ActiveBorder : SoAHudDraw.PanelBorder);
            DrawStatusMark(spriteBatch, task.Status, box);

            Color titleColor = task.Status switch
            {
                DevStatus.Done => SoAHudDraw.DimText,
                DevStatus.Later => LaterColor,
                DevStatus.Doing => DoingColor,
                _ => hovered ? Color.White : SoAHudDraw.BodyText
            };

            var titlePosition = new Vector2(row.X + TextOffset, row.Y);
            SoAHudDraw.Text(spriteBatch, task.Title, titlePosition, titleColor, TitleScale);

            if (done)
                StrikeThrough(spriteBatch, titlePosition, task.Title, TitleScale, TitleLineHeight);

            int y = row.Y + TitleLineHeight;
            foreach (string detail in task.Details)
            {
                var position = new Vector2(row.X + TextOffset + 14, y);
                bool isPath = detail.EndsWith(".png") || detail.Contains(".png,") || detail.Contains('/');
                SoAHudDraw.Text(spriteBatch, detail, position,
                    done ? SoAHudDraw.DimText : isPath ? PathColor : DetailColor, DetailScale);

                if (done)
                    StrikeThrough(spriteBatch, position, detail, DetailScale, DetailLineHeight);

                y += DetailLineHeight;
            }
        }

        private static void DrawStatusMark(SpriteBatch spriteBatch, DevStatus status, Rectangle box)
        {
            switch (status)
            {
                case DevStatus.Done:
                    SoAHudDraw.CheckMark(spriteBatch, box, SoAHudDraw.SaveBorder);
                    break;
                case DevStatus.Doing:
                    // Точка в центре: работа начата, но не закрыта
                    SoAHudDraw.Circle(spriteBatch, box.Center.ToVector2(), 4, DoingColor);
                    break;
                case DevStatus.Later:
                    // Прочерк: отложено осознанно, а не забыто
                    SoAHudDraw.Line(spriteBatch, new Vector2(box.X + 4, box.Center.Y),
                        new Vector2(box.Right - 4, box.Center.Y), 2f, LaterColor);
                    break;
            }
        }

        private static void StrikeThrough(SpriteBatch spriteBatch, Vector2 position, string text,
            float scale, int lineHeight)
        {
            float width = SoAHudDraw.Measure(text, scale).X;
            float middle = position.Y + lineHeight * 0.42f;
            SoAHudDraw.Line(spriteBatch, new Vector2(position.X, middle),
                new Vector2(position.X + width, middle), 1.5f, SoAHudDraw.DimText);
        }

        private static void DrawScrollbar(SpriteBatch spriteBatch, Rectangle viewport)
        {
            if (_contentHeight <= viewport.Height)
                return;

            var track = new Rectangle(viewport.Right + 16, viewport.Y, 4, viewport.Height);
            SoAHudDraw.Fill(spriteBatch, track, SoAHudDraw.PanelBorder * 0.4f);

            float visible = viewport.Height / (float)_contentHeight;
            int thumbHeight = Math.Max(30, (int)(viewport.Height * visible));
            int maxScroll = Math.Max(1, _contentHeight - viewport.Height);
            int thumbY = viewport.Y + (int)((viewport.Height - thumbHeight) * (_scroll / (float)maxScroll));
            SoAHudDraw.Fill(spriteBatch, new Rectangle(track.X, thumbY, track.Width, thumbHeight),
                SoAHudDraw.TitleText * 0.8f);
        }

        // --- Раскладка ---

        private static Rectangle ViewportBounds()
        {
            int width = Math.Min(ContentMaxWidth, Main.screenWidth - SideMargin * 2);
            int x = (Main.screenWidth - width) / 2;
            int top = TopMargin + HeaderHeight;
            return new Rectangle(x, top, width, Main.screenHeight - top - BottomMargin);
        }

        private static int TaskHeight(DevBoardTask task) => TitleLineHeight + task.Details.Count * DetailLineHeight;
    }
}
