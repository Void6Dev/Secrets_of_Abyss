using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Ammo;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Fishing
{
    // Ящик бездны: ловится в затопленных пещерах зон 3-5, то есть только за печатями.
    // Спрайт — ванильный океанский ящик хардмода (placeholder до собственного арта)
    public class AbyssCrate : ModItem
    {
        public override string Texture => "Terraria/Images/Item_" + ItemID.OceanCrateHard;

        public override void SetStaticDefaults()
        {
            ItemID.Sets.IsFishingCrate[Type] = true;
            ItemID.Sets.IsFishingCrateHardmode[Type] = true;
            Item.ResearchUnlockCount = 10;
        }

        public override void SetDefaults()
        {
            Item.DefaultToPlaceableTile(ModContent.TileType<TideCrate_tile>(), TideCrate_tile.AbyssPlaceStyle);
            Item.width = 12;
            Item.height = 12;
            Item.rare = ItemRarityID.LightRed;
            Item.value = Item.sellPrice(gold: 2);
        }

        public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup)
        {
            itemGroup = ContentSamples.CreativeHelper.ItemGroup.Crates;
        }

        public override bool CanRightClick() => true;

        public override void ModifyItemLoot(ItemLoot itemLoot)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<Ichthyofang>(), 1, 4, 10));
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<DarkLumen>(), 1, 6, 14));
            itemLoot.Add(ItemDropRule.Common(ItemID.GoldCoin, 1, 5, 12));

            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<AbyssArrow>(), 2, 30, 70));
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<Tidestone>(), 2, 40, 90));

            IItemDropRule[] potions = [
                ItemDropRule.Common(ItemID.LifeforcePotion, 1, 1, 3),
                ItemDropRule.Common(ItemID.EndurancePotion, 1, 1, 3),
                ItemDropRule.Common(ItemID.GillsPotion, 1, 3, 6),
                ItemDropRule.Common(ItemID.FlipperPotion, 1, 3, 6),
                ItemDropRule.Common(ItemID.SonarPotion, 1, 3, 6),
            ];
            itemLoot.Add(new OneFromRulesRule(1, potions));

            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<AbyssGrouper>(), 2, 1, 3));
            itemLoot.Add(ItemDropRule.Common(ItemID.MasterBait, 3, 2, 5));
        }
    }
}
