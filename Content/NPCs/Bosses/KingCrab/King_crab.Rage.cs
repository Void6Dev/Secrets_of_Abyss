using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Content.Worldgen;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ==========================================================================================
    //  «ЯРОСТЬ ОКЕАНА» — океан принадлежит королю, и уходить из него нарушителю он не даёт.
    //
    //  Идея: покинув воду, игрок не получает удобную сушу под ноги, а провоцирует древнюю тварь.
    //  Король перестаёт драться «по правилам»: он неуязвим, не тратит обычные атаки и занят
    //  ровно одним — тараном, захватом и броском загнать игрока обратно в свои владения.
    //
    //  Порядок: игрок вышел → предупреждение (RageWarnTicks) → если не вернулся, состояние
    //  OceanRage: разгорание ауры → охота → таран/захват → вернулся в океан → угасание → обычный AI.
    //
    //  Аура — второй проход шейдера SoA:CrabRage по силуэту КАЖДОЙ части (см. DrawRageOverlay),
    //  поэтому светятся панцирь, ноги, клешни и корона одинаково.
    //  Решения о входе и выходе принимает только сервер: биомные флаги удалённых игроков
    //  на чужих клиентах недостоверны.
    // ==========================================================================================
    public partial class King_crab
    {
        // ---------- ТЕРРИТОРИЯ ----------
        private const int RageWarnTicks = 150;             // 2.5 с предупреждения перед вспышкой
        private const float TerritoryGraceRadius = 900f;   // запас вокруг «дома»: ярость не мигает на границе
        private const int TideOceanEdgeTiles = 380;        // ширина океана у края мира (как в TideOfShadowsBiome)

        // ---------- ПЕРЕХОД ----------
        private const int RageIgniteTicks = 45;            // 0.75 с разгорания ауры
        private const int RageCalmTicks = 40;              // и столько же на угасание
        private const float RageIgniteShake = 5f;          // удар камеры в момент вспышки
        private const float RageShake = 2.2f;              // фоновый гул, пока король в ярости
        private const int RageShakeInterval = 8;

        // ---------- ОХОТА ----------
        private const int RageHuntMaxTicks = 90;           // сколько сближается, прежде чем броситься
        private const float RageChaseSpeed = 9.5f;         // скорость преследования
        private const float RageChaseAccel = 0.45f;
        private const float RageFlankOffset = 150f;        // насколько заходит с суши, отрезая путь от океана
        private const float RageChaseDeadzone = 20f;       // мёртвая зона у цели, чтобы не топтался на месте

        // ---------- ОБХОД ПРЫЖКОМ ----------
        // Нужен, когда король ближе к воде, чем игрок: таранить в сторону океана бессмысленно,
        // пока нарушитель НЕ отрезан от воды, поэтому король перепрыгивает его и заходит с суши
        private const int RageFlankMaxTicks = 90;          // предохранитель на прыжок
        private const float RageFlankSpeed = 13f;          // горизонталь обходного прыжка
        private const float RageFlankLaunch = 15f;         // и его высота
        private const float RageFlankSteer = 0.45f;        // подруливание в воздухе

        // ---------- ТАРАН ----------
        private const float RageChargeRange = 700f;        // дальше этого таран не добьёт — сначала сближаемся
        private const int RageChargeWindupTicks = 22;
        private const int RageRamTicks = 42;
        private const float RageRamSpeed = 18f;
        private const float RageRamPush = 17f;             // как далеко отбрасывает тараном
        private const float RageRamLift = 7f;

        // ---------- ЗАХВАТ И БРОСОК ----------
        private const float RageGrabRange = 200f;          // с какой дистанции хватает
        private const int RageGrabHoldTicks = 45;          // сколько держит в клешне
        private const float RageGrabHoldX = 150f;          // где именно держит (от центра тела)
        private const float RageGrabHoldY = -35f;
        private const float RageThrowSpeed = 24f;          // сила броска в сторону океана
        private const float RageThrowLift = 10f;           // и подъём в дуге
        // Без паузы после броска король хватает игрока снова на следующем же тике —
        // тот ещё не успел отлететь из зоны захвата, и бросок гасится повторным захватом
        private const int RageGrabCooldownTicks = 90;

        private const int RageRecoverTicks = 26;
        private const float RageContactPush = 13f;         // отталкивание при обычном касании
        private const int RageContactCooldownTicks = 20;   // чтобы касание не толкало каждый тик

        // Единственный урон всего состояния: король ранит нарушителя ровно один раз — броском.
        // Число фиксированное и по режимам сложности не масштабируется: это не атака, а расплата
        private const int RageThrowDamage = 75;

        // ---------- АУРА ----------
        private const float RageAuraGlowSize = 1.55f;      // диаметр внешнего свечения (доля ширины NPC)
        private const float RageAuraGlowAlpha = 0.5f;
        private const int RageParticleInterval = 3;        // как часто сыплются красные искры

        // ---------- ПОДСТАДИИ ----------
        private const float RageSubIgnite = 0f;   // остановился, аура разгорается
        private const float RageSubHunt = 1f;     // заходит между игроком и сушей
        private const float RageSubCharge = 2f;   // замах тарана
        private const float RageSubRam = 3f;      // сам таран
        private const float RageSubGrab = 4f;     // держит игрока в клешне
        private const float RageSubCalm = 5f;     // аура гаснет, возврат в обычный AI
        private const float RageSubFlank = 6f;    // прыжком заходит за спину, отрезая путь от воды

        // ---------- СОСТОЯНИЕ ----------
        private int _territoryWarnTimer;  // тикает, пока игрок вне владений (только сервер)
        private bool _territoryWarned;    // предупреждение уже показано — не спамим в чат
        private int _rageGrabCooldown;    // пауза после броска, иначе захват зацикливается
        private int _rageContactCooldown; // пауза между толчками, иначе касание толкает каждый тик
        private float _homeX;             // где король вышел в бой: это и есть «его вода»
        private float _rageAura;          // 0..1, косметика: считается на каждом клиенте из State

        private bool InOceanRage => State == CrabState.OceanRage;

        #region Территория

        // Владения короля: океан, Прилив Теней и запас вокруг точки, где начался бой
        private bool InTerritory(Player player)
        {
            if (player.ZoneBeach || player.InModBiome<TideOfShadowsBiome>())
                return true;

            // Прилив Теней — это океан у края мира. Проверяем по мировым данным, а не только
            // по биомному флагу: решение принимает сервер, а модовые флаги игрока туда
            // доезжают не так надёжно, как OceanSide, который сам живёт в мире
            int side = TideOfShadowsWorldData.OceanSide;
            int tileX = (int)(player.Center.X / 16f);
            if ((side == -1 && tileX < TideOceanEdgeTiles)
                || (side == 1 && tileX > Main.maxTilesX - TideOceanEdgeTiles))
                return true;

            return Math.Abs(player.Center.X - _homeX) <= TerritoryGraceRadius;
        }

        // Куда гнать нарушителя: к воде, из которой король вышел
        private int OceanDirection(Player player)
        {
            int dir = Math.Sign(_homeX - player.Center.X);
            return dir == 0 ? -NPC.spriteDirection : dir;
        }

        // Сторож территории. Крутится поверх обычного AI и не трогает его состояния:
        // либо копит предупреждение, либо переводит короля в OceanRage
        private void UpdateTerritoryWatch(Player target)
        {
            if (_homeX == 0f)
                _homeX = NPC.Center.X; // первая же мысль короля — где его вода

            // Решает только сервер: у клиентов нет достоверных биомных флагов чужих игроков.
            // Клиенты лишь докручивают присланный счётчик, чтобы пульс виньетки нарастал
            // плавно, а не рывками между синхронизациями.
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                if (_territoryWarnTimer > 0 && _territoryWarnTimer < RageWarnTicks)
                    _territoryWarnTimer++;
                return;
            }
            if (InOceanRage || State == CrabState.Dying)
                return;

            // Из-под земли король владения не сторожит. Раньше ярость входила прямо посреди
            // подкопа или боя свиты (до 90 с на глубине): BeginOceanRage возвращал столкновения
            // туше в толще грунта, и король там застревал, а бой со свитой обрывался.
            // Выберется наружу — начнёт отсчёт заново, с предупреждением
            if (PassingThroughTiles || InTerritory(target))
            {
                ResetTerritoryWarning();
                return;
            }

            _territoryWarnTimer++;

            if (!_territoryWarned)
            {
                _territoryWarned = true;
                AnnounceRageWarning();
                NPC.netUpdate = true;
            }

            if (_territoryWarnTimer >= RageWarnTicks)
                BeginOceanRage();
        }

        private void ResetTerritoryWarning()
        {
            // Сброс обязан уехать по сети: иначе у клиента счётчик так и останется
            // ненулевым и виньетка будет пульсировать до конца боя
            if (_territoryWarnTimer > 0)
                NPC.netUpdate = true;
            _territoryWarnTimer = 0;
            _territoryWarned = false;
        }

        private void BeginOceanRage()
        {
            _territoryWarnTimer = 0;
            _territoryWarned = false;

            // Под землёй сюда не попадаем (см. UpdateTerritoryWatch), так что столкновения
            // уже на месте; строка — страховка на случай нового стейта, забывшего о них
            NPC.noTileCollide = false;
            EnterState(CrabState.OceanRage, RageIgniteTicks, RageSubIgnite);
        }

        private static void AnnounceRageWarning()
        {
            LocalizedText text = Language.GetText("Mods.SoA.Misc.KingCrabRageWarning");
            Color color = new Color(255, 80, 60);

            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromKey(text.Key), color);
            else
                Main.NewText(text.Value, color);
        }

        #endregion

        #region Ярость: конечный автомат

        private void AIOceanRage(Player target)
        {
            if (_rageGrabCooldown > 0)
                _rageGrabCooldown--;

            // Вернулся в воду — гасим ярость с любой стадии, кроме уже начатого угасания
            if (SubState != RageSubCalm && Main.netMode != NetmodeID.MultiplayerClient
                && InTerritory(target))
            {
                ReleasePlayer(target);
                EnterSubState(RageSubCalm, RageCalmTicks);
                SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.8f }, NPC.Center);
                return;
            }

            RageAmbience();
            RageContactCheck(target);

            switch (SubState)
            {
                case RageSubIgnite: RageIgnite(); break;
                case RageSubHunt: RageHunt(target); break;
                case RageSubCharge: RageCharge(target); break;
                case RageSubRam: RageRam(target); break;
                case RageSubGrab: RageGrab(target); break;
                case RageSubFlank: RageFlank(target); break;
                default: RageCalm(); break;
            }
        }

        // 1. Вспышка: король встаёт как вкопанный, туша наливается энергией
        private void RageIgnite()
        {
            ApplyGravity();
            Brake();

            if (Timer % 2f == 0f)
                SpawnRageSparks(14);
            ScreenRumble(RageShake * 1.4f);

            // Разряды по ходу разгорания — «энергетические вспышки»
            if (Timer % 12f == 0f)
            {
                TriggerImpactRing(NPC.Center, 220f + 120f * _rageAura, 18f, 1f);
                SoundEngine.PlaySound(SoundID.Item122 with { Pitch = -0.4f, Volume = 0.6f }, NPC.Center);
            }

            if (Timer > 0f)
                return;

            SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.5f }, NPC.Center);
            TriggerImpactRing(NPC.Center, 460f, 30f, 1f);
            ScreenPunch(RageIgniteShake, 22);
            SpawnRageSparks(45);
            EnterSubState(RageSubHunt, RageHuntMaxTicks);
        }

        // 2. Охота: заходит со стороны суши, отрезая нарушителю путь от океана
        private void RageHunt(Player target)
        {
            ApplyGravity();

            int oceanDir = OceanDirection(target);
            // Встаём НЕ на игрока, а за него: тогда таран идёт точно в сторону воды
            float anchorX = target.Center.X - oceanDir * RageFlankOffset;
            RageChase(anchorX);

            FaceTarget(target);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Дотянулся — хватает. Сразу после броска захват заперт кулдауном,
            // иначе король выдернет игрока обратно, не дав тому отлететь
            if (_rageGrabCooldown <= 0 && NPC.Distance(target.Center) < RageGrabRange)
            {
                EnterSubState(RageSubGrab, RageGrabHoldTicks);
                SoundEngine.PlaySound(SoundID.Item17 with { Pitch = -0.7f }, NPC.Center);
                return;
            }

            // Таранить имеет смысл, ТОЛЬКО когда нарушитель отрезан от воды: он должен быть
            // между королём и океаном. Иначе таран в сторону моря уходит прочь от игрока
            bool cutOff = Math.Sign(target.Center.X - NPC.Center.X) == oceanDir;
            float distX = Math.Abs(target.Center.X - NPC.Center.X);

            if (cutOff && distX < RageChargeRange)
            {
                EnterSubState(RageSubCharge, RageChargeWindupTicks);
                return;
            }

            // Зайти по земле не вышло — перепрыгиваем игрока и отрезаем его от воды
            if (Timer <= 0f)
                BeginFlankLeap(target);
        }

        // 2б. Обходной прыжок: тяжёлый скачок за спину игрока, на сторону суши
        private void BeginFlankLeap(Player target)
        {
            int oceanDir = OceanDirection(target);
            float anchorX = target.Center.X - oceanDir * RageFlankOffset;
            int leapDir = Math.Sign(anchorX - NPC.Center.X);
            if (leapDir == 0)
                leapDir = -oceanDir;

            NPC.velocity.X = leapDir * RageFlankSpeed;
            NPC.velocity.Y = -RageFlankLaunch;
            NPC.direction = NPC.spriteDirection = leapDir;

            SoundEngine.PlaySound(SoundID.Item45 with { Pitch = -0.7f }, NPC.Center);
            SpawnSandBurst(NPC.Bottom - new Vector2(80f, 8f), 160, 12, 16, 4f, 2f, 7f);
            SpawnRageSparks(18);
            EnterSubState(RageSubFlank, RageFlankMaxTicks);
        }

        private void RageFlank(Player target)
        {
            ApplyGravity();

            int oceanDir = OceanDirection(target);
            float anchorX = target.Center.X - oceanDir * RageFlankOffset;

            // В воздухе подруливаем к точке за игроком, но мягко — прыжок должен читаться
            int steer = Math.Sign(anchorX - NPC.Center.X);
            NPC.velocity.X = MathHelper.Clamp(NPC.velocity.X + steer * RageFlankSteer,
                -RageFlankSpeed, RageFlankSpeed);

            SpawnRageSparks(2);

            if (!Landed() && Timer > 0f)
                return;

            TriggerImpactRing(NPC.Bottom, 280f, 24f, 1f);
            SpawnSandBurst(NPC.Bottom - new Vector2(90f, 8f), 180, 12, 22, 4f, 2f, 8f);
            ScreenPunch(5f, 16);
            EnterSubState(RageSubHunt, RageHuntMaxTicks);
        }

        // 3. Замах тарана: приседает, разворачивается в сторону воды
        private void RageCharge(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            if (Timer % 3f == 0f)
                SpawnRageSparks(6);
            if (Timer % 4f == 0f)
                SpawnSandBurst(NPC.Bottom - new Vector2(90f, 6f), 180, 10, 4, 3f, 1f, 4f);

            if (Timer > 0f)
                return;

            int oceanDir = OceanDirection(target);
            NPC.velocity.X = oceanDir * RageRamSpeed;
            NPC.velocity.Y = -4f;
            NPC.direction = NPC.spriteDirection = oceanDir;
            SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.2f }, NPC.Center);
            EnterSubState(RageSubRam, RageRamTicks);
        }

        // 4. Таран: идёт тяжёлым тараном к воде, сшибая всё на пути
        private void RageRam(Player target)
        {
            ApplyGravity();

            int oceanDir = OceanDirection(target);
            // Держим ход: гравитация и трение не должны съедать разгон
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, oceanDir * RageRamSpeed, 0.15f);

            SpawnRageSparks(3);
            SpawnSandBurst(NPC.Bottom - new Vector2(50f, 8f), 100, 10, 3, 3f, 1f, 5f);
            ScreenRumble(RageShake * 1.2f);

            bool wall = NPC.collideX;
            if (wall)
            {
                TriggerImpactRing(NPC.Center, 300f, 24f, 1f);
                ScreenPunch(6f, 18, new Vector2(-NPC.spriteDirection, 0f));
                HitStop(6);
                NPC.velocity.X = 0f;
            }

            if (!wall && Timer > 0f)
                return;

            EnterSubState(RageSubHunt, RageRecoverTicks + RageHuntMaxTicks);
        }

        // 5. Захват: держит нарушителя в клешне и швыряет в сторону океана
        private void RageGrab(Player target)
        {
            ApplyGravity();
            Brake();

            // Контактный урон в ярости обнулён целиком (см. UpdateStats), отдельно снимать нечего

            Vector2 grip = FacingToWorld(new Vector2(RageGrabHoldX, RageGrabHoldY));
            HoldPlayer(target, grip);

            if (Timer % RageParticleInterval == 0f)
                SpawnRageSparks(5, grip);
            ScreenRumble(RageShake);

            if (Timer > 0f)
                return;

            // Бросок: по дуге в сторону воды. Кулдаун обязателен — без него охота
            // подхватывает ещё не улетевшего игрока и захват зацикливается
            int oceanDir = OceanDirection(target);
            _rageGrabCooldown = RageGrabCooldownTicks;
            ShovePlayer(target, new Vector2(oceanDir * RageThrowSpeed, -RageThrowLift));
            HurtPlayerFlat(target, RageThrowDamage, oceanDir); // единственный урон всей ярости

            TriggerImpactRing(grip, 260f, 22f, 1f);
            ScreenPunch(7f, 20, new Vector2(oceanDir, -0.4f));
            SpawnRageSparks(30, grip);
            SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.6f }, NPC.Center);
            if (!Main.dedServ)
            {
                PlayClip("rage_throw", once: true); // раньше игрок улетал, а краб стоял в позе захвата
                HitStop(8);                          // кульминация — она заслуживает паузы
            }
            EnterSubState(RageSubHunt, RageRecoverTicks + RageHuntMaxTicks);
        }

        // 6. Угасание: энергия сходит, король снова уязвим и дерётся как обычно
        private void RageCalm()
        {
            ApplyGravity();
            Brake();

            if (Timer % 4f == 0f)
                SpawnRageSparks(4);

            if (Timer <= 0f)
                ReturnToScuttle();
        }

        // Сближение в ярости: разгон плавный и тяжёлый — король наваливается, а не дёргается
        private void RageChase(float targetX)
        {
            float dx = targetX - NPC.Center.X;
            if (Math.Abs(dx) < RageChaseDeadzone)
            {
                Brake();
                return;
            }

            int dir = Math.Sign(dx);
            NPC.velocity.X = MathHelper.Clamp(NPC.velocity.X + dir * RageChaseAccel,
                -RageChaseSpeed, RageChaseSpeed);
        }

        // Фоновые эффекты ярости: гул камеры и красные искры с туши
        private void RageAmbience()
        {
            if (SubState == RageSubCalm)
                return;

            if (Main.GameUpdateCount % RageShakeInterval == 0)
                ScreenRumble(RageShake);
            if (Main.GameUpdateCount % (ulong)RageParticleInterval == 0)
                SpawnRageSparks(2);
        }

        #endregion

        #region Работа с игроком

        // Физику игрока считает только его собственный клиент — на остальных машинах
        // менять его скорость бесполезно и вредно (рывки и рассинхрон)
        private static void ShovePlayer(Player player, Vector2 velocity)
        {
            if (player.whoAmI != Main.myPlayer)
                return;
            player.velocity = velocity;
        }

        private static void HoldPlayer(Player player, Vector2 worldPos)
        {
            if (player.whoAmI != Main.myPlayer)
                return;
            player.Center = worldPos;
            player.velocity = Vector2.Zero;
            player.fallStart = (int)(player.position.Y / 16f); // чтобы после броска не убиться об падение
        }

        // Отпускаем, если ярость кончилась прямо во время захвата
        private void ReleasePlayer(Player player)
        {
            if (SubState != RageSubGrab)
                return;
            _rageGrabCooldown = RageGrabCooldownTicks;
            ShovePlayer(player, new Vector2(OceanDirection(player) * RageThrowSpeed * 0.5f, -RageThrowLift));
        }

        // Толчок при касании туши в ярости — всегда в сторону воды
        private void RageShoveOnContact(Player target)
        {
            int oceanDir = OceanDirection(target);
            float push = SubState == RageSubRam ? RageRamPush : RageContactPush;
            float lift = SubState == RageSubRam ? RageRamLift : RageRamLift * 0.6f;
            ShovePlayer(target, new Vector2(oceanDir * push, -lift));
        }

        // Столкновение считаем САМИ. В ярости NPC.damage == 0, а ваниль вообще не проверяет
        // касание таких NPC (Player.Update отбрасывает damage <= 0), поэтому ни OnHitPlayer,
        // ни толчок к воде без этой проверки не сработали бы — а толчок и есть смысл состояния.
        private void RageContactCheck(Player target)
        {
            if (_rageContactCooldown > 0)
            {
                _rageContactCooldown--;
                return;
            }
            // В захвате игрок и так притянут к клешне, толкать его оттуда нечем
            if (SubState is RageSubGrab or RageSubCalm || !NPC.Hitbox.Intersects(target.Hitbox))
                return;

            _rageContactCooldown = RageContactCooldownTicks;
            RageShoveOnContact(target);

            // Урона нет, поэтому у толчка обязана быть своя отдача — иначе он читается как баг
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.4f, Volume = 0.5f }, NPC.Center);
            SpawnRageSparks(12, target.Center);
            ScreenPunch(2.5f, 10, new Vector2(OceanDirection(target), -0.3f));
        }

        // Фиксированный урон игроку. Наносим только на его собственной машине — как и толчок:
        // на остальных клиентах чужое здоровье менять бесполезно и вредно.
        // Урон не масштабируется по режиму сложности: это одно конкретное число.
        private void HurtPlayerFlat(Player player, int damage, int hitDirection)
        {
            if (player.whoAmI != Main.myPlayer || player.dead)
                return;
            player.Hurt(PlayerDeathReason.ByNPC(NPC.whoAmI), damage, hitDirection);
        }

        #endregion

        #region Визуал

        // Красные искры с туши. worldPos не задан — сыплем по всему габариту босса
        private void SpawnRageSparks(int count, Vector2? worldPos = null)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            for (int i = 0; i < count; i++)
            {
                Dust d = worldPos.HasValue
                    ? Dust.NewDustPerfect(worldPos.Value + Main.rand.NextVector2Circular(30f, 30f),
                        Main.rand.NextBool(3) ? DustID.LifeDrain : DustID.RedTorch)
                    : Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
                        Main.rand.NextBool(3) ? DustID.LifeDrain : DustID.RedTorch);

                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 2.5f, -Main.rand.NextFloat(1f, 5f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1.1f, 2f);
            }
        }

        // Плавный разгон и спад ауры: цель диктует стадия, скорость — константы перехода.
        // Считается на каждом клиенте из синхронизированных State/SubState, по сети не шлём
        private void UpdateRageAura()
        {
            bool burning = InOceanRage && SubState != RageSubCalm;
            float step = burning ? 1f / RageIgniteTicks : -1f / RageCalmTicks;
            _rageAura = MathHelper.Clamp(_rageAura + step, 0f, 1f);
        }

        // Второй проход поверх уже нарисованного короля: сначала внешнее свечение вокруг
        // ключевых узлов, затем шейдер по силуэту всех частей разом
        private void DrawRageOverlay(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (_rageAura <= 0.01f || BurrowBuried)
                return;

            float pulse = 0.8f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.2f);
            float intensity = _rageAura * pulse;

            // Внешняя аура: корпус, обе клешни и грунт под лапами
            SoAVfx.BeginAdditive(spriteBatch);
            Color glow = new Color(255, 45, 25) * (RageAuraGlowAlpha * intensity);
            glow.A = 0;
            float size = NPC.width * RageAuraGlowSize;

            SoAVfx.DrawTintedGlow(spriteBatch, AnimatedBodyCenter(), new Vector2(size), glow);
            SoAVfx.DrawTintedGlow(spriteBatch, FacingToWorld(new Vector2(ClawShoulderX, ClawShoulderY)),
                new Vector2(size * 0.5f), glow);
            SoAVfx.DrawTintedGlow(spriteBatch, FacingToWorld(new Vector2(-ClawShoulderX, ClawShoulderY)),
                new Vector2(size * 0.5f), glow);
            SoAVfx.DrawTintedGlow(spriteBatch, NPC.Bottom, new Vector2(size * 0.7f, size * 0.35f), glow);
            SoAVfx.EndAdditive(spriteBatch);

            // Энергия по силуэту: один Apply на батч — все части горят одинаково
            SoAVfx.BeginRageOverlay(spriteBatch, intensity);
            DrawLegs(spriteBatch, screenPos, Color.White);
            DrawBody(spriteBatch, screenPos, Color.White);
            DrawClawBack(spriteBatch, screenPos, Color.White);
            DrawClawFront(spriteBatch, screenPos, Color.White);
            DrawCrown(spriteBatch, screenPos, Color.White);
            SoAVfx.EndAdditive(spriteBatch);
        }

        #endregion
    }
}
