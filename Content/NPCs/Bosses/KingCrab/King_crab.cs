using System;
using System.Collections.Generic;
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
using SoA.Common.Utils;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Weapons;
using SoA.Content.Projectiles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    [AutoloadBossHead]
    public partial class King_crab : ModNPC
    {
        private enum CrabState
        {
            Scuttle,       // базовое поведение: сближение с целью, пауза между атаками
            ClawSlam,      // удар клешнёй вперёд, шоквейв по земле
            ClawSweep,     // рывок клешнёй в сторону, оставляет волну
            BubbleVolley,  // веер из 6 пузырей
            JumpCrush,     // прыжок и приземление: двойной шоквейв и кольцо пузырей
            TideCall,      // стена воды через арену (с брешью)
            Burrow,        // уход под песок и появление в другой точке
            CrushingGrip,  // захлоп клешнёй в выпаде, большой урон
            CrownCommand,  // корона светится, призывает крабов-рыцарей
            TsunamiClap,   // хлопок обеими клешнями, две расходящиеся волны
            RoyalRoar,     // рёв: отбрасывает игрока и меняет траектории пузырей
            KnightCourt,   // «королевская гвардия»: король под песком и неуязвим, пока живы рыцари
            OceanRage,     // «ярость океана»: гонит нарушителя обратно в воду (King_crab.Rage.cs)
            Dying,         // последний взгляд перед смертью
            Phase2Transition, // кат-сцена смены фазы: бой на паузе, король неуязвим
            CourtDuel,        // свита фазы 2: король уходит под песок и в бою не участвует
            Intro,            // появление: идёт под песком, из грунта встаёт корона, выход и рёв (King_crab.Cinematics.cs)
        }

        // ---------- СТАТЫ ----------
        private const int LifeMax = 4200;
        private const int Defense = 16;
        private const int DefensePhase2 = 10;      // в фазе 2 защита ниже (реф)
        private const int ContactDamage = 26;
        private const int JumpContactDamage = 34;  // на приземлении бьёт сильнее
        private const int ClawSweepContactDamage = 30;
        private const int GripContactDamage = 40;

        private const float Phase2Threshold = 0.6f;     // 60% HP
        private const float DesperateThreshold = 0.25f; // 25% HP

        // ---------- ДЛИТЕЛЬНОСТИ АТАК ----------
        // ВНИМАНИЕ: на этих же числах построены клипы в King_crab.Animation.cs.
        // Меняешь тут — проверь, что ключи клипа не вылезли за длительность.
        private const int ClawWindupTicks = 45;        // замах слэма; удар на 44-м тике
        private const int ClawRecoverTicks = 30;
        private const int ClawSweepWindupTicks = 60;   // замах-телеграф перед рывком
        private const int ClawSweepDashTicks = 30;     // сам рывок
        private const int VolleyTicks = 46;            // залп: выстрелы на 11/18/25/32/39/46
        private const int JumpCrouchTicks = 25;
        private const int TideTelegraphTicks = 55;
        private const int GripWindupTicks = 50;
        private const int GripLungeTicks = 18;
        private const int RoarWindupTicks = 45;
        private const int TsunamiWindupTicks = 55;     // хлопок на 47-м тике
        private const int CrownCommandTicks = 70;
        private const int DyingTicks = 400;            // сцена смерти целиком: взгляд, корона, пауза, рассыпание в песок
        private const int Phase2TransitionTicks = 130; // = длина клипа phase2_transition

        private const int RecoverTicks = 26;           // общий отход после тяжёлых атак
        // Пауза между атаками и скорость хода — плавные по здоровью, см. King_crab.Tempo.cs

        // ---------- ПАРАМЕТРЫ АТАК ----------
        private const int ShockwaveDamage = 28;
        private const int BubbleDamage = 22;
        private const int TsunamiWaveDamage = 24;
        private const int TideWaveDamage = 26;

        private const int VolleyShots = 6;             // веер из 6 пузырей (реф)
        private static readonly float[] VolleyShotTicks = { 11f, 18f, 25f, 32f, 39f, 46f }; // = щелчки пинцера в клипе
        private const float VolleySpread = 0.42f;      // полураствор веера (рад)
        private const float VolleySpeed = 7.5f;

        private const float SweepDashSpeed = 13f;
        private const float JumpArcSpeed = 9f;         // горизонталь прыжка
        private const float JumpLaunchSpeed = 15f;     // вертикаль прыжка
        private const float GripLungeSpeed = 16f;

        private const int TideWallCount = 7;           // валов в стене
        private const float TideWallSpacing = 62f;     // шаг по вертикали
        private const float TideWallSpeed = 6.5f;
        private const float TideWallStartDist = 760f;  // откуда стена начинает ход

        private const int RoarMinBubbles = 3;          // рёв берём, только когда есть что перенацелить
        private const float RoarPushSpeed = 9f;

        private const int KnightsPerCall = 2;

        // Набор атак по фазам: фаза 1 — медленные тяжёлые удары, фаза 2 добавляет к ним своё (реф)
        private static readonly CrabState[] Phase1Attacks =
        {
            CrabState.ClawSlam, CrabState.ClawSweep, CrabState.BubbleVolley, CrabState.Burrow,
        };
        private static readonly CrabState[] Phase2Attacks =
        {
            CrabState.JumpCrush, CrabState.TideCall, CrabState.CrushingGrip, CrabState.TsunamiClap,
            CrabState.RoyalRoar, CrabState.CrownCommand,
        };

        // Свита фазы 2: паладин и маг выходят прямо в кат-сцене, отсчёт по её таймеру
        private const int EscortWarnTicks = 95;        // бугры на местах выхода
        private const int EscortSpawnTicks = 70;       // сам выход
        private const float EscortSpawnOffsetX = 200f;
        private const int CourtDuelMaxTicks = 5400;    // предохранитель: 90 секунд под песком
        private const float CourtDuelDepth = 360f;     // глубже обычного подкопа: короля не должно быть видно
        private const int CrownCommandCooldownTicks = 1500; // 25 с между созывами
        private const int KnightCourtMaxTicks = 900;        // предохранитель: не сидим под песком вечно

        private const int BurrowDigTicks = 24;         // закапывание перед «королевской гвардией»
        private const int BurrowMaxAir = 120;          // предохранитель на вылет
        private const float BurrowEmergeSpeed = 17f;
        private const float BurrowDepth = 220f;        // насколько глубоко уходит под грунт

        // ---------- ПОДКОП: СТАДИИ ----------
        // ВНИМАНИЕ: числа завязаны на риг и отрисовку. Всё, что МЕНЬШЕ 2, риг считает «король
        // ещё не в воздухе» (King_crab.Legs.cs) и «корона снята» (King_crab.Crown.cs);
        // ровно 2 — вылет наружу. Порядок и границы менять только вместе с теми файлами.
        private const float BurrowSubPrep = 0f;     // упёрся и присел
        private const float BurrowSubSink = 0.5f;   // проваливается сквозь грунт (ещё виден)
        private const float BurrowSubTravel = 1f;   // идёт под землёй (скрыт)
        private const float BurrowSubWarn = 1.5f;   // бугор на поверхности (скрыт)
        private const float BurrowSubErupt = 2f;    // вылет наружу

        // «Королевская гвардия» и бой свиты: та же стадия провала между приседом (0) и
        // ожиданием под землёй (1). Меньше 1 — король ещё на виду и бьётся
        private const float CourtSubSink = 0.5f;

        // ---------- ПОДКОП: ТАЙМИНГИ ----------
        private const int BurrowPrepTicks = 26;        // остановка и присед перед нырком
        private const int BurrowSinkTicks = 22;        // сколько проваливается вниз
        private const int BurrowTravelMaxTicks = 170;  // предохранитель на подземный переход
        private const int BurrowWarnTicks = 38;        // бугор земли — телеграф точки выхода

        // ---------- ПОДКОП: ДВИЖЕНИЕ ----------
        private const float BurrowSinkSpeed = 5f;      // с какой скоростью начинает проваливаться
        private const float BurrowSinkAccel = 1.1f;    // и как разгоняется вниз
        private const float BurrowSinkSpeedMax = 18f;
        private const float BurrowTravelSpeed = 13f;   // скорость хода под землёй
        private const float BurrowTravelTurn = 0.12f;  // инерция подземного хода (0..1)
        private const float BurrowExitTolerance = 45f; // насколько точно подходит под игрока
        private const float BurrowWarnDepth = 120f;    // на сколько подходит к поверхности в телеграфе
        private const float BurrowEruptSpeed = 20f;    // сила выпрыгивания

        // ---------- ПОДКОП: ЭФФЕКТЫ ----------
        private const float BurrowRumbleMin = 1.2f;    // тряска в начале подземного хода
        private const float BurrowRumbleMax = 6.5f;    // и у самой точки выхода
        private const int BurrowRumbleInterval = 6;    // как часто подновлять тряску
        private const float BurrowRumbleRange = 900f;  // с какого расстояния тряска начинает расти
        private const int BurrowTrailInterval = 5;     // как часто сыпать землю над крабом
        private const float BurrowEruptShake = 11f;    // удар камеры на выпрыгивании

        // ---------- ПОВЕДЕНИЕ ----------
        private const float WalkAccel = 0.16f;
        private const float KeepDistance = 190f;       // ближе не подходит, чтобы не толкать игрока
        private const float Gravity = 0.45f;
        private const float MaxFallSpeed = 16f;
        private const float ObstacleHopSpeed = 8.5f;   // подскок через уступ при упоре в стену

        // ---------- СПУСК К ИГРОКУ ----------
        // Без этого король, оказавшись на платформе или уступе над игроком, тормозил по
        // KeepDistance и стоял сверху до конца боя: атаки в упор не достают, а спуститься нечем
        private const float DropDownHeight = 120f;     // насколько игрок должен быть ниже
        private const int DropThroughTicks = 16;       // сколько тиков игнорируем настил на спуске
        private const float DropHopSpeed = 5f;         // горизонтальный подскок в сторону игрока
        private const float DropHopLift = 5f;          // и подброс перед прыжком с уступа

        private const float TurnDeadzone = 56f;        // ближе этого по X к центру король не разворачивается
        private const float MeleeRange = 260f;         // дальность слэма и захлопа
        private const float SweepRange = 520f;
        private const float ProudDistance = 700f;      // дальше этого — «гордая поза»
        private const int SulkTicks = 30;              // «недовольство» после промаха
        private const int CrownCareTicks = 40;         // задняя клешня придерживает корону
        private const int RageTicks = 26;              // ярость от крупного урона
        private const int ShellCrackTicks = 90;        // окно 1.5x урона после тяжёлой атаки

        private const int CrownCareDamage = 45;        // с какого урона придерживает корону
        private const int RageDamage = 90;             // с какого урона впадает в ярость
        private const float ShellCrackMult = 1.5f;     // множитель в окне ShellCrack
        private const float CrownWeakMult = 2f;        // попадание по короне (фаза 2 и Desperate)

        private const int CrownHitboxSize = 80;        // сторона квадратной зоны короны (px мира)
        private const int CrownMeleeReachX = 60;       // допуск ближнего боя до зоны короны по X
        private const int CrownMeleeReachY = 40;       // и по Y

        // ---------- СОСТОЯНИЕ ----------
        // Боевое — в ai[], синхронизируется ванильно
        private CrabState State
        {
            get => (CrabState)(int)NPC.ai[0];
            set => NPC.ai[0] = (int)value;
        }

        private float Timer
        {
            get => NPC.ai[1];
            set => NPC.ai[1] = value;
        }

        private float SubState
        {
            get => NPC.ai[2];
            set => NPC.ai[2] = value;
        }

        // Свободный слот под нужды конкретной атаки (счётчик выстрелов, точка подкопа и т.п.)
        private float StateData
        {
            get => NPC.ai[3];
            set => NPC.ai[3] = value;
        }

        private bool Phase2 => NPC.life <= NPC.lifeMax * Phase2Threshold;
        private bool Desperate => NPC.life <= NPC.lifeMax * DesperateThreshold;

        // Король полностью в толще грунта: не рисуется, не бьётся и не бьёт.
        // Единая точка правды — её же спрашивает PreDraw в King_crab.Legs.cs
        private bool BurrowHidden =>
            (State == CrabState.Burrow && SubState >= BurrowSubTravel && SubState < BurrowSubErupt)
            || (State == CrabState.KnightCourt && SubState >= 1f && SubState < 2f)
            || (State == CrabState.CourtDuel && SubState >= 1f && SubState < 2f)
            || (State == CrabState.Intro && SubState < IntroSubErupt);

        // Король идёт сквозь тайлы: подкоп, гвардия, свита, спуск сквозь настил.
        // В этот момент нельзя входить ни в ярость, ни в кат-сцену смены фазы: обе возвращают
        // столкновения с тайлами, а вернуть их туше, сидящей в толще грунта, — значит замуровать
        // её там. Оба решения принимает сервер, поэтому серверного noTileCollide достаточно
        private bool PassingThroughTiles => NPC.noTileCollide;

        // ТОЛЬКО ДЛЯ ОТРИСОВКИ. Ваниль рисует NPC поверх тайлов, поэтому на стадии провала
        // голова и клешни просвечивали сквозь грунт: туша уже под землёй, а видна целиком.
        // Прячем её, как только панцирь ушёл под кромку ямы, — сам нырок при этом виден.
        //
        // В боевую логику это НЕ заходит (там остаётся BurrowHidden): _burrowSurfaceY —
        // локальная косметика, по сети не шлётся, и решать по ней, можно ли бить босса,
        // означало бы разъехаться между сервером и клиентами.
        private bool BurrowBuried
        {
            get
            {
                if (BurrowHidden)
                    return true;
                bool sinking = (State == CrabState.Burrow && SubState == BurrowSubSink)
                    || ((State == CrabState.KnightCourt || State == CrabState.CourtDuel) && SubState == CourtSubSink);
                if (!sinking || _burrowSurfaceY <= 0f)
                    return false;
                // Порог — ЦЕНТР туши, а не её верх: пока верх дойдёт до кромки, панцирь уже
                // висит на полкорпуса под землёй, и это ровно та «голова из грунта», которая
                // видна на скриншотах. Остаток нырка закрывает пылевой выброс ямы
                return NPC.Center.Y > _burrowSurfaceY;
            }
        }

        // Настроение и окна уязвимости — читаются анимацией и Vfx, шлются через SendExtraAI
        private int _rageTimer;
        private int _sulkTimer;
        private int _crownCareTimer;
        private int _vulnerableTimer;
        private bool _proudPose;

        private int _dropThrough; // тиков осталось игнорировать тайлы, проваливаясь сквозь настил

        // Кромка ямы, в которую король проваливается: чистая косметика, по сети не шлём —
        // момент нырка синхронизирован через SubState, так что клиенты возьмут её сами
        private float _burrowSurfaceY;

        // Чисто серверная кухня выбора атак.
        // «Мешок»: за цикл каждая атака фазы выпадает не больше одного раза, затем мешок
        // наполняется заново. Рандом решает только порядок: трёх залпов подряд и слэма,
        // которого не было минуту, больше не бывает
        private readonly List<CrabState> _attackBag = new();
        private bool _attackBagPhase2; // для какой фазы собран мешок
        private int _crownCommandCooldown;
        private bool _phase2Announced; // кат-сцена смены фазы уже отыграна — второй раз не входим
        private CrabState _lastAttack = CrabState.Scuttle;
        private bool _attackConnected; // атака достала игрока — иначе «недовольство»

        #region Загрузка

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1; // тело рисуем сами в PreDraw, кадров нет
            NPCID.Sets.MPAllowedEnemies[Type] = true;
            NPCID.Sets.BossBestiaryPriority.Add(Type);

            NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Confused] = true;
            NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Poisoned] = true;
            NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Venom] = true;
        }

        public override void SetDefaults()
        {
            NPC.width = 260;
            NPC.height = 200;
            NPC.damage = ContactDamage;
            NPC.defense = Defense;
            NPC.lifeMax = LifeMax;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;       // весь ИИ свой
            NPC.noGravity = true;   // гравитацию считаем сами: прыжок и подкоп ей не подчиняются
            NPC.noTileCollide = false;
            NPC.boss = true;
            NPC.npcSlots = 10f;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = Item.buyPrice(gold: 5);
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new IBestiaryInfoElement[]
            {
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.King_crab.Bestiary"),
            });
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<RoyalSpear>()));
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<RoyalClaw>(), 1, 8, 16));
        }

        public override void OnKill()
        {
            if (!DownedBossSystem.downedKingCrab)
            {
                DownedBossSystem.downedKingCrab = true;
                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.WorldData);
            }
        }

        #endregion

        #region Главный тик

        public override void AI()
        {
            if (!AcquireTarget())
                return;

            UpdateMoodTimers();
            UpdateStats();

            Player target = Main.player[NPC.target];

            // Кат-сцена смены фазы перебивает всё: пока она идёт, бой на паузе.
            // Решает только сервер — иначе клиенты войдут в стейт на своём тике и разъедутся.
            // Сквозь грунт кат-сцену не начинаем: дождёмся, пока король выберется наружу
            if (Main.netMode != NetmodeID.MultiplayerClient && Phase2 && !_phase2Announced
                && State != CrabState.Dying && State != CrabState.Phase2Transition && !PassingThroughTiles)
                BeginPhase2Transition();

            // В кат-сценах владения не сторожатся: появление ещё и задаёт, где «дом» короля
            if (State != CrabState.Phase2Transition && State != CrabState.Intro)
                UpdateTerritoryWatch(target); // сторож владений: может перебить обычный AI яростью

            switch (State)
            {
                case CrabState.Scuttle: AIScuttle(target); break;
                case CrabState.ClawSlam: AIClawSlam(); break;
                case CrabState.ClawSweep: AIClawSweep(target); break;
                case CrabState.BubbleVolley: AIBubbleVolley(target); break;
                case CrabState.JumpCrush: AIJumpCrush(target); break;
                case CrabState.TideCall: AITideCall(target); break;
                case CrabState.Burrow: AIBurrow(target); break;
                case CrabState.CrushingGrip: AICrushingGrip(target); break;
                case CrabState.CrownCommand: AICrownCommand(); break;
                case CrabState.TsunamiClap: AITsunamiClap(); break;
                case CrabState.RoyalRoar: AIRoyalRoar(target); break;
                case CrabState.KnightCourt: AIKnightCourt(target); break;
                case CrabState.OceanRage: AIOceanRage(target); break;
                case CrabState.Dying: AIDying(target); break;
                case CrabState.Phase2Transition: AIPhase2Transition(target); break;
                case CrabState.CourtDuel: AICourtDuel(target); break;
                case CrabState.Intro: AIIntro(target); break;
            }

            // Ступенька по ходу — шагом вверх. Раньше и один блок останавливал рывок
            // как «удар о стену», а ходьба упиралась и подпрыгивала
            float step = SoAPhysics.TryStepUp(NPC, MaxStepUp);
            if (step > 0f && !Main.dedServ)
                _stepVisualLift += step;

            AdvanceTimer();
        }

        // Цель и деспаун. Возвращает false, если крабу больше не с кем драться
        private bool AcquireTarget()
        {
            bool lostTarget = NPC.target < 0 || NPC.target == Main.maxPlayers
                              || Main.player[NPC.target].dead || !Main.player[NPC.target].active;
            if (lostTarget)
                NPC.TargetClosest(false);

            Player target = Main.player[NPC.target];
            if (!target.active || target.dead)
            {
                // Уходит под песок и растворяется
                NPC.noTileCollide = true;
                NPC.velocity.Y += 0.4f;
                NPC.EncourageDespawn(30);
                return false;
            }
            return true;
        }

        private void UpdateStats()
        {
            NPC.defense = Phase2 ? DefensePhase2 : Defense;

            // Контактный урон зависит от стадии: разгон и приземление больнее обычной ходьбы
            NPC.damage = State switch
            {
                CrabState.JumpCrush when SubState >= 1f => JumpContactDamage,
                CrabState.ClawSweep when SubState >= 1f => ClawSweepContactDamage,
                CrabState.CrushingGrip when SubState >= 1f => GripContactDamage,
                // В ярости туша не ранит вообще: король не убивает нарушителя, а выдворяет его.
                // Единственный урон состояния — бросок (см. RageThrowDamage), а толчок при
                // касании считает RageContactCheck: ваниль пропускает NPC с damage <= 0
                CrabState.OceanRage => 0,
                _ => ContactDamage,
            };

            // Под толщей грунта король недосягаем и не бьёт контактом. Присед и провал —
            // ещё на поверхности: это телеграф, и бить по нему можно
            // В ярости король неуязвим: пока нарушитель не в воде, бить его бесполезно —
            // об этом игроку говорит сама аура
            bool guarded = State == CrabState.KnightCourt && SubState >= 1f && KnightsAlive() > 0;
            bool cutscene = State == CrabState.Phase2Transition || State == CrabState.Intro;
            NPC.dontTakeDamage = State == CrabState.Dying || BurrowHidden || guarded || InOceanRage || cutscene;
            if (BurrowHidden || cutscene)
                NPC.damage = 0; // в кат-сцене туша не бьёт: игрок не должен умирать от статиста
        }

        private void UpdateMoodTimers()
        {
            if (_rageTimer > 0) _rageTimer--;
            if (_sulkTimer > 0) _sulkTimer--;
            if (_crownCareTimer > 0) _crownCareTimer--;
            if (_vulnerableTimer > 0) _vulnerableTimer--;
            if (_crownCommandCooldown > 0) _crownCommandCooldown--;

            // «Гордая поза» — только когда стоит без дела и игрок далеко
            Player target = Main.player[NPC.target];
            _proudPose = State == CrabState.Scuttle
                         && _sulkTimer <= 0 && _crownCareTimer <= 0
                         && Math.Abs(NPC.velocity.X) < 0.5f
                         && NPC.Distance(target.Center) > ProudDistance;
        }

        #endregion

        #region Базовое поведение

        private void AIScuttle(Player target)
        {
            TryDropToTarget(target); // игрок ниже — спускаемся, а не топчемся сверху
            ApplyGravity();
            WalkToward(target.Center.X, MaxWalkSpeed());
            FaceTarget(target);

            if (Timer > 0f)
                return;

            // Атаку выбирает только сервер — иначе клиенты разъедутся на своём rand
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            ChooseAttack(target);
        }

        private void ChooseAttack(Player target)
        {
            float dist = NPC.Distance(target.Center);

            // Продолжение связки, решённое на выходе из прошлой атаки
            if (TryTakeQueuedCombo(dist, out CrabState combo))
            {
                StartAttack(combo);
                return;
            }

            // Новая фаза — новый цикл: мешок фазы 1 не должен откладывать атаки фазы 2
            if (_attackBagPhase2 != Phase2)
            {
                _attackBag.Clear();
                _attackBagPhase2 = Phase2;
            }
            if (_attackBag.Count == 0)
                RefillAttackBag();

            Span<CrabState> ready = stackalloc CrabState[Phase1Attacks.Length + Phase2Attacks.Length];
            int count = CollectReadyAttacks(dist, ready, _lastAttack);

            // Всё, что осталось в мешке, сейчас не подходит по дистанции или условию — цикл окончен
            if (count == 0)
            {
                RefillAttackBag();
                count = CollectReadyAttacks(dist, ready, _lastAttack);
            }

            // Подходит только повтор предыдущей атаки — лучше повтор, чем стоять
            if (count == 0)
                count = CollectReadyAttacks(dist, ready, CrabState.Scuttle);

            StartAttack(count > 0 ? ready[Main.rand.Next(count)] : CrabState.BubbleVolley);
        }

        private void StartAttack(CrabState next)
        {
            _attackBag.Remove(next); // и продолжение связки засчитывается в цикл мешка
            _lastAttack = next;
            _attackConnected = false;
            EnterState(next, AttackDuration(next));

            // Брешь в приливной стене выбираем сразу: её надо успеть подсветить телеграфом
            if (next == CrabState.TideCall)
                StateData = Main.rand.Next(TideWallCount);
        }

        // Добивает мешок атаками текущей фазы. То, что в прошлом цикле так и не подошло
        // по дистанции, уже лежит в мешке и второй раз не добавляется
        private void RefillAttackBag()
        {
            AddMissingAttacks(Phase1Attacks);
            if (Phase2)
                AddMissingAttacks(Phase2Attacks);
        }

        private void AddMissingAttacks(CrabState[] attacks)
        {
            foreach (CrabState attack in attacks)
            {
                if (!_attackBag.Contains(attack))
                    _attackBag.Add(attack);
            }
        }

        private int CollectReadyAttacks(float dist, Span<CrabState> ready, CrabState skip)
        {
            int count = 0;
            foreach (CrabState attack in _attackBag)
            {
                if (attack != skip && AttackReady(attack, dist))
                    ready[count++] = attack;
            }
            return count;
        }

        // Условия атак: дистанция до цели и то, что атаке нужно для смысла
        private bool AttackReady(CrabState attack, float dist) => attack switch
        {
            CrabState.ClawSlam => dist < MeleeRange,
            CrabState.ClawSweep => dist < SweepRange,
            CrabState.Burrow => dist > MeleeRange,
            CrabState.CrushingGrip => dist < MeleeRange * 1.6f,
            CrabState.RoyalRoar => CountBubbles() >= RoarMinBubbles,
            CrabState.CrownCommand => _crownCommandCooldown <= 0 && KnightsAlive() == 0,
            _ => true,
        };

        private float AttackDuration(CrabState attack) => attack switch
        {
            CrabState.ClawSlam => ClawWindupTicks,
            CrabState.ClawSweep => ClawSweepWindupTicks,
            CrabState.BubbleVolley => VolleyTicks,
            CrabState.JumpCrush => JumpCrouchTicks,
            CrabState.TideCall => TideTelegraphTicks,
            CrabState.Burrow => BurrowPrepTicks,
            CrabState.CrushingGrip => GripWindupTicks,
            CrabState.CrownCommand => CrownCommandTicks,
            CrabState.TsunamiClap => TsunamiWindupTicks,
            CrabState.RoyalRoar => RoarWindupTicks,
            _ => PauseBetweenAttacks(),
        };

        // Возврат в стойку: промах отзывается «недовольством» (реф).
        // На низком здоровье вместо паузы — короткий вдох и продолжение связки
        private void ReturnToScuttle(bool heavyAttack = false)
        {
            bool combo = TryQueueCombo(State);
            if (!_attackConnected && !combo)
                _sulkTimer = SulkTicks;
            if (heavyAttack)
                _vulnerableTimer = ShellCrackTicks; // окно ShellCrack: 1.5x урона — живёт и во время связки

            EnterState(CrabState.Scuttle, combo ? ComboGapTicks : PauseBetweenAttacks());
        }

        private void EnterState(CrabState state, float duration, float subState = 0f)
        {
            // Темп атаки фиксируется на входе: HP меняется посреди замаха, а клип и Timer
            // должны идти одной скоростью до конца атаки
            _actionTempo = IsTempoState(state) ? TempoForHealth() : 1f;
            // Ярость, кат-сцена и прочие стейты вне атак обрывают связку
            if (state != CrabState.Scuttle && !IsTempoState(state))
                _queuedCombo = CrabState.Scuttle;

            State = state;
            Timer = duration;
            SubState = subState;
            StateData = 0f;
            // Спуск сквозь настил отменяется любой сменой стейта: иначе его окно доживёт до
            // подкопа и вернёт столкновения с тайлами посреди подземного хода.
            // Столкновения при этом возвращаем сразу: раньше атака, выбранная посреди спуска,
            // оставляла noTileCollide включённым, ApplyGravity на нём выходит сразу — и король
            // уходил сквозь мир с той скоростью, с которой падал
            if (_dropThrough > 0)
                NPC.noTileCollide = false;
            _dropThrough = 0;
            NPC.netUpdate = true;
        }

        // Смена стадии внутри атаки — без сброса StateData, он часто нужен насквозь
        private void EnterSubState(float subState, float duration)
        {
            SubState = subState;
            Timer = duration;
            NPC.netUpdate = true;
        }

        #endregion

        #region Атаки: клешни

        // CLAW SLAM: замах над головой → удар в землю, шоквейв в обе стороны
        private void AIClawSlam()
        {
            ApplyGravity();
            Brake();

            if (SubState == 0f)
            {
                if (Timer <= 1f)
                {
                    SlamImpact();
                    EnterSubState(1f, ClawRecoverTicks);
                }
                return;
            }

            if (Timer <= 0f)
                ReturnToScuttle(heavyAttack: true);
        }

        // Точка удара клешнёй — её же показывает растущая тень телеграфа (DrawSlamTelegraph)
        private Vector2 SlamImpactPoint() => NPC.Bottom + new Vector2(NPC.spriteDirection * 90f, 0f);

        private void SlamImpact()
        {
            Vector2 impact = SlamImpactPoint();
            TriggerImpactRing(impact, 260f, 26f);
            SpawnSandBurst(impact - new Vector2(50f, 10f), 100, 12, 22, 4f, 3f, 9f);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.6f }, NPC.Center);
            // Тряска, hit-stop, вспышка, трещины и второе кольцо приходят с метки slam_strike:
            // она попадает ровно в кадр удара, а этот метод зовётся боевым Timer'ом

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            for (int side = -1; side <= 1; side += 2)
            {
                Projectile.NewProjectile(NPC.GetSource_FromAI(), impact,
                    new Vector2(side * 7f, 0f), ModContent.ProjectileType<KingCrabShockwave>(),
                    ProjDamage(ShockwaveDamage), 4f, Main.myPlayer);
            }
        }

        // CLAW SWEEP: замах → рывок клешнёй в сторону, за собой оставляет волну
        private void AIClawSweep(Player target)
        {
            ApplyGravity();

            if (SubState == 0f)
            {
                Brake();
                FaceTarget(target);
                if (Timer <= 0f)
                {
                    NPC.velocity.X = NPC.spriteDirection * SweepDashSpeed * _actionTempo; // короче по времени — быстрее, дистанция та же
                    SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.4f }, NPC.Center);
                    EnterSubState(1f, ClawSweepDashTicks);

                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Bottom,
                            new Vector2(NPC.spriteDirection * 8f, 0f), ModContent.ProjectileType<KingCrabWave>(),
                            ProjDamage(TsunamiWaveDamage), 5f, Main.myPlayer, 0f);
                    }
                }
                return;
            }

            // Рывок: тормозим об стену, иначе едем до конца стадии.
            // Удар о стену раньше был вообще не озвучен — а это столкновение туши на 13 px/тик
            if (NPC.collideX)
            {
                if (Math.Abs(NPC.velocity.X) > 4f)
                    SweepWallImpact();
                NPC.velocity.X = 0f;
            }

            // Песчаный бурун ПЕРЕД клешнёй, а не под телом
            Vector2 bow = NPC.Bottom + new Vector2(NPC.spriteDirection * 110f, -8f);
            SpawnSandBurst(bow - new Vector2(30f, 0f), 60, 10, 3, 3f, 1f, 5f);
            ScreenRumble(1.4f);

            if (Timer <= 0f)
            {
                Brake();
                ReturnToScuttle(heavyAttack: true);
            }
        }

        // Врезался клешнёй в стену на полном ходу: отдача, каскад камней, тяжёлый удар камеры
        private void SweepWallImpact()
        {
            Vector2 at = NPC.Center + new Vector2(NPC.spriteDirection * NPC.width * 0.5f, 0f);
            HitStop(6);
            _clawShake = ClawShakeTicks;
            TriggerImpactRing(at, 300f, 26f, 0.4f);
            ScreenPunch(7f, 20, new Vector2(-NPC.spriteDirection, 0f));
            SpawnFlash(at, 320f, Color.White, 3);
            SpawnCracks(NPC.Bottom, 3, 80f);
            SpawnImpactDebris(at, 14, 1f, -NPC.spriteDirection); // отлетает назад, от стены
            SpawnDustCloud(at, 90f, 5);
            SpawnSparks(at, 10, 8f);
            ImpactLight(at, ImpactLightColor, 1.8f);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.5f }, at);

            for (int i = 0; i < 18; i++)
            {
                Dust rock = Dust.NewDustPerfect(at, DustID.Stone,
                    new Vector2(-NPC.spriteDirection * Main.rand.NextFloat(1f, 6f), -Main.rand.NextFloat(2f, 9f)));
                rock.scale = Main.rand.NextFloat(1f, 1.8f);
            }
        }

        // CRUSHING GRIP: изготовка → выпад с захлопом клешни
        private void AICrushingGrip(Player target)
        {
            ApplyGravity();

            if (SubState == 0f)
            {
                Brake();
                FaceTarget(target);
                if (Timer <= 0f)
                {
                    NPC.velocity.X = NPC.spriteDirection * GripLungeSpeed * _actionTempo;
                    NPC.velocity.Y = -3f;
                    SoundEngine.PlaySound(SoundID.Item17 with { Pitch = -0.7f }, NPC.Center);
                    EnterSubState(1f, GripLungeTicks);
                }
                return;
            }

            if (SubState == 1f)
            {
                if (NPC.collideX)
                    NPC.velocity.X = 0f;
                if (Timer <= 0f)
                {
                    // Щелчок пинцера в конце выпада
                    Vector2 grip = FacingToWorld(new Vector2(150f, -20f));
                    TriggerImpactRing(grip, 170f, 20f, 0.6f);
                    ScreenPunch(3f, 12);
                    SoundEngine.PlaySound(SoundID.Item23 with { Pitch = -0.5f }, NPC.Center);
                    EnterSubState(2f, RecoverTicks);
                }
                return;
            }

            Brake();
            if (Timer <= 0f)
                ReturnToScuttle(heavyAttack: true);
        }

        #endregion

        #region Атаки: вода

        // BUBBLE VOLLEY: веер из 6 пузырей; выстрелы на тех же тиках, что щелчки пинцера в клипе
        private void AIBubbleVolley(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            foreach (float shotTick in VolleyShotTicks)
            {
                if (!TimerPassed(VolleyTicks - shotTick))
                    continue;
                int shot = (int)StateData;
                StateData = shot + 1;
                FireBubble(target, shot);
            }

            if (Timer <= 0f)
                ReturnToScuttle();
        }

        private void FireBubble(Player target, int shot)
        {
            Vector2 muzzle = FacingToWorld(new Vector2(150f, -30f));
            // Нисходящий питч по номеру выстрела: очередь получает «динамику ствола»,
            // шесть одинаковых щелчков скучны по определению
            float pitch = MathHelper.Lerp(0.5f, 0.2f, VolleyShots <= 1 ? 0f : shot / (float)(VolleyShots - 1));
            SoundEngine.PlaySound(SoundID.Item54 with { Pitch = pitch }, NPC.Center);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Веер разворачивается от края к краю за 6 выстрелов
            float t = VolleyShots <= 1 ? 0.5f : shot / (float)(VolleyShots - 1);
            float spread = MathHelper.Lerp(-VolleySpread, VolleySpread, t);
            Vector2 dir = (target.Center - muzzle).SafeNormalize(new Vector2(NPC.spriteDirection, 0f));
            Vector2 velocity = dir.RotatedBy(spread) * VolleySpeed;

            // В фазе 2 пузыри слегка ведут цель (режим 1 в KingCrabBubble)
            Projectile.NewProjectile(NPC.GetSource_FromAI(), muzzle, velocity,
                ModContent.ProjectileType<KingCrabBubble>(), ProjDamage(BubbleDamage), 2f,
                Main.myPlayer, Phase2 ? 1f : 0f);
        }

        // TIDE CALL: стена валов идёт через арену, в стене одна брешь
        private void AITideCall(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            if (EveryTicks(6))
                SpawnWaterColumn(4);

            if (Timer > 0f)
                return;

            SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.3f }, NPC.Center);
            SpawnWaterColumn(30);
            if (!Main.dedServ)
            {
                // Обрушение клешней и плюха тела: раньше король призывал цунами
                // и телепортировался в стойку
                PlayClip("tide_release", once: true);
                HitStop(5);
                ScreenPunch(6f, 20, Vector2.UnitY);
                SpawnWaterSpray(NPC.Bottom, 30, 13f, -Vector2.UnitY, 0.5f);
                ImpactLight(NPC.Center, TideLightColor, 2.2f, 18);
            }

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                int dir = Math.Sign(target.Center.X - NPC.Center.X);
                if (dir == 0) dir = NPC.spriteDirection;
                if (IsSecondTideWave)
                    dir = -dir; // второй вал идёт С ДРУГОЙ стороны — это читается как замысел

                float startX = NPC.Center.X - dir * TideWallStartDist;
                float baseY = NPC.Bottom.Y;
                int gap = TideGapIndex();                          // брешь — единственный проход
                int gapWidth = Desperate ? 1 : 2;                  // в агонии стена плотнее

                for (int i = 0; i < TideWallCount; i++)
                {
                    if (i >= gap && i < gap + gapWidth)
                        continue;

                    // Валы стартуют не разом, а с рассинхроном снизу вверх — стена «встаёт»
                    Vector2 pos = new Vector2(startX - dir * i * TideWallSpeed * 2f, baseY - i * TideWallSpacing);
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), pos,
                        new Vector2(dir * TideWallSpeed, 0f), ModContent.ProjectileType<KingCrabWave>(),
                        ProjDamage(TideWaveDamage), 6f, Main.myPlayer, 1f); // режим 1 — сквозь тайлы
                }
            }

            // В агонии прилив идёт двойной волной (реф: Desperate — двойные Tide Call).
            // Флаг второго вала живёт в десятках StateData, брешь — в единицах.
            if (Desperate && !IsSecondTideWave)
            {
                StateData += TideWaveFlagStep;
                Timer = TideReleaseTicks;
                NPC.netUpdate = true;
                return;
            }

            ReturnToScuttle();
        }

        // Брешь выбирается НА ВХОДЕ в стейт (см. ChooseAttack) и живёт в StateData, чтобы
        // клиенты могли зажечь столб света в этом месте заранее — раньше проход выбирался
        // только в момент спавна валов, и предупредить о нём было нечем.
        private const float TideWaveFlagStep = 10f;  // единицы StateData — брешь, десятки — номер вала

        private int TideGapIndex() => (int)StateData % (int)TideWaveFlagStep;
        private bool IsSecondTideWave => StateData >= TideWaveFlagStep;

        // TSUNAMI CLAP: хлопок обеими клешнями, две волны расходятся по земле
        private void AITsunamiClap()
        {
            ApplyGravity();
            Brake();

            if (SubState == 0f)
            {
                // Клип бьёт метку clap_release на 47-м тике — совмещаем с ним выпуск волн
                if (TsunamiWindupTicks - Timer >= 47f)
                {
                    ClapRelease();
                    EnterSubState(1f, RecoverTicks);
                }
                return;
            }

            if (Timer <= 0f)
                ReturnToScuttle(heavyAttack: true);
        }

        private void ClapRelease()
        {
            TriggerImpactRing(NPC.Bottom, 300f, 30f, 0.35f);
            ScreenPunch(5f, 18);
            SoundEngine.PlaySound(SoundID.Item45 with { Pitch = -0.5f }, NPC.Center);
            SpawnWaterColumn(20);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            for (int side = -1; side <= 1; side += 2)
            {
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Bottom,
                    new Vector2(side * 7.5f, 0f), ModContent.ProjectileType<KingCrabWave>(),
                    ProjDamage(TsunamiWaveDamage), 6f, Main.myPlayer, 0f);
            }
        }

        // ROYAL ROAR: отбрасывает игрока и перенацеливает висящие в воздухе пузыри
        private void AIRoyalRoar(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            if (Timer > 0f)
                return;

            SoundEngine.PlaySound(SoundID.Roar, NPC.Center);
            TriggerImpactRing(NPC.Center, 420f, 34f, 0.8f);
            ScreenPunch(6f, 24);
            // Выпуск рёва: клип и его метка roar_release (свет, волна, дуги звука, пыль).
            // Раньше клип нигде не запускался, и все эффекты выпуска молчали
            if (!Main.dedServ)
                PlayClip("roar_release", once: true);

            foreach (Player p in Main.ActivePlayers)
            {
                if (p.dead || p.Distance(NPC.Center) > 900f)
                    continue;
                Vector2 away = (p.Center - NPC.Center).SafeNormalize(-Vector2.UnitY);
                ShovePlayer(p, away * RoarPushSpeed + new Vector2(0f, -2f));
            }

            // Пузыри разворачиваются на игроков и включают самонаведение
            int bubbleType = ModContent.ProjectileType<KingCrabBubble>();
            foreach (Projectile proj in Main.ActiveProjectiles)
            {
                if (proj.type != bubbleType || !proj.hostile)
                    continue;

                Player closest = Main.player[Player.FindClosest(proj.position, proj.width, proj.height)];
                proj.velocity = (closest.Center - proj.Center).SafeNormalize(Vector2.UnitX) * 8f;
                proj.ai[0] = 1f;  // режим самонаведения
                proj.ai[1] = 0f;  // сбрасываем таймер наведения
                proj.netUpdate = true;
            }

            ReturnToScuttle();
        }

        #endregion

        #region Атаки: перемещение

        // JUMP CRUSH: присед → прыжок → приземление с двойным шоквейвом и кольцом пузырей
        private void AIJumpCrush(Player target)
        {
            if (SubState == 0f)
            {
                ApplyGravity();
                Brake();
                FaceTarget(target);
                if (Timer <= 0f)
                {
                    float dx = target.Center.X - NPC.Center.X;
                    NPC.velocity.X = MathHelper.Clamp(dx / 26f, -JumpArcSpeed, JumpArcSpeed);
                    NPC.velocity.Y = -JumpLaunchSpeed;
                    SoundEngine.PlaySound(SoundID.Item45 with { Pitch = -0.8f }, NPC.Center);
                    SpawnSandBurst(NPC.Bottom - new Vector2(70f, 8f), 140, 12, 18, 4f, 2f, 7f);
                    SpawnDustCloud(NPC.Bottom, 180f, 6, 0.9f);
                    EnterSubState(1f, 200f); // сверху висит предохранитель по времени
                }
                return;
            }

            ApplyGravity();

            if (SubState == 1f)
            {
                // В полёте подруливаем слабо: прыжок должен читаться заранее
                float steer = MathHelper.Clamp((target.Center.X - NPC.Center.X) * 0.002f, -0.2f, 0.2f);
                NPC.velocity.X = MathHelper.Clamp(NPC.velocity.X + steer, -JumpArcSpeed, JumpArcSpeed);

                bool landed = NPC.velocity.Y >= 0f && (NPC.collideY || NPC.velocity.Y == 0f);
                if (landed || Timer <= 0f)
                {
                    JumpImpact();
                    EnterSubState(2f, RecoverTicks);
                }
                return;
            }

            Brake();
            if (Timer <= 0f)
                ReturnToScuttle(heavyAttack: true);
        }

        private void JumpImpact()
        {
            NPC.velocity.X = 0f;
            TriggerImpactRing(NPC.Bottom, 340f, 30f);
            TriggerImpactRing(NPC.Bottom, 200f, 20f, 0.5f);
            SpawnSandBurst(NPC.Bottom - new Vector2(90f, 10f), 180, 14, 30, 5f, 3f, 11f);
            ScreenPunch(10f, 22, Vector2.UnitY); // самый тяжёлый удар босса бьёт камеру ВНИЗ
            SoundEngine.PlaySound(SoundID.Item14, NPC.Center);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Двойной шоквейв (реф) + кольцо пузырей
            for (int side = -1; side <= 1; side += 2)
            {
                for (int wave = 0; wave < 2; wave++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Bottom,
                        new Vector2(side * (6f + wave * 3f), 0f), ModContent.ProjectileType<KingCrabShockwave>(),
                        ProjDamage(ShockwaveDamage), 4f, Main.myPlayer);
                }
            }

            int ringCount = Desperate ? 10 : 8;
            for (int i = 0; i < ringCount; i++)
            {
                Vector2 dir = Vector2.UnitX.RotatedBy(MathHelper.TwoPi * i / ringCount);
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, dir * 5.5f,
                    ModContent.ProjectileType<KingCrabBubble>(), ProjDamage(BubbleDamage), 2f, Main.myPlayer);
            }
        }

        // BURROW: присед → провал сквозь грунт → подземный ход к игроку → бугор → выпрыгивание.
        // Никаких телепортов: каждую стадию король проходит своей скоростью, а игроку её
        // показывают пыль на поверхности и нарастающая тряска камеры.
        private void AIBurrow(Player target)
        {
            switch (SubState)
            {
                case BurrowSubPrep: BurrowPrep(target); break;
                case BurrowSubSink: BurrowSink(); break;
                case BurrowSubTravel: BurrowTravel(target); break;
                case BurrowSubWarn: BurrowWarn(); break;
                default: BurrowErupt(); break;
            }
        }

        // 1. Подготовка: тормозит, приседает (клип jump_crouch), скребёт лапами грунт
        private void BurrowPrep(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            if (EveryTicks(4))
                SpawnSandBurst(NPC.Bottom - new Vector2(90f, 6f), 180, 10, 4, 3f, 1f, 4f);

            if (Timer > 0f)
                return;

            BeginSink();
            EnterSubState(BurrowSubSink, BurrowSinkTicks);
        }

        // Нырок: с этого мига грунт ему не помеха, но сам он ещё на виду.
        // Общий для подкопа, «королевской гвардии» и боя свиты — раньше в двух последних
        // король просто исчезал на месте, будто телепортировался под землю
        private void BeginSink()
        {
            NPC.noTileCollide = true;
            NPC.velocity = new Vector2(0f, BurrowSinkSpeed);
            _burrowSurfaceY = NPC.Bottom.Y; // кромка ямы — по ней сыплется земля, пока проваливается

            SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.5f }, NPC.Center);
            TriggerBurrowBurst(NPC.Bottom, 170f, 95f, 32f);
            ScreenPunch(2.5f, 12);
        }

        // 2. Провал: разгоняется вниз и уходит в толщу грунта, осыпая кромку ямы
        private void BurrowSink()
        {
            SinkTick();

            if (Timer > 0f)
                return;

            FinishSink();
            EnterSubState(BurrowSubTravel, BurrowTravelMaxTicks);
        }

        // Тик провала: разгоняется вниз, осыпая кромку ямы
        private void SinkTick()
        {
            NPC.noTileCollide = true;

            // Клиент мог получить синхронизацию уже посреди провала и пропустить тик нырка
            if (_burrowSurfaceY <= 0f)
                _burrowSurfaceY = NPC.Bottom.Y;
            NPC.velocity.X *= 0.8f;
            NPC.velocity.Y = Math.Min(NPC.velocity.Y + BurrowSinkAccel, BurrowSinkSpeedMax);

            // Земля и камни осыпаются по краю ямы, а не там, куда уже провалился корпус
            Vector2 rim = new Vector2(NPC.Center.X - 80f, _burrowSurfaceY - 8f);
            if (EveryTicks(2))
                SpawnSandBurst(rim, 160, 10, 5, 3.5f, 1f, 5f);
            if (EveryTicks(5))
                SpawnSandBurst(rim, 160, 10, 2, 2.5f, 2f, 6f, 0.9f, 1.4f, DustID.Stone);

            ScreenRumble(BurrowRumbleMin);
        }

        // Скрылся целиком: дальше его не рисуют и не бьют
        private void FinishSink()
            => TriggerBurrowBurst(new Vector2(NPC.Center.X, _burrowSurfaceY), 150f, 80f, 34f);

        // 3. Подземный ход: плавно идёт к игроку. Тряска и пыль над крабом — единственное,
        // по чему игрок читает, где он сейчас
        private void BurrowTravel(Player target)
        {
            NPC.noTileCollide = true;

            // Глубину меряем от ГРУНТА под игроком, а не от его ног: иначе стоит взлететь на
            // крыльях, и «подземный» ход уводит короля в открытый воздух
            float goalY = SurfaceAbove(target.Center.X, target.Bottom.Y) + BurrowDepth;
            Vector2 goal = new Vector2(target.Center.X, goalY);
            Vector2 desired = (goal - NPC.Center).SafeNormalize(Vector2.UnitY) * BurrowTravelSpeed;
            NPC.velocity = Vector2.Lerp(NPC.velocity, desired, BurrowTravelTurn);

            float distX = Math.Abs(goal.X - NPC.Center.X);

            // Чем ближе к точке выхода, тем сильнее трясёт
            float closeness = 1f - MathHelper.Clamp(distX / BurrowRumbleRange, 0f, 1f);
            if (EveryTicks(BurrowRumbleInterval))
                ScreenRumble(MathHelper.Lerp(BurrowRumbleMin, BurrowRumbleMax, closeness));

            if (EveryTicks(BurrowTrailInterval))
                SpawnSurfaceTrail(NPC.Center.X, target.Center.Y, closeness);

            if (distX > BurrowExitTolerance && Timer > 0f)
                return;

            StateData = NPC.Center.X; // точка выхода зафиксирована — дальше король её держит
            EnterSubState(BurrowSubWarn, BurrowWarnTicks);
        }

        // 4a. Телеграф: подтягивается под самую поверхность, над ним вспучивается бугор
        private void BurrowWarn()
        {
            NPC.noTileCollide = true;

            float surfaceY = SurfaceAbove(StateData, NPC.Center.Y);
            Vector2 goal = new Vector2(StateData, surfaceY + BurrowWarnDepth);
            NPC.velocity = Vector2.Lerp(NPC.velocity, (goal - NPC.Center) * 0.12f, 0.3f);

            float progress = 1f - Timer / BurrowWarnTicks;
            Vector2 surface = new Vector2(StateData, surfaceY);

            // Бугор растёт по мере приближения выхода
            if (EveryTicks(9))
                TriggerBurrowBurst(surface, 90f + 90f * progress, 50f + 50f * progress, 28f);
            if (EveryTicks(3))
                SpawnSandBurst(surface - new Vector2(45f, 6f), 90, 8, 5, 3f, 2f, 6f + 4f * progress);

            ScreenRumble(MathHelper.Lerp(BurrowRumbleMax * 0.7f, BurrowRumbleMax, progress));

            if (Timer > 0f)
                return;

            EruptFromGround(surface);
            EnterSubState(BurrowSubErupt, BurrowMaxAir);
        }

        // 4b. Выброс наружу: земля, камни, кольцо удара и тяжёлый удар камеры
        private void EruptFromGround(Vector2 surface)
        {
            NPC.velocity = new Vector2(0f, -BurrowEruptSpeed);

            TriggerBurrowBurst(surface, 260f, 150f, 34f);
            TriggerImpactRing(surface, 340f, 28f);
            SpawnSandBurst(surface - new Vector2(110f, 12f), 220, 16, 40, 6f, 5f, 14f);
            SpawnSandBurst(surface - new Vector2(90f, 12f), 180, 14, 14, 5f, 6f, 15f, 1f, 1.6f, DustID.Stone);
            ScreenPunch(BurrowEruptShake, 26);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.4f }, NPC.Center);
        }

        // 5. Полёт и приземление — дальше обычный AI
        private void BurrowErupt()
        {
            NPC.velocity.Y += Gravity * 0.6f;
            if (NPC.velocity.Y > 0f)
                NPC.noTileCollide = false; // на спуске снова ловим землю

            SpawnSandBurst(NPC.Bottom - new Vector2(60f, 6f), 120, 10, 4, 3f, 2f, 6f);

            if (!Landed() && Timer > 0f)
                return;

            LandFromEruption();
            ReturnToScuttle();
        }

        // Падает с большей высоты, чем в прыжке, — и приземление обязано быть тяжелее
        private void LandFromEruption()
        {
            NPC.noTileCollide = false;
            TriggerImpactRing(NPC.Bottom, 340f, 28f);
            SpawnSandBurst(NPC.Bottom - new Vector2(80f, 8f), 160, 12, 18, 4f, 2f, 7f);
            ScreenPunch(6.5f, 18, Vector2.UnitY);
            if (!Main.dedServ)
            {
                PlayClip("burrow_land", once: true); // иначе садился с поджатыми клешнями
                HitStop(5);
                SpawnCracks(NPC.Bottom, 3, 90f);
                SpawnImpactDebris(NPC.Bottom, 12, 1f);
                SpawnDustCloud(NPC.Bottom, 200f, 8);
            }
        }

        // Пыль над крабом: показывает, что под грунтом кто-то идёт
        private void SpawnSurfaceTrail(float worldX, float aboveY, float closeness)
        {
            float surfaceY = SurfaceAbove(worldX, aboveY);
            SpawnSandBurst(new Vector2(worldX - 35f, surfaceY - 4f), 70, 8,
                3 + (int)(5f * closeness), 2.5f, 2f, 5f + 4f * closeness);
        }

        // Поверхность грунта над точкой. Ищем сверху вниз: сам король уже в толще,
        // из-под него первого солида не найти
        private float SurfaceAbove(float worldX, float belowY)
        {
            float found = FindGroundY(worldX, belowY - 600f, 1200f, false);
            return float.IsNaN(found) ? belowY : found;
        }

        #endregion

        #region Атаки: свита

        // CROWN COMMAND: корона светится, из песка лезут крабы-рыцари
        private void AICrownCommand()
        {
            ApplyGravity();
            Brake();

            // Места появления рыцарей предупреждаются заранее: раньше бугор вспучивался
            // одновременно со спавном, и уклониться было нечем
            if (TimerPassed(20f))
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector2 spot = NPC.Bottom + new Vector2((i == 0 ? -1 : 1) * 180f, 0f);
                    TriggerBurrowBurst(spot, 60f, 30f, 22f);
                    SpawnSandBurst(spot - new Vector2(30f, 6f), 60, 8, 5, 2.5f, 1f, 4f);
                }
            }

            if (Timer > 0f)
                return;

            SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.4f }, NPC.Center);
            _crownCommandCooldown = CrownCommandCooldownTicks;

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < KnightsPerCall; i++)
                {
                    int side = i % 2 == 0 ? -1 : 1;
                    Vector2 spawn = NPC.Bottom + new Vector2(side * (180f + i * 40f), -20f);
                    int knight = NPC.NewNPC(NPC.GetSource_FromAI(), (int)spawn.X, (int)spawn.Y,
                        ModContent.NPCType<CrabKnight>());
                    if (knight < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                        NetMessage.SendData(MessageID.SyncNPC, number: knight);
                }
            }

            // Рыцари лезут ВРАЗНОБОЙ, с задержкой 6 тиков друг от друга: одновременный
            // выход двух читается как один эффект
            for (int i = 0; i < 2; i++)
            {
                Vector2 spot = NPC.Bottom + new Vector2((i == 0 ? -1 : 1) * 180f, 0f);
                ScheduleFx(1 + i * 6, () => TriggerBurrowBurst(spot, 110f, 70f, 26f));
            }

            // В агонии король прячется за гвардией, иначе просто возвращается в бой
            if (Desperate)
                EnterState(CrabState.KnightCourt, BurrowDigTicks);
            else
                ReturnToScuttle();
        }

        // KNIGHT COURT: пока живы рыцари, король сидит под песком и неуязвим
        private void AIKnightCourt(Player target)
        {
            if (SubState == 0f) // зарывается
            {
                ApplyGravity();
                Brake();
                SpawnSandBurst(NPC.Bottom - new Vector2(80f, 6f), 160, 10, 6, 3f, 2f, 6f);
                if (Timer <= 0f)
                {
                    BeginSink();
                    EnterSubState(CourtSubSink, BurrowSinkTicks);
                }
                return;
            }

            if (SubState == CourtSubSink) // проваливается сквозь грунт — на виду
            {
                SinkTick();
                if (Timer <= 0f)
                {
                    FinishSink();
                    EnterSubState(1f, KnightCourtMaxTicks);
                }
                return;
            }

            if (SubState == 1f) // ждёт под землёй, держась под игроком
            {
                NPC.noTileCollide = true;
                NPC.velocity = new Vector2(
                    MathHelper.Clamp((target.Center.X - NPC.Center.X) * 0.03f, -8f, 8f),
                    MathHelper.Clamp((target.Bottom.Y + BurrowDepth - NPC.Center.Y) * 0.05f, -8f, 8f));

                if (Timer % 12f == 0f)
                    SpawnSandBurst(new Vector2(NPC.Center.X - 40f, target.Bottom.Y - 4f), 80, 8, 4, 3f, 3f, 8f);

                if (KnightsAlive() == 0 || Timer <= 0f)
                {
                    NPC.velocity = new Vector2(0f, -BurrowEmergeSpeed);
                    SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.2f }, NPC.Center);
                    EnterSubState(2f, BurrowMaxAir);
                }
                return;
            }

            // Выныривает обратно в бой
            NPC.velocity.Y += Gravity * 0.6f;
            if (NPC.velocity.Y > 0f)
                NPC.noTileCollide = false;

            if (Landed() || Timer <= 0f)
            {
                NPC.noTileCollide = false;
                TriggerImpactRing(NPC.Bottom, 300f, 28f, 0.4f);
                ScreenPunch(5f, 16, Vector2.UnitY);
                if (!Main.dedServ)
                    PlayClip("burrow_land", once: true);
                ReturnToScuttle();
            }
        }

        private int KnightsAlive() => NPC.CountNPCS(ModContent.NPCType<CrabKnight>());

        private int CountBubbles()
        {
            int type = ModContent.ProjectileType<KingCrabBubble>();
            int count = 0;
            foreach (Projectile proj in Main.ActiveProjectiles)
            {
                if (proj.type == type)
                    count++;
            }
            return count;
        }

        #endregion

        #region Смена фазы

        // Кат-сцена на 130 тиков. Без неё клип phase2_transition играл ПОВЕРХ боевого:
        // король продолжал драться посреди собственной постановки, и его можно было
        // добить, не увидев сцену вообще.
        private void BeginPhase2Transition()
        {
            _phase2Announced = true;

            // Под землёй сюда не попадаем (см. PassingThroughTiles в AI), так что столкновения
            // уже на месте; строка — страховка на случай нового стейта, забывшего о них
            NPC.noTileCollide = false;
            NPC.velocity.X = 0f;
            EnterState(CrabState.Phase2Transition, Phase2TransitionTicks);
        }

        private void AIPhase2Transition(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            // Места выхода свиты предупреждаются раньше самой свиты: бугор вспучивается,
            // и у игрока есть время отойти
            if (Timer == EscortWarnTicks)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 spot = NPC.Bottom + new Vector2(side * EscortSpawnOffsetX, 0f);
                    TriggerBurrowBurst(spot, 70f, 34f, 24f);
                    SpawnSandBurst(spot - new Vector2(30f, 6f), 70, 9, 5, 2.5f, 1f, 4f);
                }
            }

            if (Timer == EscortSpawnTicks)
                SummonCourt();

            // Не через ReturnToScuttle: у кат-сцены нет «промаха», и король не должен
            // выходить из собственной постановки в позе недовольства.
            // И не в Scuttle: пока жива свита, короля в бою нет вообще
            if (Timer <= 0f)
                EnterState(CrabState.CourtDuel, BurrowDigTicks);
        }

        // Паладин слева, маг справа: разные роли обязаны занимать разные стороны,
        // иначе игрок с первой секунды зажат в один угол
        private void SummonCourt()
        {
            SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.1f }, NPC.Center);

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                SpawnEscort(ModContent.NPCType<CrabKnight>(), -EscortSpawnOffsetX);
                SpawnEscort(ModContent.NPCType<CrabMage>(), EscortSpawnOffsetX);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 spot = NPC.Bottom + new Vector2(side * EscortSpawnOffsetX, 0f);
                ScheduleFx(1 + (side + 1) * 4, () => TriggerBurrowBurst(spot, 120f, 74f, 28f));
            }
        }

        private void SpawnEscort(int type, float offsetX)
        {
            Vector2 spawn = NPC.Bottom + new Vector2(offsetX, -24f);
            int index = NPC.NewNPC(NPC.GetSource_FromAI(), (int)spawn.X, (int)spawn.Y, type);
            if (index < Main.maxNPCs && Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncNPC, number: index);
        }

        // COURT DUEL: король уходит под песок и на время боя со свитой исчезает.
        // Отличие от KnightCourt не в неуязвимости, а в отсутствии: он не бьёт,
        // его не видно и по нему нельзя попасть, пока паладин или маг живы
        private void AICourtDuel(Player target)
        {
            if (SubState == 0f) // зарывается
            {
                ApplyGravity();
                Brake();
                SpawnSandBurst(NPC.Bottom - new Vector2(80f, 6f), 160, 10, 6, 3f, 2f, 6f);
                if (Timer <= 0f)
                {
                    BeginSink();
                    EnterSubState(CourtSubSink, BurrowSinkTicks);
                }
                return;
            }

            if (SubState == CourtSubSink) // проваливается сквозь грунт — на виду
            {
                SinkTick();
                if (Timer <= 0f)
                {
                    FinishSink();
                    EnterSubState(1f, CourtDuelMaxTicks);
                }
                return;
            }

            if (SubState == 1f) // ждёт глубоко под ареной
            {
                NPC.noTileCollide = true;
                // Держится под игроком, но глубже обычного подкопа: всплыть он должен
                // там, где идёт бой, а не там, где начался
                NPC.velocity = new Vector2(
                    MathHelper.Clamp((target.Center.X - NPC.Center.X) * 0.02f, -6f, 6f),
                    MathHelper.Clamp((target.Bottom.Y + CourtDuelDepth - NPC.Center.Y) * 0.05f, -8f, 8f));

                // Предохранитель на случай, если свита не заспавнилась вовсе
                if (Main.netMode != NetmodeID.MultiplayerClient && (EscortsAlive() == 0 || Timer <= 0f))
                {
                    NPC.velocity = new Vector2(0f, -BurrowEmergeSpeed);
                    SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.3f }, NPC.Center);
                    EnterSubState(2f, BurrowMaxAir);
                }
                return;
            }

            // Возвращается в бой ударом о грунт
            NPC.velocity.Y += Gravity * 0.6f;
            if (NPC.velocity.Y > 0f)
                NPC.noTileCollide = false;

            if (Landed() || Timer <= 0f)
            {
                NPC.noTileCollide = false;
                TriggerImpactRing(NPC.Bottom, 340f, 30f, 0.45f);
                ScreenPunch(6f, 18, Vector2.UnitY);
                if (!Main.dedServ)
                    PlayClip("burrow_land", once: true);
                EnterState(CrabState.Scuttle, PauseBetweenAttacks());
            }
        }

        private int EscortsAlive()
            => NPC.CountNPCS(ModContent.NPCType<CrabKnight>()) + NPC.CountNPCS(ModContent.NPCType<CrabMage>());

        #endregion

        #region Смерть

        // Смерть отыгрывается стадией Dying: корону роняет Crown.cs по доле Timer
        private void AIDying(Player target)
        {
            ApplyGravity();
            Brake();

            // «Последний взгляд» — на игрока (реф)
            int look = Math.Sign(target.Center.X - NPC.Center.X);
            if (look != 0)
            {
                NPC.direction = look;
                NPC.spriteDirection = look;
            }

            // Пыль обрушения, hit-stop и удар камеры вешает диспетчер меток на «collapse»,
            // рассыпание в песок и сборку копья — King_crab.Cinematics.cs

            // Добыча ванильно падает в случайную точку хитбокса. У туши 260×200 копьё
            // вываливалось бы в стороне от места, где оно только что собралось из песка
            if (Timer <= DeathLootShrinkTick && NPC.width > DeathLootBox)
                ShrinkHitbox(DeathLootBox);

            if (Timer > 0f)
                return;

            NPC.life = 0;
            NPC.HitEffect();
            NPC.checkDead();
        }

        public override bool CheckDead()
        {
            if (State == CrabState.Dying)
                return true;

            // Уходим в предсмертную сцену вместо мгновенной гибели
            NPC.life = 1;
            NPC.dontTakeDamage = true;
            NPC.velocity.X = 0f;
            NPC.noTileCollide = false;
            EnterState(CrabState.Dying, DyingTicks);
            // Звук, пауза и вспышка последнего удара — OnFinalBlow на клиентах (Cinematics):
            // в сетевой игре CheckDead зовётся на сервере, где звуков нет
            return false;
        }

        #endregion

        #region Урон и слабое место

        // Зона короны: тот же анкер, по которому она рисуется (Rig: CrownOffsetX/Y)
        private Rectangle CrownZone()
        {
            Vector2 seat = FacingToWorld(new Vector2(CrownOffsetX, CrownOffsetY));
            int half = CrownHitboxSize / 2;
            return new Rectangle((int)seat.X - half, (int)seat.Y - half, CrownHitboxSize, CrownHitboxSize);
        }

        // Корона открыта как слабое место с фазы 2 — и только пока король её не придерживает
        private bool CrownExposed => Phase2 && _crownCareTimer <= 0;

        private void ApplyWeakSpot(ref NPC.HitModifiers modifiers, bool hitCrown)
        {
            if (hitCrown && CrownExposed)
                modifiers.FinalDamage *= CrownWeakMult;
            if (_vulnerableTimer > 0)
                modifiers.FinalDamage *= ShellCrackMult; // окно после тяжёлой атаки
        }

        public override void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers)
        {
            Rectangle crown = CrownZone();
            crown.Inflate(CrownMeleeReachX, CrownMeleeReachY); // ближнему бою нужен допуск по замаху
            ApplyWeakSpot(ref modifiers, crown.Intersects(player.Hitbox));
        }

        public override void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            ApplyWeakSpot(ref modifiers, CrownZone().Intersects(projectile.Hitbox));
        }

        public override void OnHitByItem(Player player, Item item, NPC.HitInfo hit, int damageDone)
            => ReactToDamage(damageDone);

        public override void OnHitByProjectile(Projectile projectile, NPC.HitInfo hit, int damageDone)
            => ReactToDamage(damageDone);

        // Реакции характера на полученный урон (реф: «забота о короне», «ярость от урона»)
        private void ReactToDamage(int damageDone)
        {
            if (State == CrabState.Dying)
                return;

            _lastHurtAmount = damageDone; // градацию клипа hurt выбирает анимация

            if (damageDone >= RageDamage)
            {
                _rageTimer = RageTicks;
                // Кольцо, пыль и звук ушли на метку rage_slam: раньше они срабатывали
                // мгновенно — за 16 тиков до того, как клешня доходила до земли
                NPC.netUpdate = true;
            }
            else if (damageDone >= CrownCareDamage && State == CrabState.Scuttle)
            {
                _crownCareTimer = CrownCareTicks;
                NPC.netUpdate = true;
            }
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            _attackConnected = true; // достал — «недовольства» не будет
            // В ярости этот хук не срабатывает вовсе: контактного урона там нет,
            // а значит нет и ванильного столкновения. Толчок к воде делает RageContactCheck
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            // Смерть: туша к этому моменту уже рассыпалась в песок (Cinematics) — кровь неуместна
            if (NPC.life <= 0)
            {
                SpawnDustCloud(NPC.Bottom, 160f, 6, 0.6f);
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
                    Main.rand.NextBool(3) ? DustID.Water : DustID.RedTorch);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 3f, -Main.rand.NextFloat(1f, 5f));
                d.noGravity = Main.rand.NextBool();
                d.scale = Main.rand.NextFloat(1f, 1.8f);
            }
        }

        #endregion

        #region Физика и мелкие хелперы

        // Спуск сквозь настил: тайлы игнорируем, но гравитацию продолжаем считать сами —
        // иначе король повиснет в воздухе (обычная ветка ниже при noTileCollide выходит сразу).
        // Окно короткое и всегда конечное, поэтому провалиться сквозь толщу мира он не может.
        private bool DropThroughTick()
        {
            if (_dropThrough <= 0)
                return false;

            _dropThrough--;
            NPC.noTileCollide = true;
            NPC.velocity.Y = Math.Min(NPC.velocity.Y + Gravity, MaxFallSpeed);
            if (_dropThrough == 0)
                NPC.noTileCollide = false;
            return true;
        }

        // Король выше игрока: сквозь платформу проваливается, с твёрдого уступа — спрыгивает
        // в сторону игрока. Сквозь сплошной грунт не проваливается никогда.
        private void TryDropToTarget(Player target)
        {
            if (_dropThrough > 0 || !Landed())
                return;
            if (target.Bottom.Y - NPC.Bottom.Y < DropDownHeight)
                return;

            int dir = Math.Sign(target.Center.X - NPC.Center.X);
            if (dir == 0)
                dir = NPC.spriteDirection;

            if (StandingOnPlatform())
            {
                _dropThrough = DropThroughTicks;
                NPC.velocity.Y = 1f;
            }
            else
            {
                NPC.velocity.Y = -DropHopLift; // подскок, чтобы сойти с кромки, а не тереться о неё
            }

            NPC.velocity.X = dir * DropHopSpeed;
            NPC.netUpdate = true;
        }

        // Под тушей настил, а не грунт? Пробуем под центром и под обоими краями: хотя бы один
        // сплошной блок — значит стоим на земле, и проваливаться нельзя.
        private bool StandingOnPlatform()
        {
            int ty = (int)((NPC.Bottom.Y + 2f) / 16f);
            bool platform = false;

            for (int i = -1; i <= 1; i++)
            {
                int tx = (int)((NPC.Center.X + i * NPC.width * 0.4f) / 16f);
                Tile tile = Framing.GetTileSafely(tx, ty);
                if (!tile.HasUnactuatedTile)
                    continue;
                if (Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                    return false;
                if (Main.tileSolidTop[tile.TileType])
                    platform = true;
            }
            return platform;
        }

        private void ApplyGravity()
        {
            if (DropThroughTick())
                return;
            if (NPC.noTileCollide)
                return;

            NPC.velocity.Y += Gravity;
            if (NPC.velocity.Y > MaxFallSpeed)
                NPC.velocity.Y = MaxFallSpeed;

            // Упёрся в уступ — подпрыгивает: пошаговой физики (step up) у краба нет
            if (NPC.collideX && NPC.velocity.Y == 0f && Math.Abs(NPC.velocity.X) < 0.5f)
                NPC.velocity.Y = -ObstacleHopSpeed;
        }

        private bool Landed() => NPC.velocity.Y >= 0f && (NPC.collideY || NPC.velocity.Y == 0f);

        private void WalkToward(float targetX, float maxSpeed)
        {
            float dx = targetX - NPC.Center.X;
            if (Math.Abs(dx) < KeepDistance)
            {
                Brake();
                return;
            }

            int dir = Math.Sign(dx);
            NPC.velocity.X = MathHelper.Clamp(NPC.velocity.X + dir * WalkAccel, -maxSpeed, maxSpeed);
        }

        private void Brake()
        {
            NPC.velocity.X *= 0.82f;
            if (Math.Abs(NPC.velocity.X) < 0.1f)
                NPC.velocity.X = 0f;
        }

        // Разворот к цели. Дёргать можно только вне выпадов: смена направления
        // запускает клип «turn» и зеркалит весь риг
        private void FaceTarget(Player target)
        {
            float dx = target.Center.X - NPC.Center.X;
            int dir = Math.Sign(dx);
            if (dir == 0)
                return;
            // Мёртвая зона: игрок над королём или прыгает через его центр — туша не
            // дёргается разворотом туда-обратно каждые несколько тиков
            if (dir != NPC.spriteDirection && NPC.spriteDirection != 0 && Math.Abs(dx) < TurnDeadzone)
                return;
            NPC.direction = dir;
            NPC.spriteDirection = dir;
        }

        // Урон снарядов: масштабирование по сложности берёт на себя tML
        private int ProjDamage(int normalDamage)
            => NPC.GetAttackDamage_ForProjectiles(normalDamage, normalDamage * 0.75f);

        // direction не задан — бьём вверх, как раньше. Для ударов в землю нужен +UnitY,
        // для выпадов и рывков — вдоль движения: направление толчка камеры обязано
        // совпадать с направлением удара, иначе вес не читается.
        private void ScreenPunch(float strength, int duration, Vector2? direction = null)
        {
            if (Main.dedServ)
                return;
            Vector2 dir = direction ?? -Vector2.UnitY;
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                NPC.Center, dir.SafeNormalize(-Vector2.UnitY), strength, 6f, duration, 1200f, FullName));
        }

        // Непрерывный гул: короткие толчки в случайные стороны, подновляемые каждые
        // BurrowRumbleInterval тиков с растущей силой. uniqueId свой, чтобы гул не путался
        // с ударными ScreenPunch. Длительность толчка чуть больше интервала — гул без провалов
        private void ScreenRumble(float strength)
        {
            if (Main.dedServ)
                return;
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                NPC.Center, Main.rand.NextVector2Unit(), strength, 8f,
                BurrowRumbleInterval + 4, 2400f, FullName + "_rumble"));
        }

        #endregion

        #region Неткод

        // ai[0..3] ванилла шлёт сама. Здесь — настроение и окна уязвимости:
        // их читают анимация (клипы) и Vfx (аура ShellCrack) на каждом клиенте
        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((short)_rageTimer);
            writer.Write((short)_sulkTimer);
            writer.Write((short)_crownCareTimer);
            writer.Write((short)_vulnerableTimer);
            writer.Write(_proudPose);
            writer.Write((short)_territoryWarnTimer); // клиенту нужен для пульса виньетки
            writer.Write(_homeX); // клиенту нужен, чтобы знать, в какую сторону вода
            writer.Write(_actionTempo); // Timer и клип атаки идут с этим темпом на всех машинах
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _rageTimer = reader.ReadInt16();
            _sulkTimer = reader.ReadInt16();
            _crownCareTimer = reader.ReadInt16();
            _vulnerableTimer = reader.ReadInt16();
            _proudPose = reader.ReadBoolean();
            _territoryWarnTimer = reader.ReadInt16();
            _homeX = reader.ReadSingle();
            _actionTempo = reader.ReadSingle();
        }

        #endregion
    }
}
