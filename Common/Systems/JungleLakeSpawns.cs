using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Systems.JungleLake;

namespace SoA.Common.Systems
{
    // Живность утонувшей деревни. Жабы и стрекозы — это всё, что от неё осталось,
    // поэтому у озера они не редкость, а фон: вес заметно выше ванильного.
    // Правится спавн-пул, а не спавн-рейт: рейт разогнал бы и врагов заодно
    public class JungleLakeSpawns : GlobalNPC
    {
        private const float DragonflyWeight = 0.4f;
        private const float FrogWeight = 0.32f;
        private const float FireflyWeight = 0.3f;
        private const float GoldenVariantWeight = 0.01f;

        // Цвет стрекозы выбирается на каждый спавн: иначе у озера живёт одна расцветка
        private static readonly int[] Dragonflies =
        [
            NPCID.BlackDragonfly, NPCID.BlueDragonfly, NPCID.GreenDragonfly,
            NPCID.OrangeDragonfly, NPCID.RedDragonfly, NPCID.YellowDragonfly
        ];

        public override void EditSpawnPool(IDictionary<int, float> pool, NPCSpawnInfo spawnInfo)
        {
            if (!JungleLakeWorldData.IsNear(spawnInfo.SpawnTileX, spawnInfo.SpawnTileY))
                return;

            pool[Dragonflies[Main.rand.Next(Dragonflies.Length)]] = DragonflyWeight;
            pool[NPCID.Frog] = FrogWeight;

            // Ночью над камышом висят светляки, днём их нет
            if (!Main.dayTime)
                pool[NPCID.Firefly] = FireflyWeight;

            pool[NPCID.GoldFrog] = GoldenVariantWeight;
            pool[NPCID.GoldDragonfly] = GoldenVariantWeight;
        }
    }
}
