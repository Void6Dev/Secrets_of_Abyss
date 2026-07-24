using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using SoA.Content.Items.Fishing;
using SoA.Content.Worldgen;

namespace SoA.Common.Players
{
    public class TideFishingPlayer : ModPlayer
    {
        private const int SnapperChanceDenominator = 3;

        public override void CatchFish(FishingAttempt attempt, ref int itemDrop, ref int npcSpawn,
            ref AdvancedPopupRequest sonar, ref Microsoft.Xna.Framework.Vector2 sonarPosition)
        {
            if (attempt.inLava || attempt.inHoney || !Player.InModBiome<TideOfShadowsBiome>())
                return;

            if (attempt.questFish == ModContent.ItemType<LanternfinQuestFish>() && attempt.uncommon)
            {
                itemDrop = ModContent.ItemType<LanternfinQuestFish>();
                return;
            }

            if (attempt.common && !attempt.crate && Main.rand.NextBool(SnapperChanceDenominator))
                itemDrop = ModContent.ItemType<ShadowSnapper>();
        }
    }
}
