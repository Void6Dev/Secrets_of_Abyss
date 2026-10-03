using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Placebles
{
    // Маячная цепь: вешается под лампой маяка, зажатой ПКМ поворачивает луч за мышью.
    // Текстура — ванильная цепь (placeholder до собственного арта)
    public class LighthouseChain : ModItem
    {
        public override string Texture => "Terraria/Images/Item_" + ItemID.Chain;

        public override void SetDefaults()
        {
            Item.DefaultToPlaceableTile(ModContent.TileType<LighthouseChain_tile>());
            Item.width = 12;
            Item.height = 28;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(copper: 40);
        }

        public override void AddRecipes()
        {
            CreateRecipe(5)
                .AddIngredient(ItemID.Chain, 5)
                .AddIngredient(ItemID.Wire, 5)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
