using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Items.Materials
{
    // Тёмный люминофор: светящаяся слизь медузы-тени, материал для света
    public class DarkLumen : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 16;
            Item.maxStack = 9999;
            Item.value = 150;
            Item.rare = ItemRarityID.Blue;
            Item.material = true;
        }

        public override void PostUpdate()
        {
            Lighting.AddLight(Item.Center, 0.25f, 0.1f, 0.45f);
        }
    }
}
