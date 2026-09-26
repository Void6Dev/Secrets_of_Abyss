using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Graphics.SandFormation;
using SoA.Common.Systems;
using SoA.Common.UI;
using SoA.Content.Items.Weapons;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Постановка появления и смерти.
    //
    // ПОЯВЛЕНИЕ — «корона из песка». Король идёт к игроку под грунтом (бугор, нарастающий гул),
    // останавливается перед ним, и наступает тишина: из песка медленно встаёт ОДНА корона.
    // Игрок видит, чья она, раньше, чем самого короля. Затем выход наружу, тяжёлое приземление,
    // рёв и надпись с именем. На повторных призывах — короткая версия без надписи.
    //
    // СМЕРТЬ — «корона уходит к достойному». Последний удар, по панцирю бегут светящиеся трещины,
    // корона слетает и катится к ногам игрока, туша рассыпается в песок сверху вниз, и из осыпи
    // песчинки собираются в Королевское копьё — ровно там, где оно и выпадет.
    //
    // Боевые решения (стадии, позиция, хитбокс) — в AI и синхронны по сети. Всё остальное —
    // косметика клиента по синхронизированным State/SubState/Timer.
    public partial class King_crab
    {
        // ---------- ПОЯВЛЕНИЕ: СТАДИИ ----------
        private const float IntroSubTravel = 0f;  // идёт под песком к игроку
        private const float IntroSubCrown = 1f;   // тишина: из грунта поднимается корона
        private const float IntroSubErupt = 2f;   // выход наружу и полёт
        private const float IntroSubStand = 2.5f; // приземлился, выпрямляется и смотрит на игрока
        private const float IntroSubRoar = 3f;    // рёв

        // ---------- ПОЯВЛЕНИЕ: ПАРАМЕТРЫ ----------
        // Первое появление целиком — около 10 секунд: подход ~2.8 с, тишина с короной 2.5 с,
        // выход, взгляд, рёв. Повторные призывы — короткая версия около 4 секунд
        private const float IntroStartDistance = 1400f;      // откуда начинается подземный ход
        private const float IntroStartDistanceShort = 700f;  // на повторных призывах
        private const float IntroStopDistance = 380f;        // выходит ПЕРЕД игроком, а не под ним
        private const float IntroTravelSpeed = 6f;           // медленно: бугор должен успеть прочитаться
        private const int IntroTravelMaxTicks = 340;         // предохранитель на подземный ход
        private const int IntroHushTicks = 150;              // тишина, пока встаёт корона
        private const int IntroHushTicksShort = 40;
        private const float IntroCrownRiseShare = 0.5f;      // за эту долю тишины корона выходит целиком
        private const float IntroTrembleShare = 0.22f;       // последняя доля тишины: корона дрожит, грунт гудит
        private const int IntroStandTicks = 40;              // выпрямился после приземления и смотрит
        private const int IntroStandTicksShort = 12;
        private const int IntroAfterRoarPause = 70;          // вдох после рёва: успеть прочитать имя
        private const int TitleCardTicks = 240;

        // ---------- СМЕРТЬ ----------
        // Ровный ритм, ~6.7 с: последний удар → взгляд и корона (клип замедлен) → оседание →
        // пауза, пока тлеют трещины → медленное рассыпание → копьё из песка → корона гаснет
        private const int DeathClipTicks = 100;       // клип death в своих тиках: взгляд, корона, оседание
        private const float DeathClipRate = 0.7f;     // клип смерти играется медленнее боевых: его смакуют
        private const int DeathCrownFallTick = (int)(40 / DeathClipRate);   // = метка crown_falls в реальных тиках
        private const int DeathClipEndTick = (int)(DeathClipTicks / DeathClipRate);
        private const int DeathDissolveStart = DeathClipEndTick + 48; // пауза после оседания: трещины тлеют
        private const int DeathDissolveEnd = DeathDissolveStart + 140;
        private const float DeathSpearFormSeconds = 1.6f;
        // Сборка копья кончается на последних тиках сцены — дальше его сменяет настоящая добыча
        private const int DeathSpearFormTick = DyingTicks - (int)(DeathSpearFormSeconds * 60f) - 2;
        private const int DeathCrownFadeStart = DyingTicks - 45; // корона у ног игрока рассыпается последней
        private const int DeathLootShrinkTick = 4;    // Timer, на котором хитбокс сжимается под добычу
        private const int DeathLootBox = 40;
        private const int DeathCrackCount = 10;
        private const int DeathCrackStagger = 9;      // трещины разгораются по очереди, а не разом
        private const int DeathCrackGrowTicks = 80;
        private const int DeathEmberInterval = 5;     // искры из трещин, пока король ещё цел
        private const int DeathSandGrainsPerColumn = 12; // одна песчинка на столько столбцов пикселей в строке
        private const int DeathSandMaxPerTick = 10;

        private static readonly Color DeathCrackColor = new(255, 190, 90);
        private static readonly Color CrownGoldColor = new(255, 205, 110);

        private struct DeathCrack
        {
            public Vector2 Local;   // точка на панцире в лицевых координатах (как BodyAnchorToWorld)
            public float Angle;
            public float Length;
            public int Delay;
        }

        private readonly List<DeathCrack> _deathCracks = new();

        // Пиксели панциря на CPU: песок сыплется только там, где у спрайта есть панцирь,
        // и его же цветом. Общие для всех экземпляров — текстура одна
        private static Color[] _bodyPixels;
        private static int _bodyPixelsWidth;
        private bool _deathSceneStarted;
        private bool _deathCrownReleased; // метка crown_falls пройдена
        private bool _spearFormStarted;

        private bool IntroShort => DownedBossSystem.downedKingCrab;
        private int IntroHush => IntroShort ? IntroHushTicksShort : IntroHushTicks;

        // 0 — тишина только началась, 1 — выход; с какого момента корона дрожит
        private float IntroHushProgress => 1f - Timer / Math.Max(1f, IntroHush);
        private float IntroTremble => MathHelper.Clamp((IntroHushProgress - (1f - IntroTrembleShare)) / IntroTrembleShare, 0f, 1f);
        private float DeathElapsed => DyingTicks - Timer;

        #region Появление: AI

        // Сцену ставит тот, кто создал NPC (сервер или одиночная игра): клиенты получат
        // стейт и позицию по сети вместе с первой синхронизацией
        public override void OnSpawn(IEntitySource source)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            NPC.TargetClosest(false);
            Player target = Main.player[NPC.target];
            int side = Math.Sign(NPC.Center.X - target.Center.X);
            if (side == 0)
                side = -1;

            // Прячем тушу в толщу грунта в стороне от игрока — оттуда начинается подземный ход
            float startX = target.Center.X + side * (IntroShort ? IntroStartDistanceShort : IntroStartDistance);
            float surfaceY = SurfaceAbove(startX, target.Bottom.Y);
            NPC.Center = new Vector2(startX, surfaceY + BurrowDepth);
            NPC.velocity = Vector2.Zero;
            NPC.noTileCollide = true;

            EnterState(CrabState.Intro, IntroTravelMaxTicks, IntroSubTravel);
            StateData = side; // сторона, с которой король подходит; дальше слот займёт точка выхода
        }

        private void AIIntro(Player target)
        {
            switch (SubState)
            {
                case IntroSubTravel: IntroTravel(target); break;
                case IntroSubCrown: IntroCrown(); break;
                case IntroSubErupt: IntroErupt(); break;
                case IntroSubStand: IntroStand(target); break;
                default: IntroRoar(target); break;
            }
        }

        // 1. Подземный ход: бугор с пылью бежит к игроку, гул нарастает с приближением
        private void IntroTravel(Player target)
        {
            NPC.noTileCollide = true;

            int side = StateData >= 0f ? 1 : -1;
            float goalX = target.Center.X + side * IntroStopDistance;
            float goalY = SurfaceAbove(goalX, target.Bottom.Y) + BurrowDepth;
            Vector2 desired = (new Vector2(goalX, goalY) - NPC.Center).SafeNormalize(Vector2.UnitY) * IntroTravelSpeed;
            NPC.velocity = Vector2.Lerp(NPC.velocity, desired, BurrowTravelTurn);

            float distX = Math.Abs(goalX - NPC.Center.X);
            float closeness = 1f - MathHelper.Clamp(distX / BurrowRumbleRange, 0f, 1f);
            if (EveryTicks(BurrowRumbleInterval))
                ScreenRumble(MathHelper.Lerp(BurrowRumbleMin, BurrowRumbleMax * 0.8f, closeness));
            if (EveryTicks(BurrowTrailInterval))
                SpawnSurfaceTrail(NPC.Center.X, target.Center.Y, closeness);
            if (EveryTicks(24))
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.2f + 0.4f * closeness, Pitch = -1f }, NPC.Center);

            if (distX > BurrowExitTolerance && Timer > 0f)
                return;

            StateData = NPC.Center.X; // точка выхода зафиксирована
            EnterSubState(IntroSubCrown, IntroHush);
        }

        // 2. Тишина. Ни гула, ни тряски — только песок, осыпающийся с поднимающейся короны.
        // Сама корона — косметика (UpdateCrownEmerging), король тем временем подбирается под неё
        private void IntroCrown()
        {
            NPC.noTileCollide = true;

            float surfaceY = SurfaceAbove(StateData, NPC.Center.Y);
            Vector2 goal = new Vector2(StateData, surfaceY + BurrowWarnDepth);
            NPC.velocity = Vector2.Lerp(NPC.velocity, (goal - NPC.Center) * 0.12f, 0.3f);

            if (EveryTicks(5))
                SpawnSandBurst(new Vector2(StateData - 24f, surfaceY - 6f), 48, 6, 2, 1.2f, 0.5f, 2f);

            // Конец тишины: под короной что-то огромное шевелится — гул возвращается,
            // грунт вокруг вздрагивает и плюётся песком всё чаще
            float tremble = IntroTremble;
            if (tremble > 0f)
            {
                if (EveryTicks(6))
                    ScreenRumble(MathHelper.Lerp(0.8f, BurrowRumbleMax * 0.7f, tremble));
                if (EveryTicks(Math.Max(2, 9 - (int)(7f * tremble))))
                {
                    float x = StateData + Main.rand.NextFloatDirection() * 110f;
                    SpawnSandBurst(new Vector2(x - 20f, surfaceY - 6f), 40, 6, 4, 2f, 2f, 5f + 4f * tremble);
                }
                if (EveryTicks(20))
                    SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.25f + 0.35f * tremble, Pitch = -1f }, NPC.Center);
            }

            if (Timer > 0f)
                return;

            EruptFromGround(new Vector2(StateData, surfaceY));
            EnterSubState(IntroSubErupt, BurrowMaxAir);
        }

        // 3. Выход наружу и тяжёлое приземление
        private void IntroErupt()
        {
            NPC.velocity.Y += Gravity * 0.6f;
            if (NPC.velocity.Y > 0f)
                NPC.noTileCollide = false; // на спуске снова ловим землю

            SpawnSandBurst(NPC.Bottom - new Vector2(60f, 6f), 120, 10, 4, 3f, 2f, 6f);

            if (!Landed() && Timer > 0f)
                return;

            LandFromEruption();
            EnterSubState(IntroSubStand, IntroShort ? IntroStandTicksShort : IntroStandTicks);
        }

        // 4. Пауза: выпрямился, стряхивает песок и смотрит на того, кто его позвал
        private void IntroStand(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            if (EveryTicks(7))
                SpawnSandBurst(NPC.Center - new Vector2(90f, 60f), 180, 60, 3, 1f, -1.5f, 0.5f); // песок осыпается с панциря

            if (Timer <= 0f)
                EnterSubState(IntroSubRoar, RoarWindupTicks);
        }

        // 5. Рёв (клип roar: вдох, пик) и выпуск — с именем на экране при первой встрече
        private void IntroRoar(Player target)
        {
            ApplyGravity();
            Brake();
            FaceTarget(target);

            if (Timer > 0f)
                return;

            SoundEngine.PlaySound(SoundID.Roar with { Pitch = -0.2f }, NPC.Center);
            TriggerImpactRing(NPC.Center, 480f, 36f, 0.8f);
            ScreenPunch(8f, 26);
            if (!Main.dedServ)
            {
                PlayClip("roar_release", once: true); // свет, волна-преломление, пыль из-под лап
                if (!IntroShort)
                {
                    BossTitleCard.Show(Language.GetText("Mods.SoA.Misc.KingCrabTitle"),
                        Language.GetText("Mods.SoA.Misc.KingCrabSubtitle"), TitleCardTicks);
                }
            }
            EnterState(CrabState.Scuttle, IntroAfterRoarPause);
        }

        #endregion

        #region Смерть

        // Сжимает хитбокс вокруг центра: добыча падает в точку, а не по всей туше
        private void ShrinkHitbox(int size)
        {
            Vector2 center = NPC.Center;
            NPC.width = size;
            NPC.height = size;
            NPC.Center = center;
        }

        // Доля рассыпания туши: 0 — цела, 1 — осыпалась целиком. Медленно трогается,
        // разгоняется и медленно доходит до ног — без рывка на старте и обрыва в конце
        private float DeathWipe()
        {
            if (State != CrabState.Dying)
                return 0f;
            float t = MathHelper.Clamp((DeathElapsed - DeathDissolveStart) / (float)(DeathDissolveEnd - DeathDissolveStart), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        // Скорость, с которой играет базовый клип: темп атаки, а в смерти — замедленно
        private float BaseClipRate => State == CrabState.Dying ? DeathClipRate : ActionTempo;

        // Прозрачность ног и клешней: гаснут чуть быстрее, чем осыпается панцирь
        private float DeathPartOpacity() => 1f - MathHelper.Clamp(DeathWipe() * 1.4f, 0f, 1f);

        // Тикается из TickVisuals (клиент). Старт сцены ловим по смене стейта, а не в CheckDead:
        // в сетевой игре смерть решает сервер, клиент узнаёт о ней из синхронизации
        private void UpdateDeathScene()
        {
            if (State != CrabState.Dying)
            {
                _deathSceneStarted = false;
                _spearFormStarted = false;
                _deathCrownReleased = false;
                return;
            }

            if (!_deathSceneStarted)
            {
                _deathSceneStarted = true;
                OnFinalBlow();
            }

            float wipe = DeathWipe();
            if (wipe > 0f && wipe < 1f)
                PourSandFromWipe(wipe);
            else if (wipe <= 0f && DeathElapsed > 20f && EveryTicks(DeathEmberInterval))
                SpawnCrackEmber();

            if (!_spearFormStarted && DeathElapsed >= DeathSpearFormTick)
            {
                _spearFormStarted = true;
                SpawnDustCloud(NPC.Bottom, 260f, 10, 0.7f); // осыпь оседает
                SandFormationEffect.StartForItem(NPC.Center, ModContent.ItemType<RoyalSpear>(), NPC.whoAmI * 7919 + 17)
                    .Play(DeathSpearFormSeconds);
                SoundEngine.PlaySound(SoundID.Item8 with { Pitch = -0.4f, Volume = 0.7f }, NPC.Center);
            }
        }

        // Последний удар: долгая пауза в кадре удара, вспышка, и по панцирю бегут трещины
        private void OnFinalBlow()
        {
            HitStop(HitStopCap);
            ScreenPunch(9f, 30);
            SpawnFlash(NPC.Center, 520f, Color.White, 4);
            ImpactLight(NPC.Center, DeathCrackColor, 3f, 34);
            ScreenWaveFollow(0.8f, 40f);
            SoundEngine.PlaySound(SoundID.NPCDeath1 with { Pitch = -0.6f }, NPC.Center);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.9f, Volume = 0.8f }, NPC.Center);

            // Трещины: разбросаны по панцирю, разгораются по очереди
            _deathCracks.Clear();
            for (int i = 0; i < DeathCrackCount; i++)
            {
                _deathCracks.Add(new DeathCrack
                {
                    Local = new Vector2(Main.rand.NextFloat(-95f, 95f), Main.rand.NextFloat(-45f, 30f)),
                    Angle = Main.rand.NextFloat(MathHelper.TwoPi),
                    Length = Main.rand.NextFloat(28f, 70f),
                    Delay = i * DeathCrackStagger,
                });
            }
        }

        // Песок сыплется с линии, которая ползёт по панцирю сверху вниз. Песчинки берутся из
        // РЕАЛЬНЫХ пикселей панциря в этой строке спрайта: сыплется только там, где панцирь есть,
        // его же цветом и с плотностью по ширине строки. Раньше песок сыпался равномерной
        // полосой на всю ширину — у купола панциря он висел в воздухе по бокам
        private void PourSandFromWipe(float wipe)
        {
            if (!EnsureBodyPixels())
                return;

            Texture2D tex = TextureAssets.Npc[Type].Value;
            int frameHeight = tex.Height / Math.Max(1, Main.npcFrameCount[Type]);
            int row = Math.Clamp((int)(frameHeight * wipe), 0, frameHeight - 1);
            int rowStart = (NPC.frame.Y + row) * _bodyPixelsWidth;

            Span<short> solid = stackalloc short[_bodyPixelsWidth];
            int solidCount = 0;
            for (int x = 0; x < _bodyPixelsWidth; x++)
            {
                if (_bodyPixels[rowStart + x].A > 32)
                    solid[solidCount++] = (short)x;
            }
            if (solidCount == 0)
                return;

            BodySpriteTransform(out Vector2 center, out float rotation, out Vector2 scale);
            bool flipped = NPC.spriteDirection == 1; // так же, как в DrawBodySprite
            int grains = Math.Clamp(solidCount / DeathSandGrainsPerColumn, 1, DeathSandMaxPerTick);

            for (int i = 0; i < grains; i++)
            {
                int column = solid[Main.rand.Next(solidCount)];
                float localX = column + 0.5f - _bodyPixelsWidth / 2f;
                if (flipped)
                    localX = -localX;
                Vector2 local = new Vector2(localX * scale.X, (row - frameHeight / 2f) * scale.Y);
                Vector2 at = center + local.RotatedBy(rotation);

                // Цвет пикселя панциря, чуть присыпанный песком — это сам панцирь осыпается
                Color shell = _bodyPixels[rowStart + column];
                Color color = Color.Lerp(shell, SoAVfx.TideSand, Main.rand.NextFloat(0.1f, 0.45f));
                color.A = 255;
                bool chunk = Main.rand.NextBool(6); // изредка откалывается кусок покрупнее
                SoAParticles.SpawnDebris(at, new Vector2(Main.rand.NextFloatDirection() * (chunk ? 2.2f : 1.2f),
                        Main.rand.NextFloat(-1.2f, 0.4f)), color,
                    chunk ? Main.rand.NextFloat(4.5f, 7f) : Main.rand.NextFloat(2f, 4f), Main.rand.Next(120, 170));
            }

            if (EveryTicks(4))
            {
                int column = solid[Main.rand.Next(solidCount)];
                float localX = (column - _bodyPixelsWidth / 2f) * (flipped ? -1f : 1f);
                Vector2 at = center + new Vector2(localX * scale.X, (row - frameHeight / 2f) * scale.Y).RotatedBy(rotation);
                SoAParticles.SpawnSmoke(at, new Vector2(Main.rand.NextFloatDirection() * 0.5f, -0.25f),
                    SoAVfx.TideSand, 40f, 110f, 0.3f, 70);
            }
            if (EveryTicks(10))
                SoundEngine.PlaySound(SoundID.Item13 with { Pitch = -0.6f, Volume = 0.3f }, NPC.Center);
        }

        // Пока король ещё цел, из трещин срываются тлеющие искры — пауза после оседания не пустая
        private void SpawnCrackEmber()
        {
            if (_deathCracks.Count == 0)
                return;
            DeathCrack crack = _deathCracks[Main.rand.Next(_deathCracks.Count)];
            if (DeathElapsed - crack.Delay < DeathCrackGrowTicks * 0.5f)
                return;

            Vector2 at = BodyAnchorToWorld(crack.Local);
            SoAParticles.SpawnStreak(at, new Vector2(Main.rand.NextFloatDirection() * 0.8f, -Main.rand.NextFloat(1.5f, 3.2f)),
                DeathCrackColor, 2.2f, gravity: -0.02f, life: Main.rand.Next(24, 40), lengthPerSpeed: 2f);
            SoAParticles.AddLight(at, DeathCrackColor, 0.6f, 6);
        }

        private bool EnsureBodyPixels()
        {
            if (_bodyPixels != null)
                return true;
            Texture2D tex = TextureAssets.Npc[Type].Value;
            if (tex == null)
                return false;
            _bodyPixels = new Color[tex.Width * tex.Height];
            tex.GetData(_bodyPixels);
            _bodyPixelsWidth = tex.Width;
            return true;
        }

        // Рисовать внутри BeginAdditive: трещины светятся изнутри панциря
        private void DrawDeathCracks(SpriteBatch sb)
        {
            if (State != CrabState.Dying || _deathCracks.Count == 0)
                return;

            float wipe = DeathWipe();
            Texture2D tex = TextureAssets.Npc[Type].Value;
            float halfH = tex == null ? 0f : tex.Height / Math.Max(1, Main.npcFrameCount[Type]) / 2f;
            float wipeLocalY = -halfH + wipe * halfH * 2f; // выше этой линии панциря уже нет
            float pulse = 0.8f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
            float dirSign = NPC.spriteDirection * ClawDirFix;

            foreach (DeathCrack crack in _deathCracks)
            {
                if (wipe > 0f && crack.Local.Y < wipeLocalY)
                    continue;

                float grow = MathHelper.Clamp((DeathElapsed - crack.Delay) / DeathCrackGrowTicks, 0f, 1f);
                if (grow <= 0f)
                    continue;

                Vector2 start = BodyAnchorToWorld(crack.Local);
                float rotation = AnimatedBodyRotation() + (dirSign > 0f ? crack.Angle : MathHelper.Pi - crack.Angle);
                float length = crack.Length * grow * NPC.scale;
                Vector2 middle = start + rotation.ToRotationVector2() * length * 0.5f;

                Color c = DeathCrackColor * (pulse * 0.9f);
                SoAVfx.DrawTintedQuad(sb, middle, new Vector2(length, 5f), rotation, c);
                SoAVfx.DrawTintedGlow(sb, start, new Vector2(26f * grow), DeathCrackColor * 0.6f);
            }
        }

        #endregion
    }
}
