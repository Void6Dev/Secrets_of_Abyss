using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles.AbyssArrow;
using SoA.Content.Items.Materials;

namespace SoA.Content.Items.Ammo
{
    public class AbyssArrow : ModItem
    {
        private const int ArrowsPerScale = 25;

        public override void SetDefaults()
        {
            Item.damage = 9;
            Item.DamageType = DamageClass.Ranged;
            Item.width = 14;
            Item.height = 32;
            Item.maxStack = 9999;
            Item.consumable = true;
            Item.knockBack = 2.5f;
            Item.value = Item.sellPrice(copper: 8);
            Item.rare = ItemRarityID.Orange;
            Item.shoot = ModContent.ProjectileType<AbyssArrowProjectile>();
            Item.shootSpeed = 4.5f;
            Item.ammo = AmmoID.Arrow;
        }

        public override void AddRecipes()
        {
            CreateRecipe(ArrowsPerScale)
                .AddIngredient(ItemID.WoodenArrow, ArrowsPerScale)
                .AddIngredient(ModContent.ItemType<Ichthyofang>())
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
