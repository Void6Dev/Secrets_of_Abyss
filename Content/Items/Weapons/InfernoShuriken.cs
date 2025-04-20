using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;
using SoA.Content.Items.Materials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Items.Weapons
{
    public class InfernoShuriken : ModItem
    {
        public override void SetDefaults() {
            Item.damage = 70;
            Item.DamageType = DamageClass.Ranged;
            Item.width = 30;
            Item.height = 30;
            Item.useTime = 16;
            Item.useAnimation = 16;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.noMelee = true;
            Item.knockBack = 3;
            Item.value = Item.sellPrice(gold: 6);
            Item.rare = ItemRarityID.Green;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.shoot = ModContent.ProjectileType<InfernoShurikenProjectile>();
            Item.shootSpeed = 16f;
            Item.consumable = false;
            Item.maxStack = 1;
        }

        public override void AddRecipes() {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<LavaShard>(), 10);
            recipe.AddIngredient(ItemID.HellstoneBar, 15);
            recipe.AddIngredient(ItemID.Bone, 80);
            recipe.AddTile(TileID.Anvils);
            recipe.Register();
        }
        
        public override void HoldItem(Player player) {
            if (player.itemAnimation > 0) {
                // Optionally, you can customize additional visuals or effects here
            }
        }
    }
}