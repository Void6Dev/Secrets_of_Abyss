using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Weapons
{
    // Апгрейд ванильного трезубца клешнёй Короля-краба.
    // Спрайт — ванильный Trident (placeholder до собственного арта).
    public class RoyalTrident : ModItem
    {
        public override string Texture => "Terraria/Images/Item_" + ItemID.Trident;

        public override void SetDefaults()
        {
            Item.damage = 24;
            Item.DamageType = DamageClass.Melee;
            Item.width = 32;
            Item.height = 32;
            Item.useTime = 26;
            Item.useAnimation = 26;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 5.5f;
            Item.value = Item.sellPrice(gold: 1, silver: 50);
            Item.rare = ItemRarityID.Green;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.shoot = ModContent.ProjectileType<RoyalTridentProjectile>();
            Item.shootSpeed = 3.5f;
        }

        // Одновременно существует только один выпад
        public override bool CanUseItem(Player player)
        {
            return player.ownedProjectileCounts[Item.shoot] < 1;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Trident)
                .AddIngredient(ModContent.ItemType<RoyalClaw>(), 3)
                .AddIngredient(ModContent.ItemType<Tidesand>(), 15)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
