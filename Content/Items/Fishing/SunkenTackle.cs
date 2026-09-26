using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Fishing
{
    // Затонувшая снасть: «мусорный» улов Прибрежья — обрывки сетей рыбацкой деревни.
    // В отличие от ванильной жести, идёт в дело: из неё собирается удочка биома.
    // Спрайт — клубок: обрывок сети, ржавый крючок, поплавок и прилипшая водоросль
    public class SunkenTackle : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 24;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(copper: 50);
            Item.rare = ItemRarityID.White;
            Item.material = true;
        }
    }
}
