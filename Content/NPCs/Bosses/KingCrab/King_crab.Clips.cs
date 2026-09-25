using SoA.Common.Graphics.Animation;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ==========================================================================================
    //  БИБЛИОТЕКА КЛИПОВ КОРОЛЯ-КРАБА — только хореография. Рантайм (выбор клипа, hit-stop,
    //  пружины, диспетчер меток) живёт в King_crab.Animation.cs.
    //
    //  Оси: rot в рад (+ = наклон/подъём вперёд по взгляду), ox/oy в px тела (+X вперёд, +Y ВНИЗ).
    //  Каналы слоёв:
    //    body       Aux = squash (+ сплющен, - вытянут);  Scale/ScaleY = прямая деформация
    //    crown      Aux = добавка свечения
    //    claw_*     Aux = раскрытие пинцера (- = перезакрытие «клац»)
    //    legs       Offset = сдвиг стойки (стопы держит IK)
    //               Aux  = добавка к FootSpread долей (0.2 = стойка шире на 20%)
    //               Aux2 = > 0.5 — шаг заблокирован (лапы больше не переставляются)
    //    eyes       Aux = сила свечения (0..1)
    //
    //  ПРАВИЛА, по которым авторены все клипы ниже (раздел 6 аудита):
    //    • интервалы между ключами НЕРОВНЫЕ — ровная сетка читается метрономом;
    //    • любое движение вверх начинается с 1–2 тиков вниз (anticipation);
    //    • любое движение вниз кончается перелётом и возвратом (overshoot);
    //    • парные конечности разведены по времени на 3–6 тиков;
    //    • дрожь на пике — трёхтактная с затуханием амплитуды;
    //    • EaseIn на замахах, EaseOut на торможениях.
    // ==========================================================================================
    public partial class King_crab
    {
        // Длительности новых клипов, которых нет среди боевых констант
        private const int SlamRecoverClipTicks = ClawRecoverTicks; // 30
        private const int JumpLandClipTicks = RecoverTicks;        // 26
        private const int TideReleaseTicks = 22;                   // = второй вал в Desperate
        private const int BurrowEruptClipTicks = 34;
        private const int BurrowSwimClipTicks = 48;
        private const int RageHuntClipTicks = 40;
        private const int RageThrowClipTicks = 12;

        private AnimPlayer BuildAnimPlayer()
        {
            AnimPlayer player = new(new[]
                {
                    // --- Базовые клипы стейтов ---
                    BuildIdle(), BuildWalk(),
                    BuildClawSlam(), BuildSlamRecover(),
                    BuildSweepWindup(), BuildSweepDash(),
                    BuildBubbleVolley(), BuildTideCall(),
                    BuildJumpCrouch(), BuildJumpRise(), BuildJumpApex(), BuildJumpFall(), BuildJumpLand(),
                    BuildGripWindup(), BuildGripLunge(), BuildGripRecover(),
                    BuildRoar(), BuildRoarRelease(),
                    BuildCrownCommand(),
                    BuildTsunamiClap(), BuildClapRecover(),
                    BuildBurrowDive(), BuildBurrowSwim(), BuildBurrowErupt(),
                    BuildProud(), BuildDispleased(), BuildGuardCrown(), BuildDeath(),
                    BuildRageIgnite(), BuildRageHunt(), BuildRageGrab(),
                    // --- Один-шот реакции (аддитивны поверх базы) ---
                    BuildTurn(), BuildHurtLight(), BuildHurt(), BuildHurtHeavy(),
                    BuildRage(), BuildPhase2Transition(), BuildLastLook(),
                    BuildIdleTwitch(), BuildProudFlourish(),
                    BuildTideRelease(), BuildBurrowLand(), BuildRageThrow(),
                },
                LayerBody, LayerCrown, LayerClawFront, LayerClawBack, LayerLegs, LayerEyes);

            player.OnEvent = OnAnimEvent; // п.1.2: метки клипов = кадрово точная косметика
            return player;
        }

        // ------------------------------------------------------------------------------------
        //  ПОКОЙ И ХОДЬБА
        // ------------------------------------------------------------------------------------

        // IDLE: вдох резче выдоха (потому и неравные интервалы), перед вдохом — микро-просадка.
        // Корона приходит в верхнюю точку на 5 тиков ПОЗЖЕ тела и перелетает её.
        // Разные периоды дыхания тела/клешней/короны клипом невыразимы (длительность одна на все
        // слои) — их доводит процедурная модуляция в UpdateBreathing().
        private static AnimClip BuildIdle() => new AnimClip("idle", 90f, loop: true)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 15f, rot: 0.008f, oy: 3f, aux: 0.02f)
            .Key(LayerBody, 26f, oy: 5.5f, aux: 0.03f)                          // anticipation: просадка перед вдохом
            .Key(LayerBody, 30f, rot: 0.015f, oy: 6f, aux: 0.03f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 45f, rot: -0.006f, oy: 1.5f, aux: -0.02f)           // вдох — быстрый
            .Key(LayerBody, 62f, rot: -0.015f, oy: -1.5f, aux: -0.04f)          // пик вдоха
            .Key(LayerBody, 78f, rot: 0.004f, oy: 1.5f, aux: 0.03f)             // выдох — долгий
            .Key(LayerClawFront, 0f, oy: -2f, rot: 0.03f, aux: 0.06f)
            .Key(LayerClawFront, 38f, oy: 3f, rot: -0.03f, aux: -0.03f)
            .Key(LayerClawFront, 70f, oy: -1f, rot: 0.01f, aux: 0.02f)
            .Key(LayerClawBack, 0f, oy: -1f, rot: 0.04f, aux: 0.04f)
            .Key(LayerClawBack, 31f, oy: 2f, rot: -0.015f, aux: -0.02f)         // своя фаза и амплитуда:
            .Key(LayerClawBack, 62f, oy: -2f, rot: 0.05f, aux: 0.05f)           // стойка не «пульсирует» целиком
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 35f, rot: 0.05f, oy: 2f)
            .Key(LayerCrown, 40f, rot: 0.06f, oy: 2.5f)                         // перелёт
            .Key(LayerCrown, 50f, rot: 0.05f, oy: 1.5f)
            .Key(LayerCrown, 75f, rot: -0.05f);

        // WALK: вертикального бобба в клипе НЕТ — его даёт реальная фаза походки (_gaitBob),
        // иначе корпус подпрыгивает не тогда, когда лапа отталкивается. Клип отвечает за крен,
        // клешни и корону. Интервалы неравные: фаза опоры длиннее фазы переноса.
        private static AnimClip BuildWalk() => new AnimClip("walk", 36f, loop: true)
            .Key(LayerBody, 0f, rot: -0.07f, aux: 0.06f)                        // опора
            .Key(LayerBody, 7f, rot: -0.02f, aux: -0.04f)                       // перенос
            .Key(LayerBody, 12f, rot: 0.03f)
            .Key(LayerBody, 16f, rot: 0.055f)                                   // упор: 2 тика удержания
            .Key(LayerBody, 18f, rot: 0.07f, aux: 0.06f)
            .Key(LayerBody, 25f, rot: 0.02f, aux: -0.04f)
            .Key(LayerBody, 30f, rot: -0.03f)
            .Key(LayerBody, 34f, rot: -0.055f)
            .Key(LayerClawFront, 0f, ox: 6f, rot: 0.12f, oy: -3f)
            .Key(LayerClawFront, 9f, ox: 0f, oy: 1f)                            // восьмёрка: вертикаль
            .Key(LayerClawFront, 18f, ox: -7f, rot: -0.1f, oy: 3f)              // с половинным периодом
            .Key(LayerClawFront, 21f, ox: -6f, rot: -0.09f, oy: 2f)             // overshoot назад
            .Key(LayerClawFront, 27f, ox: 0f, oy: -1f)
            .Key(LayerClawBack, 3f, ox: -6f, rot: -0.1f, oy: 3f)                // сдвиг фазы на 3 тика
            .Key(LayerClawBack, 12f, ox: 0f, oy: -1f)
            .Key(LayerClawBack, 21f, ox: 6f, rot: 0.12f, oy: -3f)
            .Key(LayerClawBack, 24f, ox: 5f, rot: 0.11f, oy: -2f)
            .Key(LayerClawBack, 30f, ox: 0f, oy: 1f)
            .Key(LayerCrown, 0f, rot: 0.1f)
            .Key(LayerCrown, 9f, rot: 0f, oy: 3f)
            .Key(LayerCrown, 18f, rot: -0.1f)
            .Key(LayerCrown, 27f, rot: 0f, oy: 3f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 18f, oy: 1.5f);

        // Одиночный щелчок пинцера в покое. Запускается НЕРЕГУЛЯРНО (раз в 150–300 тиков),
        // иначе читается циклом. Конверт короткий — щелчок должен быть резким.
        private static AnimClip BuildIdleTwitch() => new AnimClip("idle_twitch", 12f, loop: false)
            .Envelope(1f, 3f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 3f, aux: 0.25f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 9f, aux: -0.05f)
            .Key(LayerClawFront, 12f);

        // ------------------------------------------------------------------------------------
        //  РАЗВОРОТ И УРОН
        // ------------------------------------------------------------------------------------

        // TURN: приподнимается ПРОТИВ будущего движения (тик 2) → двухступенчатая просадка →
        // перелёт вверх. Клешни возвращаются в стойку врозь: передняя 13, задняя 16.
        private static AnimClip BuildTurn() => new AnimClip("turn", 17f, loop: false)
            .Envelope(2f, 5f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 2f, rot: -0.04f, oy: -3f)                           // anticipation
            .Key(LayerBody, 3f, oy: 8f, scale: 0.96f, aux: 0.16f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 6f, rot: 0.14f, oy: 14f, scale: 0.92f, aux: 0.25f)  // доседание
            .Key(LayerBody, 10f, oy: 3f, scale: 1.03f, aux: -0.08f)             // перелёт вверх
            .Key(LayerBody, 14f, oy: 1f, scale: 1.01f, aux: 0.02f)
            .Key(LayerBody, 17f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 5f, rot: -0.4f, oy: 16f, aux: -0.2f)
            .Key(LayerClawFront, 13f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 6f, rot: -0.4f, oy: 16f, aux: -0.2f)
            .Key(LayerClawBack, 16f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 6f, rot: -0.35f, oy: -7f)
            .Key(LayerCrown, 11f, rot: 0.12f, oy: 2f)                           // перелёт
            .Key(LayerCrown, 17f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 6f, aux: 0.18f)                                     // стойка шире: упор в развороте
            .Key(LayerLegs, 17f)
            .Event(3f, "turn_plant");

        // HURT — три градации по damageDone (см. ReactToDamage). Лёгкое попадание короля
        // почти не трогает: дёргается только корона.
        private static AnimClip BuildHurtLight() => new AnimClip("hurt_light", 8f, loop: false)
            .Envelope(1f, 3f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 2f, rot: -0.03f, ox: -4f, aux: 0.06f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 8f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 2f, rot: 0.14f, oy: -5f, ease: EaseMode.EaseOut)
            .Key(LayerCrown, 5f, rot: -0.04f, oy: 1f)
            .Key(LayerCrown, 8f);

        private static AnimClip BuildHurt() => new AnimClip("hurt", 12f, loop: false)
            .Envelope(2f, 5f)                                                   // п.5.1: конверт 6/6 съедал полклипа
            .Key(LayerBody, 0f)
            .Key(LayerBody, 3f, rot: -0.1f, ox: -14f, aux: 0.18f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 7f, rot: 0.05f, ox: -4f, aux: -0.05f)
            .Key(LayerBody, 12f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 3f, rot: 0.25f, ox: -4f, oy: -8f, aux: 0.3f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 12f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 4f, rot: 0.2f, ox: -4f, oy: -6f, aux: 0.3f, ease: EaseMode.EaseOut)
            .Key(LayerClawBack, 12f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 3f, rot: 0.25f, oy: -8f, ease: EaseMode.EaseOut)
            .Key(LayerCrown, 8f, oy: 2f)
            .Key(LayerCrown, 12f);

        // Тяжёлое попадание: корпус отлетает на 22 px, три пересечения нуля, панцирь принимает
        // удар (aux 0.18). Корона сбивается и выправляется НЕ сразу — её добивает _hurtCrownTilt.
        private static AnimClip BuildHurtHeavy() => new AnimClip("hurt_heavy", 18f, loop: false)
            .Envelope(2f, 6f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 3f, rot: -0.14f, ox: -22f, aux: 0.18f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 8f, rot: 0.07f, ox: -8f, aux: -0.08f)
            .Key(LayerBody, 13f, rot: -0.03f, ox: -3f, aux: 0.04f)
            .Key(LayerBody, 18f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 3f, rot: 0.42f, ox: -8f, oy: -14f, aux: 0.45f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 10f, rot: 0.1f, oy: -3f, aux: 0.1f)
            .Key(LayerClawFront, 18f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 5f, rot: 0.38f, ox: -8f, oy: -12f, aux: 0.45f, ease: EaseMode.EaseOut)
            .Key(LayerClawBack, 12f, rot: 0.08f, oy: -2f, aux: 0.1f)
            .Key(LayerClawBack, 18f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 3f, rot: 0.35f, oy: -12f, ease: EaseMode.EaseOut)
            .Key(LayerCrown, 9f, rot: -0.12f, oy: 4f)
            .Key(LayerCrown, 14f, rot: 0.06f, oy: -1f)
            .Key(LayerCrown, 18f)
            .Event(2f, "hurt_heavy");

        // ------------------------------------------------------------------------------------
        //  СЛЭМ КЛЕШНЁЙ
        // ------------------------------------------------------------------------------------

        // Замах уводит клешню высоко над голову с распахнутым пинцером. Дуга неравномерная:
        // медленный старт (0–6), разгон (6–14), торможение у пика (20–27), трёхтактная дрожь.
        // Знак squash на замахе ОБРАТНЫЙ прежнему: замах вверх ТЯНЕТ тело, а не сплющивает.
        private static AnimClip BuildClawSlam() => new AnimClip("claw_slam", ClawWindupTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 3f, rot: -0.12f, oy: 6f, aux: 0.1f)            // anticipation: вниз-вперёд
            .Key(LayerClawFront, 6f, rot: 0.22f, ox: -2f, oy: -6f, aux: 0.15f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 14f, rot: 0.5f, ox: -6f, oy: -16f, aux: 0.35f)
            .Key(LayerClawFront, 20f, rot: 0.78f, ox: -10f, oy: -23f, aux: 0.6f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 27f, rot: 1f, ox: -14f, oy: -28f, aux: 0.75f)  // пик
            .Key(LayerClawFront, 31f, rot: 0.94f, ox: -12f, oy: -25f, aux: 0.75f)  // дрожь: три такта
            .Key(LayerClawFront, 35f, rot: 1.05f, ox: -15f, oy: -29f, aux: 0.75f)  // с затуханием
            .Key(LayerClawFront, 38f, rot: 0.99f, ox: -13.5f, oy: -27.5f, aux: 0.75f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 45f, rot: -0.95f, ox: 28f, oy: 46f, aux: -0.25f)  // удар в землю
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 31f, rot: 0.3f, ox: -8f, oy: -8f)               // отстаёт от передней на 4 тика
            .Key(LayerClawBack, 45f, rot: 0.18f, oy: -2f)                       // на ударе балансирует ВВЕРХ
            .Key(LayerBody, 0f)
            .Key(LayerBody, 24f, rot: -0.14f, ox: -14f, oy: 4f, aux: -0.12f)    // вытянут за клешнёй
            .Key(LayerBody, 38f, rot: -0.15f, ox: -15f, oy: 4f, aux: -0.14f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 45f, rot: 0.12f, ox: 10f, oy: 2f, aux: 0.32f)       // удар — панцирь сплющило
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 31f, rot: -0.16f, oy: -2f)                         // корона отстаёт от клешни
            .Key(LayerCrown, 38f, rot: -0.17f, ease: EaseMode.EaseIn)
            .Key(LayerCrown, 45f, rot: 0.16f, oy: 3f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 26f, ox: -9f, aux: 0.12f)
            .Key(LayerLegs, 45f, ox: 5f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 30f, aux: 0.4f)
            .Event(26f, "slam_apex")
            .Event(44f, "slam_strike");

        // Продолжение удара: клешня зарывается (перелёт), отскакивает от грунта, качается, оседает.
        // Тело проваливается ЗА рукой, потом отдача вверх. Ключи — те же, что в аудите, минус 45.
        private static AnimClip BuildSlamRecover() => new AnimClip("slam_recover", SlamRecoverClipTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -0.95f, ox: 28f, oy: 46f, aux: -0.25f)
            .Key(LayerClawFront, 2f, rot: -1.05f, ox: 32f, oy: 52f, aux: -0.3f) // перелёт: зарылась
            .Key(LayerClawFront, 6f, rot: -0.88f, ox: 26f, oy: 42f, aux: -0.2f) // отскок от грунта
            .Key(LayerClawFront, 11f, rot: -0.92f, ox: 27f, oy: 45f, aux: -0.22f)
            .Key(LayerClawFront, 17f, rot: -0.9f, ox: 26f, oy: 44f, aux: -0.2f) // осела
            .Key(LayerClawFront, 30f, ease: EaseMode.EaseOut)                    // подъём в стойку
            .Key(LayerClawBack, 0f, rot: 0.18f, oy: -2f)
            .Key(LayerClawBack, 2f, rot: 0.45f, ox: -6f, oy: -10f, aux: 0.2f)   // вскинулась противовесом
            .Key(LayerClawBack, 9f, rot: 0.24f, oy: -4f)
            .Key(LayerClawBack, 16f, rot: 0.3f, oy: -6f)
            .Key(LayerClawBack, 30f)
            .Key(LayerBody, 0f, rot: 0.12f, ox: 10f, oy: 2f, aux: 0.32f)
            .Key(LayerBody, 3f, rot: 0.18f, ox: 11f, oy: 8f, aux: 0.3f)         // провалилось за рукой
            .Key(LayerBody, 9f, rot: 0.1f, ox: 6f, oy: 2f, aux: -0.08f)         // отдача вверх, вытяжка
            .Key(LayerBody, 17f, rot: 0.13f, ox: 3f, oy: 4f, aux: 0.05f)
            .Key(LayerBody, 30f, ease: EaseMode.EaseOut)
            .Key(LayerCrown, 0f, rot: 0.16f, oy: 3f)
            .Key(LayerCrown, 3f, rot: 0.24f, oy: 6f)                            // три пересечения нуля
            .Key(LayerCrown, 9f, rot: -0.06f, oy: -2f)
            .Key(LayerCrown, 16f, rot: 0.04f, oy: 1f)
            .Key(LayerCrown, 23f)
            .Key(LayerLegs, 0f, ox: 5f)
            .Key(LayerLegs, 2f, ox: 5f, oy: 4f, aux: 0.14f)                     // просадка на лапах от отдачи
            .Key(LayerLegs, 12f, ox: 2f, oy: 1f)
            .Key(LayerLegs, 30f)
            .Key(LayerEyes, 0f, aux: 0.4f)
            .Key(LayerEyes, 30f)
            .Event(2f, "slam_settle");

        // ------------------------------------------------------------------------------------
        //  РЫВОК КЛЕШНЁЙ
        // ------------------------------------------------------------------------------------

        // Сматывание неравномерное: быстро 0–26, медленно 26–50 (взвод), 55–60 — дрожь
        // взведённой пружины. Тело уплотняется перед выстрелом (aux 0.2 + scale 0.98).
        private static AnimClip BuildSweepWindup() => new AnimClip("sweep_windup", ClawSweepWindupTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 8f, ox: 8f, rot: -0.08f, aux: 0.15f)           // anticipation: бросок вперёд
            .Key(LayerClawFront, 12f, rot: 0.05f, ox: 0f, aux: 0.2f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 26f, rot: 0.35f, ox: -18f, oy: 2f, aux: 0.4f)
            .Key(LayerClawFront, 40f, rot: 0.52f, ox: -27f, oy: 6f, aux: 0.62f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 50f, rot: 0.6f, ox: -32f, oy: 8f, aux: 0.75f)
            .Key(LayerClawFront, 55f, rot: 0.66f, ox: -34f, oy: 8f, aux: 0.75f) // дрожь пружины
            .Key(LayerClawFront, 58f, rot: 0.63f, ox: -32f, oy: 8f, aux: 0.75f)
            .Key(LayerClawFront, 60f, rot: 0.65f, ox: -35f, oy: 8f, aux: 0.75f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 45f, rot: 0.2f, ox: -6f, oy: -6f)
            .Key(LayerClawBack, 60f, rot: 0.24f, ox: -8f, oy: -8f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 26f, rot: -0.05f, ox: -6f, oy: 2f, aux: 0.06f)
            .Key(LayerBody, 50f, rot: -0.1f, ox: -12f, oy: 4f, aux: 0.14f, scale: 0.99f)
            .Key(LayerBody, 58f, rot: -0.11f, ox: -13f, oy: 4f, aux: 0.2f, scale: 0.98f)
            .Key(LayerBody, 60f, rot: -0.11f, ox: -13f, oy: 4f, aux: 0.2f, scale: 0.98f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 50f, rot: -0.13f)
            .Key(LayerCrown, 60f, rot: -0.15f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 45f, ox: -10f, oy: 2f, aux: 0.12f)
            .Key(LayerLegs, 60f, ox: -12f, oy: 2f, aux: 0.15f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 45f, aux: 0.35f)
            .Event(50f, "sweep_coil");                                          // отсюда стопы скребут грунт

        // Рывок 13 px/тик: smear-кадр на 3-м тике, клешня начинает ОТСТАВАТЬ от тела (инерция),
        // в конце тело перелетает ноль назад — иначе торможение не читается.
        private static AnimClip BuildSweepDash() => new AnimClip("sweep_dash", ClawSweepDashTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -0.85f, ox: 48f, oy: 22f, aux: -0.25f)
            .Key(LayerClawFront, 3f, rot: -0.88f, ox: 52f, oy: 22f, aux: -0.3f) // перелёт вперёд
            .Key(LayerClawFront, 8f, rot: -0.72f, ox: 40f, oy: 18f, aux: -0.2f) // отстаёт от тела
            .Key(LayerClawFront, 16f, rot: -0.45f, ox: 28f, oy: 12f, aux: -0.12f)
            .Key(LayerClawFront, 24f, rot: -0.1f, ox: 10f, oy: 4f)
            .Key(LayerClawFront, 30f)
            .Key(LayerClawBack, 0f, rot: -0.5f, ox: -10f, oy: 16f, aux: -0.25f)
            .Key(LayerClawBack, 8f, rot: -0.52f, ox: -10f, oy: 20f, aux: -0.25f)  // качание от инерции
            .Key(LayerClawBack, 18f, rot: -0.46f, ox: -9f, oy: 14f, aux: -0.2f)
            .Key(LayerClawBack, 26f, rot: -0.48f, ox: -9f, oy: 16f, aux: -0.22f)
            .Key(LayerClawBack, 30f)
            .Key(LayerBody, 0f, rot: 0.14f, ox: 8f, oy: 2f, aux: -0.2f)         // стретч по ходу движения
            .Key(LayerBody, 3f, rot: 0.15f, ox: 10f, oy: 2f, aux: -0.28f, scale: 1.06f, scaleY: 0.96f) // smear
            .Key(LayerBody, 16f, rot: 0.06f, ox: 5f, aux: -0.12f)
            .Key(LayerBody, 24f, rot: -0.03f, ox: 1f, aux: 0.04f)               // перелёт назад: торможение
            .Key(LayerBody, 30f)
            .Key(LayerCrown, 0f, rot: 0.18f, oy: 2f)
            .Key(LayerCrown, 16f, rot: 0.1f, oy: 1f)
            .Key(LayerCrown, 24f, rot: -0.06f, oy: -2f)
            .Key(LayerCrown, 30f)
            .Key(LayerEyes, 0f, aux: 0.35f)
            .Key(LayerEyes, 24f, aux: 0.2f)
            .Key(LayerEyes, 30f)
            .Event(1f, "sweep_release");

        // ------------------------------------------------------------------------------------
        //  ЗАЛП ПУЗЫРЕЙ
        // ------------------------------------------------------------------------------------

        // Выстрелы AI идут на тиках 11/18/25/32/39/46 — теперь щелчок пинцера есть на КАЖДОМ
        // (раньше на 18/32 клешня стояла). Перед щелчком пинцер распахивается шире, чем в момент
        // выстрела, иначе «плевка» не видно. Откат тела нарастает: шестой пузырь — выхлоп.
        private static AnimClip BuildBubbleVolley() => new AnimClip("bubble_volley", VolleyTicks, loop: false)
            .Key(LayerClawFront, 0f, ox: 8f, oy: 4f)
            .Key(LayerClawFront, 9f, ox: 9f, oy: 3f, aux: 0.5f)                 // anticipation раскрытия
            .Key(LayerClawFront, 11f, ox: 10f, oy: 2f, aux: 0.4f)
            .Key(LayerClawFront, 15f, ox: 7f, oy: 5f, aux: -0.15f)
            .Key(LayerClawFront, 18f, ox: 10f, oy: 2f, aux: 0.4f)
            .Key(LayerClawFront, 22f, ox: 7f, oy: 5f, aux: -0.15f)
            .Key(LayerClawFront, 25f, ox: 10f, oy: 2f, aux: 0.4f)
            .Key(LayerClawFront, 29f, ox: 7f, oy: 5f, aux: -0.15f)
            .Key(LayerClawFront, 32f, ox: 10f, oy: 2f, aux: 0.4f)
            .Key(LayerClawFront, 36f, ox: 7f, oy: 5f, aux: -0.15f)
            .Key(LayerClawFront, 39f, ox: 10f, oy: 2f, aux: 0.4f)
            .Key(LayerClawFront, 43f, ox: 7f, oy: 5f, aux: -0.15f)
            .Key(LayerClawFront, 46f, ox: 11f, oy: 1f, aux: 0.45f)              // шестой — самый резкий
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 11f, rot: 0.12f, ox: -6f, oy: -3f)              // отдача в такт
            .Key(LayerClawBack, 20f, rot: 0.15f, ox: -7f, oy: -4f)
            .Key(LayerClawBack, 25f, rot: 0.12f, ox: -4f, oy: -3f)
            .Key(LayerClawBack, 32f, rot: 0.16f, ox: -8f, oy: -5f)
            .Key(LayerClawBack, 39f, rot: 0.12f, ox: -5f, oy: -3f)
            .Key(LayerClawBack, 46f, rot: 0.18f, ox: -9f, oy: -5f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 11f, ox: -3f, rot: -0.02f)                          // эскалация отката
            .Key(LayerBody, 14f, ox: 1f)                                        // с перелётом через ноль
            .Key(LayerBody, 18f, ox: -4f, rot: -0.025f)
            .Key(LayerBody, 21f, ox: 1f)
            .Key(LayerBody, 25f, ox: -5f, rot: -0.03f)
            .Key(LayerBody, 28f, ox: 1f)
            .Key(LayerBody, 32f, ox: -6f, rot: -0.035f)
            .Key(LayerBody, 35f, ox: 1f)
            .Key(LayerBody, 39f, ox: -7f, rot: -0.04f)
            .Key(LayerBody, 42f, ox: 1f)
            .Key(LayerBody, 46f, ox: -9f, rot: -0.05f, aux: 0.2f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 11f, rot: -0.02f)                                  // шесть микро-кивков
            .Key(LayerCrown, 18f, rot: 0.01f)
            .Key(LayerCrown, 25f, rot: -0.02f)
            .Key(LayerCrown, 32f, rot: 0.01f)
            .Key(LayerCrown, 39f, rot: -0.02f)
            .Key(LayerCrown, 46f, rot: 0.02f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 11f, ox: -2f)                                       // отдача доходит до стоп
            .Key(LayerLegs, 15f)
            .Key(LayerLegs, 25f, ox: -2f)
            .Key(LayerLegs, 29f)
            .Key(LayerLegs, 39f, ox: -2f)
            .Key(LayerLegs, 46f, ox: -3f)
            .Event(11f, "volley_shot")
            .Event(18f, "volley_shot")
            .Event(25f, "volley_shot")
            .Event(32f, "volley_shot")
            .Event(39f, "volley_shot")
            .Event(46f, "volley_shot");

        // ------------------------------------------------------------------------------------
        //  ПРИЗЫВ ПРИЛИВА
        // ------------------------------------------------------------------------------------

        // Главный «королевский» жест. Приседает перед тем, как вздыбиться. Подъём с медленным
        // стартом и разгоном. Дрожь на пике ХАОТИЧНАЯ (не синусоида): 33/39/44/48/55.
        // Клешни разведены на 6 тиков и дрожат с разной амплитудой — иначе руки читаются
        // одним объектом.
        private static AnimClip BuildTideCall() => new AnimClip("tide_call", TideTelegraphTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 8f, rot: -0.05f, oy: 4f, aux: 0.1f)            // anticipation
            .Key(LayerClawFront, 14f, rot: 0.35f, ox: -4f, oy: -12f, aux: 0.25f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 22f, rot: 0.85f, ox: -8f, oy: -26f, aux: 0.4f)
            .Key(LayerClawFront, 28f, rot: 1.05f, ox: -10f, oy: -32f, aux: 0.45f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 33f, rot: 1.07f, ox: -10f, oy: -34f, aux: 0.45f)
            .Key(LayerClawFront, 39f, rot: 0.98f, ox: -9f, oy: -29f, aux: 0.45f)
            .Key(LayerClawFront, 44f, rot: 1.08f, ox: -10f, oy: -35f, aux: 0.48f)
            .Key(LayerClawFront, 48f, rot: 1f, ox: -9f, oy: -31f, aux: 0.45f)
            .Key(LayerClawFront, 55f, rot: 1.1f, ox: -10f, oy: -36f, aux: 0.5f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 10f, rot: -0.04f, oy: 3f, aux: 0.08f)
            .Key(LayerClawBack, 20f, rot: 0.6f, ox: -6f, oy: -20f, aux: 0.3f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 34f, rot: 1.04f, ox: -10f, oy: -30f, aux: 0.45f)  // догоняет с лагом 6
            .Key(LayerClawBack, 41f, rot: 1.09f, ox: -11f, oy: -33f, aux: 0.45f)  // своя амплитуда дрожи
            .Key(LayerClawBack, 47f, rot: 1.02f, ox: -10f, oy: -28f, aux: 0.45f)
            .Key(LayerClawBack, 55f, rot: 1.07f, ox: -11f, oy: -32f, aux: 0.5f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 8f, oy: 6f, aux: 0.15f)                             // присел перед подъёмом
            .Key(LayerBody, 28f, rot: -0.08f, oy: -8f, aux: -0.12f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 40f, rot: -0.085f, oy: -9f, aux: -0.13f)
            .Key(LayerBody, 55f, rot: -0.09f, oy: -10f, aux: -0.14f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 30f, oy: -3f, aux: 0.3f)
            .Key(LayerCrown, 38f, rot: -0.1f, oy: -5f, aux: 0.45f)              // заваливается назад
            .Key(LayerCrown, 45f, rot: -0.08f, oy: -4f, aux: 0.6f)
            .Key(LayerCrown, 55f, rot: -0.12f, oy: -6f, aux: 0.5f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 26f, oy: -4f, aux: 0.15f)                           // на цыпочках, стойка шире
            .Key(LayerLegs, 55f, oy: -5f, aux: 0.15f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 30f, aux: 0.5f)
            .Key(LayerEyes, 55f, aux: 0.6f)
            .Event(28f, "tide_gather");

        // Обрушение клешней после выпуска вала — играется один-шотом поверх кроссфейда в стойку,
        // поэтому амплитуда умеренная: базовый клип в это же время сам сводит руки вниз.
        private static AnimClip BuildTideRelease() => new AnimClip("tide_release", TideReleaseTicks, loop: false)
            .Envelope(1f, 6f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 5f, rot: -0.5f, oy: 18f, aux: -0.2f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 11f, rot: -0.12f, oy: 4f)
            .Key(LayerClawFront, 22f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 7f, rot: -0.46f, oy: 16f, aux: -0.2f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 13f, rot: -0.1f, oy: 3f)
            .Key(LayerClawBack, 22f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 5f, rot: 0.1f, oy: 10f, aux: 0.35f, ease: EaseMode.EaseIn)  // плюхнуло
            .Key(LayerBody, 11f, rot: -0.02f, oy: -3f, aux: -0.1f)
            .Key(LayerBody, 16f, oy: 2f, aux: 0.05f)
            .Key(LayerBody, 22f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 5f, rot: 0.2f, oy: -8f)                            // подскочила
            .Key(LayerCrown, 12f, rot: -0.08f, oy: 3f)
            .Key(LayerCrown, 22f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 5f, oy: 5f, aux: 0.12f)
            .Key(LayerLegs, 22f);

        // ------------------------------------------------------------------------------------
        //  ПРЫЖОК
        // ------------------------------------------------------------------------------------

        // Присед: микро-подъём (anticipation) → быстрый провал → доседание → тело уже пошло
        // вверх на последнем тике, когда стейт сменится. Бёдра просели → IK согнёт колени.
        private static AnimClip BuildJumpCrouch() => new AnimClip("jump_crouch", JumpCrouchTicks, loop: false)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 6f, oy: -4f, aux: -0.06f)                           // anticipation к приседу
            .Key(LayerBody, 14f, oy: 7f, aux: 0.25f, ease: EaseMode.EaseIn)     // быстрый провал
            .Key(LayerBody, 20f, oy: 10f, aux: 0.38f)
            .Key(LayerBody, 22f, oy: 10f, aux: 0.42f)
            .Key(LayerBody, 25f, oy: 8f, aux: 0.3f)                             // пружина разжимается
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 16f, rot: -0.5f, ox: -14f, oy: 20f, aux: -0.25f)
            .Key(LayerClawFront, 25f, rot: -0.55f, ox: -15f, oy: 22f, aux: -0.25f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 19f, rot: -0.5f, ox: -14f, oy: 20f, aux: -0.25f)
            .Key(LayerClawBack, 25f, rot: -0.54f, ox: -15f, oy: 21f, aux: -0.25f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 6f, oy: -3f)
            .Key(LayerCrown, 25f, oy: 7f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 14f, oy: 5f, aux: 0.14f)                            // колени гнутся, стопы наружу
            .Key(LayerLegs, 20f, oy: 8f, aux: 0.2f)
            .Key(LayerLegs, 25f, oy: 6f, aux: 0.2f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 20f, aux: 0.4f)
            .Event(14f, "jump_squat");

        // Три фазы полёта вместо одного лупа — падение начинает читаться падением.
        // Взлёт: тело вытянуто, нос задран, клешни отстают вниз-назад, корона всплывает.
        private static AnimClip BuildJumpRise() => new AnimClip("jump_rise", 30f, loop: true)
            .Key(LayerBody, 0f, rot: -0.12f, aux: -0.25f)
            .Key(LayerBody, 15f, rot: -0.1f, aux: -0.22f)
            .Key(LayerClawFront, 0f, rot: -0.8f, ox: -16f, oy: 28f, aux: -0.25f)
            .Key(LayerClawFront, 15f, rot: -0.78f, ox: -15f, oy: 26f, aux: -0.25f)
            .Key(LayerClawBack, 0f, rot: -0.82f, ox: -16f, oy: 30f, aux: -0.25f)
            .Key(LayerClawBack, 18f, rot: -0.79f, ox: -15f, oy: 27f, aux: -0.25f)
            .Key(LayerCrown, 0f, oy: -6f)
            .Key(LayerCrown, 15f, oy: -5f);

        // Апекс: тело выравнивается, клешни РАСКРЫВАЮТСЯ — краб группируется перед падением.
        private static AnimClip BuildJumpApex() => new AnimClip("jump_apex", 24f, loop: true)
            .Key(LayerBody, 0f, aux: -0.05f)
            .Key(LayerBody, 12f, rot: 0.02f, aux: 0f)
            .Key(LayerClawFront, 0f, rot: -0.5f, ox: -6f, oy: 18f, aux: 0.2f)
            .Key(LayerClawFront, 12f, rot: -0.45f, ox: -4f, oy: 16f, aux: 0.25f)
            .Key(LayerClawBack, 0f, rot: -0.52f, ox: -7f, oy: 19f, aux: 0.2f)
            .Key(LayerClawBack, 14f, rot: -0.47f, ox: -5f, oy: 17f, aux: 0.25f)
            .Key(LayerCrown, 0f, oy: -3f)
            .Key(LayerCrown, 12f, oy: -2f);

        // Падение: тело сплющено по вертикали, нос опущен, клешни выброшены вперёд-вниз
        // (готовятся принять удар), корона прижата.
        private static AnimClip BuildJumpFall() => new AnimClip("jump_fall", 26f, loop: true)
            .Key(LayerBody, 0f, rot: 0.16f, aux: 0.15f)
            .Key(LayerBody, 13f, rot: 0.14f, aux: 0.12f)
            .Key(LayerClawFront, 0f, rot: -0.9f, ox: 20f, oy: 34f, aux: 0f)
            .Key(LayerClawFront, 13f, rot: -0.86f, ox: 18f, oy: 32f, aux: 0.05f)
            .Key(LayerClawBack, 0f, rot: -0.86f, ox: 14f, oy: 32f, aux: 0f)
            .Key(LayerClawBack, 15f, rot: -0.82f, ox: 12f, oy: 30f, aux: 0.05f)
            .Key(LayerCrown, 0f, oy: 5f)
            .Key(LayerCrown, 13f, oy: 4f);

        // Приземление: ЧЕТЫРЕ пересечения нуля — вот что превращает приземление в приземление.
        // _squashImpact даёт только один экспоненциальный спад, клип кладёт поверх пружину.
        private static AnimClip BuildJumpLand() => new AnimClip("jump_land", JumpLandClipTicks, loop: false)
            .Key(LayerBody, 0f, oy: 14f, aux: 0.55f)                            // максимальный сквош
            .Key(LayerBody, 4f, oy: -4f, aux: -0.12f, ease: EaseMode.EaseOut)   // отскок
            .Key(LayerBody, 9f, oy: 4f, aux: 0.18f)                             // вторая просадка
            .Key(LayerBody, 15f, oy: -1f, aux: -0.04f)
            .Key(LayerBody, 22f, oy: 1f, aux: 0.05f)
            .Key(LayerBody, 26f)
            .Key(LayerClawFront, 0f, rot: -1.1f, ox: 30f, oy: 46f, aux: -0.1f)  // распластаны наружу
            .Key(LayerClawFront, 5f, rot: -0.7f, ox: 20f, oy: 30f, aux: 0.1f)
            .Key(LayerClawFront, 12f, rot: -0.34f, ox: 10f, oy: 14f)
            .Key(LayerClawFront, 26f)
            .Key(LayerClawBack, 0f, rot: -1.06f, ox: 26f, oy: 44f, aux: -0.1f)
            .Key(LayerClawBack, 8f, rot: -0.62f, ox: 16f, oy: 26f, aux: 0.1f)
            .Key(LayerClawBack, 15f, rot: -0.3f, ox: 8f, oy: 12f)
            .Key(LayerClawBack, 26f)
            .Key(LayerCrown, 0f, oy: 8f)                                        // вдавлена
            .Key(LayerCrown, 4f, oy: -10f, rot: 0.2f)                           // подлетает
            .Key(LayerCrown, 10f, oy: 3f, rot: -0.06f)
            .Key(LayerCrown, 17f, oy: -1f, rot: 0.02f)
            .Key(LayerCrown, 26f)
            .Key(LayerLegs, 0f, oy: 8f, aux: 0.2f)                              // лапы подламываются
            .Key(LayerLegs, 6f, oy: 4f, aux: 0.16f)
            .Key(LayerLegs, 14f, oy: 1f, aux: 0.06f)
            .Key(LayerLegs, 26f)
            .Event(1f, "jump_impact");

        // ------------------------------------------------------------------------------------
        //  ЗАХЛОП КЛЕШНЁЙ
        // ------------------------------------------------------------------------------------

        // Изготовка: медленный старт, трёхтактная дрожь напряжения, глаз разгорается НЕРОВНО —
        // так страшнее, чем ровное свечение.
        private static AnimClip BuildGripWindup() => new AnimClip("grip_windup", GripWindupTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 3f, ox: 4f, aux: 0.1f)                         // anticipation вперёд
            .Key(LayerClawFront, 12f, rot: 0.04f, ox: -5f, aux: 0.2f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 25f, rot: 0.1f, ox: -12f, oy: -2f, aux: 0.4f)
            .Key(LayerClawFront, 42f, rot: 0.2f, ox: -22f, oy: -6f, aux: 0.75f)
            .Key(LayerClawFront, 46f, rot: 0.23f, ox: -21f, oy: -5f, aux: 0.75f)  // дрожь: три такта
            .Key(LayerClawFront, 48f, rot: 0.19f, ox: -23f, oy: -7f, aux: 0.78f)
            .Key(LayerClawFront, 50f, rot: 0.21f, ox: -22f, oy: -6f, aux: 0.8f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 40f, rot: 0.25f, ox: -8f, oy: -8f)
            .Key(LayerClawBack, 50f, rot: 0.28f, ox: -9f, oy: -9f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 42f, rot: -0.09f, ox: -10f, oy: 4f, aux: 0.22f)
            .Key(LayerBody, 50f, rot: -0.1f, ox: -11f, oy: 4f, aux: 0.26f, scale: 0.99f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 42f, rot: -0.1f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 40f, ox: -8f, oy: 2f, aux: 0.14f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 35f, aux: 0.5f)                                     // мигание: разгорается неровно
            .Key(LayerEyes, 38f, aux: 0.25f)
            .Key(LayerEyes, 42f, aux: 0.7f)
            .Key(LayerEyes, 50f, aux: 0.9f);

        // Выпад: пинцер ПЕРЕЗАКРЫВАЕТСЯ сильнее нормы и отдаёт назад — это и даёт «клац»
        // вместо «сомкнулось». Метка grip_snap на 17-м тике, в кадр пика.
        private static AnimClip BuildGripLunge() => new AnimClip("grip_lunge", GripLungeTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -0.75f, ox: 44f, oy: 16f, aux: 0.75f)
            .Key(LayerClawFront, 6f, rot: -0.75f, ox: 45f, oy: 16f, aux: 0.7f)  // почти не двигается: напряжение
            .Key(LayerClawFront, 13f, rot: -0.75f, ox: 45f, oy: 16f, aux: 0.45f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 16f, rot: -0.76f, ox: 44f, oy: 16f, aux: -0.3f)  // перезакрытие
            .Key(LayerClawFront, 18f, rot: -0.75f, ox: 44f, oy: 16f, aux: -0.25f) // отдача пинцера
            .Key(LayerClawBack, 0f, rot: -0.4f, ox: -10f, oy: 12f, aux: -0.25f)
            .Key(LayerClawBack, 18f, rot: -0.44f, ox: -11f, oy: 14f, aux: -0.25f)
            .Key(LayerBody, 0f, rot: 0.07f, ox: 12f, aux: -0.1f)
            .Key(LayerBody, 16f, rot: 0.04f, ox: 8f, aux: 0.08f)
            .Key(LayerBody, 18f, rot: 0.03f, ox: 7f, aux: 0.05f)
            .Key(LayerCrown, 0f, rot: 0.16f)
            .Key(LayerCrown, 18f, rot: 0.06f)
            .Key(LayerEyes, 0f, aux: 0.9f)
            .Key(LayerEyes, 18f, aux: 0.6f)
            .Event(17f, "grip_snap");

        // Отход: 6 тиков вибрации клешни после удара — интерполяция ключами такую частоту
        // съест, поэтому дрожь тут крупная (5/7/9/11), а мелкую добавляет _clawShake.
        private static AnimClip BuildGripRecover() => new AnimClip("grip_recover", RecoverTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -0.75f, ox: 44f, oy: 16f, aux: -0.25f)
            .Key(LayerClawFront, 5f, rot: -0.74f, ox: 42f, oy: 16f, aux: -0.22f)
            .Key(LayerClawFront, 7f, rot: -0.76f, ox: 46f, oy: 17f, aux: -0.25f)
            .Key(LayerClawFront, 9f, rot: -0.74f, ox: 43f, oy: 16f, aux: -0.22f)
            .Key(LayerClawFront, 11f, rot: -0.75f, ox: 45f, oy: 16f, aux: -0.24f)
            .Key(LayerClawFront, 14f, rot: -0.4f, ox: 28f, oy: 12f, aux: -0.1f) // подбирается
            .Key(LayerClawFront, 20f, rot: -0.16f, ox: 12f, oy: 6f)
            .Key(LayerClawFront, 26f)
            .Key(LayerClawBack, 0f, rot: -0.44f, ox: -11f, oy: 14f, aux: -0.25f)
            .Key(LayerClawBack, 8f, rot: -0.3f, ox: -8f, oy: 8f, aux: -0.1f)
            .Key(LayerClawBack, 26f)
            .Key(LayerBody, 0f, rot: 0.03f, ox: 7f, aux: 0.05f)
            .Key(LayerBody, 6f, rot: 0.05f, ox: 6f, aux: 0.12f)
            .Key(LayerBody, 20f, rot: -0.06f, ox: -4f, aux: -0.06f)             // отдача назад
            .Key(LayerBody, 26f)
            .Key(LayerCrown, 0f, rot: 0.06f)
            .Key(LayerCrown, 6f, rot: 0.1f, oy: 2f)
            .Key(LayerCrown, 16f, rot: -0.05f, oy: -2f)
            .Key(LayerCrown, 26f)
            .Key(LayerEyes, 0f, aux: 0.6f)
            .Key(LayerEyes, 26f);

        // ------------------------------------------------------------------------------------
        //  РЁВ
        // ------------------------------------------------------------------------------------

        // Вдох (0–14): тело СЖИМАЕТСЯ, клешни поджимаются, глаза почти гаснут — на метке
        // roar_inhale пыль втягивается к морде. Дальше разгон 14→30→38→45 с дрожью на пике.
        private static AnimClip BuildRoar() => new AnimClip("roar", RoarWindupTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 14f, rot: -0.3f, oy: 12f, aux: -0.2f)          // поджались на вдохе
            .Key(LayerClawFront, 30f, rot: 0.9f, ox: -10f, oy: -24f, aux: 0.6f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 38f, rot: 1.08f, ox: -12f, oy: -29f, aux: 0.75f)
            .Key(LayerClawFront, 42f, rot: 1.04f, ox: -11f, oy: -27f, aux: 0.75f)
            .Key(LayerClawFront, 45f, rot: 1.1f, ox: -12f, oy: -30f, aux: 0.78f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 14f, rot: -0.28f, oy: 11f, aux: -0.2f)
            .Key(LayerClawBack, 33f, rot: 0.92f, ox: -10f, oy: -22f, aux: 0.6f, ease: EaseMode.EaseOut)
            .Key(LayerClawBack, 40f, rot: 1.06f, ox: -12f, oy: -26f, aux: 0.75f)
            .Key(LayerClawBack, 45f, rot: 1.12f, ox: -12f, oy: -28f, aux: 0.78f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 14f, oy: 6f, aux: 0.2f)                             // вдох: сжался
            .Key(LayerBody, 30f, rot: -0.14f, oy: -11f, aux: -0.12f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 38f, rot: -0.155f, oy: -13f, aux: -0.14f)
            .Key(LayerBody, 42f, rot: -0.15f, oy: -12.5f, aux: -0.14f)
            .Key(LayerBody, 45f, rot: -0.16f, oy: -14f, aux: -0.15f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 14f, oy: 3f)
            .Key(LayerCrown, 38f, oy: -4f, aux: 0.3f)
            .Key(LayerCrown, 45f, oy: -5f, aux: 0.35f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 14f, oy: -2f)
            .Key(LayerLegs, 38f, oy: 5f, aux: 0.18f)                            // упёрлись, стойка шире
            .Key(LayerLegs, 45f, oy: 5f, aux: 0.2f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 12f, aux: -0.1f)                                    // погасли на вдохе
            .Key(LayerEyes, 30f, aux: 0.6f)
            .Key(LayerEyes, 45f, aux: 1f)
            .Event(8f, "roar_inhale")
            .Event(44f, "roar_peak");

        // Выпуск: тело выбрасывается ВПЕРЁД, клешни хлещут вниз, корона чуть не слетает.
        // Глаза дают вспышку с перебором (1.3 клампится, но при склейке даёт плато).
        private static AnimClip BuildRoarRelease() => new AnimClip("roar_release", RecoverTicks, loop: false)
            .Key(LayerBody, 0f, rot: -0.16f, oy: -14f, aux: -0.15f)
            .Key(LayerBody, 3f, rot: 0.16f, ox: 14f, oy: -2f, aux: 0.1f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 8f, rot: 0.2f, ox: 10f, oy: 10f, aux: 0.3f)         // перелёт вниз
            .Key(LayerBody, 14f, rot: -0.05f, ox: 2f, oy: -3f, aux: -0.08f)     // отдача назад
            .Key(LayerBody, 20f, rot: 0.04f, oy: 2f, aux: 0.04f)                // оседание
            .Key(LayerBody, 26f)
            .Key(LayerClawFront, 0f, rot: 1.1f, ox: -12f, oy: -30f, aux: 0.78f)
            .Key(LayerClawFront, 4f, rot: -0.5f, ox: 18f, oy: 24f, aux: 0.1f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 9f, rot: -0.66f, ox: 22f, oy: 32f, aux: -0.05f)
            .Key(LayerClawFront, 16f, rot: -0.28f, ox: 10f, oy: 14f)
            .Key(LayerClawFront, 26f)
            .Key(LayerClawBack, 0f, rot: 1.12f, ox: -12f, oy: -28f, aux: 0.78f)
            .Key(LayerClawBack, 6f, rot: -0.44f, ox: 14f, oy: 22f, aux: 0.1f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 12f, rot: -0.6f, ox: 18f, oy: 28f, aux: -0.05f)
            .Key(LayerClawBack, 19f, rot: -0.24f, ox: 8f, oy: 12f)
            .Key(LayerClawBack, 26f)
            .Key(LayerCrown, 0f, oy: -5f, aux: 0.35f)
            .Key(LayerCrown, 4f, rot: -0.3f, oy: -8f, aux: 0.5f)                // чуть не слетела
            .Key(LayerCrown, 11f, rot: 0.14f, oy: 4f, aux: 0.3f)
            .Key(LayerCrown, 18f, rot: -0.05f, oy: -1f, aux: 0.15f)
            .Key(LayerCrown, 26f)
            .Key(LayerLegs, 0f, oy: 5f, aux: 0.2f)
            .Key(LayerLegs, 8f, oy: 8f, aux: 0.22f)
            .Key(LayerLegs, 18f, oy: 2f, aux: 0.08f)
            .Key(LayerLegs, 26f)
            .Key(LayerEyes, 0f, aux: 1f)
            .Key(LayerEyes, 3f, aux: 1.3f)                                      // вспышка с перебором
            .Key(LayerEyes, 26f, aux: 0.5f)
            .Event(1f, "roar_release");

        // ------------------------------------------------------------------------------------
        //  КОРОЛЕВСКИЙ ПРИКАЗ
        // ------------------------------------------------------------------------------------

        // Дуга широкая и неспешная: рука уходит вперёд-вверх, ЗАМИРАЕТ на 35, потом резкий
        // указующий тычок на 46 — вот это и есть команда. Задняя клешня КАСАЕТСЯ короны на 24,
        // и корона в ответ приподнимается: взаимодействие двух частей рига.
        private static AnimClip BuildCrownCommand() => new AnimClip("crown_command", CrownCommandTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 8f, ox: 4f, oy: -4f, aux: 0.15f)
            .Key(LayerClawFront, 15f, ox: 10f, oy: -14f, aux: 0.3f)
            .Key(LayerClawFront, 24f, ox: 14f, oy: -18f, aux: 0.35f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 35f, ox: 15f, oy: -17f, aux: 0.35f)            // замерла
            .Key(LayerClawFront, 46f, rot: -0.15f, ox: 26f, oy: -8f, aux: 0.15f, ease: EaseMode.EaseIn) // тычок
            .Key(LayerClawFront, 55f, ox: 16f, oy: -12f, aux: 0.3f)
            .Key(LayerClawFront, 64f, ox: 10f, oy: -14f, aux: 0.3f)
            .Key(LayerClawFront, 70f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 20f, rot: 1.2f, ox: -14f, oy: -30f)
            .Key(LayerClawBack, 24f, rot: 1.24f, ox: -14f, oy: -32f)            // касание короны
            .Key(LayerClawBack, 34f, rot: 1.18f, ox: -14f, oy: -28f)
            .Key(LayerClawBack, 45f, rot: 1.15f, ox: -14f, oy: -27f)
            .Key(LayerClawBack, 70f, rot: 1.2f, ox: -14f, oy: -30f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 10f, ox: -6f, oy: 2f, aux: 0.08f)                   // anticipation назад
            .Key(LayerBody, 24f, rot: -0.04f, oy: -6f, aux: -0.08f)
            .Key(LayerBody, 46f, rot: -0.02f, ox: 4f, oy: -5f, aux: -0.04f)
            .Key(LayerBody, 70f, rot: -0.04f, oy: -6f, aux: -0.08f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 15f, aux: 0.3f)
            .Key(LayerCrown, 26f, oy: -3f, aux: 0.25f)                          // отозвалась на касание
            .Key(LayerCrown, 34f, aux: 0.3f)
            .Key(LayerCrown, 46f, oy: -2f, aux: 0.8f)                           // момент тычка
            .Key(LayerCrown, 55f, aux: 0.6f)
            .Key(LayerCrown, 68f, aux: 1f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 15f, aux: 0.3f)
            .Key(LayerEyes, 30f, aux: 0.22f)
            .Key(LayerEyes, 46f, aux: 0.8f)                                     // вспыхивают в такт короне
            .Key(LayerEyes, 55f, aux: 0.55f)
            .Key(LayerEyes, 68f, aux: 0.9f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 20f, ox: -4f, oy: -3f, aux: 0.18f)                  // царственная широкая стойка
            .Key(LayerLegs, 70f, ox: -4f, oy: -3f, aux: 0.18f)
            .Event(24f, "crown_touch")
            .Event(46f, "crown_point");

        // ------------------------------------------------------------------------------------
        //  ХЛОПОК ОБЕИМИ КЛЕШНЯМИ
        // ------------------------------------------------------------------------------------

        // Развод больше НЕ симметричный: раньше claw_front и claw_back имели идентичные ключи —
        // самый заметный признак дешёвой анимации. Разведены на 4 тика и 0.08 рад, сходятся
        // в одну точку только в момент столкновения (55).
        private static AnimClip BuildTsunamiClap() => new AnimClip("tsunami_clap", TsunamiWindupTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 12f, rot: 0.3f, oy: -8f, aux: 0.3f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 26f, rot: 0.65f, ox: -10f, oy: -18f, aux: 0.5f)
            .Key(LayerClawFront, 38f, rot: 0.85f, ox: -14f, oy: -24f, aux: 0.7f)
            .Key(LayerClawFront, 44f, rot: 0.98f, ox: -18f, oy: -29f, aux: 0.8f) // перелёт ШИРЕ, чем надо
            .Key(LayerClawFront, 47f, rot: 0.9f, ox: -16f, oy: -26f, aux: 0.75f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 52f, rot: -1.05f, ox: 32f, oy: 22f, aux: -0.3f) // перелёт за точку встречи
            .Key(LayerClawFront, 55f, rot: -1f, ox: 26f, oy: 20f, aux: -0.25f)   // откат от столкновения
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 16f, rot: 0.34f, oy: -7f, aux: 0.3f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 30f, rot: 0.6f, ox: -10f, oy: -16f, aux: 0.5f)
            .Key(LayerClawBack, 41f, rot: 0.79f, ox: -14f, oy: -22f, aux: 0.7f)
            .Key(LayerClawBack, 45f, rot: 0.9f, ox: -18f, oy: -27f, aux: 0.8f)
            .Key(LayerClawBack, 47f, rot: 0.82f, ox: -16f, oy: -24f, aux: 0.75f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 52f, rot: -1.02f, ox: 31f, oy: 22f, aux: -0.3f)
            .Key(LayerClawBack, 55f, rot: -1f, ox: 26f, oy: 20f, aux: -0.25f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 26f, rot: -0.05f, oy: -4f, aux: -0.12f)
            .Key(LayerBody, 47f, rot: -0.08f, oy: -8f, aux: -0.2f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 55f, rot: 0.08f, oy: 4f, aux: 0.3f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 47f, oy: -4f, ease: EaseMode.EaseIn)
            .Key(LayerCrown, 55f, oy: 3f, rot: 0.12f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 40f, oy: -2f, aux: 0.1f)
            .Key(LayerLegs, 55f, oy: 6f, aux: 0.18f)                            // вдавливает в грунт
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 40f, aux: 0.4f)
            .Key(LayerEyes, 55f, aux: 0.6f)
            .Event(44f, "clap_apex")
            .Event(47f, "clap_release");

        private static AnimClip BuildClapRecover() => new AnimClip("clap_recover", RecoverTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -1f, ox: 26f, oy: 20f, aux: -0.25f)
            .Key(LayerClawFront, 4f, rot: -0.75f, ox: 18f, oy: 14f, aux: 0.1f)  // разлетаются от отдачи
            .Key(LayerClawFront, 9f, rot: -0.6f, ox: 22f, oy: 10f, aux: 0.05f)  // вибрация
            .Key(LayerClawFront, 12f, rot: -0.62f, ox: 19f, oy: 11f, aux: 0.05f)
            .Key(LayerClawFront, 15f, rot: -0.6f, ox: 21f, oy: 10f, aux: 0.05f)
            .Key(LayerClawFront, 18f, rot: -0.3f, ox: 10f, oy: 5f)              // подъём в стойку
            .Key(LayerClawFront, 26f)
            .Key(LayerClawBack, 0f, rot: -1f, ox: 26f, oy: 20f, aux: -0.25f)
            .Key(LayerClawBack, 6f, rot: -0.72f, ox: 17f, oy: 13f, aux: 0.1f)
            .Key(LayerClawBack, 11f, rot: -0.58f, ox: 20f, oy: 9f, aux: 0.05f)
            .Key(LayerClawBack, 14f, rot: -0.6f, ox: 18f, oy: 10f, aux: 0.05f)
            .Key(LayerClawBack, 21f, rot: -0.26f, ox: 8f, oy: 4f)
            .Key(LayerClawBack, 26f)
            .Key(LayerBody, 0f, rot: 0.08f, oy: 4f, aux: 0.35f)
            .Key(LayerBody, 4f, rot: 0.02f, oy: -6f, aux: -0.15f, ease: EaseMode.EaseOut) // подбросило
            .Key(LayerBody, 10f, rot: 0.04f, oy: 3f, aux: 0.14f)
            .Key(LayerBody, 17f, rot: -0.01f, oy: -1f, aux: -0.04f)
            .Key(LayerBody, 26f)
            .Key(LayerCrown, 0f, oy: 3f, rot: 0.12f)
            .Key(LayerCrown, 4f, oy: -9f, rot: -0.16f)                          // подскок и два перелёта
            .Key(LayerCrown, 11f, oy: 3f, rot: 0.08f)
            .Key(LayerCrown, 18f, oy: -1f, rot: -0.03f)
            .Key(LayerCrown, 26f)
            .Key(LayerLegs, 0f, oy: 6f, aux: 0.18f)
            .Key(LayerLegs, 8f, oy: 3f, aux: 0.12f)
            .Key(LayerLegs, 26f)
            .Key(LayerEyes, 0f, aux: 0.6f)
            .Key(LayerEyes, 26f);

        // ------------------------------------------------------------------------------------
        //  ПОДКОП: ТРИ КЛИПА ВМЕСТО СТАТИЧНОГО «TUCKED»
        // ------------------------------------------------------------------------------------

        // Ныряет РУКАМИ: клешни вбиваются в грунт вперёд-вниз, гребут назад, тело идёт носом вниз.
        private static AnimClip BuildBurrowDive() => new AnimClip("burrow_dive", BurrowSinkTicks, loop: false)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 4f, rot: -1f, ox: 24f, oy: 40f, aux: 0.3f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 10f, rot: -0.85f, ox: -8f, oy: 34f, aux: -0.1f) // гребок назад
            .Key(LayerClawFront, 16f, rot: -0.78f, ox: -12f, oy: 31f, aux: -0.25f)
            .Key(LayerClawFront, 22f, rot: -0.75f, ox: -12f, oy: 30f, aux: -0.25f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 6f, rot: -0.95f, ox: 20f, oy: 38f, aux: 0.3f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 13f, rot: -0.84f, ox: -6f, oy: 33f, aux: -0.1f)
            .Key(LayerClawBack, 22f, rot: -0.75f, ox: -12f, oy: 30f, aux: -0.25f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 4f, rot: -0.04f, oy: -3f, aux: -0.08f)              // anticipation
            .Key(LayerBody, 10f, rot: 0.2f, oy: 6f, aux: 0.2f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 22f, rot: 0.14f, oy: 4f, aux: 0.15f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 10f, rot: 0.2f, oy: 4f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 10f, oy: 4f, aux: 0.15f)
            .Key(LayerLegs, 22f, oy: 6f, aux: 0.1f, aux2: 1f)                   // лапы больше не шагают
            .Event(4f, "burrow_dig");

        // Под землёй король ГРЕБЁТ: клешни попеременно (фазы 0 и 24), тело волнообразно кренится.
        // Игрок этого не видит — но метки гребков задают РИТМ пыли на поверхности.
        private static AnimClip BuildBurrowSwim() => new AnimClip("burrow_swim", BurrowSwimClipTicks, loop: true)
            .Key(LayerClawFront, 0f, rot: -0.55f, ox: 12f, oy: 26f, aux: -0.1f)
            .Key(LayerClawFront, 12f, rot: -0.8f, ox: -14f, oy: 32f, aux: -0.25f)
            .Key(LayerClawFront, 24f, rot: -0.75f, ox: -12f, oy: 30f, aux: -0.2f)
            .Key(LayerClawFront, 36f, rot: -0.6f, ox: 6f, oy: 27f, aux: -0.15f)
            .Key(LayerClawBack, 0f, rot: -0.78f, ox: -13f, oy: 31f, aux: -0.25f)
            .Key(LayerClawBack, 12f, rot: -0.62f, ox: 4f, oy: 27f, aux: -0.15f)
            .Key(LayerClawBack, 24f, rot: -0.55f, ox: 12f, oy: 26f, aux: -0.1f)
            .Key(LayerClawBack, 36f, rot: -0.8f, ox: -14f, oy: 32f, aux: -0.25f)
            .Key(LayerBody, 0f, rot: 0.06f, aux: 0.1f)
            .Key(LayerBody, 16f, rot: -0.06f, aux: 0.05f)
            .Key(LayerBody, 32f, rot: 0.04f, aux: 0.12f)
            .Key(LayerLegs, 0f, oy: 6f, aux2: 1f)
            .Event(2f, "burrow_stroke")
            .Event(26f, "burrow_stroke");

        // Вылет: краб РАСКРЫВАЕТСЯ как взрыв, корона взлетает, потом группируется к падению.
        private static AnimClip BuildBurrowErupt() => new AnimClip("burrow_erupt", BurrowEruptClipTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -0.78f, ox: -13f, oy: 31f, aux: -0.25f)
            .Key(LayerClawFront, 3f, rot: 0.9f, ox: -14f, oy: -30f, aux: 0.8f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 10f, rot: 1.05f, ox: -16f, oy: -34f, aux: 0.85f)
            .Key(LayerClawFront, 20f, rot: 0.2f, ox: -6f, oy: -6f, aux: 0.3f)
            .Key(LayerClawFront, 34f, rot: -0.9f, ox: 20f, oy: 34f, aux: 0f)    // стык с jump_fall
            .Key(LayerClawBack, 0f, rot: -0.78f, ox: -13f, oy: 31f, aux: -0.25f)
            .Key(LayerClawBack, 5f, rot: 0.86f, ox: -14f, oy: -28f, aux: 0.8f, ease: EaseMode.EaseOut)
            .Key(LayerClawBack, 12f, rot: 1f, ox: -16f, oy: -32f, aux: 0.85f)
            .Key(LayerClawBack, 22f, rot: 0.16f, ox: -6f, oy: -4f, aux: 0.3f)
            .Key(LayerClawBack, 34f, rot: -0.86f, ox: 14f, oy: 32f, aux: 0f)
            .Key(LayerBody, 0f, rot: 0.14f, oy: 4f, aux: 0.15f)
            .Key(LayerBody, 3f, rot: -0.14f, oy: -8f, aux: -0.3f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 12f, rot: -0.1f, oy: -5f, aux: -0.24f)
            .Key(LayerBody, 22f, rot: 0.04f, aux: -0.05f)
            .Key(LayerBody, 34f, rot: 0.16f, aux: 0.15f)
            .Key(LayerCrown, 0f, oy: 2f)
            .Key(LayerCrown, 10f, oy: -10f, aux: 0.4f)
            .Key(LayerCrown, 22f, oy: -3f, aux: 0.15f)
            .Key(LayerCrown, 34f, oy: 5f)
            .Key(LayerLegs, 0f, oy: 6f, aux2: 1f)
            .Key(LayerLegs, 10f, oy: -4f, aux: 0.2f, aux2: 1f)
            .Key(LayerLegs, 34f, aux2: 1f)
            .Key(LayerEyes, 0f, aux: 0.3f)
            .Key(LayerEyes, 8f, aux: 0.8f)
            .Key(LayerEyes, 34f, aux: 0.4f)
            .Event(1f, "burrow_out");

        // Посадка после вылета/подкопа — один-шот, чтобы не удлинять боевую стадию.
        private static AnimClip BuildBurrowLand() => new AnimClip("burrow_land", RecoverTicks, loop: false)
            .Envelope(1f, 6f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 3f, oy: 12f, aux: 0.45f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 8f, oy: -4f, aux: -0.12f)
            .Key(LayerBody, 14f, oy: 3f, aux: 0.12f)
            .Key(LayerBody, 20f, oy: -1f, aux: -0.03f)
            .Key(LayerBody, 26f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 3f, rot: -0.3f, ox: 12f, oy: 18f, aux: -0.1f)
            .Key(LayerClawFront, 12f, rot: -0.08f, ox: 3f, oy: 4f)
            .Key(LayerClawFront, 26f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 5f, rot: -0.28f, ox: 10f, oy: 16f, aux: -0.1f)
            .Key(LayerClawBack, 14f, rot: -0.06f, ox: 2f, oy: 3f)
            .Key(LayerClawBack, 26f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 3f, oy: 7f)
            .Key(LayerCrown, 9f, oy: -8f, rot: 0.16f)
            .Key(LayerCrown, 16f, oy: 2f, rot: -0.04f)
            .Key(LayerCrown, 26f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 4f, oy: 7f, aux: 0.2f)
            .Key(LayerLegs, 14f, oy: 2f, aux: 0.08f)
            .Key(LayerLegs, 26f);

        // ------------------------------------------------------------------------------------
        //  НАСТРОЕНЧЕСКИЕ ПОЗЫ
        // ------------------------------------------------------------------------------------

        // «Гордая поза»: третий ключ со сдвигом фазы, чтобы дыхание не было симметричной
        // синусоидой. Корона живёт своим ритмом (её период доводит UpdateBreathing).
        private static AnimClip BuildProud() => new AnimClip("proud", 100f, loop: true)
            .Key(LayerBody, 0f, rot: -0.06f, oy: -8f, aux: -0.18f)
            .Key(LayerBody, 42f, rot: -0.05f, oy: -10f, aux: -0.2f)
            .Key(LayerBody, 66f, rot: -0.045f, oy: -11f, aux: -0.18f)
            .Key(LayerClawFront, 0f, rot: 0.5f, ox: -6f, oy: -12f, aux: 0.35f)
            .Key(LayerClawFront, 44f, rot: 0.55f, ox: -6f, oy: -15f, aux: 0.38f)
            .Key(LayerClawFront, 72f, rot: 0.52f, ox: -6f, oy: -13f, aux: 0.35f)
            .Key(LayerClawBack, 0f, rot: 0.55f, ox: -6f, oy: -15f, aux: 0.35f)
            .Key(LayerClawBack, 38f, rot: 0.5f, ox: -6f, oy: -12f, aux: 0.32f)
            .Key(LayerClawBack, 64f, rot: 0.56f, ox: -6f, oy: -16f, aux: 0.36f)
            .Key(LayerCrown, 0f, oy: -2f)
            .Key(LayerCrown, 50f, rot: 0.03f, oy: -4f)
            .Key(LayerEyes, 0f, aux: 0.15f)
            .Key(LayerEyes, 50f, aux: 0.25f);

        // Одиночный жест раз в ~4 секунды: щелчок пинцером и доворот корпуса к игроку.
        private static AnimClip BuildProudFlourish() => new AnimClip("proud_flourish", 44f, loop: false)
            .Envelope(4f, 8f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 12f, rot: 0.05f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 30f, rot: 0.04f)
            .Key(LayerBody, 44f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 10f, rot: 0.14f, oy: -6f, aux: 0.4f)
            .Key(LayerClawFront, 14f, aux: -0.15f, rot: 0.1f, oy: -4f)          // клац
            .Key(LayerClawFront, 22f, rot: 0.12f, oy: -5f, aux: 0.3f)
            .Key(LayerClawFront, 26f, aux: -0.1f, rot: 0.09f)
            .Key(LayerClawFront, 44f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 14f, rot: 0.06f, oy: -2f)
            .Key(LayerCrown, 44f);

        // «Недовольство»: на 28–30 тике голова уже НАЧИНАЕТ подниматься, чтобы кроссфейд
        // в стойку подхватил начатое движение, а не дёрнул из мёртвой позы.
        private static AnimClip BuildDispleased() => new AnimClip("displeased", SulkTicks, loop: false)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 14f, rot: 0.13f, oy: 9f, aux: 0.25f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 26f, rot: 0.14f, oy: 10f, aux: 0.25f)
            .Key(LayerBody, 30f, rot: 0.11f, oy: 7f, aux: 0.2f)                 // начало подъёма
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 14f, rot: -0.85f, ox: 2f, oy: 42f, aux: -0.25f)
            .Key(LayerClawFront, 30f, rot: -0.8f, ox: 2f, oy: 39f, aux: -0.22f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 17f, rot: -0.85f, ox: 2f, oy: 42f, aux: -0.25f)
            .Key(LayerClawBack, 30f, rot: -0.82f, ox: 2f, oy: 40f, aux: -0.24f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 16f, rot: 0.22f, ox: 7f, oy: 5f)
            .Key(LayerCrown, 30f, rot: 0.23f, ox: 8f, oy: 5f)
            .Event(14f, "sulk_sigh");

        // «Забота о короне» — 40 тиков = CrownCareTicks (было 25, и 15 тиков поза стояла).
        // Фазы: 0–10 вскидывание, 10–26 удержание с микро-поправками, 26–40 опускание.
        private static AnimClip BuildGuardCrown() => new AnimClip("guard_crown", CrownCareTicks, loop: false)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 10f, rot: 1.2f, ox: -14f, oy: -30f, ease: EaseMode.EaseOut)
            .Key(LayerClawBack, 14f, rot: 1.22f, ox: -14f, oy: -31f)            // подправляет корону
            .Key(LayerClawBack, 20f, rot: 1.18f, ox: -12f, oy: -29f)
            .Key(LayerClawBack, 26f, rot: 1.21f, ox: -15f, oy: -31f)
            .Key(LayerClawBack, 40f, rot: 0.2f, ox: -2f, oy: -4f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 12f, ox: 8f, oy: -6f, aux: 0.3f)
            .Key(LayerClawFront, 30f, ox: 7f, oy: -5f, aux: 0.28f)
            .Key(LayerClawFront, 40f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 12f, rot: 0.08f, ox: -7f, oy: 5f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 28f, rot: 0.09f, ox: -8f, oy: 6f)
            .Key(LayerBody, 40f, rot: 0.03f, ox: -3f, oy: 2f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 14f, ox: -6f, aux: 0.08f)
            .Key(LayerLegs, 40f);

        // ------------------------------------------------------------------------------------
        //  ЯРОСТЬ ОТ УРОНА
        // ------------------------------------------------------------------------------------

        // Лучший клип набора: три пересечения нуля. Метка rage_slam теперь ПОТРЕБЛЯЕТСЯ —
        // пыль, кольцо и звук уходят в кадр удара клешнёй, а не в момент получения урона.
        private static AnimClip BuildRage() => new AnimClip("rage", 26f, loop: false)
            .Envelope(2f, 6f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 7f, rot: -0.14f, oy: -14f, aux: -0.14f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 12f, rot: -0.13f, oy: -12f, aux: -0.12f)
            .Key(LayerBody, 16f, rot: 0.14f, oy: 6f, aux: 0.3f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 20f, oy: 2f, aux: -0.05f)
            .Key(LayerBody, 26f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 7f, rot: 0.9f, ox: -10f, oy: -24f, aux: 0.6f)
            .Key(LayerClawFront, 16f, rot: -1.1f, ox: 26f, oy: 44f, aux: -0.35f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 19f, rot: -1.16f, ox: 28f, oy: 48f, aux: -0.35f)  // перелёт
            .Key(LayerClawFront, 26f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 9f, rot: 0.5f, ox: -8f, oy: -12f, aux: 0.4f)
            .Key(LayerClawBack, 17f, rot: 0.62f, ox: -9f, oy: -16f, aux: 0.45f) // балансирует вверх
            .Key(LayerClawBack, 26f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 8f, rot: -0.14f, oy: -5f)
            .Key(LayerCrown, 17f, rot: 0.16f, oy: 3f)
            .Key(LayerCrown, 22f, rot: -0.05f, oy: -1f)
            .Key(LayerCrown, 26f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 16f, oy: 5f, aux: 0.16f)
            .Key(LayerLegs, 26f)
            .Key(LayerEyes, 0f, aux: 1f)
            .Key(LayerEyes, 20f, aux: 1f)
            .Key(LayerEyes, 26f, aux: 0.4f)
            .Event(16f, "rage_slam");

        // ------------------------------------------------------------------------------------
        //  «ЯРОСТЬ ОКЕАНА» — СВОИ ПОЗЫ ВМЕСТО ПЕРЕИСПОЛЬЗОВАННЫХ БОЕВЫХ
        // ------------------------------------------------------------------------------------

        // Разгорание: не позирует, а УПИРАЕТСЯ — клешни вбиты в грунт, всю тушу трясёт.
        private static AnimClip BuildRageIgnite() => new AnimClip("rage_ignite", RageIgniteTicks, loop: false)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 6f, rot: 0.1f, oy: 6f, aux: 0.22f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 13f, rot: 0.08f, oy: 7f, ox: -3f, aux: 0.2f)        // тряска туши
            .Key(LayerBody, 19f, rot: 0.11f, oy: 6f, ox: 3f, aux: 0.24f)
            .Key(LayerBody, 25f, rot: 0.07f, oy: 8f, ox: -2f, aux: 0.2f)
            .Key(LayerBody, 31f, rot: 0.1f, oy: 6f, ox: 2f, aux: 0.24f)
            .Key(LayerBody, 38f, rot: -0.06f, oy: -4f, aux: -0.1f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 45f, rot: -0.1f, oy: -8f, aux: -0.16f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 6f, rot: -0.95f, ox: 26f, oy: 42f, aux: -0.2f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 20f, rot: -0.98f, ox: 28f, oy: 44f, aux: -0.25f)
            .Key(LayerClawFront, 32f, rot: -0.94f, ox: 26f, oy: 42f, aux: -0.2f)
            .Key(LayerClawFront, 45f, rot: -0.2f, ox: 8f, oy: 10f, aux: 0.3f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 9f, rot: -0.92f, ox: 22f, oy: 40f, aux: -0.2f, ease: EaseMode.EaseIn)
            .Key(LayerClawBack, 24f, rot: -0.96f, ox: 24f, oy: 43f, aux: -0.25f)
            .Key(LayerClawBack, 36f, rot: -0.9f, ox: 22f, oy: 40f, aux: -0.2f)
            .Key(LayerClawBack, 45f, rot: -0.16f, ox: 6f, oy: 9f, aux: 0.3f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 12f, rot: 0.14f, oy: 3f)
            .Key(LayerCrown, 22f, rot: -0.12f, oy: 2f)
            .Key(LayerCrown, 32f, rot: 0.1f, oy: 3f)
            .Key(LayerCrown, 45f, rot: -0.06f, oy: -4f, aux: 0.4f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 10f, oy: 6f, aux: 0.2f)
            .Key(LayerLegs, 45f, oy: 4f, aux: 0.25f)
            .Key(LayerEyes, 0f, aux: 0.4f)
            .Key(LayerEyes, 45f, aux: 1f);

        // Походка ярости: ниже, шире, клешни выставлены вперёд. Раньше в ярости король
        // ходил тем же walk, что и на прогулке.
        private static AnimClip BuildRageHunt() => new AnimClip("rage_hunt", RageHuntClipTicks, loop: true)
            .Key(LayerBody, 0f, rot: 0.06f, oy: 6f, aux: 0.14f)
            .Key(LayerBody, 8f, rot: 0.1f, oy: 5f, aux: 0.08f)
            .Key(LayerBody, 20f, rot: 0.06f, oy: 7f, aux: 0.16f)
            .Key(LayerBody, 28f, rot: 0.02f, oy: 5f, aux: 0.08f)
            .Key(LayerClawFront, 0f, rot: -0.5f, ox: 26f, oy: 18f, aux: 0.3f)
            .Key(LayerClawFront, 14f, rot: -0.42f, ox: 30f, oy: 14f, aux: 0.4f)
            .Key(LayerClawFront, 28f, rot: -0.52f, ox: 24f, oy: 19f, aux: 0.28f)
            .Key(LayerClawBack, 0f, rot: -0.34f, ox: 14f, oy: 20f, aux: 0.25f)
            .Key(LayerClawBack, 18f, rot: -0.44f, ox: 20f, oy: 16f, aux: 0.35f)
            .Key(LayerClawBack, 32f, rot: -0.32f, ox: 13f, oy: 21f, aux: 0.22f)
            .Key(LayerCrown, 0f, rot: 0.06f, oy: 2f, aux: 0.3f)
            .Key(LayerCrown, 20f, rot: -0.06f, oy: 3f, aux: 0.4f)
            .Key(LayerLegs, 0f, oy: 5f, aux: 0.25f)
            .Key(LayerEyes, 0f, aux: 1f);

        // Удержание: пинцер СДАВЛИВАЕТ импульсами каждые ~8 тиков, тело подтягивает жертву
        // к морде, вторая клешня угрожающе поднята. Раньше здесь 27 тиков стоял grip_lunge.
        private static AnimClip BuildRageGrab() => new AnimClip("rage_grab", RageGrabHoldTicks, loop: false)
            .Key(LayerClawFront, 0f, rot: -0.75f, ox: 44f, oy: 16f, aux: 0.3f)
            .Key(LayerClawFront, 4f, rot: -0.76f, ox: 42f, oy: 16f, aux: -0.25f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 10f, rot: -0.74f, ox: 40f, oy: 15f, aux: -0.1f)
            .Key(LayerClawFront, 14f, rot: -0.76f, ox: 38f, oy: 15f, aux: -0.35f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 20f, rot: -0.74f, ox: 37f, oy: 14f, aux: -0.15f)
            .Key(LayerClawFront, 24f, rot: -0.77f, ox: 35f, oy: 14f, aux: -0.35f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 31f, rot: -0.74f, ox: 34f, oy: 13f, aux: -0.15f)
            .Key(LayerClawFront, 36f, rot: -0.78f, ox: 32f, oy: 13f, aux: -0.4f, ease: EaseMode.EaseIn)
            .Key(LayerClawFront, 45f, rot: -0.75f, ox: 32f, oy: 13f, aux: -0.2f)
            .Key(LayerClawBack, 0f, rot: 0.6f, ox: -10f, oy: -14f, aux: 0.5f)   // угрожающе поднята
            .Key(LayerClawBack, 16f, rot: 0.72f, ox: -12f, oy: -19f, aux: 0.6f)
            .Key(LayerClawBack, 30f, rot: 0.64f, ox: -11f, oy: -16f, aux: 0.55f)
            .Key(LayerClawBack, 45f, rot: 0.76f, ox: -12f, oy: -20f, aux: 0.65f)
            .Key(LayerBody, 0f, rot: 0.06f, ox: 8f, aux: 0.08f)
            .Key(LayerBody, 14f, rot: 0.02f, ox: 2f, aux: 0.14f)                // подтягивает к морде
            .Key(LayerBody, 24f, rot: -0.02f, ox: -2f, aux: 0.1f)
            .Key(LayerBody, 36f, rot: -0.06f, ox: -5f, aux: 0.16f)
            .Key(LayerBody, 45f, rot: -0.05f, ox: -5f, aux: 0.12f)
            .Key(LayerCrown, 0f, rot: 0.1f, aux: 0.4f)
            .Key(LayerCrown, 18f, rot: -0.05f, oy: -3f, aux: 0.5f)
            .Key(LayerCrown, 36f, rot: 0.06f, aux: 0.45f)
            .Key(LayerLegs, 0f, oy: 4f, aux: 0.22f)
            .Key(LayerEyes, 0f, aux: 1f)
            .Event(4f, "rage_squeeze")
            .Event(14f, "rage_squeeze")
            .Event(24f, "rage_squeeze")
            .Event(36f, "rage_squeeze");

        // Бросок: замах через плечо → выброс → перелёт руки. Один-шот в момент выпуска,
        // раньше игрок улетал, а краб стоял в позе захвата.
        private static AnimClip BuildRageThrow() => new AnimClip("rage_throw", RageThrowClipTicks, loop: false)
            .Envelope(1f, 3f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 2f, rot: 0.5f, ox: -18f, oy: -14f, ease: EaseMode.EaseIn)  // замах
            .Key(LayerClawFront, 6f, rot: -0.6f, ox: 22f, oy: 10f, aux: 0.5f)   // выброс
            .Key(LayerClawFront, 8f, rot: -0.7f, ox: 26f, oy: 14f, aux: 0.6f)   // перелёт руки
            .Key(LayerClawFront, 12f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 2f, rot: -0.1f, ox: -8f, aux: -0.1f)
            .Key(LayerBody, 6f, rot: 0.14f, ox: 10f, aux: 0.2f, ease: EaseMode.EaseIn)
            .Key(LayerBody, 12f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 6f, rot: 0.2f, oy: 4f)
            .Key(LayerCrown, 12f);

        // ------------------------------------------------------------------------------------
        //  СМЕРТЬ
        // ------------------------------------------------------------------------------------

        // Между «последним взглядом» (12) и оседанием (44) было 32 тика на два ключа.
        // Добавлены: дрожь (20), последнее усилие подняться (28), срыв (36).
        // Клешни падают ВРОЗЬ и активная (передняя) держится дольше: 68 против 52.
        private static AnimClip BuildDeath() => new AnimClip("death", DyingTicks, loop: false)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 12f, rot: -0.12f, oy: -10f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 20f, rot: -0.09f, oy: -9f, ox: -2f)                 // держится, но дрожит
            .Key(LayerBody, 28f, rot: -0.11f, oy: -11f, ox: 1f)                 // последнее усилие
            .Key(LayerBody, 36f, rot: -0.02f, oy: -2f, ease: EaseMode.EaseIn)   // срыв
            .Key(LayerBody, 44f, rot: 0.03f, oy: 4f, aux: 0.12f)
            .Key(LayerBody, 60f, rot: 0.12f, oy: 14f, aux: 0.22f)
            .Key(LayerBody, 78f, rot: 0.22f, oy: 25f, aux: 0.32f)
            .Key(LayerBody, 92f, rot: 0.28f, oy: 32f, aux: 0.4f, scale: 0.97f)
            .Key(LayerBody, 100f, rot: 0.28f, oy: 34f, aux: 0.4f, scale: 0.96f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 25f, rot: 0.15f, oy: -6f)                      // последний слабый подъём
            .Key(LayerClawFront, 68f, rot: -1.2f, ox: 28f, oy: 48f, aux: -0.2f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 52f, rot: -1.2f, ox: 28f, oy: 48f, aux: -0.2f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 50f, oy: 5f, aux: 0.2f)
            .Key(LayerLegs, 70f, oy: 8f, aux: 0.45f)                            // лапы разъезжаются наружу
            .Key(LayerLegs, 88f, ox: 10f, oy: 10f, aux: 0.6f, aux2: 1f)         // шагать больше не пытается
            .Key(LayerLegs, 100f, ox: 10f, oy: 10f, aux: 0.6f, aux2: 1f)
            .Key(LayerEyes, 0f, aux: 0.9f)
            .Key(LayerEyes, 35f, aux: 0.9f)
            .Key(LayerEyes, 60f, aux: 0.4f)
            .Key(LayerEyes, 82f, aux: 0.1f)
            .Key(LayerEyes, 84f, aux: 0.55f)                                    // последняя искра
            .Key(LayerEyes, 88f, aux: 0.1f)
            .Key(LayerEyes, 100f, aux: 0f)
            .Event(12f, "last_look")
            .Event(40f, "crown_falls")
            .Event(88f, "collapse");

        // «Последний взгляд» отдельным акцентом — накладывается поверх death по метке last_look
        private static AnimClip BuildLastLook() => new AnimClip("last_look", 40f, loop: false)
            .Envelope(3f, 12f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 12f, rot: -0.05f, oy: -6f, ease: EaseMode.EaseOut)
            .Key(LayerBody, 40f)
            .Key(LayerClawFront, 0f)
            .Key(LayerClawFront, 20f, rot: -0.12f, oy: 8f)
            .Key(LayerClawFront, 40f)
            .Key(LayerClawBack, 0f)
            .Key(LayerClawBack, 24f, rot: -0.12f, oy: 8f)
            .Key(LayerClawBack, 40f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 12f, aux: 0.35f)
            .Key(LayerEyes, 40f, aux: 0.1f);

        // ------------------------------------------------------------------------------------
        //  ПЕРЕХОД В ФАЗУ 2
        // ------------------------------------------------------------------------------------

        private static AnimClip BuildPhase2Transition() => new AnimClip("phase2_transition", 130f, loop: false)
            .Envelope(4f, 14f)
            .Key(LayerBody, 0f)
            .Key(LayerBody, 10f, rot: 0.06f, oy: 6f, ox: -3f)
            .Key(LayerBody, 20f, rot: -0.06f, oy: 7f, ox: 3f)
            .Key(LayerBody, 30f, rot: 0.07f, oy: 8f, ox: -3f)
            .Key(LayerBody, 40f, rot: -0.06f, oy: 8f, ox: 3f)
            .Key(LayerBody, 50f, rot: 0.05f, oy: 7f, ox: -2f)
            .Key(LayerBody, 60f, rot: -0.04f, oy: 6f)
            .Key(LayerBody, 85f, rot: -0.15f, oy: -20f, aux: -0.2f, ease: EaseMode.EaseOut) // вздыбился
            .Key(LayerBody, 108f, rot: -0.12f, oy: -16f, aux: -0.15f)
            .Key(LayerBody, 130f)
            .Key(LayerClawFront, 0f, rot: -0.4f, ox: -8f, oy: 12f, aux: -0.2f)  // сжат в тряске
            .Key(LayerClawFront, 60f, rot: -0.35f, ox: -7f, oy: 10f, aux: -0.15f)
            .Key(LayerClawFront, 85f, rot: 1.1f, ox: -14f, oy: -28f, aux: 0.75f, ease: EaseMode.EaseOut)
            .Key(LayerClawFront, 110f, rot: 1f, ox: -12f, oy: -25f, aux: 0.6f)
            .Key(LayerClawFront, 130f)
            .Key(LayerClawBack, 0f, rot: -0.4f, ox: -8f, oy: 12f, aux: -0.2f)
            .Key(LayerClawBack, 60f, rot: -0.35f, ox: -7f, oy: 10f, aux: -0.15f)
            .Key(LayerClawBack, 89f, rot: 1.1f, ox: -14f, oy: -26f, aux: 0.75f, ease: EaseMode.EaseOut)
            .Key(LayerClawBack, 113f, rot: 1f, ox: -12f, oy: -23f, aux: 0.6f)
            .Key(LayerClawBack, 130f)
            .Key(LayerCrown, 0f)
            .Key(LayerCrown, 12f, rot: 0.12f)
            .Key(LayerCrown, 24f, rot: -0.12f)
            .Key(LayerCrown, 36f, rot: 0.11f)
            .Key(LayerCrown, 48f, rot: -0.1f)
            .Key(LayerCrown, 60f, rot: -0.08f, aux: 0.2f)
            .Key(LayerCrown, 85f, rot: 0f, oy: -6f, aux: 0.8f)
            .Key(LayerCrown, 110f, aux: 0.55f)
            .Key(LayerCrown, 130f, aux: 0.3f)
            .Key(LayerEyes, 0f)
            .Key(LayerEyes, 40f, aux: 0.15f)
            .Key(LayerEyes, 52f, aux: 0.05f)                                    // мигнул и погас
            .Key(LayerEyes, 66f, aux: 0.65f)
            .Key(LayerEyes, 85f, aux: 1f)
            .Key(LayerEyes, 130f, aux: 0.6f)
            .Key(LayerLegs, 0f)
            .Key(LayerLegs, 60f, oy: 3f, aux: 0.1f)
            .Key(LayerLegs, 85f, oy: -5f, aux: 0.22f)
            .Key(LayerLegs, 130f)
            .Event(6f, "p2_quake")
            .Event(24f, "p2_quake")
            .Event(42f, "p2_quake")
            .Event(60f, "p2_ignite")
            .Event(77f, "p2_hush")                                              // обрыв эмбиента перед ударом
            .Event(85f, "p2_roar");
    }
}
