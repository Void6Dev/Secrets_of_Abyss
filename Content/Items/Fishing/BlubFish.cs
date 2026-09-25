using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Fishing
{
    public class BlubFish : ModItem
    {
        private const int WellFedTicks = 8 * 60 * 60;

        public override void SetStaticDefaults()
        {
            ItemID.Sets.IsFood[Type] = true;
        }

        public override void SetDefaults()
        {
            Item.DefaultToFood(24, 16, BuffID.WellFed, WellFedTicks);
            Item.value = Item.sellPrice(silver: 3);
            Item.rare = ItemRarityID.Blue;
        }
    }
}
