using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Tools
{
    // Зубодробилка: кирка-молот из клыков ихтиофагов, прибрежный инструмент до Короля-краба.
    // По силе — на уровне золотой кирки и молота; своя фишка — под водой копает быстрее,
    // ведь добывать приходится на дне Прилива Теней
    public class Toothbreaker : ModItem
    {
        private const int PickPower = 55;          // достаточно для обсидиана и метеорита
        private const int HammerPower = 55;
        private const float UnderwaterSpeedBonus = 1.3f;

        public override void SetDefaults()
        {
            Item.damage = 12;
            Item.DamageType = DamageClass.Melee;
            Item.width = 44;
            Item.height = 43;
            Item.useTime = 16;
            Item.useAnimation = 22;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 5f;
            Item.value = Item.sellPrice(silver: 60);
            Item.rare = ItemRarityID.Blue;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.useTurn = true;
            Item.pick = PickPower;
            Item.hammer = HammerPower;
        }

        public override float UseSpeedMultiplier(Player player) => player.wet ? UnderwaterSpeedBonus : 1f;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<Ichthyofang>(), 10)
                .AddIngredient(ModContent.ItemType<Tidestone>(), 30)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
