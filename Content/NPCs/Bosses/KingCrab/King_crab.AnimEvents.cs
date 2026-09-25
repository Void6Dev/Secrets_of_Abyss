using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using SoA.Common.Graphics;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ==========================================================================================
    //  ДИСПЕТЧЕР МЕТОК КЛИПОВ.
    //
    //  Раньше метки времени были авторены, но не потреблялись: `ConsumeAnimEvent` вызывался
    //  ровно один раз на весь бой. Вся инфраструктура «звук/пыль/тряска ровно в кадр удара»
    //  простаивала, поэтому 130-тиковый переход в фазу 2 шёл вообще без звука.
    //
    //  Правило: на метки вешается ТОЛЬКО КЛИЕНТСКАЯ КОСМЕТИКА. Боевые решения по-прежнему
    //  принимает синхронизированный Timer, поэтому кадрово точный импакт не стоит ни одного
    //  байта по сети. Диспетчер зовётся из AnimPlayer.OnEvent, а тот тикается в PreDraw —
    //  на выделенном сервере он не крутится вовсе.
    // ==========================================================================================
    public partial class King_crab
    {
        private void OnAnimEvent(string name)
        {
            if (Main.dedServ)
                return;

            switch (name)
            {
                // ---------- Разворот и урон ----------
                case "turn_plant": OnTurnPlant(); break;
                case "hurt_heavy": HitStop(3); break;

                // ---------- Слэм клешнёй ----------
                case "slam_apex": OnSlamApex(); break;
                case "slam_strike": OnSlamStrike(); break;
                case "slam_settle": OnSlamSettle(); break;

                // ---------- Рывок ----------
                case "sweep_coil": OnSweepCoil(); break;
                case "sweep_release": OnSweepRelease(); break;

                // ---------- Залп ----------
                case "volley_shot": OnVolleyShot(); break;

                // ---------- Прилив ----------
                case "tide_gather": OnTideGather(); break;

                // ---------- Прыжок ----------
                case "jump_squat": OnJumpSquat(); break;
                case "jump_impact": OnJumpImpactFrame(); break;

                // ---------- Захлоп ----------
                case "grip_snap": OnGripSnap(); break;

                // ---------- Рёв ----------
                case "roar_inhale": OnRoarInhale(); break;
                case "roar_peak": HitStop(4); break;
                case "roar_release": OnRoarRelease(); break;

                // ---------- Королевский приказ ----------
                case "crown_touch": OnCrownTouch(); break;
                case "crown_point": OnCrownPoint(); break;

                // ---------- Хлопок ----------
                case "clap_apex": OnClapApex(); break;
                case "clap_release": OnClapReleaseFrame(); break;

                // ---------- Подкоп ----------
                case "burrow_dig": OnBurrowDig(); break;
                case "burrow_stroke": OnBurrowStroke(); break;
                case "burrow_out": OnBurrowOut(); break;

                // ---------- Настроение ----------
                case "sulk_sigh": OnSulkSigh(); break;
                case "rage_slam": OnRageSlam(); break;
                case "rage_squeeze": OnRageSqueeze(); break;

                // ---------- Смерть ----------
                case "last_look": _anim.PlayOnce("last_look"); break;
                case "crown_falls": OnCrownFalls(); break;
                case "collapse": OnCollapse(); break;

                // ---------- Переход в фазу 2 ----------
                case "p2_quake": OnPhase2Quake(); break;
                case "p2_ignite": OnPhase2Ignite(); break;
                case "p2_hush": OnPhase2Hush(); break;
                case "p2_roar": OnPhase2Roar(); break;
            }
        }

        // ------------------------------------------------------------------------------------
        //  РАЗВОРОТ
        // ------------------------------------------------------------------------------------

        private void OnTurnPlant()
        {
            ScreenRumble(0.8f);
            SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.3f, Pitch = -0.5f }, NPC.Center);

            // Пыль из-под двух опорных ног, а не из абстрактного центра
            if (_legs == null)
                return;
            for (int s = 0; s < 2; s++)
            {
                Vector2 foot = _legs[s].Foot;
                SpawnSandBurst(foot - new Vector2(24f, 8f), 48, 10, 6, 2.5f, 1f, 4f);
            }
        }

        // ------------------------------------------------------------------------------------
        //  СЛЭМ
        // ------------------------------------------------------------------------------------

        // На пике замаха песчинки ПОДНИМАЮТСЯ к клешне — обратная гравитация читается
        // как «набирает силу» и заранее показывает, откуда придёт удар
        private void OnSlamApex()
        {
            Vector2 claw = _clawWristWorld[0];
            for (int i = 0; i < 14; i++)
            {
                Vector2 from = claw + Main.rand.NextVector2Circular(120f, 90f) + new Vector2(0f, 70f);
                Dust d = Dust.NewDustPerfect(from, DustID.Sand, (claw - from).SafeNormalize(Vector2.Zero) * 3.5f);
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.9f, 1.5f);
            }
            SoundEngine.PlaySound(SoundID.Item45 with { Volume = 0.35f, Pitch = -0.9f }, NPC.Center);
        }

        private void OnSlamStrike()
        {
            Vector2 impact = SlamImpactPoint();

            HitStop(4);
            // Удар идёт В ЗЕМЛЮ, значит и камеру бьёт вниз, а не вверх
            ScreenPunch(5.5f, 16, Vector2.UnitY);
            SpawnFlash(impact, 400f, Color.White, 2);
            SpawnCracks(impact, 4, 90f);
            SpawnGroundColumn(impact, 26, 9f);
            SpawnImpactDebris(impact, 16, 1f);
            SpawnDustCloud(impact, 160f, 7);
            ImpactLight(impact, ImpactLightColor, 2.2f);
            ScreenWave(impact, 0.35f, 26f);

            // Двойной фронт: второе кольцо меньшего радиуса с задержкой читается мощнее
            ScheduleFx(3, () => TriggerImpactRing(impact, 170f, 20f, 0.45f));
            // Звук слоями: удар → треск камня → шорох осыпающегося песка
            ScheduleFx(4, () => SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.8f, Volume = 0.7f }, impact));
            ScheduleFx(8, () => SoundEngine.PlaySound(SoundID.Item13 with { Pitch = -0.4f, Volume = 0.45f }, impact));
        }

        // Пыль оседает: вторая волна мелких частиц уже после удара
        private void OnSlamSettle()
        {
            Vector2 impact = SlamImpactPoint();
            ScheduleFx(20, () => SpawnSandBurst(impact - new Vector2(70f, 4f), 140, 8, 18, 2f, 0.4f, 2.5f, 0.7f, 1.1f));
        }

        // ------------------------------------------------------------------------------------
        //  РЫВОК
        // ------------------------------------------------------------------------------------

        // Взвод: стопы скребут грунт — краб упирается. Раньше пыль была только во время рывка.
        private void OnSweepCoil()
        {
            if (_legs == null)
                return;
            foreach (Leg leg in _legs)
                SpawnSandBurst(leg.Foot - new Vector2(14f, 6f), 28, 8, 2, 1.8f, 0.4f, 2.2f);
            SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.4f, Pitch = -0.6f }, NPC.Center);
        }

        private void OnSweepRelease()
        {
            HitStop(2);
            ScreenPunch(5f, 14, new Vector2(NPC.spriteDirection, 0f));
            SpawnFlash(_clawWristWorld[0], 220f, new Color(200, 230, 255), 3);
            SpawnDustCloud(NPC.Bottom - new Vector2(NPC.spriteDirection * 60f, 0f), 120f, 4, 0.8f);
        }

        // ------------------------------------------------------------------------------------
        //  ЗАЛП
        // ------------------------------------------------------------------------------------

        // Дульная вспышка, брызги отдачи веером назад и нарастающий откат: шестой пузырь
        // должен ощущаться как выхлоп, а не как первый
        private void OnVolleyShot()
        {
            int shot = Math.Clamp((int)StateData - 1, 0, VolleyShots - 1);
            bool last = shot >= VolleyShots - 1;
            Vector2 muzzle = FacingToWorld(new Vector2(150f, -30f));

            TriggerImpactRing(muzzle, last ? 140f : 60f, last ? 16f : 10f, 0.1f);
            SpawnFlash(muzzle, last ? 150f : 90f, new Color(150, 220, 255), 2);
            ScreenPunch(last ? 2.5f : 1.2f, last ? 8 : 5, new Vector2(-NPC.spriteDirection, 0f));
            SpawnWaterSpray(muzzle, last ? 12 : 5, last ? 9f : 6f, new Vector2(NPC.spriteDirection, -0.2f), 0.45f);
            ImpactLight(muzzle, TideLightColor, last ? 1.6f : 1f, last ? 10 : 7);

            for (int i = 0; i < (last ? 10 : 5); i++)
            {
                Dust d = Dust.NewDustPerfect(muzzle, DustID.Water,
                    new Vector2(-NPC.spriteDirection * Main.rand.NextFloat(1f, 4f),
                        Main.rand.NextFloatDirection() * 2.2f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.9f, 1.6f);
            }

            // Капли срываются с пинцера между выстрелами
            ScheduleFx(4, () =>
            {
                Dust drip = Dust.NewDustPerfect(muzzle + new Vector2(0f, 6f), DustID.Water, new Vector2(0f, 1.4f));
                drip.scale = 1.1f;
            });

            // Стоит в воде — всплеск под ним на каждом выстреле
            if (Collision.WetCollision(NPC.position, NPC.width, NPC.height))
                SpawnWaterColumn(4);
        }

        // ------------------------------------------------------------------------------------
        //  ПРИЛИВ
        // ------------------------------------------------------------------------------------

        // Вода начинает собираться ПО ВСЕЙ ШИРИНЕ арены, а не только под боссом:
        // раньше жест короля не был связан с результатом визуально никак
        private void OnTideGather()
        {
            for (int i = 0; i < 26; i++)
            {
                float x = NPC.Center.X + Main.rand.NextFloat(-TideWallStartDist, TideWallStartDist);
                float ground = FindGroundY(x, NPC.Bottom.Y - 200f, 600f, true);
                if (float.IsNaN(ground))
                    continue;
                Dust d = Dust.NewDustPerfect(new Vector2(x, ground), DustID.Water,
                    new Vector2(0f, -Main.rand.NextFloat(3f, 8f)));
                d.noGravity = Main.rand.NextBool();
                d.scale = Main.rand.NextFloat(1f, 1.8f);
            }
            ScreenRumble(1.4f);
            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.9f, Volume = 0.5f }, NPC.Center);
        }

        // ------------------------------------------------------------------------------------
        //  ПРЫЖОК
        // ------------------------------------------------------------------------------------

        // Пыль НА ПРИСЕДЕ, а не только на толчке: песок сдувается из-под краба кольцом наружу
        private void OnJumpSquat()
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 at = NPC.Bottom + new Vector2(side * 60f, -6f);
                for (int i = 0; i < 8; i++)
                {
                    Dust d = Dust.NewDustPerfect(at, GroundDustType(at),
                        new Vector2(side * Main.rand.NextFloat(1.5f, 4f), -Main.rand.NextFloat(0.3f, 1.4f)));
                    d.scale = Main.rand.NextFloat(1f, 1.6f);
                }
            }
            HitStop(3); // пауза перед взлётом — классика
        }

        private void OnJumpImpactFrame()
        {
            HitStop(6);
            Vector2 at = NPC.Bottom;
            SpawnFlash(at, 460f, Color.White, 3);
            SpawnCracks(at, 5, 130f);
            SpawnGroundColumn(at, 34, 12f);
            SpawnImpactDebris(at, 26, 1.25f);
            SpawnDustCloud(at, 260f, 12, 1.3f);
            ImpactLight(at, ImpactLightColor, 2.8f, 18);
            ScreenWave(at, 0.7f, 34f);

            // Третье кольцо с задержкой + оседающая пыль + затухающий гул
            ScheduleFx(4, () => TriggerImpactRing(at, 260f, 24f, 0.7f));
            ScheduleFx(6, () => SoundEngine.PlaySound(SoundID.Item13 with { Pitch = -0.5f }, at));
            ScheduleFx(20, () => SpawnSandBurst(at - new Vector2(110f, 4f), 220, 8, 26, 2.2f, 0.3f, 2.2f, 0.7f, 1.1f));
            for (int i = 1; i <= 6; i++)
            {
                int delay = i * 5;
                float strength = 1.6f * (1f - i / 7f);
                ScheduleFx(delay, () => ScreenRumble(strength)); // грунт оседает 30 тиков
            }
        }

        // ------------------------------------------------------------------------------------
        //  ЗАХЛОП
        // ------------------------------------------------------------------------------------

        // Щелчок пинцера в AI происходит по Timer <= 0, то есть на кадр ПОЗЖЕ пика клипа.
        // Косметику вешаем на метку — она приходит ровно в кадр смыкания.
        private void OnGripSnap()
        {
            Vector2 grip = _clawWristWorld[0];
            HitStop(6);
            _clawShake = ClawShakeTicks;
            ScreenPunch(3.5f, 12, new Vector2(NPC.spriteDirection, 0f));
            SpawnFlash(grip, 180f, new Color(255, 220, 200), 2);
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = -0.4f, Volume = 0.8f }, grip);
            SpawnSparks(grip, 14, 9f);
            ImpactLight(grip, SparkColor, 1.6f, 10);

            // Искры между половинками пинцера
            for (int i = 0; i < 12; i++)
            {
                Dust d = Dust.NewDustPerfect(grip, DustID.Iron, Main.rand.NextVector2Circular(4f, 4f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.7f, 1.3f);
            }

            // Промах: пинцер щёлкает в пустоту с эхом и брызгами
            if (!_attackConnected)
                ScheduleFx(9, () => SoundEngine.PlaySound(
                    SoundID.Tink with { Pitch = -0.7f, Volume = 0.35f }, grip));
        }

        // ------------------------------------------------------------------------------------
        //  РЁВ
        // ------------------------------------------------------------------------------------

        // Вдох: частицы ВТЯГИВАЮТСЯ к морде. Эффектнейший телеграф, стоит десяти строк.
        private void OnRoarInhale()
        {
            Vector2 mouth = FacingToWorld(new Vector2(90f, -20f));
            for (int i = 0; i < 30; i++)
            {
                Vector2 from = mouth + Main.rand.NextVector2CircularEdge(280f, 220f);
                Dust d = Dust.NewDustPerfect(from, Main.rand.NextBool(3) ? DustID.Water : DustID.Sand,
                    (mouth - from).SafeNormalize(Vector2.Zero) * Main.rand.NextFloat(4f, 7f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.8f, 1.4f);
            }
            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.6f, Volume = 0.55f }, NPC.Center);
        }

        private void OnRoarRelease()
        {
            HitStop(5);
            Vector2 mouth = FacingToWorld(new Vector2(90f, -20f));
            SpawnFlash(mouth, 320f, new Color(255, 210, 140), 3);
            ImpactLight(mouth, RoyalLightColor, 2.6f, 20);
            ScreenWaveFollow(0.9f, 45f);
            SpawnDustCloud(NPC.Bottom, 300f, 10, 1.4f); // рёв сдувает пыль из-под лап

            // Видимый конус звука: три расходящиеся дуги от морды
            for (int i = 0; i < 3; i++)
            {
                int delay = i * 4;
                float size = 200f + i * 130f;
                ScheduleFx(delay + 1, () => TriggerImpactRing(mouth, size, 18f, 0.6f));
            }
            for (int i = 1; i <= 5; i++)
            {
                int delay = i * 5;
                float strength = 1.8f * (1f - i / 6f);
                ScheduleFx(delay, () => ScreenRumble(strength)); // гул ещё 25 тиков после
            }
        }

        // ------------------------------------------------------------------------------------
        //  КОРОЛЕВСКИЙ ПРИКАЗ
        // ------------------------------------------------------------------------------------

        private void OnCrownTouch()
            => SoundEngine.PlaySound(SoundID.Tink with { Pitch = 0.5f, Volume = 0.4f }, NPC.Center);

        // Приказ: кольцо ОТ КОРОНЫ, короткая золотая вспышка и низкий гул
        private void OnCrownPoint()
        {
            Vector2 crown = CrownSeatWorld(out _);
            TriggerImpactRing(crown, 320f, 26f, 0.8f);
            SpawnFlash(crown, 520f, new Color(255, 205, 110), 10);
            ImpactLight(crown, RoyalLightColor, 2.4f, 24);
            SoundEngine.PlaySound(SoundID.Item29 with { Pitch = -0.5f, Volume = 0.6f }, NPC.Center);
            ScreenRumble(1.2f);
        }

        // ------------------------------------------------------------------------------------
        //  ХЛОПОК
        // ------------------------------------------------------------------------------------

        private void OnClapApex()
            => SoundEngine.PlaySound(SoundID.Item45 with { Pitch = -0.3f, Volume = 0.4f }, NPC.Center);

        private void OnClapReleaseFrame()
        {
            HitStop(5);
            Vector2 mid = FacingToWorld(new Vector2(60f, -10f));
            SpawnFlash(mid, 300f, Color.White, 2);
            for (int side = -1; side <= 1; side += 2)
                SpawnWaterSpray(mid, 18, 11f, new Vector2(side, -0.25f), 0.35f); // хлопок бьёт вбок
            SpawnDustCloud(NPC.Bottom, 200f, 8);
            ImpactLight(mid, TideLightColor, 2.2f, 16);
            ScreenWave(mid, 0.5f, 28f);

            // Хлопок бьёт ВБОК, а не сферой: сплющенное кольцо + горизонтальный разлёт песка
            SoAFlatRing(mid, 420f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 at = NPC.Bottom + new Vector2(side * 70f, -6f);
                for (int i = 0; i < 14; i++)
                {
                    Dust d = Dust.NewDustPerfect(at, GroundDustType(at),
                        new Vector2(side * Main.rand.NextFloat(3f, 9f), -Main.rand.NextFloat(0.2f, 2f)));
                    d.scale = Main.rand.NextFloat(1f, 1.8f);
                }
            }

            // Пыль из-под ВСЕХ восьми стоп, а не только из центра
            if (_legs != null)
            {
                foreach (Leg leg in _legs)
                    SpawnSandBurst(leg.Foot - new Vector2(18f, 6f), 36, 8, 4, 2.2f, 0.6f, 3f);
            }

            ScheduleFx(3, () => SoundEngine.PlaySound(
                SoundID.Item14 with { Pitch = -0.9f, Volume = 0.6f }, NPC.Center)); // схлопывание воздуха
        }

        // Сплющенное кольцо: обычное кольцо шейдера круглое, а хлопок расходится вбок
        private void SoAFlatRing(Vector2 pos, float width)
        {
            for (int i = 0; i < 3; i++)
            {
                int delay = i * 3;
                float w = width * (0.6f + i * 0.2f);
                ScheduleFx(delay + 1, () =>
                {
                    Color c = new Color(190, 235, 255) * 0.5f;
                    c.A = 0;
                    SpawnFlash(pos + new Vector2(0f, 0f), w * 0.5f, c, 5);
                });
            }
        }

        // ------------------------------------------------------------------------------------
        //  ПОДКОП
        // ------------------------------------------------------------------------------------

        // Края ямы должны осыпаться ВНУТРЬ: раньше песок по кромке летел вверх
        private void OnBurrowDig()
        {
            Vector2 rim = NPC.Bottom;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 10; i++)
                {
                    Vector2 at = rim + new Vector2(side * Main.rand.NextFloat(60f, 130f), -4f);
                    Dust d = Dust.NewDustPerfect(at, GroundDustType(at),
                        new Vector2(-side * Main.rand.NextFloat(1f, 3f), Main.rand.NextFloat(1f, 3.5f)));
                    d.scale = Main.rand.NextFloat(1f, 1.7f);
                }
            }
            SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.7f }, NPC.Center);
        }

        // Ритм пыли по гребкам, а не ровно каждые BurrowTrailInterval тиков:
        // ритмичная пыль читается как «под землёй кто-то живой»
        private void OnBurrowStroke()
        {
            if (!BurrowBuried)
                return;
            float surfaceY = SurfaceAbove(NPC.Center.X, NPC.Center.Y);
            SpawnSandBurst(new Vector2(NPC.Center.X - 40f, surfaceY - 4f), 80, 8, 6, 2.5f, 1.5f, 5f);
            ScreenRumble(1.1f);
        }

        private void OnBurrowOut()
        {
            HitStop(6);
            _sandyFeet = SandyFeetTicks;
            AddShellGrains(5);
            SpawnGroundColumn(NPC.Bottom, 40, 16f);
            SpawnFlash(NPC.Bottom, 380f, new Color(240, 215, 160), 3);
            SpawnImpactDebris(NPC.Bottom, 30, 1.5f);
            SpawnDustCloud(NPC.Bottom, 220f, 10, 1.2f);
            ImpactLight(NPC.Bottom, ImpactLightColor, 2.4f, 18);
            ScreenWave(NPC.Bottom, 0.8f, 36f);

            for (int i = 0; i < 16; i++)
            {
                Dust rock = Dust.NewDustPerfect(NPC.Bottom + Main.rand.NextVector2Circular(70f, 20f), DustID.Stone,
                    new Vector2(Main.rand.NextFloatDirection() * 5f, -Main.rand.NextFloat(6f, 15f)));
                rock.scale = Main.rand.NextFloat(1f, 1.7f);
            }
        }

        // ------------------------------------------------------------------------------------
        //  НАСТРОЕНИЕ
        // ------------------------------------------------------------------------------------

        // Тяжёлый выдох: звук и облачко пыли из-под морды
        private void OnSulkSigh()
        {
            Vector2 mouth = FacingToWorld(new Vector2(90f, 10f));
            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -1f, Volume = 0.4f }, NPC.Center);
            for (int i = 0; i < 8; i++)
            {
                Dust d = Dust.NewDustPerfect(mouth, GroundDustType(mouth),
                    new Vector2(NPC.spriteDirection * Main.rand.NextFloat(0.5f, 2f), Main.rand.NextFloat(0.2f, 1f)));
                d.scale = Main.rand.NextFloat(0.8f, 1.3f);
            }
        }

        // Пыль и кольцо в КАДР УДАРА клешнёй по земле. В ReactToDamage они срабатывали
        // мгновенно — за 16 тиков до самого удара.
        private void OnRageSlam()
        {
            Vector2 at = _clawWristWorld[0];
            HitStop(3);
            TriggerImpactRing(at, 200f, 22f, 0.5f);
            SpawnSandBurst(at - new Vector2(60f, 8f), 120, 10, 16, 3.5f, 2f, 7f);
            SpawnCracks(at, 2, 60f);
            SpawnFlash(at, 240f, Color.White, 2);
            SpawnImpactDebris(at, 8, 0.9f);
            SpawnDustCloud(at, 120f, 4);
            ImpactLight(at, RageLightColor, 2f);
            ScreenPunch(4f, 14, Vector2.UnitY);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.3f }, NPC.Center);
        }

        // Держа игрока, король СДАВЛИВАЕТ импульсами: раньше 45 тиков ровного удержания
        private void OnRageSqueeze()
        {
            Vector2 grip = FacingToWorld(new Vector2(RageGrabHoldX, RageGrabHoldY));
            ScreenPunch(2f, 6, new Vector2(NPC.spriteDirection, 0f));
            SpawnRageSparks(10, grip);
            SpawnFlash(grip, 160f, new Color(255, 60, 40), 3);
            SoundEngine.PlaySound(SoundID.Item17 with { Pitch = -0.5f, Volume = 0.6f }, grip);
        }

        // ------------------------------------------------------------------------------------
        //  СМЕРТЬ
        // ------------------------------------------------------------------------------------

        private void OnCrownFalls()
        {
            HitStop(6);
            ScreenPunch(2f, 20, Vector2.UnitY);
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = -0.5f, Volume = 0.9f }, NPC.Center);
        }

        private void OnCollapse()
        {
            HitStop(5);
            SpawnSandBurst(NPC.Bottom - new Vector2(90f, 8f), 180, 12, 24, 4f, 2f, 7f);
            SpawnCracks(NPC.Bottom, 3, 100f);
            SpawnImpactDebris(NPC.Bottom, 12, 0.8f);
            SpawnDustCloud(NPC.Bottom, 300f, 14, 0.8f);
            ScreenPunch(4f, 14, Vector2.UnitY);
            ScheduleFx(20, () => SpawnSandBurst(NPC.Bottom - new Vector2(120f, 4f), 240, 8, 22,
                2f, 0.3f, 2f, 0.7f, 1.1f));
        }

        // ------------------------------------------------------------------------------------
        //  ПЕРЕХОД В ФАЗУ 2
        // ------------------------------------------------------------------------------------

        // Нарастающая тряска и камешки, сыплющиеся с панциря по контуру
        private void OnPhase2Quake()
        {
            float elapsed = _p2QuakeStep++;
            ScreenRumble(MathHelper.Lerp(0.5f, 4f, MathHelper.Clamp(elapsed / 2f, 0f, 1f)));
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -1f, Volume = 0.35f + 0.15f * elapsed }, NPC.Center);

            for (int i = 0; i < 10; i++)
            {
                Vector2 at = AnimatedFacingToWorld(new Vector2(Main.rand.NextFloat(-100f, 100f), -55f) * NPC.scale);
                Dust d = Dust.NewDustPerfect(at, DustID.Stone, new Vector2(0f, Main.rand.NextFloat(1f, 3f)));
                d.scale = Main.rand.NextFloat(0.9f, 1.4f);
            }
        }

        private void OnPhase2Ignite()
        {
            _p2QuakeStep = 0;
            _wetTimer = WetTicks; // дальше вода стекает с панциря
            SpawnFlash(FacingToWorld(new Vector2(0f, -15f)), 300f, new Color(255, 120, 60), 4);
            SpawnWaterColumn(25);
            ImpactLight(FacingToWorld(new Vector2(0f, -15f)), new Color(255, 120, 60), 2.5f, 20);
            TriggerImpactRing(NPC.Center, 300f, 24f, 0.5f);
            SoundEngine.PlaySound(SoundID.Item74 with { Pitch = -0.5f }, NPC.Center);
        }

        // Резкая тишина на 8 тиков перед ударом — приём, который работает всегда
        private void OnPhase2Hush() => SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -1f, Volume = 0.25f }, NPC.Center);

        private void OnPhase2Roar()
        {
            HitStop(8);
            ScreenPunch(10f, 30, -Vector2.UnitY);
            TriggerImpactRing(NPC.Center, 560f, 36f, 0.7f);
            SpawnFlash(NPC.Center, 700f, Color.White, 5);
            SpawnWaterColumn(45);
            SpawnRageSparks(40);
            SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.6f }, NPC.Center);
            ScreenWaveFollow(1.1f, 60f, waves: 3);
            ImpactLight(NPC.Center, new Color(200, 230, 255), 3f, 30);
            SpawnWaterSpray(NPC.Bottom, 40, 16f, -Vector2.UnitY, 0.35f);
            SpawnImpactDebris(NPC.Bottom, 20, 1.3f);

            // Вертикальный столб воды на весь экран
            for (int i = 0; i < 60; i++)
            {
                Dust d = Dust.NewDustPerfect(NPC.Bottom + new Vector2(Main.rand.NextFloat(-70f, 70f), 0f),
                    DustID.Water, new Vector2(Main.rand.NextFloatDirection() * 2f, -Main.rand.NextFloat(12f, 26f)));
                d.noGravity = Main.rand.NextBool();
                d.scale = Main.rand.NextFloat(1.4f, 2.6f);
            }
            for (int i = 1; i <= 8; i++)
            {
                int delay = i * 5;
                float strength = 3f * (1f - i / 9f);
                ScheduleFx(delay, () => ScreenRumble(strength));
            }
        }

        // ------------------------------------------------------------------------------------
        //  ОБЩИЕ ЭМИТТЕРЫ
        // ------------------------------------------------------------------------------------

        // Вертикальный выброс грунта — в дополнение к горизонтальному разлёту
        private void SpawnGroundColumn(Vector2 at, int count, float speed)
        {
            if (Main.dedServ)
                return;
            Vector3 tint = GetGroundTint(at);
            int type = GroundDustType(at);
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(at + new Vector2(Main.rand.NextFloat(-30f, 30f), 0f), type,
                    new Vector2(Main.rand.NextFloatDirection() * 1.5f, -Main.rand.NextFloat(speed * 0.5f, speed)));
                d.scale = Main.rand.NextFloat(1.1f, 2f);
                d.color = new Color(tint.X, tint.Y, tint.Z);
                if (Main.rand.NextBool(3))
                    d.noGravity = true;
            }
        }
    }
}
