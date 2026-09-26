using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Animation;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Рантайм кейфреймовой анимации Короля-краба. Сами клипы — в King_crab.Clips.cs.
    //
    // Здесь живёт всё, что клипами не выражается:
    //   • выбор базового клипа по стейту И ПОДСТАДИИ (recover-стадии больше не держат
    //     замороженный кадр);
    //   • hit-stop как ПАУЗА ПРОИГРЫВАНИЯ (NPC.velocity и Timer не трогаются — иначе
    //     рассинхрон позиции в мультиплеере);
    //   • диспетчер меток клипов: звук/пыль/тряска ровно в кадр удара;
    //   • вторичная физика — пружина короны, крен тела от ускорения, отставание запястий;
    //   • дыхание с периодами, не совпадающими ни с одним клипом;
    //   • цвет глаз от типа угрозы и взгляд, следящий за игроком.
    //
    // Чистая косметика: тикается в PreDraw на клиентах из синхронизированного состояния,
    // по сети ничего не шлёт и ai[] не тратит.
    public partial class King_crab
    {
        private const string LayerBody = "body";            // Aux = поза squash & stretch (-1..1)
        private const string LayerCrown = "crown";          // Aux = добавка свечения
        private const string LayerClawFront = "claw_front";  // активная клешня; Aux = раскрытие пинцера
        private const string LayerClawBack = "claw_back";
        private const string LayerLegs = "legs";            // Offset = сдвиг стойки; Aux = ширина; Aux2 = стоп шагов
        private const string LayerEyes = "eyes";            // Aux = сила свечения (0..1)

        private const float AnimIntensity = 1f;   // общий множитель силы всех клипов (крути тут)
        private const int HurtClipCooldown = 20;  // не дёргаем «hurt» чаще, чем раз в треть секунды
        private const float EyeGlowSize = 46f;    // диаметр глоу глаз (px мира до NPC.scale)
        // Итоговая поза догоняет цель с этой долей за тик: гасит скачки на стыках клипов,
        // которые не закрывает кроссфейд. 1 — выключено; меньше 0.4 — вялые удары
        private const float PoseSmoothing = 0.55f;

        // ---------- РАЗВОРОТ ----------
        // Разворот зеркалит весь риг за один тик. Чтобы это читалось как поворот туши,
        // а не как подмена картинки, силуэт после смены стороны «раскрывается» из узкого
        private const int TurnUnfoldTicks = 9;
        private const float TurnSquashMin = 0.3f;

        // ---------- HIT-STOP ----------
        private const int HitStopCap = 10;        // предохранитель: дольше поза стоять не должна

        // ---------- ВТОРИЧНАЯ ФИЗИКА ----------
        private const float CrownSpringK = 0.28f;      // жёсткость пружины короны
        private const float CrownSpringDamp = 0.22f;   // и её демпфирование
        private const float CrownSpringDrive = 0.02f;  // вклад горизонтального ускорения тела
        private const float CrownSpringMax = 0.3f;     // предел отклонения (рад)
        private const float CrownSpeedSway = 0.06f;    // доп. мотание короны от скорости хода
        private const float BodyLeanPerAccel = 0.012f; // крен тела на единицу ускорения
        private const float BodyLeanMax = 0.09f;
        private const float ClawLagRate = 0.42f;       // скорость догона запястьем целевой точки
        private const int ClawShakeTicks = 6;          // дрожь клешни после тяжёлого удара
        private const float ClawShakeAmp = 2.5f;
        private const int HurtCrownTiltTicks = 20;     // сбитая корона выправляется не сразу
        private const float HurtCrownTiltAmount = 0.35f;

        // ---------- ДЫХАНИЕ ----------
        // Периоды нарочно взаимно непериодичные и не равные длине ни одного клипа:
        // рисунок движения не повторяется точно, и стойка перестаёт читаться циклом.
        private const float BreathBodyPeriod = 77f;
        private const float BreathClawPeriod = 76f;
        private const float BreathCrownPeriod = 113f;
        private const float BreathBodyAmp = 0.008f;    // сознательно незаметно, подсознательно заметно

        // ---------- НЕРЕГУЛЯРНЫЕ ЖЕСТЫ ----------
        private const int IdleTwitchMin = 150;
        private const int IdleTwitchMax = 300;
        private const int ProudFlourishMin = 200;
        private const int ProudFlourishMax = 300;

        // ---------- ПОРОГИ ВЫБОРА КЛИПА ----------
        private const float WalkClipEnterSpeed = 0.75f; // гистерезис: на одном пороге клип дёргался
        private const float WalkClipExitSpeed = 0.3f;   // туда-обратно каждые пару тиков

        private AnimPlayer _anim;
        private int _lastLife;
        private int _lastSpriteDir;
        private int _prevRageTimer;
        private int _hurtClipCooldown;
        private int _lastHurtAmount;   // сколько сняли последним попаданием — выбор градации hurt
        private int _animHitStop;      // тиков, на которые поза замерла
        private int _turnUnfold;       // тиков осталось раскрываться после разворота

        // Ключ стадии: пока он не меняется, Play() на том же имени — no-op (и это правильно).
        // Как только меняется — клип перезапускается, даже если имя то же (второй вал TideCall).
        private int _stageKey;
        private bool _walkClipActive;

        private float _eyesGlow;       // итог тика: база от фазы/ярости + канал Aux слоя eyes
        private Color _eyesColor = new(255, 75, 30);
        private Vector2 _eyesLook;

        private float _crownSpring;
        private float _crownSpringVel;
        private float _bodyLean;
        private float _prevVelocityX;
        private float _bodyAccelX;
        private readonly Vector2[] _clawLag = new Vector2[2];
        private int _clawShake;
        private int _hurtCrownTilt;

        private float _breathBody;
        private float _breathClaw;
        private float _breathCrown;
        private float _calmness;       // 1 — король ничем не занят, 0 — в атаке

        private int _idleTwitchTimer;
        private int _proudFlourishTimer;
        private int _p2QuakeStep;      // номер толчка в тряске перехода: гул нарастает
        private int _lastKnightCount;  // заметил ли король потерю гвардии
        private float _phase2Aura;     // 0..1: аура фазы 2 разгорается, а не вспыхивает скачком
        private float _crownGaze;      // доворот корпуса к упавшей короне
        private float _prevSquash;     // для скрипа панциря на резких сменах деформации
        private int _creakCooldown;

        // --- Публичное API для боевой логики ---

        // Ручной запуск клипа: once = реакция поверх базы, иначе смена базового клипа
        public void PlayClip(string name, bool once = false)
        {
            _anim ??= BuildAnimPlayer();
            if (once)
                _anim.PlayOnce(name);
            else
                _anim.Play(name);
        }

        // Полл метки времени из клипа. Основной путь теперь — колбэк OnAnimEvent,
        // полл остался для боевых мест, которым удобнее спросить самим.
        public bool ConsumeAnimEvent(string name) => _anim?.ConsumeEvent(name) ?? false;

        // Заморозка позы на N тиков. Только скорость проигрывания анимации на клиенте:
        // детерминирована от синхронизированного Timer, SendExtraAI трогать не нужно.
        private void HitStop(int ticks)
        {
            if (Main.dedServ)
                return;
            _animHitStop = Math.Min(HitStopCap, Math.Max(_animHitStop, ticks));
        }

        // --- Тик анимации (первым делом в PreDraw) ---

        private void UpdateAnimation()
        {
            _anim ??= BuildAnimPlayer();
            if (Main.gamePaused)
                return;

            UpdateAnimTimers();
            TriggerAnimReactions();

            // Hit-stop: поза стоит, но всё остальное (таймеры, пружины, частицы) продолжает жить
            if (_animHitStop > 0)
            {
                _animHitStop--;
            }
            else
            {
                string clip = DesiredBaseClip();
                bool stageChanged = RefreshStageKey();
                _anim.Play(clip, restart: stageChanged, fade: ClipFade(clip));
                _anim.Update(ActionTempo); // клип атаки идёт в темпе её Timer'а — удар в кадр удара
            }

            UpdateBreathing();
            UpdateBodyLean();
            UpdateCrownSpring();
            UpdateEyes();
            UpdateTelegraphHum();
        }

        // Низкочастотный гул под КАЖДЫМ телеграфом длиннее 40 тиков. Тишина перед долгим
        // замахом — упущенная возможность: гул подсознательно сообщает «сейчас будет тяжело».
        private void UpdateTelegraphHum()
        {
            if (Main.dedServ || Main.GameUpdateCount % 14 != 0)
                return;

            bool longTelegraph = State switch
            {
                CrabState.ClawSweep => SubState == 0f,
                CrabState.CrushingGrip => SubState == 0f,
                CrabState.TideCall => true,
                CrabState.TsunamiClap => SubState == 0f,
                CrabState.RoyalRoar => true,
                CrabState.CrownCommand => true,
                CrabState.ClawSlam => SubState == 0f,
                CrabState.Burrow => SubState == BurrowSubPrep,
                _ => false,
            };
            if (!longTelegraph)
                return;

            // Громкость растёт к развязке: гул сам работает индикатором прогресса
            float total = AttackDuration(State);
            float progress = MathHelper.Clamp(1f - Timer / Math.Max(1f, total), 0f, 1f);
            SoundEngine.PlaySound(SoundID.Item21 with
            {
                Volume = 0.1f + 0.22f * progress,
                Pitch = -1f,
                MaxInstances = 0,
            }, NPC.Center);
        }

        // Тинт, виньетка, десатурация — полноэкранных фильтров, меняющих яркость кадра, у босса
        // НЕТ намеренно: бой должен идти на неизменно светлом экране. Единственное исключение —
        // волна-преломление RoarShockwaveFx на самых тяжёлых ударах (ScreenWave в
        // King_crab.Particles.cs): она только сдвигает пиксели кадра, не затемняя его.

        private void UpdateAnimTimers()
        {
            if (_hurtClipCooldown > 0) _hurtClipCooldown--;
            if (_turnUnfold > 0) _turnUnfold--;
            _stepVisualLift *= StepVisualDecay;
            if (_clawShake > 0) _clawShake--;
            if (_hurtCrownTilt > 0) _hurtCrownTilt--;
            if (_idleTwitchTimer > 0) _idleTwitchTimer--;
            if (_proudFlourishTimer > 0) _proudFlourishTimer--;

            if (_creakCooldown > 0) _creakCooldown--;

            _bodyAccelX = NPC.velocity.X - _prevVelocityX;
            _prevVelocityX = NPC.velocity.X;

            // Аура фазы 2 разгорается 40 тиков: раньше Phase2 переключался мгновенно по HP,
            // и аура вспыхивала скачком посреди боя
            _phase2Aura = MathHelper.Clamp(_phase2Aura + (Phase2 ? 1f / 40f : -1f / 40f), 0f, 1f);

            // Король следит за своей короной: пока она лежит на песке, корпус доворачивается к ней
            float gazeTarget = 0f;
            if (_crownMode == CrownMode.Landed)
                gazeTarget = MathHelper.Clamp((_crownPos.X - NPC.Center.X) / 400f, -1f, 1f) * 0.05f;
            _crownGaze = MathHelper.Lerp(_crownGaze, gazeTarget, 0.05f);

            // Скрип панциря на резкой смене деформации — редкий и тихий
            float squashDelta = Math.Abs(_bodySquash - _prevSquash);
            _prevSquash = _bodySquash;
            if (squashDelta > 0.12f && _creakCooldown <= 0 && !Main.dedServ && Main.rand.NextBool(3))
            {
                _creakCooldown = 45;
                SoundEngine.PlaySound(SoundID.Item50 with { Volume = 0.18f, Pitch = -0.9f }, NPC.Center);
            }

            // Замечает потерю гвардии: короткая реакция боли, когда рыцарей стало меньше
            int knights = KnightsAlive();
            if (knights < _lastKnightCount && State != CrabState.Dying)
                _anim.PlayOnce("hurt_light");
            _lastKnightCount = knights;

            // «Спокойствие» гасит идловую мелочёвку в атаках: дыхание клешней и короны
            bool calm = State == CrabState.Scuttle && _rageTimer <= 0;
            _calmness = MathHelper.Lerp(_calmness, calm ? 1f : 0f, 0.08f);
        }

        // Реакции — детерминированно из синхронизированного состояния, без своего неткода
        private void TriggerAnimReactions()
        {
            if (_lastSpriteDir != 0 && NPC.spriteDirection != _lastSpriteDir && State != CrabState.Dying)
                OnTurnAround();
            _lastSpriteDir = NPC.spriteDirection;

            // phase2_transition теперь БАЗОВЫЙ клип стейта Phase2Transition, а не реакция
            // поверх боевого: бой на это время встаёт на паузу (см. BeginPhase2Transition)

            if (_rageTimer > 0 && _prevRageTimer <= 0)
                _anim.PlayOnce("rage");
            _prevRageTimer = _rageTimer;

            // Градация урона: попадание на 5 и на 44 больше не выглядят одинаково
            if (_lastLife > 0 && NPC.life < _lastLife && _hurtClipCooldown <= 0
                && State != CrabState.Dying && _rageTimer <= 0)
            {
                int taken = Math.Max(_lastHurtAmount, _lastLife - NPC.life);
                if (taken >= CrownCareDamage)
                {
                    _anim.PlayOnce("hurt_heavy");
                    _hurtCrownTilt = HurtCrownTiltTicks;
                    _clawShake = ClawShakeTicks;
                }
                else if (taken >= CrownCareDamage / 3)
                {
                    _anim.PlayOnce("hurt");
                }
                else
                {
                    _anim.PlayOnce("hurt_light");
                }
                _hurtClipCooldown = HurtClipCooldown;
            }
            _lastLife = NPC.life;
            _lastHurtAmount = 0;

            // Нерегулярные жесты: щелчок пинцера в покое и «королевский флориш» в гордой позе.
            // Именно нерегулярность — иначе оба читаются циклом.
            if (_calmness > 0.6f && Math.Abs(NPC.velocity.X) < 0.5f)
            {
                if (_idleTwitchTimer <= 0)
                {
                    _idleTwitchTimer = Main.rand.Next(IdleTwitchMin, IdleTwitchMax);
                    if (!_proudPose)
                        _anim.PlayOnce("idle_twitch");
                }
                if (_proudPose && _proudFlourishTimer <= 0)
                {
                    _proudFlourishTimer = Main.rand.Next(ProudFlourishMin, ProudFlourishMax);
                    _anim.PlayOnce("proud_flourish");
                }
            }
        }

        // Ключ стадии для рестарта клипа. StateData включаем ТОЛЬКО для TideCall: там он
        // отмечает второй вал в агонии. В BubbleVolley тот же слот считает выстрелы —
        // клип рестартовал бы на каждом.
        private bool RefreshStageKey()
        {
            int extra = State == CrabState.TideCall ? (int)StateData : 0;
            int key = ((int)State * 397 + (int)(SubState * 4f)) * 31 + extra;
            if (key == _stageKey)
                return false;
            _stageKey = key;
            return true;
        }

        // Длина кроссфейда по ПАРЕ (откуда → куда): после тяжёлого удара поза отпускает
        // медленно, продолжение удара блендить почти не нужно, быстрый выпад — резко.
        // Самые короткие фейды (3 тика) читались рывком — нижняя граница поднята; вес
        // выпадам теперь дают hit-stop и сглаживание позы, а не обрыв перехода
        private float ClipFade(string to)
        {
            if (IsRecoverClip(to))
                return 6f;
            if (to is "sweep_dash" or "grip_lunge" or "burrow_erupt")
                return 5f;
            if (to is "jump_rise" or "jump_apex" or "jump_fall" or "burrow_dive" or "burrow_swim")
                return 9f;

            string from = _anim.CurrentClip;
            if (IsRecoverClip(from))
                return 22f;
            if (from is "sweep_dash" or "grip_lunge")
                return 9f;
            return 14f;
        }

        private static bool IsRecoverClip(string name) => name
            is "slam_recover" or "grip_recover" or "clap_recover" or "jump_land" or "roar_release";

        // Маппинг стейта AI И ПОДСТАДИИ → клип. Каждая recover-стадия получила свой клип:
        // раньше на них держался последний ключ атакующего клипа (до 30 тиков статуи).
        private string DesiredBaseClip()
        {
            switch (State)
            {
                case CrabState.Dying:
                    return "death";
                case CrabState.Intro:
                    // Под песком гребёт, наружу — раскрытием и фазами полёта, приземлился — рёв
                    if (SubState < IntroSubErupt)
                        return "burrow_swim";
                    if (SubState < IntroSubRoar)
                        return BurrowMaxAir - Timer < BurrowEruptClipTicks ? "burrow_erupt" : AirborneClip();
                    return "roar";
                case CrabState.Phase2Transition:
                    return "phase2_transition";
                case CrabState.ClawSlam:
                    return SubState == 0f ? "claw_slam" : "slam_recover";
                case CrabState.ClawSweep:
                    return SubState == 0f ? "sweep_windup" : "sweep_dash";
                case CrabState.BubbleVolley:
                    return "bubble_volley";
                case CrabState.TideCall:
                    return "tide_call";
                case CrabState.JumpCrush:
                    return SubState == 0f ? "jump_crouch"
                        : SubState == 1f ? AirborneClip() : "jump_land";
                case CrabState.CrushingGrip:
                    return SubState == 0f ? "grip_windup"
                        : SubState == 1f ? "grip_lunge" : "grip_recover";
                case CrabState.RoyalRoar:
                    return "roar";
                case CrabState.CrownCommand:
                    return "crown_command";
                case CrabState.TsunamiClap:
                    return SubState == 0f ? "tsunami_clap" : "clap_recover";
                case CrabState.Burrow:
                    return SubState switch
                    {
                        BurrowSubPrep => "jump_crouch",
                        BurrowSubSink => "burrow_dive",
                        BurrowSubTravel or BurrowSubWarn => "burrow_swim",
                        // Вылет: сначала раскрытие, дальше обычные фазы полёта
                        _ => BurrowMaxAir - Timer < BurrowEruptClipTicks ? "burrow_erupt" : AirborneClip(),
                    };
                case CrabState.KnightCourt:
                case CrabState.CourtDuel:
                    return SubState == 0f ? "burrow_dive"
                        : SubState < 2f ? "burrow_swim"
                        : KnightCourtEruptElapsed() < BurrowEruptClipTicks ? "burrow_erupt" : AirborneClip();
                case CrabState.OceanRage:
                    return SubState switch
                    {
                        RageSubIgnite => "rage_ignite",
                        RageSubCharge => "sweep_windup",  // скрутился пружиной перед тараном
                        RageSubRam => "sweep_dash",       // лёг в таран
                        RageSubGrab => "rage_grab",
                        RageSubFlank => AirborneClip(),
                        RageSubCalm => "idle",
                        _ => "rage_hunt",
                    };
                case CrabState.Scuttle:
                    if (_sulkTimer > 0)
                        return "displeased";
                    if (_crownCareTimer > 0)
                        return "guard_crown";
                    if (_proudPose)
                        return "proud";
                    return WalkOrIdle();
                default:
                    return WalkOrIdle();
            }
        }

        private float KnightCourtEruptElapsed() => BurrowMaxAir - Timer;

        // Три фазы полёта вместо одного лупа: подъём / зависание / падение
        private string AirborneClip() => NPC.velocity.Y < -4f ? "jump_rise"
            : NPC.velocity.Y > 4f ? "jump_fall" : "jump_apex";

        // Порог с гистерезисом: на одиночном пороге 0.5 клип дёргался туда-обратно
        private string WalkOrIdle()
        {
            float speed = Math.Abs(NPC.velocity.X);
            if (_walkClipActive && speed < WalkClipExitSpeed)
                _walkClipActive = false;
            else if (!_walkClipActive && speed > WalkClipEnterSpeed)
                _walkClipActive = true;
            return _walkClipActive ? "walk" : "idle";
        }

        // Ширина силуэта после разворота: из узкого в полный по easeOut — разворот читается
        // поворотом туши, а не мгновенной подменой картинки
        private float TurnSquash()
        {
            if (_turnUnfold <= 0)
                return 1f;
            float t = 1f - _turnUnfold / (float)TurnUnfoldTicks;
            return MathHelper.Lerp(TurnSquashMin, 1f, 1f - (1f - t) * (1f - t));
        }

        // --- Вторичная физика ---

        // Процедурный крен от ускорения: разгон и торможение сразу получают вес.
        // Это то, чего не хватало на «развороте тело немного наклоняется».
        private void UpdateBodyLean()
        {
            float target = MathHelper.Clamp(_bodyAccelX * BodyLeanPerAccel * 60f, -BodyLeanMax, BodyLeanMax);
            _bodyLean = MathHelper.Lerp(_bodyLean, target, 0.18f);
        }

        // Демпфированный угловой осциллятор: на вход идёт горизонтальное ускорение тела
        // и плюха панциря. Корона перестаёт быть приклеенной наклейкой.
        private void UpdateCrownSpring()
        {
            float drive = -_bodyAccelX * 60f * CrownSpringDrive - _squashImpact * 0.35f;
            _crownSpringVel += -CrownSpringK * _crownSpring - CrownSpringDamp * _crownSpringVel + drive;
            _crownSpringVel = MathHelper.Clamp(_crownSpringVel, -0.35f, 0.35f);
            _crownSpring = MathHelper.Clamp(_crownSpring + _crownSpringVel, -CrownSpringMax, CrownSpringMax);
        }

        private void UpdateBreathing()
        {
            float t = Main.GameUpdateCount;
            _breathBody = (float)Math.Sin(t * MathHelper.TwoPi / BreathBodyPeriod);
            _breathClaw = (float)Math.Sin(t * MathHelper.TwoPi / BreathClawPeriod + 1.7f);
            _breathCrown = (float)Math.Sin(t * MathHelper.TwoPi / BreathCrownPeriod + 0.6f);
        }

        // --- Глаза ---

        private void UpdateEyes()
        {
            float baseGlow = _rageTimer > 0 ? 1f : Desperate ? 0.6f : Phase2 ? 0.35f : 0f;

            // Реакция на замах игрока: держит атаку рядом — глаза чуть ярче.
            // Спорно по читаемости, но именно такие мелочи создают репутацию.
            Player watched = Main.player[NPC.target];
            if (watched is { active: true, dead: false } && watched.controlUseItem
                && NPC.Distance(watched.Center) < MeleeRange)
                baseGlow += 0.12f;

            _eyesGlow = MathHelper.Clamp(baseGlow + AnimPose(LayerEyes).Aux, 0f, 1f);

            // Цветовой код по типу угрозы: один цвет глаз = один тип атаки. Читается за три
            // боя и работает даже периферийным зрением.
            Color target = State switch
            {
                CrabState.TideCall or CrabState.BubbleVolley or CrabState.TsunamiClap => new Color(110, 190, 255),
                CrabState.CrownCommand or CrabState.RoyalRoar => new Color(255, 200, 90),
                CrabState.OceanRage => new Color(255, 45, 25),
                CrabState.ClawSlam or CrabState.ClawSweep or CrabState.CrushingGrip
                    or CrabState.JumpCrush => new Color(255, 75, 30),
                _ => new Color(255, 110, 50),
            };
            _eyesColor = Color.Lerp(_eyesColor, target, 1f / 8f);

            // Зрачок смещается к игроку — мгновенно оживляет морду. По X ограничен знаком
            // взгляда, чтобы глаз не «уезжал» за затылок.
            Player look = Main.player[NPC.target];
            if (look is { active: true, dead: false })
            {
                Vector2 dir = (look.Center - AnimatedFacingToWorld(new Vector2(0f, -15f) * NPC.scale))
                    .SafeNormalize(Vector2.Zero);
                float forward = NPC.spriteDirection * ClawDirFix;
                dir.X = MathHelper.Clamp(dir.X * forward, -0.25f, 1f) * forward;
                _eyesLook = Vector2.Lerp(_eyesLook, dir * 4f, 0.12f);
            }
        }

        // --- Интеграция с ригом (зовётся из Legs/Claws/Crown) ---

        private LayerPose AnimPose(string layer)
        {
            LayerPose p = _anim?.Pose(layer) ?? LayerPose.Identity;
            return AnimIntensity == 1f ? p : LayerPose.Faded(p, AnimIntensity);
        }

        // «Лицевой» знак: +X в клипах = вперёд по взгляду (та же логика, что у клешней)
        private float AnimDirSign => NPC.spriteDirection * ClawDirFix;

        // Центр тела со смещением клипа — единая точка крепления панциря/бёдер/плеч/короны.
        // _gaitBob подмешивает оседание корпуса в РЕАЛЬНУЮ фазу походки: раньше клип
        // подпрыгивал в своём ритме (36 тиков), а лапы шагали в своём (12→6) — отсюда
        // и брался эффект «плывёт, а не идёт».
        private Vector2 AnimatedBodyCenter()
        {
            LayerPose p = AnimPose(LayerBody);
            return NPC.Center - new Vector2(0f, BodyLift - _gaitBob * GaitBobHeight - _stepVisualLift)
                + new Vector2(p.Offset.X * AnimDirSign, p.Offset.Y).RotatedBy(NPC.rotation) * NPC.scale;
        }

        private float AnimatedBodyRotation()
            => NPC.rotation + AnimPose(LayerBody).Rotation * AnimDirSign
               + _bodyLean * AnimDirSign + _crownGaze;

        // Анимированный вариант FacingToWorld — ТОЛЬКО для отрисовки.
        // Боевой FacingToWorld (зона короны, глаза для пыли) остаётся без смещений клипов.
        private Vector2 AnimatedFacingToWorld(Vector2 forwardOffset)
        {
            float fx = forwardOffset.X * AnimDirSign;
            return AnimatedBodyCenter() + new Vector2(fx, forwardOffset.Y).RotatedBy(AnimatedBodyRotation());
        }

        // Свечение глаз — слой eyes. Текстуры нет: мягкий глоу через SoAVfx поверх морды.
        private void DrawEyesGlow(SpriteBatch spriteBatch)
        {
            if (_eyesGlow <= 0.01f)
                return;
            if ((State == CrabState.Burrow && SubState < 2f) || (State == CrabState.KnightCourt && SubState < 2f))
                return; // под землёй не видно

            LayerPose p = AnimPose(LayerEyes);
            Vector2 pos = AnimatedFacingToWorld((new Vector2(0f, -15f) + p.Offset) * NPC.scale) + _eyesLook * NPC.scale;
            Color glow = _eyesColor * (_eyesGlow * 0.85f);
            glow.A = 0;

            SoAVfx.BeginAdditive(spriteBatch);
            SoAVfx.DrawTintedGlow(spriteBatch, pos, new Vector2(EyeGlowSize * NPC.scale) * p.Scale, glow);
            SoAVfx.EndAdditive(spriteBatch);
        }
    }
}
