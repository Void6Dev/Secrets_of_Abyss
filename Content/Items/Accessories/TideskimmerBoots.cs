using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Players;

namespace SoA.Content.Items.Accessories
{
    public class TideskimmerBoots : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 24;
            Item.accessory = true;
            Item.value = Item.sellPrice(gold: 4, silver: 20);
            Item.rare = ItemRarityID.Purple;
        }

        // Вся физика нырка живёт в TideskimmerPlayer: там она видит хуки движения
        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.GetModPlayer<TideskimmerPlayer>().hasTideskimmerBoots = true;
        }
    }
}
