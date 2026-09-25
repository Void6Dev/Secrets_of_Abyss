using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using SoA.Content.Items.Fishing;
using SoA.Content.Items.Materials;
using SoA.Content.Worldgen;

namespace SoA.Common.Players
{
    // Клёв в Приливе Теней. Улов решает зона биома, а не только сила ловли:
    // зоны 1-2 — открытая вода Прибрежья и средней глубины, зоны 3-5 — затопленные
    // пещеры за печатями, туда попадают только с прогрессом и снаряжением
    public class TideFishingPlayer : ModPlayer
    {
        // Последняя зона, которая считается «верхней» водой
        // Прибрежье — это только Затопленный порт (зона I). Прежние зоны 1-2
        // слиты в одну, поэтому граница клёва поехала с 2 на 1
        private const int CoastalZoneMax = 1;

        private const int CommonFishChanceDenominator = 3;
        private const int JunkChanceDenominator = 4;
        private const int LumenStackMin = 2;
        private const int LumenStackMax = 5;

        public override void GetFishingLevel(Item fishingRod, Item bait, ref float fishingLevel)
        {
            if (fishingRod.type == ModContent.ItemType<TidecallerRod>()
                && Player.InModBiome<TideOfShadowsBiome>())
            {
                fishingLevel += TidecallerRod.BiomeFishingBonus;
            }
        }

        public override void CatchFish(FishingAttempt attempt, ref int itemDrop, ref int npcSpawn,
            ref AdvancedPopupRequest sonar, ref Vector2 sonarPosition)
        {
            if (attempt.inLava || attempt.inHoney)
                return;

            int zone = TideZoneAt(attempt.X, attempt.Y);
            if (zone == 0)
                return;

            if (attempt.questFish == ModContent.ItemType<LanternfishQuestFish>() && attempt.uncommon)
            {
                itemDrop = ModContent.ItemType<LanternfishQuestFish>();
                return;
            }

            if (attempt.crate)
            {
                itemDrop = CrateForZone(zone);
                return;
            }

            int catchType = zone <= CoastalZoneMax ? CoastalCatch(attempt) : AbyssCatch(attempt);
            if (catchType > 0)
                itemDrop = catchType;
        }

        // Тёмный люминофор капает пучком, одна капля за подсечку выглядела бы издевательством
        public override void ModifyCaughtFish(Item item)
        {
            if (item.type == ModContent.ItemType<DarkLumen>())
                item.stack = Main.rand.Next(LumenStackMin, LumenStackMax);
        }

        // Зона по обмерам генерации; в водоёмах, построенных руками, границ нет,
        // поэтому там биом определяется по тайлам и считается Прибрежьем
        private int TideZoneAt(int tileX, int tileY)
        {
            int zone = TideOfShadowsWorldData.ZoneAt(tileX, tileY);
            if (zone != 0)
                return zone;

            return Player.InModBiome<TideOfShadowsBiome>() ? 1 : 0;
        }

        // Легендарный улов не перехватывается намеренно: ванильный океан отдаёт там
        // Reaver Shark и прочее, и для морского биома это уместнее своей мелочи
        private static int CoastalCatch(FishingAttempt attempt)
        {
            if (attempt.legendary)
                return 0;

            if (attempt.veryrare)
                return ModContent.ItemType<DarkLumen>();

            if (attempt.rare)
                return ModContent.ItemType<GlowGuppy>();

            if (attempt.uncommon)
            {
                return Main.rand.NextBool()
                    ? ModContent.ItemType<GlowGuppy>()
                    : ModContent.ItemType<SunkenTackle>();
            }

            if (!attempt.common)
                return 0;

            if (Main.rand.NextBool(CommonFishChanceDenominator))
                return ModContent.ItemType<BlubFish>();

            return Main.rand.NextBool(JunkChanceDenominator) ? ModContent.ItemType<SunkenTackle>() : 0;
        }

        private static int AbyssCatch(FishingAttempt attempt)
        {
            if (attempt.legendary || attempt.veryrare)
                return ModContent.ItemType<Ichthyofang>();

            if (attempt.rare)
                return ModContent.ItemType<AbyssGrouper>();

            if (attempt.uncommon)
            {
                return Main.rand.NextBool()
                    ? ModContent.ItemType<AbyssGrouper>()
                    : ModContent.ItemType<DarkLumen>();
            }

            if (!attempt.common)
                return 0;

            return Main.rand.NextBool(CommonFishChanceDenominator)
                ? ModContent.ItemType<BlubFish>()
                : ModContent.ItemType<GlowGuppy>();
        }

        // Ящик бездны лежит за печатями и требует хардмода: до Стены Плоти в глубину не пройти
        private static int CrateForZone(int zone)
        {
            if (zone > CoastalZoneMax && Main.hardMode)
                return ModContent.ItemType<AbyssCrate>();

            return ModContent.ItemType<TideCrate>();
        }
    }
}
