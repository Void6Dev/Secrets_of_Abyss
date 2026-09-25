using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Fishing
{
    // Затонувшая снасть: «мусорный» улов Прибрежья — обрывки сетей рыбацкой деревни.
    // В отличие от ванильной жести, идёт в дело: из неё собирается удочка биома.
    // Спрайт — ванильная жестянка (placeholder до собственного арта)
    public class SunkenTackle : ModItem
    {
        public override string Texture => "Terraria/Images/Item_" + ItemID.TinCan;

        public override void SetDefaults()
        {
            Item.width = 18;
            Item.height = 18;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(copper: 50);
            Item.rare = ItemRarityID.White;
            Item.material = true;
        }
    }
}
