using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Animation;
using SoA.Content.Tiles.Furniture;
using SoA.Content.Tiles.Lighthouse;
using SoA.Content.Tiles.Shrines;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ЛАКОМСТВО. Жемчужина на королевском алтаре — приманка. Позванный с алтаря король выходит
    // из песка не перед игроком, а сбоку от алтаря, со стороны, дальней от игрока; тянется
    // клешнёй в чашу, подкидывает жемчужину в пасть, хрустит — и только потом оборачивается
    // к тому, кто его позвал, и ревёт.
    //
    // AI: точка жемчужины (_feastPearl, шлётся в SendExtraAI) и стадия IntroSubFeast.
    // Хват, полёт и хруст — косметика клиента по меткам клипа «feast»; клешню к чаше
    // доводит IK поверх клипа (FeastReach), потому что алтарь стоит где придётся.
    public partial class King_crab
    {
        private const float IntroSubFeast = 2.75f; // между Stand и Roar: ключ стадии (int)(Sub*4) свой

        // ---------- ЛАКОМСТВО: ПАРАМЕТРЫ ----------
        private const float FeastSearchRange = 1000f;  // как далеко от игрока искать алтарь с жемчужиной
        private const float FeastStandoff = 175f;      // центр короля от жемчужины по X: клешня достаёт без растяжки
        private const float FeastExitTolerance = 10f;  // выход из-под грунта точнее обычного
        private const float FeastMinReachX = 90f;      // ближе — клешня полезет сквозь собственный панцирь
        private const float FeastMaxReachX = 270f;     // дальше — не дотянуться, обойдётся без лакомства
        private const float FeastMaxReachY = 200f;
        private const int FeastTicks = 132;

        // Ключевые тики клипа feast. IK клешни и метки читают одни и те же числа
        private const float FeastReachStart = 8f;   // замах назад кончился, клешня пошла к чаше
        private const float FeastGrabTick = 34f;    // клац: жемчужина в пинцере
        private const float FeastLiftStart = 42f;   // подержал — поднимает
        private const float FeastTossTick = 62f;    // подкинул
        private const int PearlFlightTicks = 14;
        private const float FeastChompTick = FeastTossTick + PearlFlightTicks; // поймал пастью
        private const float FeastChewTick = 88f;
        private const float FeastCrunchTick = 98f;  // раскусил

        private const float PearlFlightArc = 46f;   // высота дуги подброса, px
        private const float PearlScale = 1f;        // жемчужина вырезана из спрайта алтаря в том же масштабе

        // Точка между половинами пинцера в KingCrabClawBase.png и пасть на панцире от центра
        // тела — менять при перерисовке спрайтов
        private static readonly Vector2 ClawBaseGrip = new(100f, 68f);
        private static readonly Vector2 MouthLocal = new(0f, 26f);

        private Vector2 _feastPearl; // мировая точка жемчужины; Zero — позвали не с алтаря
        private bool HasFeast => _feastPearl != Vector2.Zero;

        private enum PearlHold : byte { None, Claw, Flying }

        // Косметика клиента
        private Asset<Texture2D> _pearlTex;
        private PearlHold _pearlHold;
        private Vector2 _pearlFrom;
        private int _pearlFlight;
        private readonly Vector2[] _clawGripWorld = new Vector2[2];

        private bool InFeast => State == CrabState.Intro && SubState == IntroSubFeast && HasFeast;
        private float FeastElapsed => FeastTicks - Timer;

        #region AI

        // Зовётся из OnSpawn: есть ли свежая жемчужина рядом с призвавшим
        private void ClaimFeast(Player target)
        {
            _feastPearl = CrabRoyalAltar_tile.TryClaimPearl(target.Center, FeastSearchRange, out Vector2 pearl)
                ? pearl : Vector2.Zero;
        }

        // Король выходит со стороны алтаря, дальней от игрока: игрок смотрит на трапезу
        // через алтарь, а не стоит у короля под клешнёй
        private int FeastSide(Player target, int fallback)
        {
            int side = Math.Sign(_feastPearl.X - target.Center.X);
            return side == 0 ? fallback : side;
        }

        // Приземлился не там (обрыв, вода, уступ) — не дотянуться, трапезы не будет
        private bool CanReachFeast()
        {
            if (!HasFeast)
                return false;
            float dx = Math.Abs(_feastPearl.X - NPC.Center.X);
            float dy = Math.Abs(_feastPearl.Y - NPC.Center.Y);
            return dx >= FeastMinReachX && dx <= FeastMaxReachX && dy <= FeastMaxReachY;
        }

        private void FaceX(float worldX)
        {
            int dir = Math.Sign(worldX - NPC.Center.X);
            if (dir == 0)
                return;
            NPC.direction = dir;
            NPC.spriteDirection = dir;
        }

        // 4b. Трапеза. Сам AI только стоит и смотрит на чашу — всё действие в клипе
        private void IntroFeast()
        {
            ApplyGravity();
            Brake();
            FaceX(_feastPearl.X);

            if (Timer > 0f)
                return;

            _feastPearl = Vector2.Zero;
            EnterSubState(IntroSubRoar, RoarWindupTicks);
        }

        #endregion

        #region Клешня: IK к чаше

        // Вес IK: клешня тянется к чаше, держит жемчужину и отпускает позу обратно клипу,
        // который к подбросу задирает её над головой
        private float FeastReachWeight()
        {
            if (!InFeast)
                return 0f;

            float t = FeastElapsed;
            if (t < FeastReachStart)
                return 0f;
            if (t < FeastGrabTick)
                return SmoothStep((t - FeastReachStart) / (FeastGrabTick - FeastReachStart));
            if (t < FeastLiftStart)
                return 1f;
            if (t < FeastTossTick)
                return 1f - SmoothStep((t - FeastLiftStart) / (FeastTossTick - FeastLiftStart));
            return 0f;
        }

        private static float SmoothStep(float t)
        {
            t = MathHelper.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        // Обратное к DrawClaw: какое запястье (в координатах от центра тела) поставит точку
        // хвата пинцера ровно на жемчужину. Только передняя клешня
        private Vector2 FeastReach(Vector2 wrist, LayerPose pose)
        {
            float weight = FeastReachWeight();
            if (weight <= 0f)
                return wrist;

            float dirSign = NPC.spriteDirection * ClawDirFix;
            bool flip = dirSign > 0f;
            float side = flip ? 1f : -1f;
            float bodyRot = AnimatedBodyRotation();
            float rot = bodyRot - side * (ClawStanceRaise + pose.Rotation);

            Vector2 gripTex = ClawBaseGrip - ClawBaseShoulder;
            Vector2 gripOffset = (new Vector2(flip ? -gripTex.X : gripTex.X, gripTex.Y) * NPC.scale * ClawScale).RotatedBy(rot);

            Vector2 shoulderLocal = new(ClawShoulderX, ClawShoulderY);
            Vector2 shoulderWorld = BodyAnchorToWorld(shoulderLocal);
            Vector2 v = (_feastPearl - gripOffset - shoulderWorld).RotatedBy(-bodyRot) / NPC.scale;
            Vector2 target = new Vector2(v.X * dirSign, v.Y) + shoulderLocal;
            return Vector2.Lerp(wrist, target, weight);
        }

        #endregion

        #region Косметика

        // Из TickVisuals: полёт жемчужины и сброс, если сцену прервали
        private void UpdateFeast()
        {
            if (State != CrabState.Intro)
                _pearlHold = PearlHold.None;
            if (_pearlHold == PearlHold.Flying)
                _pearlFlight++;
        }

        private Vector2 MouthWorld() => BodyAnchorToWorld(MouthLocal);

        private void OnFeastGrab()
        {
            if (!HasFeast)
                return;
            CrabRoyalAltar_tile.TakePearl(_feastPearl);
            _pearlHold = PearlHold.Claw;
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = -0.4f }, _feastPearl);
            HitStop(2);
        }

        private void OnFeastToss()
        {
            if (_pearlHold != PearlHold.Claw)
                return;
            _pearlHold = PearlHold.Flying;
            _pearlFrom = _clawGripWorld[0];
            _pearlFlight = 0;
            SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.3f, Volume = 0.6f }, _pearlFrom);
        }

        private void OnFeastChomp()
        {
            _pearlHold = PearlHold.None;
            SoundEngine.PlaySound(SoundID.Item2 with { Pitch = -0.8f }, MouthWorld());
            ScreenPunch(2.5f, 10, Vector2.UnitY);
        }

        private void OnFeastChew()
            => SoundEngine.PlaySound(SoundID.Item2 with { Pitch = -0.6f, Volume = 0.7f }, MouthWorld());

        // Хруст: осколки перламутра из пасти
        private void OnFeastCrunch()
        {
            Vector2 mouth = MouthWorld();
            SoundEngine.PlaySound(SoundID.Item27 with { Pitch = -0.5f }, mouth);
            HitStop(3);
            for (int k = 0; k < 14; k++)
            {
                Dust d = Dust.NewDustPerfect(mouth, k % 2 == 0 ? DustID.PurpleCrystalShard : DustID.TreasureSparkle,
                    new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3.5f, -0.5f)));
                d.noGravity = k % 2 == 1;
                d.scale = Main.rand.NextFloat(0.8f, 1.3f);
            }
        }

        // Жемчужина в пинцере — рисуется из DrawClaw между когтем и основанием
        private void DrawPearlInClaw(SpriteBatch spriteBatch, int idx, Vector2 screenPos, Color drawColor)
        {
            if (idx == 0 && _pearlHold == PearlHold.Claw)
                DrawPearl(spriteBatch, _clawGripWorld[0], 0f, PearlScale, screenPos, drawColor);
        }

        // Подброшенная жемчужина: дуга от пинцера к пасти, крутится и чуть мельчает к концу
        private void DrawPearlFlight(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (_pearlHold != PearlHold.Flying)
                return;

            float t = MathHelper.Clamp(_pearlFlight / (float)PearlFlightTicks, 0f, 1f);
            Vector2 at = Vector2.Lerp(_pearlFrom, MouthWorld(), t) - Vector2.UnitY * (4f * PearlFlightArc * t * (1f - t));
            DrawPearl(spriteBatch, at, t * MathHelper.TwoPi, PearlScale * MathHelper.Lerp(1f, 0.75f, t), screenPos, drawColor);
        }

        private void DrawPearl(SpriteBatch spriteBatch, Vector2 world, float rotation, float scale, Vector2 screenPos, Color drawColor)
        {
            _pearlTex ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabPearl", AssetRequestMode.ImmediateLoad);
            Texture2D tex = _pearlTex.Value;
            // Перламутр чуть светится сам — в тёмной пещере не пропадает в клешне
            Color color = Color.Lerp(drawColor, Color.White, 0.35f);
            spriteBatch.Draw(tex, world - screenPos, null, color, rotation, tex.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        #endregion
    }
}
