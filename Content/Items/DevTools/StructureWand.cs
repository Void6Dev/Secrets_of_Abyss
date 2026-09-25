using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.UI;
using SoA.Common.Utils;

namespace SoA.Content.Items.DevTools
{
    // Жезл выделения (v3): зажатая ЛКМ протягивает прямоугольный мазок, режим
    // (заменить / добавить / вырезать) складывает из мазков постройку любой формы.
    // Режимы и сохранение — в тулбаре слева, ПКМ сбрасывает выделение.
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

        public override void HoldItem(Player player)
        {
            if (player.whoAmI != Main.myPlayer)
                return;

            StructureToolUi.NotifyHeld();

            if (InputBlocked())
            {
                StructureSelection.CancelDrag();
                return;
            }

            // Начатую протяжку интерфейс не перехватывает — курсор может заехать на него
            if (!StructureSelection.Dragging && StructureToolUi.CapturesMouse(Main.MouseScreen.ToPoint()))
                return;

            // Колесико — быстрая смена режима без похода в тулбар
            if (Main.mouseMiddle && Main.mouseMiddleRelease)
            {
                StructureSelection.CycleMode();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }

            Point tile = Main.MouseWorld.ToTileCoordinates();

            if (Main.mouseLeft)
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

            if (Main.mouseRight && Main.mouseRightRelease && StructureSelection.Count > 0)
            {
                StructureSelection.Clear();
                Main.NewText(ToolText.Get("Chat.SelectionCleared"), Color.Orange);
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
        }

        // Пока открыт инвентарь, чат или диалог сохранения — мышь принадлежит им
        private static bool InputBlocked()
            => StructureSaveDialog.IsOpen
            || Main.playerInventory
            || Main.drawingPlayerChat
            || Main.editSign
            || Main.editChest
            || Main.LocalPlayer.dead;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Wood, 1)
                .AddTile(TileID.WorkBenches)
                .Register();
        }
    }
}
