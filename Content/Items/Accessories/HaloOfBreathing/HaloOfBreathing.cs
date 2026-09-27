using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Accessories
{
    // Нимб дыхания: в большом глубоком водоёме воздух тратится втрое медленнее,
    // вокруг носителя — мягкий свет. Плавать не учит. Логика — в HOBusage
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
            var modPlayer = player.GetModPlayer<HOBusage>();
            modPlayer.hasHaloOfBreathing = true;
            modPlayer.showHaloVisual = !hideVisual;
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
