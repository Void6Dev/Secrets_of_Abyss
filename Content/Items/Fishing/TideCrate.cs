using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Critters;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Fishing
{
    // Ящик прилива: ловится в зонах 1-2 Прилива Теней.
    // Спрайт — ванильный океанский ящик (placeholder до собственного арта)
    public class TideCrate : ModItem
    {

        public override void SetStaticDefaults()
        {
            // Наборы проверяются только для ванильных id, но нужны для кроссмодной совместимости
            ItemID.Sets.IsFishingCrate[Type] = true;
            Item.ResearchUnlockCount = 10;
        }

        public override void SetDefaults()
        {
            Item.DefaultToPlaceableTile(ModContent.TileType<TideCrate_tile>(), TideCrate_tile.TidePlaceStyle);
            // Хитбокс меньше спрайта: так ящик аккуратнее выглядит на поплавке
            Item.width = 12;
            Item.height = 12;
            Item.rare = ItemRarityID.Green;
            Item.value = Item.sellPrice(silver: 60);
        }

        public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup)
        {
            itemGroup = ContentSamples.CreativeHelper.ItemGroup.Crates;
        }

        public override bool CanRightClick() => true;

        public override void ModifyItemLoot(ItemLoot itemLoot)
        {
            // Снасти — главная причина ловить ящики: из них собирается удочка прилива.
            // Ботинок прилива тут намеренно нет, они награда за сундук капитана
            // на затонувшем корабле, и ящик не должен её обесценивать
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<SunkenTackle>(), 1, 2, 6));

            // Морская утварь рыбацкой деревни
            IItemDropRule[] seafarerTools = [
                ItemDropRule.Common(ItemID.Sextant),
                ItemDropRule.Common(ItemID.FishermansGuide),
                ItemDropRule.Common(ItemID.WeatherRadio),
            ];
            itemLoot.Add(new OneFromRulesRule(6, seafarerTools));

            itemLoot.Add(ItemDropRule.Common(ItemID.GoldCoin, 1, 1, 4));

            IItemDropRule[] blocks = [
                ItemDropRule.Common(ModContent.ItemType<Tidesand>(), 1, 25, 60),
                ItemDropRule.Common(ModContent.ItemType<Tidestone>(), 1, 25, 60),
            ];
            itemLoot.Add(new OneFromRulesRule(1, blocks));

            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<DarkLumen>(), 3, 2, 6));

            IItemDropRule[] bait = [
                ItemDropRule.Common(ModContent.ItemType<PlanktonWispItem>(), 1, 2, 5),
                ItemDropRule.Common(ItemID.ApprenticeBait, 1, 3, 6),
                ItemDropRule.Common(ItemID.JourneymanBait, 1, 2, 4),
            ];
            itemLoot.Add(new OneFromRulesRule(2, bait));

            IItemDropRule[] potions = [
                ItemDropRule.Common(ItemID.GillsPotion, 1, 2, 5),
                ItemDropRule.Common(ItemID.FishingPotion, 1, 2, 5),
                ItemDropRule.Common(ItemID.SonarPotion, 1, 2, 5),
                ItemDropRule.Common(ItemID.CratePotion, 1, 2, 5),
                ItemDropRule.Common(ItemID.ShinePotion, 1, 2, 5),
            ];
            itemLoot.Add(new OneFromRulesRule(2, potions));

            IItemDropRule[] food = [
                ItemDropRule.Common(ModContent.ItemType<BlubFish>(), 1, 1, 3),
                ItemDropRule.Common(ModContent.ItemType<GlowGuppy>(), 1, 1, 3),
            ];
            itemLoot.Add(new OneFromRulesRule(3, food));
        }
    }
}
