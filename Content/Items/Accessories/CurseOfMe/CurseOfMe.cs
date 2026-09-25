using Terraria;
using Terraria.ModLoader;
using Terraria.ID;

namespace SoA.Content.Items.Accessories.CurseOfMe
{
    public class CurseOfMe : ModItem
    {

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.accessory = true;
            Item.value = Item.sellPrice(silver: 30);
            Item.rare = ItemRarityID.Blue;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.GetModPlayer<COMusage>().CurseOfMe = true;
        }
	}
}
