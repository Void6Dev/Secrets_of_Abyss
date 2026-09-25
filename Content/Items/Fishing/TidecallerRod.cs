using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles.Fishing;

namespace SoA.Content.Items.Fishing
{
    // Удочка прилива: собирается из обломков снастей деревни на клешне Короля-краба.
    // Прибавка к силе ловли внутри биома лежит в TideFishingPlayer.GetFishingLevel
    public class TidecallerRod : ModItem
    {
        private const int PolePower = 30;
        private const float BobberThrowSpeed = 17f;

        // Насколько удочка сильнее именно в Приливе Теней
        public const float BiomeFishingBonus = 8f;

        public override string Texture => "Terraria/Images/Item_" + ItemID.FisherofSouls;

        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 28;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.useAnimation = 12;
            Item.useTime = 12;
            Item.autoReuse = false;
            Item.noMelee = true;
            Item.fishingPole = PolePower;
            Item.shootSpeed = BobberThrowSpeed;
            Item.shoot = ModContent.ProjectileType<TideBobber>();
            Item.rare = ItemRarityID.Green;
            Item.value = Item.sellPrice(gold: 3);
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<SunkenTackle>(), 5)
                .AddIngredient(ModContent.ItemType<RoyalClaw>(), 2)
                .AddIngredient(ModContent.ItemType<DarkLumen>(), 8)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
