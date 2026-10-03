using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.Graphics.Capture;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Срез ввода за кадр. Фронты нажатий считаем сами: ванильные mouseLeftRelease и
    // Main.inputTextEnter зависят от того, в какой фазе кадра их читать
    public readonly struct UiInput
    {
        public readonly Point Mouse;
        public readonly bool LeftClick;
        public readonly bool LeftHeld;
        public readonly bool RightClick;
        public readonly int Scroll;
        public readonly bool EnterPressed;
        public readonly bool EscapePressed;

        public UiInput(Point mouse, bool leftClick, bool leftHeld, bool rightClick, int scroll,
            bool enterPressed, bool escapePressed)
        {
            Mouse = mouse;
            LeftClick = leftClick;
            LeftHeld = leftHeld;
            RightClick = rightClick;
            Scroll = scroll;
            EnterPressed = enterPressed;
            EscapePressed = escapePressed;
        }
    }

    // Единая точка ввода и отрисовки интерфейса инструмента: тулбар и окна
    // (сохранение, библиотека) сами по себе статические, чтобы порядок обработки был явным.
    public class StructureToolUi : ModSystem
    {
        private const int HeldGraceFrames = 3;
        private const int SuppressFramesAfterClose = 3;

        private static KeyboardState _previousKeys;
        private static bool _previousLeft;
        private static bool _previousRight;
        private static int _heldFrames;
        private static int _suppressFrames;
        private static int _lockedHotbarSlot;
        private static bool _waitForMouseRelease;

        public static ModKeybind UndoKeybind { get; private set; }

        // Жезл сообщает, что он в руке — так UI не зависит от классов контента
        public static void NotifyHeld() => _heldFrames = HeldGraceFrames;

        public static bool ToolbarVisible => _heldFrames > 0;

        public static bool ModalOpen => StructureSaveDialog.IsOpen || StructureLibraryDialog.IsOpen;

        // После закрытия окна кнопка мыши ещё зажата — жезл не должен принять
        // этот же клик за начало мазка или за размещение постройки
        public static bool WaitingForMouseRelease => _waitForMouseRelease;

        // Мир закрыт целиком: полноэкранная карта, настройки, полноэкранные окна
        // (бестиарий, конфиг модов) или режим камеры, где ЛКМ выделяет кадр
        public static bool GameScreenCovered()
            => Main.mapFullscreen
            || Main.ingameOptionsWindow
            || Main.InGameUI.IsVisible
            || CaptureManager.Instance.Active;

        // Мышь занята интерфейсом — жезл не должен выделять зону в мире
        public static bool CapturesMouse(Point mouse)
            => ModalOpen || (ToolbarVisible && StructureToolbar.Bounds().Contains(mouse));

        public override void Load()
        {
            UndoKeybind = KeybindLoader.RegisterKeybind(Mod, "StructureUndo", Keys.Z);
        }

        public override void Unload() => UndoKeybind = null;

        public override void OnWorldLoad()
        {
            // Main.blockInput сами не ставим (он вешает клавиатуру целиком), но если
            // остался включённым от чужого кода — снимаем на входе в мир
            Main.blockInput = false;
        }

        public override void OnWorldUnload()
        {
            StructureSaveDialog.Close();
            StructureLibraryDialog.Close();
            StructurePlacement.End();
            StructureSelection.Reset();
            StructureHistory.Clear();
        }

        public static void NotifyModalOpened()
        {
            _lockedHotbarSlot = Main.LocalPlayer.selectedItem;
            Main.clrInput();
        }

        public static void NotifyDialogClosed()
        {
            _suppressFrames = SuppressFramesAfterClose;
            _waitForMouseRelease = true;
        }

        // Общая отмена для кнопки тулбара и клавиши
        public static void Undo()
        {
            if (!StructureHistory.CanUndo)
            {
                Main.NewText(ToolText.Get("Chat.NothingToUndo"), Color.Orange);
                return;
            }

            // Откат размещения меняет мир — в сетевой игре размещения и не было
            StructureHistory.Undo(out bool restoredWorld);
            Main.NewText(ToolText.Get(restoredWorld ? "Chat.UndonePlacement" : "Chat.UndoneSelection"),
                Color.LightBlue);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        public override void UpdateUI(GameTime gameTime)
        {
            KeyboardState keys = Keyboard.GetState();
            var input = new UiInput(
                Main.MouseScreen.ToPoint(),
                Main.mouseLeft && !_previousLeft,
                Main.mouseLeft,
                Main.mouseRight && !_previousRight,
                PlayerInput.ScrollWheelDeltaForUI,
                keys.IsKeyDown(Keys.Enter) && !_previousKeys.IsKeyDown(Keys.Enter),
                keys.IsKeyDown(Keys.Escape) && !_previousKeys.IsKeyDown(Keys.Escape));

            _previousKeys = keys;
            _previousLeft = Main.mouseLeft;
            _previousRight = Main.mouseRight;

            if (_heldFrames > 0)
                _heldFrames--;
            if (_waitForMouseRelease && !Main.mouseLeft && !Main.mouseRight)
                _waitForMouseRelease = false;

            if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
            {
                StructureSaveDialog.Close();
                StructureLibraryDialog.Close();
                return;
            }

            if (ModalOpen)
            {
                // Окна модальные: колесо масштабирует превью, а не листает хотбар
                StructureToolbar.ClearHover();
                SuppressVanillaKeyReaction();
                Main.LocalPlayer.mouseInterface = true;
                Main.LocalPlayer.selectedItem = _lockedHotbarSlot;
                PlayerInput.WritingText = true;
                Main.instance.HandleIME();

                if (StructureSaveDialog.IsOpen)
                    StructureSaveDialog.HandleInput(input);
                else
                    StructureLibraryDialog.HandleInput(input);
                return;
            }

            if (_suppressFrames > 0)
            {
                _suppressFrames--;
                SuppressVanillaKeyReaction();
            }

            // На карте и под полноэкранными окнами тулбар не рисуется — и кликов не ловит
            if (ToolbarVisible && !GameScreenCovered())
                StructureToolbar.HandleInput(input);
            else
                StructureToolbar.ClearHover();
        }

        // Ванильный Enter открывает чат, Esc — инвентарь. Гасим только эти два эффекта:
        // Main.blockInput для этого использовать нельзя, он вешает всю клавиатуру
        private static void SuppressVanillaKeyReaction()
        {
            Main.drawingPlayerChat = false;
            Main.playerInventory = false;
        }

        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (ToolbarVisible && !GameScreenCovered())
            {
                StructureToolbar.Draw(spriteBatch);
                if (!ModalOpen)
                    StructureCursorHud.Draw(spriteBatch);
            }

            if (StructureSaveDialog.IsOpen)
            {
                StructureSaveDialog.ReadTypedText();
                StructureSaveDialog.Draw(spriteBatch);
            }
            else if (StructureLibraryDialog.IsOpen)
            {
                StructureLibraryDialog.Draw(spriteBatch);
            }
        }
    }
}
