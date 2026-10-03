using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.UI;
using SoA.Common.Utils;

namespace SoA.Content.Items.DevTools
{
    // Жезл построек (v4). Выделение: зажатая ЛКМ протягивает прямоугольный мазок, режим
    // (заменить / добавить / вырезать) складывает из мазков постройку любой формы,
    // ПКМ сбрасывает выделение. Размещение: постройка из библиотеки идёт призраком за
    // курсором, ЛКМ ставит, СКМ отражает, ПКМ выходит. Режимы, библиотека, отмена
    // и сохранение — в тулбаре слева.
    // Предмет намеренно неиспользуемый (useStyle None): вся мышь читается вручную,
    // иначе ванильный свинг мешал бы протяжке.
    public class StructureWand : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 28;
            Item.useStyle = ItemUseStyleID.None;
            Item.autoReuse = false;
            Item.rare = ItemRarityID.Red;
            Item.value = 0;
        }

        // Фронты кнопок считаем сами: ванильные *Release другие интерфейсы
        // сбрасывают по-разному, а нам нужен именно момент нажатия
        private static bool _wasLeft;
        private static bool _wasRight;
        private static bool _wasMiddle;

        // Текущее нажатие ЛКМ началось в мире, а не над интерфейсом. Только такое
        // нажатие тянет мазок: клик по карте, инвентарю или окну другого мода,
        // уведённый потом в мир, выделения не создаёт
        private static bool _leftOwnedByWorld;

        public override void HoldItem(Player player)
        {
            if (player.whoAmI != Main.myPlayer)
                return;

            // Только что взяли жезл в руку — уже зажатые кнопки нажатием не считаются
            bool justEquipped = !StructureToolUi.ToolbarVisible;
            StructureToolUi.NotifyHeld();
            if (justEquipped)
            {
                _wasLeft = Main.mouseLeft;
                _wasRight = Main.mouseRight;
                _wasMiddle = Main.mouseMiddle;
                _leftOwnedByWorld = false;
            }

            var press = new MousePress(
                Main.mouseLeft && !_wasLeft,
                Main.mouseRight && !_wasRight,
                Main.mouseMiddle && !_wasMiddle);
            _wasLeft = Main.mouseLeft;
            _wasRight = Main.mouseRight;
            _wasMiddle = Main.mouseMiddle;

            if (InputBlocked())
            {
                StructureSelection.CancelDrag();
                _leftOwnedByWorld = false;
                return;
            }

            if (StructureToolUi.UndoKeybind?.JustPressed == true)
            {
                StructureSelection.CancelDrag();
                _leftOwnedByWorld = false;
                StructureToolUi.Undo();
                return;
            }

            // Нажатие над интерфейсом целиком принадлежит ему
            if (MouseOverInterface(player))
                press = default;
            else if (press.Left)
                _leftOwnedByWorld = true;
            if (!Main.mouseLeft)
                _leftOwnedByWorld = false;

            Point tile = Main.MouseWorld.ToTileCoordinates();

            if (StructurePlacement.Active)
            {
                HandlePlacement(tile, press);
                return;
            }

            // Колесико — быстрая смена режима без похода в тулбар
            if (press.Middle)
            {
                StructureSelection.CycleMode();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }

            // Начатую протяжку интерфейс не перехватывает — курсор может заехать на него
            if (Main.mouseLeft && _leftOwnedByWorld)
            {
                if (StructureSelection.Dragging)
                    StructureSelection.UpdateDrag(tile);
                else
                    StructureSelection.BeginDrag(tile);
            }
            else if (StructureSelection.Dragging)
            {
                StructureSelection.CommitDrag();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }

            if (press.Right && StructureSelection.Count > 0)
            {
                StructureSelection.Clear();
                Main.NewText(ToolText.Get("Chat.SelectionCleared"), Color.Orange);
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
        }

        private static void HandlePlacement(Point cursorTile, MousePress press)
        {
            StructureSelection.CancelDrag();
            StructurePlacement.UpdateCursor(cursorTile);

            if (press.Middle)
            {
                StructurePlacement.ToggleMirror();
                StructurePlacement.UpdateCursor(cursorTile);
                SoundEngine.PlaySound(SoundID.MenuTick);
            }

            if (press.Right)
            {
                StructurePlacement.End();
                SoundEngine.PlaySound(SoundID.MenuClose);
                return;
            }

            if (!press.Left)
                return;

            // Постройка меняет сразу сотни клеток — синхронизировать это нечем,
            // поэтому только одиночная игра (инструмент авторский)
            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                Main.NewText(ToolText.Get("Chat.PlaceSingleplayerOnly"), Color.Orange);
                return;
            }

            StructurePlacement.PlaceNow();
            Main.NewText(ToolText.Get("Chat.Placed", StructurePlacement.Name), Color.LightGreen);
            SoundEngine.PlaySound(SoundID.Dig);
        }

        // Пока открыт инвентарь, чат, карта или окно инструмента — мышь принадлежит им.
        // После закрытия окна ждём отпускания кнопки, чтобы клик не ушёл в мир
        private static bool InputBlocked()
            => StructureToolUi.ModalOpen
            || StructureToolUi.WaitingForMouseRelease
            || StructureToolUi.GameScreenCovered()
            || Main.playerInventory
            || Main.drawingPlayerChat
            || Main.editSign
            || Main.editChest
            || Main.LocalPlayer.dead;

        // Курсор над интерфейсом: ванилла и окна модов ставят mouseInterface, пока мышь
        // над ними. Флаг сбрасывается и выставляется в разных фазах кадра, поэтому
        // смотрим и прошлый кадр — нажатие поймано, даже если UI обновился позже жезла
        private static bool MouseOverInterface(Player player)
            => player.mouseInterface
            || player.lastMouseInterface
            || Main.blockMouse
            || StructureToolUi.CapturesMouse(Main.MouseScreen.ToPoint());

        private readonly record struct MousePress(bool Left, bool Right, bool Middle);
    }
}
