using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using SoA.Content.Items.Accessories.UseAccessories;

namespace SoA.Content.Items.Accessories
{
    public class CurseOfMe : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.accessory = true;
            Item.value = Item.sellPrice(silver: 30);
            Item.rare = ItemRarityID.Blue;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.GetModPlayer<COMusage>().CurseOfMe = true;
        }



		public override void AddRecipes() 
        {
			Recipe recipe = CreateRecipe();
			recipe.AddIngredient(ItemID.LifeCrystal, 2);
			recipe.AddIngredient(ItemID.ManaCrystal, 2);
			recipe.AddTile(TileID.Anvils);
			recipe.Register();
		}
	}
}