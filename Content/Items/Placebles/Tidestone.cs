using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using SoA.Content.Tiles.Nature;

namespace SoA.Content.Items.Placebles
{
    public class Tidestone : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 16;
            Item.maxStack = 9999;
            Item.value = 0;
            Item.rare = ItemRarityID.White;

            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 10;
            Item.useAnimation = 15;
            Item.autoReuse = true;

            Item.consumable = true;
            Item.createTile = ModContent.TileType<Tidestone_tile>();
        }
    }
}
