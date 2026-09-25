using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Materials
{
    // Клык глубоководной твари — основной материал приливной ветки.
    // LegacyName — чтобы предметы в старых сохранениях не стали unloaded после
    // переименования класса из AbyssScale_small.
    [LegacyName("AbyssScale_small")]
    public class Ichthyofang : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 32;
            Item.scale = 1;
            Item.value = Item.buyPrice(gold: 1);
            Item.maxStack = 9999;
            Item.rare = ItemRarityID.Orange;
        }
    }
}
