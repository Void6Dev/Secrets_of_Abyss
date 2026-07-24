using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Weapons
{
    public class Aquasaw : ModItem
    {
        public override void SetStaticDefaults()
        {
            ItemID.Sets.IsChainsaw[Type] = true;
        }
        public override void SetDefaults() {
			Item.damage = 32;
			Item.DamageType = DamageClass.MeleeNoSpeed; // ignores melee speed bonuses. There's no need for drill animations to play faster, nor drills to dig faster with melee speed.
			Item.width = 30;
			Item.height = 24;
			// IsDrill/IsChainsaw effects must be applied manually, so 60% or 0.6 times the time of the corresponding pickaxe. In this case, 60% of 7 is 4 and 60% of 25 is 15.
			// If you decide to copy values from vanilla drills or chainsaws, you should multiply each one by 0.6 to get the expected behavior.
			Item.useTime = 4;
			Item.useAnimation = 15;
			Item.useStyle = ItemUseStyleID.Shoot;
			Item.knockBack = 5f;
			Item.value = Item.buyPrice(gold: 12, silver: 60);
			Item.rare = ItemRarityID.LightRed;
			Item.UseSound = SoundID.Item23;
			Item.shoot = ModContent.ProjectileType<AquasawProjectile>(); // Create the drill projectile
			Item.shootSpeed = 32f; // Adjusts how far away from the player to hold the projectile
			Item.noMelee = true; // Turns off damage from the item itself, as we have a projectile
			Item.noUseGraphic = true; // Stops the item from drawing in your hands, for the aforementioned reason
			Item.channel = true; // Important as the projectile checks if the player channels

			// tileBoost changes the range of tiles that the item can reach.
			// To match Titanium Drill, we should set this to -1, but we'll set it to 10 blocks of extra range for the sake of an example.
			Item.tileBoost = 1;

			Item.axe = 25; // How strong the drill is, see https://terraria.wiki.gg/wiki/Pickaxe_power for a list of common values
		}

        public override void AddRecipes(){
            CreateRecipe()
                .AddIngredient(ItemID.SawtoothShark)
                .AddIngredient(ItemID.SharkFin, 2)
                .AddIngredient(ModContent.ItemType<Tidesand>(), 25)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}