using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Placebles
{
    // Королевский приливный алтарь: генерируется в затонувшем галеоне,
    // можно выкопать и перенести или скрафтить заново
    public class CrabRoyalAltar : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 26;
            Item.height = 30;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(gold: 1);
            Item.rare = ItemRarityID.Blue;

            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 10;
            Item.useAnimation = 15;
            Item.autoReuse = true;
            Item.useTurn = true;

            Item.consumable = true;
            Item.createTile = ModContent.TileType<CrabRoyalAltar_tile>();
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<Tidesand>(), 25)
                .AddIngredient(ModContent.ItemType<Ichthyofang>(), 5)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
