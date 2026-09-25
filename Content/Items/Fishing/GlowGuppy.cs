using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Fishing
{
    // Светлячковая гуппи: мелочь верхних зон, светится в руках и идёт в зелья света.
    // Спрайт — ванильная неоновая тетра (placeholder до собственного арта)
    public class GlowGuppy : ModItem
    {
        private const int ShinePotionsPerFish = 2;

        public override string Texture => "Terraria/Images/Item_" + ItemID.NeonTetra;

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 16;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(silver: 5);
            Item.rare = ItemRarityID.Blue;
            Item.material = true;
        }

        public override void PostUpdate()
        {
            Lighting.AddLight(Item.Center, 0.15f, 0.35f, 0.4f);
        }

        public override void AddRecipes()
        {
            Recipe.Create(ItemID.ShinePotion, ShinePotionsPerFish)
                .AddIngredient(Type)
                .AddIngredient(ItemID.BottledWater, ShinePotionsPerFish)
                .AddTile(TileID.Bottles)
                .Register();
        }
    }
}
