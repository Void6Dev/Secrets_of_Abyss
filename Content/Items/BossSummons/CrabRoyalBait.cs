using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.BossSummons
{
    public class CrabRoyalBait : ModItem
    {
        public override void SetStaticDefaults()
        {
            ItemID.Sets.SortingPriorityBossSpawns[Type] = 12;
        }

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 22;
            Item.maxStack = 9999;
            Item.rare = ItemRarityID.Purple;
            Item.value = Item.sellPrice(silver: 20);
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<AbyssScale_small>(), 5)
                .AddIngredient(ItemID.PinkPearl, 1)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
