using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ModLoader.Utilities;
using Microsoft.Xna.Framework;
using SoA.Content.Items.Materials;
using SoA.Content.Worldgen;

namespace SoA.Content.NPCs.Enemies
{
    internal class Eel : ModNPC
    {
        private enum EelState
        {
            Swim,
            Windup,
            Dash,
            Recover
        }

        private const float SwimSpeed = 7.5f;
        private const float SwimInertia = 20f;
        private const float DashSpeed = 28f;
        private const float DashRange = 1000f;
        private const int WindupTicks = 40;
        private const int DashTicks = 30;
        private const int RecoverTicks = 20;
        private const int CooldownTicks = 180;
        private const int DashDamageBonus = 20;

        // Состояние и таймер в NPC.ai — ваниль сама синхронизирует их в мультиплеере
        private EelState State
        {
            get => (EelState)NPC.ai[0];
            set => NPC.ai[0] = (float)value;
        }

        private ref float Timer => ref NPC.ai[1];

        private int _baseDamage;

        private const int FrameCount = 8;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
            NPCID.Sets.NPCBestiaryDrawModifiers value = new()
            {
                Velocity = 1f
            };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, value);
        }

        public override void SetDefaults()
        {
            // Хитбокс под горизонтальный спрайт (~273x52), чуть меньше тела
            NPC.width = 200;
            NPC.height = 36;
            NPC.damage = 40;
            NPC.defense = 15;
            NPC.lifeMax = 200;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = 60f;
            NPC.knockBackResist = 0.8f;
            NPC.aiStyle = -1; // Полностью кастомный ИИ: ванильный aiStyle конфликтовал с ним
            NPC.noGravity = true;

            SpawnModBiomes = new[] { ModContent.GetInstance<TideOfShadowsBiome>().Type };
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new IBestiaryInfoElement[]
            {
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.Eel.Bestiary")
            });
        }

        public override void FindFrame(int frameHeight)
        {
            // Волна по телу бежит быстрее, когда угорь плывёт быстрее
            NPC.frameCounter += 1.0 + NPC.velocity.Length() * 0.12;
            if (NPC.frameCounter >= 8.0)
            {
                NPC.frameCounter = 0.0;
                NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
            }
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            return SpawnCondition.Ocean.Chance * 0.3f;
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<AbyssScale_small>(), 4, 1, 3));
        }

        private static readonly string[] GoreNames = ["EelGoreHead", "EelGoreBody", "EelGoreTail"];

        public override void HitEffect(NPC.HitInfo hit)
        {
            // Гор и пыль — клиентский визуал, на сервере не спавним
            if (NPC.life <= 0 && Main.netMode != NetmodeID.Server)
            {
                for (int i = 0; i < GoreNames.Length; i++)
                {
                    // Куски вдоль тела: голова спереди по направлению взгляда, хвост сзади
                    float segmentX = NPC.direction == 1
                        ? NPC.width * (GoreNames.Length - 1 - i) / (float)GoreNames.Length
                        : NPC.width * i / (float)GoreNames.Length;
                    Vector2 segmentPosition = new(NPC.position.X + segmentX, NPC.position.Y);
                    Gore.NewGore(NPC.GetSource_Death(), segmentPosition, NPC.velocity * 0.5f,
                        Mod.Find<ModGore>(GoreNames[i]).Type);
                }

                for (int i = 0; i < 10; i++)
                {
                    Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.Electric,
                        2.5f * Main.rand.NextFloatDirection(), -2.5f, 0, default, 0.7f);
                }
            }
        }

        public override void AI()
        {
            Lighting.AddLight(NPC.Center, 0.3f, 0.6f, 1.2f);

            if (_baseDamage == 0)
                _baseDamage = NPC.damage;

            if (!NPC.wet)
            {
                // Рывок продолжается и вне воды: выпрыгивает по дуге и падает обратно
                if (State == EelState.Dash && Timer > 0f)
                {
                    Timer--;
                    NPC.velocity.Y += 0.25f;
                    if (Math.Abs(NPC.velocity.X) > 0.5f)
                        FaceTowards(NPC.Center + NPC.velocity);

                    if (Main.netMode != NetmodeID.Server)
                    {
                        Dust trail = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Electric);
                        trail.velocity = NPC.velocity * 0.2f;
                        trail.scale = 0.9f;
                        trail.noGravity = true;
                    }

                    if (Timer <= 0f)
                    {
                        State = EelState.Recover;
                        Timer = RecoverTicks;
                    }
                    return;
                }

                // Иначе бьётся на суше, как рыба: подпрыгивает в случайные стороны
                State = EelState.Swim;
                NPC.damage = _baseDamage;
                NPC.velocity.Y = Math.Min(NPC.velocity.Y + 0.3f, 10f);

                bool grounded = NPC.velocity.Y == 0f || NPC.collideY;
                if (grounded)
                {
                    NPC.velocity.Y = Main.rand.NextFloat(-6.5f, -3.5f);
                    NPC.velocity.X = Main.rand.NextFloat(2f, 5f) * (Main.rand.NextBool() ? 1 : -1);
                    FaceTowards(NPC.Center + NPC.velocity);
                    SoundEngine.PlaySound(SoundID.NPCHit1 with { Volume = 0.3f, Pitch = 0.6f }, NPC.Center);
                    NPC.netUpdate = true;

                    if (Main.netMode != NetmodeID.Server)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            Dust splash = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
                            splash.velocity = new Vector2(Main.rand.NextFloatDirection() * 2f, -Main.rand.NextFloat(1f, 3f));
                        }
                    }
                }

                // Кувыркается в полёте
                NPC.rotation += NPC.velocity.X * 0.05f;
                return;
            }

            // В воде выравниваемся после кувырков на суше
            NPC.rotation *= 0.85f;

            NPC.TargetClosest(faceTarget: false);
            Player target = Main.player[NPC.target];

            if (target.dead || !target.active)
            {
                // Цели нет — медленно уплывает и деспавнится
                NPC.EncourageDespawn(120);
                Vector2 drift = new(NPC.direction * SwimSpeed * 0.6f, -1.5f);
                NPC.velocity = (NPC.velocity * (SwimInertia - 1f) + drift) / SwimInertia;
                return;
            }

            switch (State)
            {
                case EelState.Swim:
                    NPC.damage = _baseDamage;
                    SwimTowards(target.Center);
                    FaceTowards(target.Center);

                    if (Timer > 0f)
                    {
                        Timer--;
                    }
                    else if (Vector2.Distance(NPC.Center, target.Center) < DashRange
                        && Collision.CanHitLine(NPC.position, NPC.width, NPC.height,
                            target.position, target.width, target.height))
                    {
                        State = EelState.Windup;
                        Timer = WindupTicks;
                        NPC.netUpdate = true;
                    }
                    break;

                case EelState.Windup:
                    // Телеграф: медленно отплывает назад, прицеливаясь, вокруг искры
                    Timer--;
                    FaceTowards(target.Center);

                    Vector2 away = (NPC.Center - target.Center).SafeNormalize(Vector2.UnitX);
                    if (away.Y < 0f && HeadOutOfWater())
                        away.Y = 0.5f;
                    NPC.velocity = (NPC.velocity * 9f + away * 2f) / 10f;

                    if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
                    {
                        Dust spark = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Electric);
                        spark.velocity *= 0.3f;
                        spark.scale = 0.8f;
                        spark.noGravity = true;
                    }

                    if (Timer <= 0f)
                    {
                        State = EelState.Dash;
                        Timer = DashTicks;
                        // Упреждение: целимся туда, где игрок окажется через треть секунды
                        Vector2 lead = target.Center + target.velocity * 20f;
                        NPC.velocity = (lead - NPC.Center).SafeNormalize(Vector2.UnitX) * DashSpeed;
                        NPC.damage = _baseDamage + DashDamageBonus;
                        SoundEngine.PlaySound(SoundID.Item96 with { Volume = 0.6f, Pitch = 0.4f }, NPC.Center);
                        NPC.netUpdate = true;
                    }
                    break;

                case EelState.Dash:
                    // Скорость не перезаписываем — рывок плавно затухает, нокбэк и тайлы работают честно
                    Timer--;
                    NPC.velocity *= 0.98f;
                    if (Math.Abs(NPC.velocity.X) > 0.5f)
                        FaceTowards(NPC.Center + NPC.velocity);

                    if (Main.netMode != NetmodeID.Server)
                    {
                        Dust trail = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Electric);
                        trail.velocity = NPC.velocity * 0.2f;
                        trail.scale = 0.9f;
                        trail.noGravity = true;
                    }

                    if (Timer <= 0f)
                    {
                        State = EelState.Recover;
                        Timer = RecoverTicks;
                    }
                    break;

                case EelState.Recover:
                    // Гасим остаток скорости перед возвращением к плаванию
                    Timer--;
                    NPC.damage = _baseDamage;
                    NPC.velocity *= 0.92f;

                    if (Timer <= 0f)
                    {
                        State = EelState.Swim;
                        Timer = CooldownTicks;
                    }
                    break;
            }
        }

        private void SwimTowards(Vector2 destination)
        {
            Vector2 desired = (destination - NPC.Center).SafeNormalize(Vector2.UnitX) * SwimSpeed;

            // Обычным плаванием из воды не выталкиваемся: у поверхности прижимаемся вниз
            if (desired.Y < 0f && HeadOutOfWater())
                desired.Y = 1f;

            NPC.velocity = (NPC.velocity * (SwimInertia - 1f) + desired) / SwimInertia;
        }

        private bool HeadOutOfWater()
        {
            return !Collision.WetCollision(NPC.position - new Vector2(0f, 16f), NPC.width, 8);
        }

        private void FaceTowards(Vector2 point)
        {
            NPC.direction = point.X >= NPC.Center.X ? 1 : -1;
            NPC.spriteDirection = NPC.direction;
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            target.AddBuff(BuffID.Electrified, 150);
        }
    }
}
