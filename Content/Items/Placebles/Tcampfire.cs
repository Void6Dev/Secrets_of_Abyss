using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Placebles
{
    // Приливный костёр: даёт бафф уютного костра и горит даже под водой
    public class Tcampfire : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 16;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(copper: 60);
            Item.rare = ItemRarityID.White;

            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 10;
            Item.useAnimation = 15;
            Item.autoReuse = true;
            Item.useTurn = true;

            Item.consumable = true;
            Item.createTile = ModContent.TileType<Tcampfire_tile>();
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Wood, 10)
                .AddIngredient(ModContent.ItemType<Ttorch>(), 3)
                .Register();
        }
    }
}
