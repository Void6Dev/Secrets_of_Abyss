using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Content.Worldgen;

namespace SoA.Content.Items.Fishing
{
    // Квестовая рыба рыбака: фонарник из тёмной воды Прилива Теней
    public class LanternfishQuestFish : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 26;
            Item.height = 18;
            Item.DefaultToQuestFish();
        }

        public override bool IsQuestFish() => true;

        // Квест выдаётся только если Прилив Теней есть в мире
        public override bool IsAnglerQuestAvailable() => TideOfShadowsWorldData.OceanSide != 0;

        public override void AnglerQuestChat(ref string description, ref string catchLocation)
        {
            description = Language.GetTextValue("Mods.SoA.Items.LanternfinQuestFish.QuestDescription");
            catchLocation = Language.GetTextValue("Mods.SoA.Items.LanternfinQuestFish.QuestCatchLocation");
        }

        public override void PostUpdate()
        {
            Lighting.AddLight(Item.Center, 0f, 0.5f, 1f);
        }
    }
}
