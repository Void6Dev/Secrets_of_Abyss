using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using SoA.Content.Items.Placebles;
using Microsoft.Xna.Framework;

namespace SoA.Content.Items.Accessories
{
    public class HaloOfBreathing : ModItem
    {
        // Спрайт лежит рядом с кодом в папке контента, а не по пути пространства имён
        public override string Texture => "SoA/Content/Items/Accessories/HaloOfBreathing/HaloOfBreathing";

        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 28;
            Item.accessory = true;
            Item.value = Item.sellPrice(silver: 90);
            Item.rare = ItemRarityID.Blue;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            if (IsInDeepWater(player))
            {
                player.gills = true;
                player.accDivingHelm = true;
                player.accFlipper = true; // свободное плавание, пока нимб активен
                var modPlayer = player.GetModPlayer<HOBusage>();
                modPlayer.hasHaloOfBreathing = true;
                modPlayer.showHaloVisual = !hideVisual;
            }
        }

        private static bool IsInDeepWater(Player player)
        {
            int waterTiles = 0;
            int checkRadius = 5; 

            
            Point tilePosition = player.Center.ToTileCoordinates();

            for (int x = -checkRadius; x <= checkRadius; x++)
            {
                for (int y = -checkRadius; y <= checkRadius; y++)
                {
                    int checkX = tilePosition.X + x;
                    int checkY = tilePosition.Y + y;

                    if (checkX >= 0 && checkX < Main.maxTilesX && checkY >= 0 && checkY < Main.maxTilesY)
                    {
                        Tile tile = Main.tile[checkX, checkY];
                        if (tile != null && tile.LiquidAmount > 128 && tile.LiquidType == LiquidID.Water) 
                        {
                            waterTiles++;
                        }
                    }
                }
            }
            return waterTiles >= 40; 
        }

        public override void AddRecipes() 
        {
			Recipe recipe = CreateRecipe();
			recipe.AddIngredient(ItemID.Seashell, 1);
			recipe.AddIngredient(ModContent.ItemType<Tidesand>(), 30);
			recipe.AddTile(TileID.WaterFountain);
			recipe.Register();
		}
    }
}
