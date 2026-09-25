using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
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
        public readonly bool RightClick;
        public readonly bool EnterPressed;
        public readonly bool EscapePressed;

        public UiInput(Point mouse, bool leftClick, bool rightClick, bool enterPressed, bool escapePressed)
        {
            Mouse = mouse;
            LeftClick = leftClick;
            RightClick = rightClick;
            EnterPressed = enterPressed;
            EscapePressed = escapePressed;
        }
    }

    // Единая точка ввода и отрисовки интерфейса инструмента: тулбар и диалог
    // сохранения сами по себе статические, чтобы порядок обработки был явным.
    public class StructureToolUi : ModSystem
    {
        private const int HeldGraceFrames = 3;
        private const int SuppressFramesAfterClose = 3;

        private static KeyboardState _previousKeys;
        private static bool _previousLeft;
        private static bool _previousRight;
        private static int _heldFrames;
        private static int _suppressFrames;

        // Жезл сообщает, что он в руке — так UI не зависит от классов контента
        public static void NotifyHeld() => _heldFrames = HeldGraceFrames;

        public static bool ToolbarVisible => _heldFrames > 0;

        // Мышь занята интерфейсом — жезл не должен выделять зону в мире
        public static bool CapturesMouse(Point mouse)
            => StructureSaveDialog.IsOpen
            || (ToolbarVisible && StructureToolbar.Bounds().Contains(mouse));

        public override void OnWorldLoad()
        {
            // Main.blockInput сами не ставим (он вешает клавиатуру целиком), но если
            // остался включённым от чужого кода — снимаем на входе в мир
            Main.blockInput = false;
        }

        public override void OnWorldUnload()
        {
            StructureSaveDialog.Close();
            StructureSelection.Clear();
        }

        public override void UpdateUI(GameTime gameTime)
        {
            KeyboardState keys = Keyboard.GetState();
            var input = new UiInput(
                Main.MouseScreen.ToPoint(),
                Main.mouseLeft && !_previousLeft,
                Main.mouseRight && !_previousRight,
                keys.IsKeyDown(Keys.Enter) && !_previousKeys.IsKeyDown(Keys.Enter),
                keys.IsKeyDown(Keys.Escape) && !_previousKeys.IsKeyDown(Keys.Escape));

            _previousKeys = keys;
            _previousLeft = Main.mouseLeft;
            _previousRight = Main.mouseRight;

            if (_heldFrames > 0)
                _heldFrames--;

            if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
            {
                StructureSaveDialog.Close();
                return;
            }

            if (StructureSaveDialog.IsOpen)
            {
                StructureToolbar.ClearHover();
                SuppressVanillaKeyReaction();
                Main.LocalPlayer.mouseInterface = true;
                PlayerInput.WritingText = true;
                Main.instance.HandleIME();
                StructureSaveDialog.HandleInput(input);
                return;
            }

            if (_suppressFrames > 0)
            {
                _suppressFrames--;
                SuppressVanillaKeyReaction();
            }

            if (ToolbarVisible)
                StructureToolbar.HandleInput(input);
            else
                StructureToolbar.ClearHover();
        }

        public static void NotifyDialogClosed() => _suppressFrames = SuppressFramesAfterClose;

        // Ванильный Enter открывает чат, Esc — инвентарь. Гасим только эти два эффекта:
        // Main.blockInput для этого использовать нельзя, он вешает всю клавиатуру
        private static void SuppressVanillaKeyReaction()
        {
            Main.drawingPlayerChat = false;
            Main.playerInventory = false;
        }

        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (ToolbarVisible)
                StructureToolbar.Draw(spriteBatch);
            if (StructureSaveDialog.IsOpen)
            {
                StructureSaveDialog.ReadTypedText();
                StructureSaveDialog.Draw(spriteBatch);
            }
        }
    }
}
