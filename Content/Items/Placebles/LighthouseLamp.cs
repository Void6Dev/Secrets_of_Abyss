using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Placebles
{
    // Лампа маяка: ставится в фонарную комнату, луч поворачивают маячной цепью.
    // Текстура — ванильный хрустальный шар (placeholder до собственного арта)
    public class LighthouseLamp : ModItem
    {
        public override string Texture => "Terraria/Images/Item_" + ItemID.CrystalBall;

        public override void SetDefaults()
        {
            Item.DefaultToPlaceableTile(ModContent.TileType<LighthouseLamp_tile>());
            Item.width = 28;
            Item.height = 28;
            Item.maxStack = 99;
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
