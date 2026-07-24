using Terraria;
using Terraria.ModLoader;
using Terraria.ID;


namespace SoA.Content.Items.Materials
{
    public class AbyssScale_small : ModItem
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