using System;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Systems.JungleLake;
using SoA.Content.Items.Fishing;

namespace SoA.Content.NPCs.Enemies
{
    // Утонувший житель: ночью деревня возвращается к своим сваям. Ходьба на
    // ванильном ИИ бойца, всё своё — только вода, которая с них течёт.
    // Спрайт — ванильный зомби (placeholder до собственного арта)
    internal class DrownedVillager : ModNPC
    {
        private const int FrameCount = 6;
        private const double WalkFrameSpeed = 7.0;
        private const int DripChanceDenominator = 14;

        public override string Texture => "Terraria/Images/NPC_" + NPCID.Zombie;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            NPC.width = 18;
            NPC.height = 40;
            NPC.damage = 22;
            NPC.defense = 8;
            NPC.lifeMax = 80;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath2;
            NPC.value = 120f;
            NPC.knockBackResist = 0.35f;
            NPC.aiStyle = NPCAIStyleID.Fighter;
            AIType = NPCID.Zombie;
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Jungle,
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Times.NightTime,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.DrownedVillager.Bestiary")
            ]);
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            // Только ночью и только у своей деревни
            if (Main.dayTime || !JungleLakeWorldData.IsNear(spawnInfo.SpawnTileX, spawnInfo.SpawnTileY, 30))
                return 0f;

            return 0.5f;
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<SunkenTackle>(), 2, 1, 2));
            // Справочник рыбака — вещь, которую житель носил при себе
            npcLoot.Add(ItemDropRule.Common(ItemID.FishermansGuide, 60));
        }

        // С жителя постоянно течёт вода: без этого он читается просто зомби
        public override void AI()
        {
            if (Main.netMode == NetmodeID.Server || !Main.rand.NextBool(DripChanceDenominator))
                return;

            Dust drip = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
            drip.velocity = new Microsoft.Xna.Framework.Vector2(0f, Main.rand.NextFloat(0.4f, 1.2f));
            drip.scale = Main.rand.NextFloat(0.7f, 1.1f);
        }

        public override void FindFrame(int frameHeight)
        {
            if (Math.Abs(NPC.velocity.X) < 0.1f)
            {
                NPC.frame.Y = 0;
                NPC.frameCounter = 0.0;
                return;
            }

            NPC.frameCounter += 1.0 + Math.Abs(NPC.velocity.X) * 0.35;
            if (NPC.frameCounter < WalkFrameSpeed)
                return;

            NPC.frameCounter = 0.0;
            NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            int count = NPC.life <= 0 ? 14 : 4;
            for (int i = 0; i < count; i++)
            {
                Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.Water,
                    hit.HitDirection, -1f, 0, default, 1f);
            }
        }
    }
}
