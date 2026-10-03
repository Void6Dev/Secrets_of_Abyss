using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Weapons;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles;

namespace SoA.Content.Items.Weapons
{
    // Зубодробилка: молот из клыков. Весь цикл удара ведёт ToothbreakerClub
    public class Toothbreaker : ClubItem
    {
        public const string TexturePath = "SoA/Content/Items/Weapons/Toothbreaker";

        public override void SetDefaults()
        {
            DefaultToClub(ModContent.ProjectileType<ToothbreakerClub>(), damage: 42, knockback: 7f);
            Item.width = 60;
            Item.height = 56;
            Item.value = Item.sellPrice(silver: 60);
            Item.rare = ItemRarityID.Blue;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<Ichthyofang>(), 10)
                .AddRecipeGroup(SoARecipeGroups.SilverBar, 12)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
