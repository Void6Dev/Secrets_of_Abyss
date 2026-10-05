using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Furniture;
using SoA.Content.Tiles.Lighthouse;
using SoA.Content.Tiles.Shrines;

namespace SoA.Content.Items.Placeables
{
    // Лампа маяка: ставится в фонарную комнату, луч поворачивают маячной цепью.
    public class LighthouseLamp : ModItem
    {
        public override void SetDefaults()
        {
            Item.DefaultToPlaceableTile(ModContent.TileType<LighthouseLamp_tile>());
            Item.width = 32;
            Item.height = 32;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(silver: 50);
            Item.rare = ItemRarityID.Blue;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Glass, 10)
                .AddRecipeGroup(RecipeGroupID.IronBar, 6)
                .AddIngredient(ItemID.Torch, 5)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
