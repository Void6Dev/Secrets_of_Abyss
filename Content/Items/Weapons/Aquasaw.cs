using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Weapons
{
    // Аквапила: бензопила на водяном приводе, прибрежный инструмент до Стены плоти.
    // Улучшение Пилозуба (60% силы топора → 90%). Своя фишка — в воде пилит быстрее,
    // а по мокрым врагам бьёт сильнее (множитель в AquasawProjectile)
    public class Aquasaw : ModItem
    {
        private const int AxePower = 18;               // ×5 = 90% в подсказке
        private const float UnderwaterSpeedBonus = 1.35f;

        public override void SetStaticDefaults()
        {
            ItemID.Sets.IsChainsaw[Type] = true;
        }

        public override void SetDefaults()
        {
            Item.damage = 20;
            Item.DamageType = DamageClass.MeleeNoSpeed; // скорость ближнего боя пилу не разгоняет — как у ванильных
            Item.width = 30;
            Item.height = 24;
            // Время бура/пилы задаётся уже умноженным на 0.6 от времени обычного топора (так у ванили)
            Item.useTime = 4;
            Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 4f;
            Item.value = Item.sellPrice(gold: 1);
            Item.rare = ItemRarityID.Green;
            Item.UseSound = SoundID.Item23;
            Item.shoot = ModContent.ProjectileType<AquasawProjectile>();
            Item.shootSpeed = 32f;     // как далеко от игрока держится пила
            Item.noMelee = true;       // бьёт снаряд пилы, а не сам предмет
            Item.noUseGraphic = true;  // в руках рисуется снаряд
            Item.channel = true;
            Item.tileBoost = 1;
            Item.axe = AxePower;
        }

        // В воде водяной привод работает на полную: пилит и режет быстрее
        public override float UseSpeedMultiplier(Player player) => player.wet ? UnderwaterSpeedBonus : 1f;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.SawtoothShark)
                .AddIngredient(ItemID.SharkFin, 2)
                .AddIngredient(ModContent.ItemType<Tidesand>(), 25)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
