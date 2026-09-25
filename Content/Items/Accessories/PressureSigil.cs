using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Players;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Fishing;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Accessories
{
    // Печати давления I-IV: снаряжение под глубину, а не ключ от неё. Проход в зону
    // открывает печать прилива (убитый босс), а вот выжить в ней даёт эта линейка:
    // каждая ступень гасит один уровень давления.
    //
    // Одна вещь на все четыре ступени: следующая крафтится из предыдущей, поэтому
    // в сундуке не копится четыре похожих кругляша.
    public abstract class PressureSigil : ModItem
    {
        // 1..4 — сколько уровней давления гасит
        public abstract int Tier { get; }

        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.accessory = true;
            Item.value = Item.sellPrice(gold: 2 * Tier);
            Item.rare = Tier switch
            {
                1 => ItemRarityID.Orange,
                2 => ItemRarityID.LightRed,
                3 => ItemRarityID.Pink,
                _ => ItemRarityID.Yellow
            };
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            TidePressurePlayer pressure = player.GetModPlayer<TidePressurePlayer>();
            // Две печати разом не складываются: считается лучшая надетая
            if (Tier > pressure.SigilTier)
                pressure.SigilTier = Tier;
        }
    }

    public class PressureSigil1 : PressureSigil
    {
        public override int Tier => 1;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient<SunkenTackle>(12)
                .AddIngredient<RoyalClaw>(4)
                .AddIngredient<Tidestone>(20)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }

    public class PressureSigil2 : PressureSigil
    {
        public override int Tier => 2;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient<PressureSigil1>()
                .AddIngredient<DarkLumen>(10)
                .AddIngredient<Ichthyofang>(8)
                .AddTile(TileID.MythrilAnvil)
                .Register();
        }
    }

    public class PressureSigil3 : PressureSigil
    {
        public override int Tier => 3;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient<PressureSigil2>()
                .AddIngredient<DarkLumen>(18)
                .AddIngredient<Ichthyofang>(16)
                .AddTile(TileID.MythrilAnvil)
                .Register();
        }
    }

    public class PressureSigil4 : PressureSigil
    {
        public override int Tier => 4;

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient<PressureSigil3>()
                .AddIngredient<DarkLumen>(30)
                .AddIngredient<Ichthyofang>(25)
                .AddIngredient(ItemID.LunarBar, 8)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
