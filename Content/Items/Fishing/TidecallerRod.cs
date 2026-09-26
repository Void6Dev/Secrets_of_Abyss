using Microsoft.Xna.Framework;
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

        // Леска светится тем же лиловым, что и Тёмный люминофор, из которого собрана удочка
        private static readonly Color LineColor = new(170, 110, 255);

        public override void SetDefaults()
        {
            // Спрайт 44x44 по диагонали, кончик в правом верхнем углу — там, где ванильная
            // удочка выпускает леску, поэтому точку выхода лески не переопределяем
            Item.width = 44;
            Item.height = 44;
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

        public override void ModifyFishingLine(Projectile bobber, ref Vector2 lineOriginOffset, ref Color lineColor)
            => lineColor = LineColor;
    }
}
