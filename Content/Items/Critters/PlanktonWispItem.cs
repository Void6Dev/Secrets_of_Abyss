using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.NPCs.Critters;

namespace SoA.Content.Items.Critters
{
    // Пойманный планктонный огонёк: выпускается обратно или идёт как наживка
    public class PlanktonWispItem : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 14;
            Item.height = 14;
            Item.maxStack = 9999;
            Item.value = 100;
            Item.rare = ItemRarityID.Blue;
            Item.bait = 17;

            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 15;
            Item.useAnimation = 15;
            Item.autoReuse = false;
            Item.consumable = true;
            Item.noUseGraphic = true;
            Item.makeNPC = ModContent.NPCType<PlanktonWisp>();
        }

        public override void PostUpdate()
        {
            Lighting.AddLight(Item.Center, 0.1f, 0.4f, 0.45f);
        }
    }
}
