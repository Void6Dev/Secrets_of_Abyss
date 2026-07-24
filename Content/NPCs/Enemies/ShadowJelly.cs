using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.Worldgen;

namespace SoA.Content.NPCs.Enemies
{
    // Медуза-тень: светится в тёмной воде, дропает люминофор.
    // Ванильный ИИ медузы (рывки к игроку в воде)
    internal class ShadowJelly : ModNPC
    {
        private const int FrameCount = 4;
        private const double PulseFrameSpeed = 10.0;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            NPC.width = 24;
            NPC.height = 28;
            NPC.damage = 24;
            NPC.defense = 4;
            NPC.lifeMax = 60;
            NPC.HitSound = SoundID.NPCHit25;
            NPC.DeathSound = SoundID.NPCDeath28;
            NPC.value = 55f;
            NPC.knockBackResist = 0.4f;
            NPC.aiStyle = NPCAIStyleID.Jellyfish;
            AIType = NPCID.BlueJellyfish;
            NPC.noGravity = true;

            SpawnModBiomes = [ ModContent.GetInstance<TideOfShadowsBiome>().Type ];
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.ShadowJelly.Bestiary")
            ]);
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            if (!spawnInfo.Player.InModBiome<TideOfShadowsBiome>() || !spawnInfo.Water)
                return 0f;
            return 0.45f;
        }

        public override void AI()
        {
            Lighting.AddLight(NPC.Center, 0.35f, 0.15f, 0.6f);

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(14))
            {
                Dust glow = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.PurpleTorch);
                glow.noGravity = true;
                glow.velocity *= 0.2f;
                glow.scale = 0.8f;
            }
        }

        public override void FindFrame(int frameHeight)
        {
            NPC.frameCounter += 1.0;
            if (NPC.frameCounter >= PulseFrameSpeed)
            {
                NPC.frameCounter = 0.0;
                NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
            }
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<DarkLumen>(), 1, 1, 2));
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server || NPC.life > 0)
                return;

            for (int i = 0; i < 15; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.PurpleTorch);
                d.noGravity = true;
                d.velocity *= 1.5f;
            }
        }
    }
}
