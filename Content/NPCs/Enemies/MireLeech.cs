using System;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Systems.JungleLake;

namespace SoA.Content.NPCs.Enemies
{
    // Тинная пиявка: единственный, кто выиграл от падения деревни. Живёт в воде
    // озера и не выходит на берег — плавание целиком на ванильном ИИ пираньи.
    // Спрайт — ванильная пиранья (placeholder до собственного арта)
    internal class MireLeech : ModNPC
    {
        private const int FrameCount = 4;
        private const double SwimFrameSpeed = 5.0;

        public override string Texture => "Terraria/Images/NPC_" + NPCID.Piranha;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            NPC.width = 24;
            NPC.height = 16;
            NPC.damage = 18;
            NPC.defense = 4;
            NPC.lifeMax = 45;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = 60f;
            NPC.knockBackResist = 0.5f;
            NPC.aiStyle = NPCAIStyleID.Piranha;
            AIType = NPCID.Piranha;
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Jungle,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.MireLeech.Bestiary")
            ]);
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            // Только в воде самого озера: в джунглевых лужах пиявке взяться неоткуда
            if (!spawnInfo.Water || !JungleLakeWorldData.IsNear(spawnInfo.SpawnTileX, spawnInfo.SpawnTileY, 6))
                return 0f;

            return 0.35f;
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ItemID.Worm, 3, 1, 2));
        }

        public override void FindFrame(int frameHeight)
        {
            NPC.frameCounter += 1.0 + NPC.velocity.Length() * 0.3;
            if (NPC.frameCounter < SwimFrameSpeed)
                return;

            NPC.frameCounter = 0.0;
            NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            int count = NPC.life <= 0 ? 10 : 3;
            for (int i = 0; i < count; i++)
            {
                Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.GreenBlood,
                    hit.HitDirection, -1f, 0, default, 0.9f);
            }
        }
    }
}
