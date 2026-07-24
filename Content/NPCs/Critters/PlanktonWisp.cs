using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Critters;
using SoA.Content.Worldgen;

namespace SoA.Content.NPCs.Critters
{
    internal class PlanktonWisp : ModNPC
    {
        private const int FrameCount = 3;
        private const double FlickerFrameSpeed = 8.0;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
            NPCID.Sets.CountsAsCritter[Type] = true;
        }

        public override void SetDefaults()
        {
            NPC.width = 12;
            NPC.height = 12;
            NPC.damage = 0;
            NPC.defense = 0;
            NPC.lifeMax = 5;
            NPC.HitSound = SoundID.NPCHit25;
            NPC.DeathSound = SoundID.NPCDeath25;
            NPC.value = 0f;
            NPC.knockBackResist = 1f;
            NPC.aiStyle = NPCAIStyleID.Firefly;
            AIType = NPCID.Firefly;
            NPC.noGravity = true;
            NPC.friendly = true;
            NPC.npcSlots = 0.4f;
            NPC.catchItem = ModContent.ItemType<PlanktonWispItem>();

            SpawnModBiomes = [ModContent.GetInstance<TideOfShadowsBiome>().Type];
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.PlanktonWisp.Bestiary")
            ]);
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            if (!spawnInfo.Player.InModBiome<TideOfShadowsBiome>() || spawnInfo.Water)
                return 0f;
            return 0.35f;
        }

        public override void AI()
        {
            Lighting.AddLight(NPC.Center, 0.12f, 0.5f, 0.55f);
        }

        public override void FindFrame(int frameHeight)
        {
            NPC.frameCounter += 1.0;
            if (NPC.frameCounter >= FlickerFrameSpeed)
            {
                NPC.frameCounter = 0.0;
                NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
            }
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server || NPC.life > 0)
                return;

            for (int i = 0; i < 8; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.BlueTorch);
                d.noGravity = true;
                d.velocity *= 1.2f;
            }
        }
    }
}
