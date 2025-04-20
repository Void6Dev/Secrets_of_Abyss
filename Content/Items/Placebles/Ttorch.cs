using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Other;
namespace SoA.Content.Items.Placebles
{
    public class Ttorch : ModItem
    {

        public override void SetDefaults()
        {
            // Устанавливаем параметры факела
            Item.width = 10;
            Item.height = 12;
            Item.maxStack = 9999;
            Item.value = 50;
            Item.rare = ItemRarityID.White;

            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 10;
            Item.useAnimation = 15;
            Item.autoReuse = true;

            Item.consumable = true;
            Item.createTile = ModContent.TileType<Ttorch_tile>();
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe(3);
            recipe.AddIngredient(ItemID.Torch);
            recipe.AddIngredient(ItemID.Coral, 1); // Пример: используем кораллы
            recipe.AddTile(TileID.WorkBenches);
            recipe.Register();
        }
        
    }
}