using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Systems;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    [AutoloadBossHead]
    public partial class King_crab : ModNPC
    {
        private enum CrabState
        {
            Scuttle,      // приближается крабьей походкой, выбирает атаку
            ClawSlam,     // замах и удар клешнёй: шоквейвы по земле (фаза 1)
            ClawSweep,    // рывок клешнёй вбок: сокращает дистанцию + направленная волна (фаза 1)
            BubbleVolley, // веер пузырей
            KnightCourt,  // переход: неуязвим, пока живы Крабы-рыцари
            JumpCrush,    // прыжок с приземлением (фаза 2)
            TideCall,     // стена воды с брешью (фаза 2)
            Burrow,       // закапывается и выныривает под игроком (антизастревание/антикайт)
            CrushingGrip, // захлоп клешни в упор: короткий выпад с большим уроном (фаза 2)
            RoyalRoar,    // рёв: отброс игроков и перенацеливание пузырей (фаза 2)
            CrownCommand, // корона светится: повторный созыв рыцарей (фаза 2)
            TsunamiClap,  // хлопок обеими клешнями: два вала расходятся по земле (фаза 2)
            Dying         // постановочная смерть: «последний взгляд» на игрока
        }

        // --- Баланс (ранний прехардмод, после Глаза) ---
        private const float Phase2LifeRatio = 0.6f;
        private const float DesperationLifeRatio = 0.25f;   // финал «Большая вода»
        private const int ShellCrackTicks = 180;             // окно контратаки после тяжёлого удара
        private const float ShellCrackDamageMult = 1.5f;    // усиление урона по вскрытому панцирю
        private const int ContactDamageP1 = 22;
        private const int ContactDamageP2 = 28;
        private const int JumpContactDamage = 34;
        private const int ShockwaveDamage = 20;
        private const int BubbleDamage = 16;
        private const int DefenseP1 = 10;
        private const int DefenseP2 = 6;
        private const float MultiplayerLifeBonus = 0.3f;    // +%HP за каждого доп. игрока в мультиплеере

        // --- Тайминги атак ---
        private const int AttackDelayP1 = 150;
        private const int AttackDelayP2 = 100;
        private const int ClawWindupTicks = 45;
        private const int ClawSweepWindupTicks = 60;   // замах-телеграф перед рывком
        private const int ClawSweepDashTicks = 30;     // длительность самого рывка
        private const float ClawSweepDashSpeed = 15f;  // стартовая скорость рывка (затухает к концу)
        private const int ClawSweepContactDamage = 30; // контактный урон на рывке (между слэмом и прыжком)
        private const float ClawSweepRange = 680f;     // на средней дистанции рвётся вместо пузырей
        private const int VolleyTicks = 46;
        private const int JumpCrouchTicks = 25;
        private const int TideTelegraphTicks = 55;
        private const float ClawRange = 340f;
        private const float FaceDeadzone = 40f;

        // --- Crushing Grip (захлоп клешни, фаза 2) ---
        private const int GripWindupTicks = 50;
        private const int GripLungeTicks = 18;
        private const float GripLungeSpeed = 12f;
        private const int GripContactDamage = 40;

        // --- Royal Roar (фаза 2) ---
        private const int RoarWindupTicks = 45;
        private const float RoarPushRange = 560f;
        private const float RoarPushForce = 10f;
        private const int RoarMinBubbles = 3;   // рёв выбирается, только когда есть что перенацелить

        // --- Tsunami Clap (фаза 2) ---
        private const int TsunamiWindupTicks = 55;
        private const int TsunamiWaveDamage = 24;
        private const float TsunamiWaveSpeed = 8f;

        // --- Crown Command (повторный созыв рыцарей, фаза 2) ---
        private const int CrownCommandTicks = 70;
        private const int CrownCommandCooldownTicks = 1500; // 25 с между созывами

        // --- Характер ---
        private const float ProudDistance = 700f; // игрок дальше — «гордая поза»
        private const int SulkTicks = 45;         // «недовольство» после промаха захлопа
        private const int CrownCareTicks = 40;    // задняя клешня придерживает корону
        private const float RageHitRatio = 0.05f; // один удар больше этой доли HP — ярость
        private const int RageTicks = 60;
        private const int DyingTicks = 100;       // «последний взгляд» перед смертью

        // --- Корона — слабое место (фаза 2 и Desperate) ---
        private const float CrownDamageMult = 1.5f;
        private const float CrownOffsetX = 0f;   // вынос короны вперёд от центра тела: сидит на «лбу» (px спрайта, крути тут)
        private const float CrownOffsetY = -60f;   // высота короны над центром тела
        private const int CrownHitboxSize = 80;    // сторона квадратной зоны короны (px мира)
        private const int CrownMeleeReachX = 60;   // допуск ближнего боя до зоны короны по X
        private const int CrownMeleeReachY = 40;   // и по Y

        // --- Закапывание ---
        private const int StuckTriggerTicks = 90;    // сколько «застревать», прежде чем закопаться
        private const float UnreachableHeight = 140f; // игрок выше на столько px = недосягаем
        private const int ProgressWindow = 45;        // окно проверки прогресса по горизонтали (тиков)
        private const float ProgressMin = 45f;        // на сколько px должен приблизиться за окно
        private const int BurrowDigTicks = 24;       // закапывание
        private const int BurrowWarnTicks = 38;      // фонтан песка — телеграф точки выхода
        private const int BurrowMaxAir = 120;        // предохранитель на вылет
        private const float BurrowDepth = 12f;       // насколько ниже поверхности прячется корпус
        private const int BurrowScanRadiusTiles = 7;  // гориз. радиус поиска блока: 15 колонок (±7)
        private const float BurrowScanUp = 0f;        // старт сканирования — от уровня игрока
        private const float BurrowScanDepth = 480f;   // глубина полосы поиска — 30 блоков вниз
        private const float JumpApexAbove = 80f;      // апекс прыжка ~5 блоков над игроком
        private const float JumpRiseSpeed = 15f;      // скорость подъёма к апексу (оба прыжка)
        private const float BurrowFallGravity = 0.9f; // доп. ускорение падения на выныривании (окно «сквозь рельеф» вдвое короче)
        private const float BurrowFallMaxSpeed = 24f; // потолок скорости ускоренного падения

        // --- Двор рыцарей (уход под землю на время боя с рыцарями) ---
        private const int KnightDigTicks = 22;        // закапывание при призыве рыцарей
        private const float KnightHideDepth = 170f;   // насколько глубже поверхности прячется корпус (полностью скрыт)
        private const float KnightSinkSpeed = 6f;      // скорость погружения под землю
        private const int KnightMinHideTicks = 90;     // минимум под землёй, чтобы рыцари успели появиться

        // Локальное (несинхронизируемое) состояние: ИИ считается на сервере,
        // клиенты получают позицию/ai[] через ванильную синхронизацию
        private bool _initialized;
        private float _stepCycle;
        private int _attacksSinceTide;
        private float _damageScale = 1f; // множитель сложности (эксперт/мастер), снятый с NPC.damage
        private int _vulnerableTimer;    // «треснувший панцирь»; синхронизируется через SendExtraAI
        private bool _announcedDesperation; // локальная защёлка one-time эффекта (NPC.life синхронизирован)
        private int _stuckTimer;         // копится, когда король не может добраться до игрока
        private float _burrowSurfaceY;   // Y поверхности в точке выхода из-под земли
        private float _knightBurrowSurfaceY; // Y поверхности над укрытием короля во время боя рыцарей
        private int _progressTimer;      // счётчик окна проверки прогресса
        private float _progressAnchorX;  // собственный X краба в начале окна (0 = окно не начато)
        private float _jumpApexY;        // целевая высота апекса прыжка (~5 блоков над игроком)
        private int _crownCooldown;      // тики до следующего Crown Command
        private int _sulkTimer;          // «недовольство» после промаха; синхронизируется через SendExtraAI
        private int _crownCareTimer;     // задняя клешня придерживает корону (косметика из HitEffect, есть на всех сторонах)
        private int _rageTimer;          // вспышка ярости после тяжёлого удара (косметика из HitEffect)
        private bool _attackLandedHit;   // выпад задел игрока (серверный факт для решения о «недовольстве»)
        private bool _proudPose;         // игрок далеко — король держит осанку (детерминировано из позиций)

        // Состояние и таймер в NPC.ai — ваниль сама синхронизирует их в мультиплеере
        private CrabState State
        {
            get => (CrabState)NPC.ai[0];
            set => NPC.ai[0] = (float)value;
        }

        private ref float Timer => ref NPC.ai[1];
        private ref float SubState => ref NPC.ai[2];
        private bool Phase2 => NPC.ai[3] == 1f;
        private bool Desperate => Phase2 && NPC.life < NPC.lifeMax * DesperationLifeRatio;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.BossBestiaryPriority.Add(Type);
            NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Confused] = true;

            // Портрет в бестиарии рисуем сами (DrawBestiary) — тут только вписываем гиганта в рамку
            NPCID.Sets.NPCBestiaryDrawModifiers drawModifiers = new()
            {
                Scale = 0.6f,
                PortraitScale = 0.7f,
                Position = new Vector2(0f, 8f),
                PortraitPositionYOverride = 8f,
            };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, drawModifiers);
        }

        public override void SetDefaults()
        {
            NPC.width = 210;
            NPC.height = 130;
            NPC.scale = 0.8f;
            DrawOffsetY = -BodyLift; // приподнимаем спрайт тела над землёй (ноги дорисованы отдельно)
            NPC.boss = true;
            NPC.aiStyle = -1;
            NPC.damage = ContactDamageP1;
            NPC.defense = DefenseP1;
            NPC.lifeMax = 2600;
            NPC.knockBackResist = 0f;
            NPC.value = Item.buyPrice(gold: 4);
            NPC.npcSlots = 10f;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath14;
            NPC.lavaImmune = true;

            SpawnModBiomes = [ ModContent.GetInstance<Worldgen.TideOfShadowsBiome>().Type];

            if (!Main.dedServ)
                Music = MusicID.Boss1;
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.King_crab.Bestiary")
            ]);
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<RoyalClaw>()));
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<AbyssScale_small>(), 1, 8, 12));
        }

        public override void OnKill()
        {
            DownedBossSystem.downedKingCrab = true;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.WorldData);
        }

        public override void AI()
        {
            if (!_initialized)
            {
                _initialized = true;
                _damageScale = NPC.damage / (float)ContactDamageP1;
                ScaleToPlayerCount();
                State = CrabState.Scuttle;
                Timer = 90f;
                SoundEngine.PlaySound(SoundID.Roar, NPC.Center);
                RoarShockwaveFx.Trigger(NPC, 50f, 1f);
                CameraFocusFx.Focus(NPC, 65, 20); // камера на босса — волна видимо идёт из него
            }

            NPC.TargetClosest(faceTarget: false);
            Player target = Main.player[NPC.target];

            // Постановочная смерть идёт до всего остального: короля уже ничто не отвлечёт
            if (State == CrabState.Dying)
            {
                UpdateDying(target);
                return;
            }

            if (target.dead || !target.active)
            {
                // Игрок мёртв/ушёл — король уходит прочь сквозь рельеф за экран и деспавнится
                NPC.EncourageDespawn(60);
                NPC.noTileCollide = true;
                int fleeDir = NPC.Center.X <= target.Center.X ? -1 : 1;
                NPC.velocity.X = fleeDir * 12f;
                NPC.velocity.Y -= 0.3f; // всплывает и уходит за кадр
                NPC.rotation = 0f;
                return;
            }

            NPC.noTileCollide = false; // в бою рельеф снова твёрдый

            bool enraged = !target.ZoneBeach; // игрок сбежал с побережья — король в ярости
            NPC.defense = Phase2 ? DefenseP2 : DefenseP1;
            // Неуязвим, пока сидит под землёй (в обороне рыцарей и при закапывании);
            // на выныривании (SubState 2) — уязвим
            NPC.dontTakeDamage = (State == CrabState.KnightCourt && SubState < 2f)
                || (State == CrabState.Burrow && SubState < 2f);

            if (_crownCooldown > 0)
                _crownCooldown--;
            if (_crownCareTimer > 0)
                _crownCareTimer--;

            // Ярость после тяжёлого удара: глаза «горят» — сигнал, что пауза будет короткой
            if (_rageTimer > 0)
            {
                _rageTimer--;
                if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(3))
                {
                    Dust fury = Dust.NewDustPerfect(EyesPos() + Main.rand.NextVector2Circular(18f, 10f),
                        DustID.RedTorch, new Vector2(0f, -Main.rand.NextFloat(0.5f, 1.5f)));
                    fury.noGravity = true;
                    fury.scale = 1.4f;
                }
            }

            // Desperate: корона тлеет золотом — подсказка слабого места
            if (Desperate)
            {
                float crownPulse = 0.7f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.15f);
                Lighting.AddLight(CrownCenter(), 0.8f * crownPulse, 0.65f * crownPulse, 0.18f * crownPulse);
            }

            // «Треснувший панцирь»: тлеющее окно контратаки после тяжёлого удара
            if (_vulnerableTimer > 0)
            {
                _vulnerableTimer--;
                // Пульсирующая подсветка трещин — читаемый сигнал «бей сейчас», гаснет к концу окна
                float crackPulse = 0.55f + 0.35f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
                float crackFade = _vulnerableTimer / (float)ShellCrackTicks;
                Lighting.AddLight(NPC.Center, 0.95f * crackPulse * (0.45f + crackFade), 0.28f * crackPulse, 0.12f * crackPulse);

                if (Main.netMode != NetmodeID.Server)
                {
                    if (Main.rand.NextBool(2))
                    {
                        Dust ember = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.RedTorch);
                        ember.velocity = new Vector2(Main.rand.NextFloatDirection() * 1.5f, -Main.rand.NextFloat(1f, 3f));
                        ember.noGravity = true;
                        ember.scale = 1.3f;
                    }
                    // Пар из трещин добавляет объём — окно заметно даже боковым зрением
                    if (Main.rand.NextBool(3))
                    {
                        Dust steam = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height / 2, DustID.Smoke);
                        steam.velocity = new Vector2(Main.rand.NextFloatDirection(), -Main.rand.NextFloat(1.5f, 3.5f));
                        steam.noGravity = true;
                        steam.scale = Main.rand.NextFloat(1f, 1.6f);
                        steam.alpha = 80;
                    }
                }
            }

            // Вход в финал «Большая вода» — единожды: рёв и всплеск
            if (Desperate && !_announcedDesperation)
            {
                _announcedDesperation = true;
                SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.5f, Volume = 0.9f }, NPC.Center);
                // Каскад из 3 волн, каждая с толчком камеры — рёв финала отличим от обычного
                RoarShockwaveFx.Trigger(NPC, 95f, 1.35f, waves: 3, shakePerWave: 12f);
                CameraFocusFx.Focus(NPC, 110, 24); // держим босса в кадре весь каскад
                TriggerImpactRing(NPC.Center, 300f, 28f, 1f);   // мелкий акцент у тела
                SpawnWaterColumn(34);
                if (Main.netMode != NetmodeID.Server)
                {
                    for (int i = 0; i < 30; i++)
                    {
                        Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
                            Main.rand.NextBool() ? DustID.Water : DustID.RedTorch);
                        d.velocity = new Vector2(Main.rand.NextFloatDirection() * 4f, -Main.rand.NextFloat(3f, 8f));
                        d.noGravity = true;
                        d.scale = Main.rand.NextFloat(1.3f, 2f);
                    }
                }
            }

            // Переход между фазами: король зовёт рыцарей и уходит в глухую оборону
            if (!Phase2 && State != CrabState.KnightCourt && NPC.life < NPC.lifeMax * Phase2LifeRatio)
            {
                State = CrabState.KnightCourt;
                SubState = 0f;              // 0: призыв+закапывание, 1: ждём под землёй, 2: выныривание в фазу 2
                Timer = KnightDigTicks;
                NPC.velocity.X = 0f;
                _knightBurrowSurfaceY = 0f;
                _stuckTimer = 0;
                SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.4f }, NPC.Center);
                RoarShockwaveFx.Trigger(NPC, 45f, 0.9f);
                CameraFocusFx.Focus(NPC, 60, 18);
                NPC.netUpdate = true;
            }

            switch (State)
            {
                case CrabState.Scuttle:
                    UpdateScuttle(target, enraged);
                    break;
                case CrabState.ClawSlam:
                    UpdateClawSlam(target);
                    break;
                case CrabState.ClawSweep:
                    UpdateClawSweep(target);
                    break;
                case CrabState.BubbleVolley:
                    UpdateBubbleVolley(target);
                    break;
                case CrabState.KnightCourt:
                    UpdateKnightCourt(target);
                    break;
                case CrabState.JumpCrush:
                    UpdateJumpCrush(target, enraged);
                    break;
                case CrabState.TideCall:
                    UpdateTideCall(target);
                    break;
                case CrabState.Burrow:
                    UpdateBurrow(target);
                    break;
                case CrabState.CrushingGrip:
                    UpdateCrushingGrip(target);
                    break;
                case CrabState.RoyalRoar:
                    UpdateRoyalRoar(target);
                    break;
                case CrabState.CrownCommand:
                    UpdateCrownCommand(target);
                    break;
                case CrabState.TsunamiClap:
                    UpdateTsunamiClap(target);
                    break;
            }
        }

        // --- Стадии ---

        private void UpdateScuttle(Player target, bool enraged)
        {
            SetContactDamage(ContactDamage(enraged));

            // «Недовольство» после промаха: на мгновение опускает голову и не действует
            if (_sulkTimer > 0)
            {
                _sulkTimer--;
                NPC.velocity.X *= 0.85f;
                NPC.rotation *= 0.85f;
                return;
            }

            // «Гордая поза»: игрок далеко — король вышагивает с поднятыми клешнями (косметика)
            _proudPose = !enraged && Grounded()
                && Vector2.Distance(NPC.Center, target.Center) > ProudDistance;

            MoveTowards(target, enraged);
            TryDropThroughPlatform(target);

            // Застрял у стены или игрок засел выше досягаемости — закапываемся,
            // но только если рядом есть реальный блок для выхода (иначе бьём пузырями)
            UpdateStuck(target);
            if (_stuckTimer >= StuckTriggerTicks && Grounded()
                && FindBurrowSurface(target, out _, out _))
            {
                BeginBurrow();
                NPC.netUpdate = true;
                return;
            }

            Timer--;
            if (Timer <= 0f && Grounded())
            {
                PickNextAttack(target);
                NPC.netUpdate = true;
            }
        }

        private void UpdateStuck(Player target)
        {
            float distX = Math.Abs(target.Center.X - NPC.Center.X);
            bool wallBlocked = NPC.collideX;
            bool tooHigh = target.Center.Y < NPC.Center.Y - UnreachableHeight && distX < 460f;

            if (wallBlocked || tooHigh)
                _stuckTimer += 2;
            else
                _stuckTimer = Math.Max(0, _stuckTimer - 2);

            // Окно прогресса: краб ХОТЕЛ идти (игрок в стороне), но сам почти не сдвинулся с места —
            // значит реально застрял (яма/склон/тупик), а не просто «не догоняет» убегающего игрока
            if (_progressAnchorX <= 0f)
            {
                _progressAnchorX = NPC.Center.X;
                _progressTimer = ProgressWindow;
            }
            else if (--_progressTimer <= 0)
            {
                bool wantedToMove = distX > 240f;
                float selfMoved = Math.Abs(NPC.Center.X - _progressAnchorX);
                if (wantedToMove && selfMoved < ProgressMin)
                    _stuckTimer += StuckTriggerTicks; // сам не двигается — за порог
                _progressAnchorX = NPC.Center.X;
                _progressTimer = ProgressWindow;
            }
        }

        private void PickNextAttack(Player target)
        {
            _progressAnchorX = 0f; // окно прогресса начнём заново после атаки
            _proudPose = false;
            float dist = Vector2.Distance(NPC.Center, target.Center);
            _attacksSinceTide++;

            // Двор пуст и корона отдохнула — повторный созыв рыцарей (высший приоритет)
            if (Phase2 && _crownCooldown <= 0 && !NPC.AnyNPCs(ModContent.NPCType<CrabKnight>()))
            {
                State = CrabState.CrownCommand;
                Timer = CrownCommandTicks;
                return;
            }

            if (Phase2 && _attacksSinceTide >= (Desperate ? 2 : 3))
            {
                _attacksSinceTide = 0;
                State = CrabState.TideCall;
                Timer = TideTelegraphTicks;
                return;
            }

            // На экране полно живых пузырей — рёв перенаправит их в игрока
            if (Phase2 && CountActiveBubbles() >= RoarMinBubbles && Main.rand.NextBool(2))
            {
                State = CrabState.RoyalRoar;
                Timer = RoarWindupTicks;
                return;
            }

            if (dist < ClawRange)
            {
                if (Phase2)
                {
                    if (Main.rand.NextBool(2)) // миксап ближней дистанции: прыжок или захлоп
                    {
                        State = CrabState.CrushingGrip;
                        Timer = GripWindupTicks;
                        SubState = 0f;
                    }
                    else
                    {
                        State = CrabState.JumpCrush;
                        Timer = JumpCrouchTicks;
                        SubState = 0f;
                    }
                }
                else if (Main.rand.NextBool(3))
                {
                    BeginClawSweep(); // миксап на ближней: рывок вместо статичного слэма
                }
                else
                {
                    // Направленный удар: клешня со стороны игрока, иногда финт в другую сторону
                    float side = target.Center.X >= NPC.Center.X ? 1f : -1f;
                    if (Main.rand.NextBool(3))
                        side = -side;
                    State = CrabState.ClawSlam;
                    Timer = ClawWindupTicks;
                    SubState = side;
                }
                return;
            }

            if (Phase2 && dist < 620f && Main.rand.NextBool(3))
            {
                if (Main.rand.NextBool(2)) // средняя дистанция: прыжок или хлопок волнами
                {
                    State = CrabState.TsunamiClap;
                    Timer = TsunamiWindupTicks;
                }
                else
                {
                    State = CrabState.JumpCrush;
                    Timer = JumpCrouchTicks;
                    SubState = 0f;
                }
                return;
            }

            // Фаза 1, средняя дистанция: рывок клешнёй сокращает разрыв вместо статичных пузырей
            if (!Phase2 && dist < ClawSweepRange)
            {
                BeginClawSweep();
                return;
            }

            State = CrabState.BubbleVolley;
            Timer = VolleyTicks;
        }

        private void UpdateClawSlam(Player target)
        {
            Timer--;
            NPC.velocity.X *= 0.85f;
            FaceTowards(target.Center);

            int slamDir = SubState >= 0f ? 1 : -1;
            NPC.rotation *= 0.8f; // корпус держим ровно — телеграф теперь отыгрывает клешня

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                Vector2 clawSpot = NPC.Bottom + new Vector2(slamDir * NPC.width * 0.4f, -10f);
                Dust d = Dust.NewDustDirect(clawSpot - new Vector2(8f, 8f), 16, 16, DustID.Sand);
                d.velocity = new Vector2(slamDir * Main.rand.NextFloat(0f, 1.5f), -Main.rand.NextFloat(1f, 3f));
            }

            if (Timer <= 0f)
            {
                NPC.rotation = 0f;
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.8f, Pitch = -0.3f }, NPC.Center);
                SpawnDirectionalShockwave(slamDir);
                Shake(5.5f, 12);
                TriggerImpactRing(NPC.Bottom, 200f, 24f);
                OpenShellCrack(); // размах открыл панцирь — окно наказания
                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        private void BeginClawSweep()
        {
            State = CrabState.ClawSweep;
            SubState = 0f; // 0: замах, ±1: рывок (знак = направление)
            Timer = ClawSweepWindupTicks;
        }

        // Рывок клешнёй: отводит клешню назад (телеграф), затем стремительно проезжает
        // в сторону игрока с усиленным контактом и оставляет одну направленную волну.
        private void UpdateClawSweep(Player target)
        {
            if (SubState == 0f) // замах-телеграф: клешня отводится НАЗАД
            {
                Timer--;
                NPC.velocity.X *= 0.85f;
                FaceTowards(target.Center);
                NPC.rotation *= 0.85f; // корпус ровно — отвод отыгрывает клешня

                int aimDir = target.Center.X >= NPC.Center.X ? 1 : -1;

                if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
                {
                    // Песок из-под отведённой (задней) клешни — читаем «сейчас метнётся»
                    Vector2 clawSpot = NPC.Bottom + new Vector2(-aimDir * NPC.width * 0.4f, -10f);
                    Dust d = Dust.NewDustDirect(clawSpot - new Vector2(8f, 8f), 16, 16, DustID.Sand);
                    d.velocity = new Vector2(-aimDir * Main.rand.NextFloat(0f, 1.5f), -Main.rand.NextFloat(1f, 3f));
                }

                if (Timer <= 0f)
                {
                    int dashDir = target.Center.X >= NPC.Center.X ? 1 : -1;
                    SubState = dashDir; // ±1, никогда 0 — однозначно отличает рывок от замаха
                    Timer = ClawSweepDashTicks;
                    NPC.velocity.X = dashDir * ClawSweepDashSpeed;
                    NPC.rotation = 0f;
                    SetContactDamage(ClawSweepContactDamage);
                    SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.2f }, NPC.Center); // свист клешни
                    NPC.netUpdate = true;
                }
                return;
            }

            // Рывок
            Timer--;
            int dir = SubState >= 0f ? 1 : -1;
            NPC.velocity.X = dir * ClawSweepDashSpeed * Math.Max(0.25f, Timer / ClawSweepDashTicks);
            NPC.rotation *= 0.85f; // корпус ровно — выпад отыгрывает клешня
            if (Grounded() && NPC.collideX)
                NPC.velocity.Y = -8f; // хоп через уступ, как в MoveTowards
            Collision.StepUp(ref NPC.position, ref NPC.velocity, NPC.width, NPC.height, ref NPC.stepSpeed, ref NPC.gfxOffY);

            if (Main.netMode != NetmodeID.Server)
            {
                Dust d = Dust.NewDustDirect(NPC.Bottom - new Vector2(NPC.width / 2f, 8f), NPC.width, 10, DustID.Sand);
                d.velocity = new Vector2(-dir * Main.rand.NextFloat(1f, 3f), -Main.rand.NextFloat(1f, 3f));
            }

            if (Timer <= 0f)
            {
                NPC.rotation = 0f;
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.7f, Pitch = -0.2f }, NPC.Center);
                SpawnDirectionalShockwave(dir);
                Shake(4.5f, 10);
                TriggerImpactRing(NPC.Bottom, 210f, 26f);
                OpenShellCrack(); // рывок вскрыл панцирь — окно наказания
                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        private void UpdateBubbleVolley(Player target)
        {
            Timer--;
            NPC.velocity.X *= 0.9f;
            FaceTowards(target.Center);

            int tick = (int)Timer;
            if (tick == 35)
                SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.7f }, NPC.Center);

            // 6 пузырей веером, каждые 7 тиков
            if (tick <= 35 && tick % 7 == 0 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int shotIndex = (35 - tick) / 7;
                float spread = MathHelper.ToRadians(38f);
                float angleOffset = MathHelper.Lerp(-spread, spread, shotIndex / 5f);
                Vector2 mouth = MouthPos();
                Vector2 dir = (target.Center - mouth).SafeNormalize(Vector2.UnitX).RotatedBy(angleOffset);
                // Урон hostile-снарядов игра удваивает, поэтому делим на 2
                Projectile.NewProjectile(NPC.GetSource_FromAI(), mouth, dir * 5.5f,
                    ModContent.ProjectileType<KingCrabBubble>(), BubbleDamage / 2, 2f, Main.myPlayer,
                    Phase2 ? 1f : 0f);
            }

            if (Timer <= 0f)
            {
                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        private void UpdateKnightCourt(Player target)
        {
            if (SubState == 0f) // призыв рыцарей и погружение под землю
            {
                SetContactDamage(0);
                NPC.noTileCollide = true;
                FaceTowards(target.Center);

                // Первый тик стадии: рыцари появляются на поверхности, король запоминает
                // уровень грунта над собой — сюда он потом вынырнет
                if (Timer == KnightDigTicks)
                {
                    _knightBurrowSurfaceY = FindGroundY(NPC.Center.X, NPC.Bottom.Y - 4f, BurrowScanDepth, false);
                    if (float.IsNaN(_knightBurrowSurfaceY))
                        _knightBurrowSurfaceY = NPC.Bottom.Y;
                    SpawnCrabKnights();
                    // Пыльный купол на всё время погружения
                    TriggerBurrowBurst(new Vector2(NPC.Center.X, _knightBurrowSurfaceY), 280f, 190f, KnightDigTicks + 15f);
                }

                Timer--;
                NPC.velocity.X *= 0.6f;
                NPC.velocity.Y = KnightSinkSpeed; // плавно тонет в песок

                SpawnSandBurst(new Vector2(NPC.Center.X - NPC.width / 2f, _knightBurrowSurfaceY - 8f),
                    NPC.width, 16, 3, 3f, 1f, 4f);

                // Корпус целиком ушёл под грунт — замираем и ждём гибели рыцарей
                if (NPC.Center.Y >= _knightBurrowSurfaceY + KnightHideDepth)
                {
                    SubState = 1f;
                    Timer = KnightMinHideTicks;
                    NPC.velocity = Vector2.Zero;
                    NPC.netUpdate = true;
                }
                return;
            }

            if (SubState == 1f) // спрятан под землёй, ждём гибели обоих рыцарей
            {
                SetContactDamage(0);
                NPC.noTileCollide = true;
                NPC.velocity = Vector2.Zero;
                // Держим корпус на месте, чтобы гравитация не утаскивала его глубже
                NPC.Center = new Vector2(NPC.Center.X, _knightBurrowSurfaceY + KnightHideDepth);
                if (Timer > 0f)
                    Timer--;

                // Оба рыцаря пали — начинаем выныривание в фазу 2 (апекс над землёй, не над игроком)
                if (Timer <= 0f && !NPC.AnyNPCs(ModContent.NPCType<CrabKnight>()))
                    LaunchFromBurrow(target, _knightBurrowSurfaceY,
                        _knightBurrowSurfaceY - JumpApexAbove - 40f, 380f, 340f, 55f);
                return;
            }

            // SubState 2: вылет из-под земли во вторую фазу
            if (UpdateBurrowDescent())
            {
                // Приземлился — панцирь трескается, начинается вторая стадия
                NPC.rotation = 0f;
                NPC.noTileCollide = false;
                NPC.ai[3] = 1f; // фаза 2 (здесь позже подключится изменённая текстура)
                SpawnShockwaves();
                Shake(16f, 26); // эффектный вход в фазу 2
                RoarShockwaveFx.Trigger(NPC, 55f, 1.2f); // рёв входа в фазу 2
                CameraFocusFx.Focus(NPC, 70, 18);
                TriggerImpactRing(NPC.Bottom, 260f, 26f, 1f);   // мелкий акцент у тела
                TriggerBurrowBurst(NPC.Bottom, 340f, 200f, 40f);
                SpawnWaterColumn(28);
                State = CrabState.Scuttle;
                Timer = 40f;
                SoundEngine.PlaySound(SoundID.NPCDeath14 with { Volume = 0.8f, Pitch = 0.3f }, NPC.Center);
                SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.2f }, NPC.Center);

                if (Main.netMode != NetmodeID.Server)
                {
                    for (int i = 0; i < 35; i++)
                    {
                        Dust shard = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
                            Main.rand.NextBool() ? DustID.RedTorch : DustID.Smoke);
                        shard.velocity = new Vector2(Main.rand.NextFloatDirection() * 5f, -Main.rand.NextFloat(2f, 7f));
                        shard.scale = Main.rand.NextFloat(1.2f, 2f);
                    }
                }
                NPC.netUpdate = true;
            }
        }

        private void SpawnCrabKnights()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            for (int side = -1; side <= 1; side += 2)
            {
                int index = NPC.NewNPC(NPC.GetSource_FromAI(),
                    (int)(NPC.Center.X + side * 190f), (int)NPC.Bottom.Y,
                    ModContent.NPCType<CrabKnight>());
                if (index < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncNPC, number: index);
            }
        }

        private void UpdateJumpCrush(Player target, bool enraged)
        {
            if (SubState == 0f) // присед-телеграф
            {
                Timer--;
                NPC.velocity.X *= 0.8f;
                FaceTowards(target.Center);
                NPC.rotation = (float)Math.Sin(Timer * 0.5f) * 0.04f; // дрожит перед прыжком

                if (Main.netMode != NetmodeID.Server)
                {
                    Dust d = Dust.NewDustDirect(NPC.Bottom - new Vector2(NPC.width / 2f, 10f), NPC.width, 10, DustID.Sand);
                    d.velocity = new Vector2(Main.rand.NextFloatDirection() * 3f, -Main.rand.NextFloat(2f, 4f));
                }

                if (Timer <= 0f)
                {
                    SubState = 1f;
                    Timer = 180f; // предохранитель, если приземление не случится
                    float jumpX = MathHelper.Clamp((target.Center.X - NPC.Center.X) / 40f, -14f, 14f);
                    _jumpApexY = target.Center.Y - JumpApexAbove; // целимся на ~5 блоков над игроком
                    NPC.velocity = new Vector2(jumpX, -JumpRiseSpeed);
                    SetContactDamage(enraged ? JumpContactDamage * 2 : JumpContactDamage);
                    SoundEngine.PlaySound(SoundID.Item14 with { Pitch = 0.4f }, NPC.Center);
                    NPC.netUpdate = true;
                }
                return;
            }

            // в воздухе
            Timer--;
            NPC.rotation = NPC.velocity.X * 0.02f;
            DriveJumpRise(); // тянем вверх до апекса ~5 блоков над игроком, затем отпускаем

            bool landed = Timer < 170f && Grounded();
            if (landed || Timer <= 0f)
            {
                NPC.rotation = 0f;
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 1f, Pitch = -0.5f }, NPC.Center);
                SpawnShockwaves();
                SpawnBubbleRing();
                Shake(11f, 20);
                TriggerImpactRing(NPC.Bottom, 280f, 30f);
                OpenShellCrack(); // тяжёлое приземление — панцирь вскрыт

                SpawnSandBurst(NPC.Bottom - new Vector2(NPC.width / 2f, 16f), NPC.width, 16, 25, 4f, 2f, 6f);

                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        private void UpdateTideCall(Player target)
        {
            Timer--;
            NPC.velocity.X *= 0.85f;
            FaceTowards(target.Center);
            NPC.rotation *= 0.85f; // корпус ровно — клешни воздевают сами (ClawTideRaise)

            if ((int)Timer == TideTelegraphTicks - 1)
                SoundEngine.PlaySound(SoundID.Item13 with { Volume = 1f, Pitch = -0.6f }, NPC.Center);

            if (Main.netMode != NetmodeID.Server)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
                d.noGravity = true;
                d.velocity = new Vector2(0f, -Main.rand.NextFloat(2f, 5f));
                d.scale = 1.6f;
            }

            if (Timer <= 0f)
            {
                NPC.rotation = 0f;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    // Волна приходит из-за спины короля и прокатывается через арену.
                    // Брешь из двух соседних «окон» — место для уклонения.
                    int primarySide = NPC.Center.X >= target.Center.X ? 1 : -1;
                    SpawnTideWave(primarySide, target);
                    // Финал: встречная волна с другой стороны и своей брешью — двойное давление
                    if (Desperate)
                        SpawnTideWave(-primarySide, target);
                }
                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        private void SpawnTideWave(int side, Player target)
        {
            float spawnX = NPC.Center.X + side * 950f;
            float speedX = -side * 7.5f;
            const int waveCount = 8;
            const float spacing = 58f;
            // Брешь держим у уровня игрока (середина стены) ± окно, чтобы проём всегда был достижим
            int mid = (waveCount - 1) / 2;
            int gapIndex = Math.Clamp(mid - 1 + Main.rand.Next(3), 0, waveCount - 2);
            float topY = target.Center.Y - spacing * (waveCount - 1) / 2f;

            for (int i = 0; i < waveCount; i++)
            {
                if (i == gapIndex || i == gapIndex + 1)
                    continue;
                Projectile.NewProjectile(NPC.GetSource_FromAI(),
                    new Vector2(spawnX, topY + spacing * i), new Vector2(speedX, 0f),
                    ModContent.ProjectileType<KingCrabWave>(), BubbleDamage / 2, 2f, Main.myPlayer,
                    1f); // режим «прилив»: летит прямо, сквозь тайлы
            }
        }

        // Захлоп клешни: раскрытый замах в упор, короткий выпад с большим контактным уроном.
        // Попадание фиксирует OnHitPlayer; промах — «недовольство» и удлинённое окно наказания.
        private void UpdateCrushingGrip(Player target)
        {
            if (SubState == 0f) // замах: клешня раскрывается во всю ширь
            {
                Timer--;
                NPC.velocity.X *= 0.85f;
                FaceTowards(target.Center);
                NPC.rotation *= 0.85f;

                if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
                {
                    Vector2 clawSpot = NPC.Center + new Vector2(NPC.direction * NPC.width * 0.45f, 0f);
                    Dust d = Dust.NewDustDirect(clawSpot - new Vector2(10f, 10f), 20, 20, DustID.Water);
                    d.noGravity = true;
                    d.velocity = new Vector2(NPC.direction * 0.5f, -Main.rand.NextFloat(0.5f, 1.5f));
                }

                if (Timer <= 0f)
                {
                    SubState = target.Center.X >= NPC.Center.X ? 1f : -1f; // знак = направление выпада
                    Timer = GripLungeTicks;
                    _attackLandedHit = false;
                    NPC.velocity.X = SubState * GripLungeSpeed;
                    SetContactDamage(GripContactDamage);
                    SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.4f }, NPC.Center);
                    NPC.netUpdate = true;
                }
                return;
            }

            // Выпад: короткий рывок, в конце — щелчок захлопа
            Timer--;
            int dir = SubState >= 0f ? 1 : -1;
            NPC.velocity.X = dir * GripLungeSpeed * Math.Max(0.3f, Timer / GripLungeTicks);
            Collision.StepUp(ref NPC.position, ref NPC.velocity, NPC.width, NPC.height, ref NPC.stepSpeed, ref NPC.gfxOffY);

            if (Timer <= 0f)
            {
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.9f, Pitch = 0.2f }, NPC.Center);
                Shake(4f, 8);
                OpenShellCrack(); // тяжёлая атака вскрывает панцирь

                // Промах решает сервер; клиенты получат _sulkTimer через SendExtraAI
                if (!_attackLandedHit && Main.netMode != NetmodeID.MultiplayerClient)
                    _sulkTimer = SulkTicks;

                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        // Рёв: отбрасывает игроков и перенацеливает живые пузыри в цель
        private void UpdateRoyalRoar(Player target)
        {
            Timer--;
            NPC.velocity.X *= 0.85f;
            FaceTowards(target.Center);
            NPC.rotation *= 0.85f;

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height / 2, DustID.Water);
                d.noGravity = true;
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 0.5f, -Main.rand.NextFloat(1f, 3f));
                d.scale = 1.4f;
            }

            if (Timer > 0f)
                return;

            SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.15f }, NPC.Center);
            RoarShockwaveFx.Trigger(NPC, 70f, 1.15f);
            CameraFocusFx.Focus(NPC, 55, 16);
            Shake(7f, 14);

            // Отброс: каждый клиент толкает СВОЕГО игрока — позиция игрока клиент-авторитетна,
            // серверный толчок чужого игрока его клиент всё равно перезаписал бы
            if (!Main.dedServ)
            {
                Player local = Main.LocalPlayer;
                if (local.active && !local.dead && Vector2.Distance(local.Center, NPC.Center) < RoarPushRange)
                {
                    Vector2 away = (local.Center - NPC.Center).SafeNormalize(Vector2.UnitX);
                    local.velocity += new Vector2(away.X * RoarPushForce, Math.Min(away.Y * RoarPushForce, -4f));
                }
            }

            // Перенацеливание пузырей (кроме стен «прилива» — их строй не ломаем)
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                int bubbleType = ModContent.ProjectileType<KingCrabBubble>();
                foreach (Projectile proj in Main.ActiveProjectiles)
                {
                    if (proj.type != bubbleType || !proj.hostile || proj.ai[0] == 2f)
                        continue;
                    proj.velocity = (target.Center - proj.Center).SafeNormalize(Vector2.UnitX)
                        * Math.Max(proj.velocity.Length(), 4f) * 1.25f;
                    proj.netUpdate = true;
                }
            }

            State = CrabState.Scuttle;
            Timer = AttackDelay();
            NPC.netUpdate = true;
        }

        private int CountActiveBubbles()
        {
            int bubbleType = ModContent.ProjectileType<KingCrabBubble>();
            int count = 0;
            foreach (Projectile proj in Main.ActiveProjectiles)
                if (proj.type == bubbleType && proj.hostile && proj.ai[0] != 2f)
                    count++;
            return count;
        }

        // Корона светится и созывает новых рыцарей — повторяемый Crown Command фазы 2
        private void UpdateCrownCommand(Player target)
        {
            Timer--;
            NPC.velocity.X *= 0.85f;
            FaceTowards(target.Center);
            NPC.rotation *= 0.85f;

            float glow = 0.6f + 0.4f * (float)Math.Sin(Main.GameUpdateCount * 0.25f);
            Lighting.AddLight(CrownCenter(), 0.9f * glow, 0.75f * glow, 0.2f * glow);
            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                Dust spark = Dust.NewDustPerfect(CrownCenter() + Main.rand.NextVector2Circular(30f, 16f),
                    DustID.GoldCoin, new Vector2(0f, -Main.rand.NextFloat(1f, 2.5f)));
                spark.noGravity = true;
            }

            if ((int)Timer == CrownCommandTicks / 2)
            {
                SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.2f, Volume = 0.8f }, NPC.Center);
                SpawnCrabKnights();
                _crownCooldown = CrownCommandCooldownTicks;
            }

            if (Timer <= 0f)
            {
                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        // Хлопок обеими клешнями: два водяных вала расходятся по земле в обе стороны
        private void UpdateTsunamiClap(Player target)
        {
            Timer--;
            NPC.velocity.X *= 0.85f;
            FaceTowards(target.Center);
            NPC.rotation *= 0.85f;

            if ((int)Timer == TsunamiWindupTicks - 1)
                SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.8f, Pitch = -0.4f }, NPC.Center);

            // Вода стягивается к воздетым клешням — читаем «сейчас хлопнет»
            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                Dust d = Dust.NewDustDirect(NPC.position - new Vector2(40f, 60f), NPC.width + 80, NPC.height, DustID.Water);
                d.noGravity = true;
                d.velocity = (NPC.Center - d.position) * 0.05f;
                d.scale = 1.5f;
            }

            if (Timer <= 0f)
            {
                NPC.rotation = 0f;
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 1f, Pitch = -0.2f }, NPC.Center);
                SoundEngine.PlaySound(SoundID.Splash with { Volume = 1f, Pitch = -0.3f }, NPC.Center);
                Shake(8f, 16);
                TriggerImpactRing(NPC.Bottom, 240f, 26f);
                SpawnWaterColumn(20);
                OpenShellCrack(); // тяжёлая атака вскрывает панцирь

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    for (int dir = -1; dir <= 1; dir += 2)
                        Projectile.NewProjectile(NPC.GetSource_FromAI(),
                            NPC.Bottom + new Vector2(dir * 60f, -30f), new Vector2(dir * TsunamiWaveSpeed, 0f),
                            ModContent.ProjectileType<KingCrabWave>(), TsunamiWaveDamage / 2, 3f, Main.myPlayer);
                }

                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        // «Последний взгляд»: король замирает, смотрит на игрока и только потом умирает по-настоящему
        private void UpdateDying(Player target)
        {
            NPC.dontTakeDamage = true;
            NPC.damage = 0;
            NPC.velocity.X *= 0.9f;
            NPC.rotation *= 0.9f;
            FaceTowards(target.Center);
            Timer--;

            if (Main.netMode != NetmodeID.Server)
            {
                if (Main.rand.NextBool(2))
                {
                    Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
                    d.velocity = new Vector2(Main.rand.NextFloatDirection(), Main.rand.NextFloat(0.5f, 2f));
                    d.scale = 1.3f;
                }
                if (Main.rand.NextBool(4))
                {
                    Dust ember = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.RedTorch);
                    ember.noGravity = true;
                    ember.velocity *= 0.4f;
                }
            }

            if (Timer <= 0f && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.life = 0;
                NPC.dontTakeDamage = false;
                NPC.StrikeInstantKill();
            }
        }

        // Гравитационно-независимый апекс: тянем вверх с постоянной скоростью до _jumpApexY,
        // затем отпускаем — дальше падение обеспечивает движковая гравитация.
        private void DriveJumpRise()
        {
            if (NPC.Center.Y > _jumpApexY && NPC.velocity.Y < 0f)
                NPC.velocity.Y = -JumpRiseSpeed;
            else if (NPC.velocity.Y < -0.1f)
                NPC.velocity.Y = 0f;
        }

        // Ignition вылета из-под земли в сторону игрока — общий для Burrow и KnightCourt.
        // apexY — целевая высота апекса; приземление разруливает UpdateBurrowDescent.
        private void LaunchFromBurrow(Player target, float surfaceY, float apexY,
            float burstWidth, float burstHeight, float burstDuration)
        {
            SubState = 2f;
            Timer = BurrowMaxAir; // предохранитель на вылет
            _burrowSurfaceY = surfaceY;
            _jumpApexY = apexY;
            int toward = Math.Sign(target.Center.X - NPC.Center.X);
            if (toward == 0)
                toward = NPC.direction;
            NPC.velocity = new Vector2(toward * 2f, -JumpRiseSpeed);
            SetContactDamage(JumpContactDamage);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = 0.2f, Volume = 1f }, NPC.Center);
            TriggerBurrowBurst(new Vector2(NPC.Center.X, surfaceY), burstWidth, burstHeight, burstDuration); // взрыв грунта
            SpawnSandBurst(new Vector2(NPC.Center.X - 30f, surfaceY - 10f), 60, 10, 30, 5f, 4f, 10f, 1.4f, 2.4f);
            NPC.netUpdate = true;
        }

        // Спуск после вылета: тянем к апексу, затем детерминированно приземляемся.
        // Весь полёт проходим рельеф насквозь и встаём на грунт вручную — ванильная коллизия
        // широкого (13 тайлов) корпуса на неровной земле влипает в боковой уступ и застревает.
        // Возвращает true при приземлении.
        private bool UpdateBurrowDescent()
        {
            Timer--;
            NPC.rotation = NPC.velocity.X * 0.02f;
            DriveJumpRise(); // тянем вверх до апекса, затем отпускаем
            NPC.noTileCollide = true;

            if (NPC.velocity.Y > 0f) // падаем — ищем, куда встать поверх рельефа
            {
                // Ускоряем спуск: пока корпус проходит рельеф насквозь, движковая гравитация
                // тянет слишком долго — окно «не коллайдит» урезаем примерно вдвое
                NPC.velocity.Y = Math.Min(NPC.velocity.Y + BurrowFallGravity, BurrowFallMaxSpeed);

                float landingY = HighestGroundUnderBody();
                if (float.IsNaN(landingY))
                    landingY = _burrowSurfaceY; // под телом пусто — встаём на запомненную поверхность
                if (NPC.Bottom.Y >= landingY)
                {
                    NPC.position.Y = landingY - NPC.height; // ставим низ корпуса ровно на грунт
                    NPC.velocity = Vector2.Zero;
                    NPC.noTileCollide = false;
                    return true;
                }
            }
            return Timer <= 0f;
        }

        private void BeginBurrow()
        {
            _stuckTimer = 0;
            _progressAnchorX = 0f;
            State = CrabState.Burrow;
            SubState = 0f;
            Timer = BurrowDigTicks;
            NPC.velocity.X = 0f;
            SoundEngine.PlaySound(SoundID.NPCDeath14 with { Pitch = -0.5f, Volume = 0.8f }, NPC.Center);
            TriggerBurrowBurst(NPC.Bottom, 260f, 170f, BurrowDigTicks + 12f); // пыль закапывания
        }

        // Ищем ближайшую к игроку колонку с реальным блоком в полосе вокруг него.
        // Нет блока в зоне → false (тогда из воздуха не выныриваем).
        private bool FindBurrowSurface(Player target, out float surfaceX, out float surfaceY)
        {
            surfaceX = 0f;
            surfaceY = 0f;
            float startY = target.Center.Y - BurrowScanUp;
            int centerTileX = (int)(target.Center.X / 16f);

            for (int off = 0; off <= BurrowScanRadiusTiles; off++)
            {
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    int tx = centerTileX + off * dir;
                    float wx = tx * 16f + 8f;
                    float gy = FindGroundY(wx, startY, BurrowScanDepth, false);
                    if (!float.IsNaN(gy))
                    {
                        surfaceX = wx;
                        surfaceY = gy;
                        return true;
                    }
                    if (off == 0)
                        break; // центральная колонка одна
                }
            }
            return false;
        }

        private void UpdateBurrow(Player target)
        {
            if (SubState < 2f)
                SetContactDamage(0); // под землёй король не бьётся о игрока

            if (SubState == 0f) // закапывание
            {
                Timer--;
                NPC.velocity.X *= 0.6f;
                NPC.noTileCollide = true;
                FaceTowards(target.Center);

                SpawnSandBurst(NPC.Bottom - new Vector2(NPC.width / 2f, 8f), NPC.width, 16, 3, 3f, 1f, 4f);

                if (Timer <= 0f)
                {
                    if (FindBurrowSurface(target, out float surfaceX, out float surfaceY))
                    {
                        _burrowSurfaceY = surfaceY;
                        NPC.Center = new Vector2(surfaceX, surfaceY + NPC.height / 2f + BurrowDepth);
                        NPC.velocity = Vector2.Zero;
                        SubState = 1f;
                        Timer = BurrowWarnTicks;
                        SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.7f, Volume = 0.6f }, NPC.Center);
                        // Телеграф точки выхода: узкий высокий фонтан на всё окно предупреждения
                        TriggerBurrowBurst(new Vector2(NPC.Center.X, _burrowSurfaceY), 170f, 230f, BurrowWarnTicks + 8f);
                        NPC.netUpdate = true;
                    }
                    else
                    {
                        // Игрок ушёл туда, где нет блока — не выпрыгиваем из воздуха, отменяем
                        NPC.noTileCollide = false;
                        _stuckTimer = 0;
                        State = CrabState.Scuttle;
                        Timer = AttackDelay();
                        NPC.netUpdate = true;
                    }
                }
                return;
            }

            if (SubState == 1f) // телеграф выхода: фонтан песка из земли
            {
                Timer--;
                NPC.velocity = Vector2.Zero;
                NPC.noTileCollide = true;

                SpawnSandBurst(new Vector2(NPC.Center.X - 20f, _burrowSurfaceY - 4f), 40, 6, 4, 2f, 4f, 8f, 1.3f, 2.2f);

                if (Timer <= 0f)
                    LaunchFromBurrow(target, _burrowSurfaceY, target.Center.Y - JumpApexAbove, 360f, 320f, 50f);
                return;
            }

            // SubState 2: вылет вверх и приземление поверх рельефа
            if (UpdateBurrowDescent())
            {
                NPC.rotation = 0f;
                NPC.noTileCollide = false;
                SpawnShockwaves();
                Shake(10f, 18);
                TriggerImpactRing(NPC.Bottom, 250f, 28f);
                TriggerBurrowBurst(NPC.Bottom, 280f, 150f, 32f); // пыль от приземления
                OpenShellCrack(); // после выныривания панцирь вскрыт
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 1f, Pitch = -0.5f }, NPC.Center);

                SpawnSandBurst(NPC.Bottom - new Vector2(NPC.width / 2f, 12f), NPC.width, 12, 20, 4f, 2f, 6f);

                State = CrabState.Scuttle;
                Timer = AttackDelay();
                NPC.netUpdate = true;
            }
        }

        // --- Хелперы ---

        private void MoveTowards(Player target, bool enraged)
        {
            float maxSpeed = Phase2 ? 6.2f : 4.4f;
            if (enraged)
                maxSpeed *= 2.2f;
            if (NPC.wet)
                maxSpeed *= 1.25f;

            // Крабья походка: 30 тиков шаг, 20 тиков пауза (в ярости — без пауз)
            _stepCycle = (_stepCycle + 1f) % 50f;
            bool stepping = _stepCycle < 30f || enraged;

            int dir = target.Center.X >= NPC.Center.X ? 1 : -1;
            if (stepping)
                NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, dir * maxSpeed, 0.08f);
            else
                NPC.velocity.X *= 0.9f;

            FaceTowards(target.Center);

            if (Grounded())
            {
                bool targetAbove = target.Center.Y < NPC.Center.Y - 80f
                    && Math.Abs(target.Center.X - NPC.Center.X) < 260f;
                if (NPC.collideX)
                    NPC.velocity.Y = -9f;
                else if (targetAbove && Main.rand.NextBool(40))
                    NPC.velocity.Y = NPC.wet ? -13f : -11.5f;
            }

            Collision.StepUp(ref NPC.position, ref NPC.velocity, NPC.width, NPC.height, ref NPC.stepSpeed, ref NPC.gfxOffY);
            NPC.rotation = NPC.velocity.X * 0.015f;
        }

        // Одна волна-снаряд по земле. Урон hostile-снарядов игра удваивает, поэтому делим на 2.
        private void SpawnShockwave(int dir, float speed)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            Projectile.NewProjectile(NPC.GetSource_FromAI(),
                NPC.Bottom + new Vector2(dir * 50f, -24f), new Vector2(dir * speed, 0f),
                ModContent.ProjectileType<KingCrabShockwave>(), ShockwaveDamage / 2, 4f, Main.myPlayer);
        }

        private void SpawnShockwaves()
        {
            for (int dir = -1; dir <= 1; dir += 2)
                SpawnShockwave(dir, 7.5f);
        }

        // Одна нацеленная волна (быстрее обычной): безопасна противоположная сторона
        private void SpawnDirectionalShockwave(int dir) => SpawnShockwave(dir, 8.5f);

        private int AttackDelay()
        {
            if (Desperate)
                return (int)(AttackDelayP2 * 0.6f);
            return Phase2 ? AttackDelayP2 : AttackDelayP1;
        }

        private void SpawnBubbleRing()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            const int count = 8;
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = (MathHelper.TwoPi / count * i).ToRotationVector2();
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, dir * 4.5f,
                    ModContent.ProjectileType<KingCrabBubble>(), BubbleDamage / 2, 2f, Main.myPlayer);
            }
        }

        private void SetContactDamage(int baseDamage)
        {
            NPC.damage = (int)(baseDamage * _damageScale);
        }

        // Базовый контактный урон по текущей фазе; в ярости — вдвое
        private int ContactDamage(bool enraged)
        {
            int baseDamage = Phase2 ? ContactDamageP2 : ContactDamageP1;
            return enraged ? baseDamage * 2 : baseDamage;
        }

        // Открывает окно «треснувшего панциря» и подаёт резкий сигнал «бей сейчас»:
        // хруст + вспышка осколков. Централизует все точки вскрытия (слэм/рывок/прыжок/выныривание).
        private void OpenShellCrack()
        {
            _vulnerableTimer = ShellCrackTicks;
            SoundEngine.PlaySound(SoundID.NPCDeath14 with { Pitch = 0.6f, Volume = 0.7f }, NPC.Center);
            if (Main.netMode == NetmodeID.Server)
                return;
            for (int i = 0; i < 14; i++)
            {
                Dust shard = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
                    Main.rand.NextBool() ? DustID.RedTorch : DustID.Smoke);
                shard.velocity = new Vector2(Main.rand.NextFloatDirection() * 3f, -Main.rand.NextFloat(1f, 4f));
                shard.noGravity = true;
                shard.scale = Main.rand.NextFloat(1.2f, 1.8f);
            }
        }

        // Масштаб HP по числу игроков (только мультиплеер). Считается один раз на сервере;
        // клиенты получают итоговый lifeMax через SendExtraAI. HP-only — урон оставляем на _damageScale.
        private void ScaleToPlayerCount()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || Main.netMode == NetmodeID.SinglePlayer)
                return;
            int players = 0;
            for (int i = 0; i < Main.maxPlayers; i++)
                if (Main.player[i].active)
                    players++;
            if (players <= 1)
                return;
            float mult = 1f + (players - 1) * MultiplayerLifeBonus;
            NPC.lifeMax = (int)(NPC.lifeMax * mult);
            NPC.life = NPC.lifeMax;
            NPC.netUpdate = true;
        }

        // Тряска экрана от тяжёлых ударов. Чисто визуально — только на клиентах.
        // FullName как идентификатор: новый удар заменяет прежнюю тряску, а не копит.
        private void Shake(float strength, int durationTicks)
        {
            if (Main.dedServ)
                return;
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                NPC.Center, Main.rand.NextVector2Unit(), strength, 6f, durationTicks, 1200f, FullName));
        }

        private Vector2 MouthPos()
        {
            return NPC.Center + new Vector2(NPC.direction * 30f, 10f);
        }

        // Центр короны в мире — та же «лицевая» привязка, что у клешней (FacingToWorld в partial Claws)
        private Vector2 CrownCenter() => FacingToWorld(new Vector2(CrownOffsetX, CrownOffsetY) * NPC.scale);

        private Vector2 EyesPos() => FacingToWorld(new Vector2(0f, -15f) * NPC.scale); // глаза в центре морды

        private Rectangle CrownRect()
        {
            Vector2 c = CrownCenter();
            int size = (int)(CrownHitboxSize * NPC.scale);
            return new Rectangle((int)c.X - size / 2, (int)c.Y - size / 2, size, size);
        }

        // Золотой звон и искры при ударе в корону + король хватается за неё
        private void CrownHitFeedback()
        {
            _crownCareTimer = CrownCareTicks;
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = 0.4f, Volume = 0.7f }, CrownCenter());
            if (Main.netMode == NetmodeID.Server)
                return;
            for (int i = 0; i < 8; i++)
            {
                Dust d = Dust.NewDustPerfect(CrownCenter(),
                    DustID.GoldCoin, Main.rand.NextVector2Circular(2.5f, 2.5f) - new Vector2(0f, 1.5f));
                d.noGravity = true;
            }
        }

        private const float GroundSnapTolerance = 10f; // «на земле», если блок в пределах стольки px под ногами

        private bool Grounded()
        {
            if (NPC.collideY || NPC.velocity.Y == 0f)
                return true;
            // На неровной поверхности (склон/уступ/полублок) velocity.Y почти никогда не строго 0,
            // а collideY мигает — из-за этого краб «не на земле» и не может прыгнуть. Подстрахуемся
            // реальной проверкой тайлов: сплошной блок в паре пикселей под ногами по ширине корпуса.
            if (NPC.velocity.Y < 0f)
                return false; // движемся вверх — точно не на земле
            float startY = NPC.Bottom.Y - 2f;
            float step = (NPC.width - 8f) / 2f;
            for (float wx = NPC.Left.X + 4f; wx <= NPC.Right.X - 4f; wx += step)
            {
                float groundY = FindGroundY(wx, startY, GroundSnapTolerance + 2f, false);
                if (!float.IsNaN(groundY) && groundY - NPC.Bottom.Y <= GroundSnapTolerance)
                    return true;
            }
            return false;
        }

        // Самая высокая (наименьший Y) земля под ВСЕЙ шириной корпуса. NaN — под телом пусто.
        // Служит точкой приземления при выныривании: краб встаёт ПОВЕРХ неровности, а не влипает в неё.
        private float HighestGroundUnderBody()
        {
            int x0 = (int)(NPC.Left.X / 16f);
            int x1 = (int)(NPC.Right.X / 16f);
            float startY = NPC.Top.Y - 16f;
            float highest = float.NaN;
            for (int x = x0; x <= x1; x++)
            {
                float gy = FindGroundY(x * 16f + 8f, startY, 1600f, false);
                if (float.IsNaN(gy))
                    continue;
                if (float.IsNaN(highest) || gy < highest)
                    highest = gy;
            }
            return highest;
        }

        // Если игрок внизу и краб стоит на платформе — проваливаемся сквозь неё (но не сквозь
        // сплошной блок). Столкновения включает обратно верх AI, когда краб уже ниже платформы.
        private void TryDropThroughPlatform(Player target)
        {
            bool playerBelow = target.Center.Y > NPC.Bottom.Y + 16f
                && Math.Abs(target.Center.X - NPC.Center.X) < NPC.width * 0.6f;
            if (playerBelow && NPC.velocity.Y >= 0f && StandingOnPlatform())
                NPC.noTileCollide = true;
        }

        // true — под ногами именно платформа (solidTop) и нет сплошного блока в той же строке.
        private bool StandingOnPlatform()
        {
            int y = (int)((NPC.Bottom.Y + 4f) / 16f);
            if (y < 0 || y >= Main.maxTilesY)
                return false;

            int x0 = (int)(NPC.Left.X / 16f);
            int x1 = (int)(NPC.Right.X / 16f);
            bool platform = false;
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || x >= Main.maxTilesX)
                    continue;
                Tile t = Main.tile[x, y];
                if (t == null || !t.HasUnactuatedTile)
                    continue;
                if (Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType])
                    return false; // реальная земля — не проваливаемся
                if (Main.tileSolidTop[t.TileType])
                    platform = true;
            }
            return platform;
        }

        private void FaceTowards(Vector2 point)
        {
            float dx = point.X - NPC.Center.X;
            if (Math.Abs(dx) < FaceDeadzone)
                return; // в мёртвой зоне сохраняем прежнее направление
            NPC.direction = dx >= 0f ? 1 : -1;
            NPC.spriteDirection = NPC.direction;
        }

        // «Треснувший панцирь» и корона-слабое место: пока окно открыто / попал по короне — больше урона
        public override void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            if (_vulnerableTimer > 0)
                modifiers.FinalDamage *= ShellCrackDamageMult;
            // Ближний бой по короне: игрок должен реально дотягиваться до её зоны
            if (Phase2 && player.Hitbox.Intersects(CrownMeleeRect()))
                modifiers.FinalDamage *= CrownDamageMult;
        }

        public override void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            if (_vulnerableTimer > 0)
                modifiers.FinalDamage *= ShellCrackDamageMult;
            if (Phase2 && projectile.Hitbox.Intersects(CrownRect()))
                modifiers.FinalDamage *= CrownDamageMult;
        }

        private Rectangle CrownMeleeRect()
        {
            Rectangle reach = CrownRect();
            reach.Inflate(CrownMeleeReachX, CrownMeleeReachY);
            return reach;
        }

        public override void OnHitByItem(Player player, Item item, NPC.HitInfo hit, int damageDone)
        {
            if (Phase2 && player.Hitbox.Intersects(CrownMeleeRect()))
                CrownHitFeedback();
        }

        public override void OnHitByProjectile(Projectile projectile, NPC.HitInfo hit, int damageDone)
        {
            if (Phase2 && projectile.Hitbox.Intersects(CrownRect()))
                CrownHitFeedback();
        }

        // Выпад достал игрока — «недовольства» после захлопа не будет (серверный хук)
        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            _attackLandedHit = true;
        }

        // Смерть постановочная: первый «смертельный» удар лишь запускает «последний взгляд»
        public override bool CheckDead()
        {
            if (State == CrabState.Dying && Timer <= 0f)
                return true;

            NPC.life = 1;
            NPC.dontTakeDamage = true;
            State = CrabState.Dying;
            SubState = 0f;
            Timer = DyingTicks;
            NPC.velocity.X = 0f;
            SoundEngine.PlaySound(SoundID.NPCDeath14 with { Pitch = -0.3f }, NPC.Center);
            CameraFocusFx.Focus(NPC, 80, 20);
            NPC.netUpdate = true;
            return false;
        }

        // Окно уязвимости влияет на урон, считаемый на клиенте атакующего — синхронизируем
        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(_vulnerableTimer);
            writer.Write(_sulkTimer);  // «недовольство» решает сервер по факту попадания
            writer.Write(NPC.lifeMax); // масштаб HP по игрокам считается на сервере — раздаём клиентам
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _vulnerableTimer = reader.ReadInt32();
            _sulkTimer = reader.ReadInt32();
            NPC.lifeMax = reader.ReadInt32();
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            // Тяжёлый одиночный удар: «забота о короне» + ярость (следующая атака почти без паузы).
            // HitEffect играется на всех сторонах — косметика совпадёт без своей синхронизации
            if (hit.Damage >= NPC.lifeMax * RageHitRatio && State != CrabState.Dying)
            {
                _crownCareTimer = CrownCareTicks;
                _rageTimer = RageTicks;
                if (Main.netMode != NetmodeID.MultiplayerClient && State == CrabState.Scuttle && Timer > 15f)
                {
                    Timer = 15f;
                    NPC.netUpdate = true;
                }
            }

            if (Main.netMode == NetmodeID.Server)
                return;

            for (int i = 0; i < 3; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.RedTorch);
                d.velocity = new Vector2(hit.HitDirection * Main.rand.NextFloat(1f, 3f), -Main.rand.NextFloat(1f, 2f));
                d.noGravity = true;
            }

            if (NPC.life <= 0)
            {
                for (int i = 0; i < 60; i++)
                {
                    Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
                        Main.rand.NextBool() ? DustID.RedTorch : DustID.Water);
                    d.velocity = new Vector2(Main.rand.NextFloatDirection() * 6f, -Main.rand.NextFloat(2f, 8f));
                    d.scale = Main.rand.NextFloat(1.3f, 2.2f);
                }
            }
        }
    }
}
