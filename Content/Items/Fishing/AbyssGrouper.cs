using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Fishing
{
    // Бездонный групер: улов затопленных пещер зон 3-5, сытная еда на долгую вылазку.
    // Спрайт — ванильный эбонкой (placeholder до собственного арта)
    public class AbyssGrouper : ModItem
    {
        private const int WellFedTicks = 12 * 60 * 60;

        public override string Texture => "Terraria/Images/Item_" + ItemID.Ebonkoi;

        public override void SetStaticDefaults()
        {
            ItemID.Sets.IsFood[Type] = true;
        }

        public override void SetDefaults()
        {
            Item.DefaultToFood(26, 20, BuffID.WellFed2, WellFedTicks);
            Item.value = Item.sellPrice(silver: 20);
            Item.rare = ItemRarityID.LightRed;
        }
    }
}
