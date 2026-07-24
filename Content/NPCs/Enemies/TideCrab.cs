using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Worldgen;

namespace SoA.Content.NPCs.Enemies
{
    internal class TideCrab : ModNPC
    {
        private const int FrameCount = 4;
        private const double WalkFrameSpeed = 6.0;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            NPC.width = 30;
            NPC.height = 20;
            NPC.damage = 16;
            NPC.defense = 8;
            NPC.lifeMax = 45;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = 40f;
            NPC.knockBackResist = 0.6f;
            NPC.aiStyle = NPCAIStyleID.Fighter;
            AIType = NPCID.Crab;

            SpawnModBiomes = [ModContent.GetInstance<TideOfShadowsBiome>().Type];
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.TideCrab.Bestiary")
            ]);
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            // Только сухой берег Прилива Теней
            if (!spawnInfo.Player.InModBiome<TideOfShadowsBiome>() || spawnInfo.Water)
                return 0f;
            return 0.3f;
        }

        public override void FindFrame(int frameHeight)
        {
            if (NPC.velocity.Y != 0f)
            {
                // В прыжке/падении держим один кадр
                NPC.frame.Y = frameHeight;
                return;
            }

            if (System.Math.Abs(NPC.velocity.X) < 0.1f)
            {
                NPC.frame.Y = 0;
                NPC.frameCounter = 0.0;
                return;
            }

            NPC.frameCounter += 1.0 + System.Math.Abs(NPC.velocity.X) * 0.4;
            if (NPC.frameCounter >= WalkFrameSpeed)
            {
                NPC.frameCounter = 0.0;
                NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
            }
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            int count = NPC.life <= 0 ? 12 : 3;
            for (int i = 0; i < count; i++)
            {
                Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.PurpleTorch,
                    hit.HitDirection, -1f, 0, default, 0.9f);
            }
        }
    }
}
