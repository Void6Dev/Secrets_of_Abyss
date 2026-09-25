using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Animation;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Корона Короля-краба — отдельный спрайт поверх панциря.
    // Чистая косметика: сидит на теле (едет вместе со squash & stretch), светится как слабое место,
    // при «заботе» прижимается клешнёй, в постановочной смерти слетает и падает на землю.
    // Считается локально на каждом клиенте из синхронизированных State/Timer — по сети ничего не шлём.
    public partial class King_crab
    {
        // Наклон и прижатие при «заботе» (CrownCareTilt/Press) — в King_crab.Rig.cs
        private const float CrownFallGravity = 0.35f; // гравитация слетевшей короны
        private const float CrownDropAtDying = 0.6f;  // доля Timer стадии Dying, когда корона слетает

        private Asset<Texture2D> _crownTex;
        private Asset<Texture2D> _crownGlowTex;

        // Слетевшая корона: локальная симуляция; старт детерминирован по синхронизированному Timer
        // Корона живёт отдельным маленьким автоматом: пока король под землёй (или умирает)
        // она не исчезает, а сваливается с головы и остаётся лежать на грунте; когда король
        // возвращается наверх, она сама плавно взлетает обратно и садится на панцирь.
        private enum CrownMode { Seated, Falling, Landed, Returning }

        private const int CrownReturnTicks = 40;   // длительность возврата на голову
        private const float CrownReturnArc = 46f;  // высота дуги, по которой корона летит назад
        private const float CrownRestOffset = 6f;  // насколько основание короны утоплено в грунт

        private CrownMode _crownMode;
        private Vector2 _crownPos;   // позиция ОСНОВАНИЯ короны (пивот — низ текстуры)
        private Vector2 _crownVel;
        private float _crownRot;
        private float _crownRotVel;
        private float _crownReturnT;
        private Vector2 _crownReturnFrom;
        private float _crownReturnRotFrom;

        private void UpdateCrown()
        {
            // Чистая косметика: считается на клиентах из синхронизированных State/Timer.
            // На сервере нет текстур, а они нужны для посадки короны на панцирь.
            if (Main.dedServ)
                return;

            EnsureCrownTextures();
            if (_crownTex?.Value == null)
                return;

            // Смерть — корона слетает НАСОВСЕМ. Уход под землю — временно, потом вернётся.
            bool dying = State == CrabState.Dying && Timer <= DyingTicks * CrownDropAtDying;
            bool underground = (State == CrabState.Burrow && SubState < 2f)
                            || (State == CrabState.KnightCourt && SubState < 2f);

            if (_crownMode == CrownMode.Seated && (dying || underground))
                DropCrown();

            switch (_crownMode)
            {
                case CrownMode.Falling:
                    UpdateCrownFall();
                    break;

                case CrownMode.Landed:
                    // Король вынырнул и жив — корона возвращается сама
                    if (!dying && !underground)
                        BeginCrownReturn();
                    break;

                case CrownMode.Returning:
                    // Успел снова уйти под землю — роняем обратно
                    if (dying || underground)
                        DropCrown();
                    else
                        UpdateCrownReturn();
                    break;
            }
        }

        // Срыв с головы: соскальзывает назад и вверх
        private void DropCrown()
        {
            _crownMode = CrownMode.Falling;
            _crownPos = CrownSeatWorld(out _crownRot);
            int dir = NPC.spriteDirection;
            _crownVel = new Vector2(-dir * 1.5f, -3f);
            _crownRotVel = -dir * 0.05f;
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = -0.2f, Volume = 0.6f }, _crownPos);
        }

        private void UpdateCrownFall()
        {
            _crownVel.Y += CrownFallGravity;
            _crownPos += _crownVel;
            _crownRot += _crownRotVel;

            float ground = FindGroundY(_crownPos.X, _crownPos.Y - 8f, 600f, true);
            if (float.IsNaN(ground) || _crownPos.Y < ground - CrownRestOffset)
                return;

            _crownPos.Y = ground - CrownRestOffset;
            _crownRot *= 0.4f; // почти выравнивается, лёжа на песке
            _crownMode = CrownMode.Landed;
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = 0.1f, Volume = 0.5f }, _crownPos);

            for (int i = 0; i < 6; i++)
            {
                Dust d = Dust.NewDustPerfect(_crownPos + new Vector2(0f, 8f), DustID.Sand,
                    new Vector2(Main.rand.NextFloatDirection() * 1.5f, -Main.rand.NextFloat(0.5f, 1.5f)));
                d.scale = 1.1f;
            }
        }

        private void BeginCrownReturn()
        {
            _crownMode = CrownMode.Returning;
            _crownReturnT = 0f;
            _crownReturnFrom = _crownPos;
            _crownReturnRotFrom = _crownRot;
            SoundEngine.PlaySound(SoundID.Item25 with { Pitch = 0.4f, Volume = 0.45f }, _crownPos);
        }

        // Возврат: летит по дуге к посадочному месту, которое само едет вместе с королём,
        // поэтому цель пересчитываем каждый тик, а не запоминаем один раз
        private void UpdateCrownReturn()
        {
            _crownReturnT = Math.Min(1f, _crownReturnT + 1f / CrownReturnTicks);
            float e = MathHelper.SmoothStep(0f, 1f, _crownReturnT);

            Vector2 seat = CrownSeatWorld(out float seatRot);
            _crownPos = Vector2.Lerp(_crownReturnFrom, seat, e);
            _crownPos.Y -= (float)Math.Sin(e * Math.PI) * CrownReturnArc; // подскок, чтобы не ползла по земле
            _crownRot = MathHelper.Lerp(_crownReturnRotFrom, seatRot, e);

            if (_crownReturnT < 1f)
                return;

            _crownMode = CrownMode.Seated;
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = 0.5f, Volume = 0.4f }, _crownPos);
        }

        private void EnsureCrownTextures()
        {
            _crownTex ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabCrown", AssetRequestMode.ImmediateLoad);
            _crownGlowTex ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabCrown_Glow", AssetRequestMode.ImmediateLoad);
        }

        // Мировая позиция ОСНОВАНИЯ короны, когда она сидит на панцире. Пивот у короны —
        // низ текстуры, поэтому от центра привязки отступаем на полвысоты вдоль корпуса.
        private Vector2 CrownSeatWorld(out float rotation)
        {
            Vector2 center = CrownWorldPos(out rotation);
            Vector2 crownScale = new Vector2(NPC.scale) * AnimPose(LayerCrown).Scale;
            float half = _crownTex?.Value == null ? 0f : _crownTex.Value.Height / 2f * crownScale.Y;
            return center + new Vector2(0f, half).RotatedBy(AnimatedBodyRotation());
        }

        // Положение короны на панцире: та же привязка и поправка на squash, что у DrawBody
        private Vector2 CrownWorldPos(out float rotation)
        {
            LayerPose crownPose = AnimPose(LayerCrown);
            float sx = 1f + _bodySquash * BodySquashAmount;
            float sy = 1f - _bodySquash * BodySquashAmount;
            Texture2D tex = TextureAssets.Npc[Type].Value;
            int frameCount = Math.Max(1, Main.npcFrameCount[Type]);
            float halfHeightWorld = tex.Height / frameCount / 2f * NPC.scale;
            Vector2 bodyCenter = AnimatedBodyCenter()
                + new Vector2(0f, halfHeightWorld * (1f - sy));

            float dirSign = NPC.spriteDirection * ClawDirFix;
            Vector2 local = (new Vector2(CrownOffsetX * dirSign * sx, CrownOffsetY * sy)
                + new Vector2(crownPose.Offset.X * dirSign, crownPose.Offset.Y)) * NPC.scale;
            Vector2 world = bodyCenter + local.RotatedBy(AnimatedBodyRotation());

            rotation = AnimatedBodyRotation() + crownPose.Rotation * dirSign;
            // Покачивание в шаг авторит walk-клип (слой crown), не _stepCycle

            // Вторичная физика: демпфированный осциллятор от ускорения тела и плюхи панциря.
            // Без него корона — приклеенная наклейка, и именно это читается как «анимация».
            rotation += dirSign * _crownSpring;

            // Амплитуда мотания растёт со скоростью хода: на 7.5 корона должна мотаться
            // заметно сильнее, чем на 4.2, а клипом это не выразить
            float speedT = MathHelper.Clamp(Math.Abs(NPC.velocity.X) / FullSpeed, 0f, 1f);
            rotation += dirSign * crownPose.Rotation * speedT * CrownSpeedSway * 10f;

            // Своё дыхание с периодом 113 тиков — не совпадает ни с телом, ни с клешнями
            rotation += _breathCrown * 0.012f * _calmness;

            // Сбитая тяжёлым ударом корона выправляется НЕ сразу: живёт 20 тиков и медленно
            // возвращается. Визуально «корона съехала», и это стыкуется с guard_crown.
            if (_hurtCrownTilt > 0)
                rotation += dirSign * HurtCrownTiltAmount * (_hurtCrownTilt / (float)HurtCrownTiltTicks);

            // «Забота о короне»: сбилась от удара — король прижимает её клешнёй.
            // Прижатие идёт кривой 0 → 6 → 4 → 0, а не постоянными 4 px: под клешнёй корона
            // обязана просаживаться сильнее именно в момент касания.
            if (_crownCareTimer > 0)
            {
                float care = _crownCareTimer / (float)CrownCareTicks;
                float press = care > 0.75f ? MathHelper.Lerp(0f, 1.5f, (1f - care) / 0.25f)
                    : MathHelper.Lerp(0f, 1f, care / 0.75f);
                rotation += dirSign * CrownCareTilt * care;
                world.Y += CrownCarePress * press;
            }
            return world;
        }

        // Зовётся из PreDraw между телом и клешнями: клешня «заботы» ложится поверх короны
        private void DrawCrown(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            EnsureCrownTextures();
            Texture2D tex = _crownTex.Value;
            if (tex == null)
                return;

            // Под землёй корону больше НЕ прячем: она к этому моменту уже свалилась с головы
            // и лежит на поверхности (см. UpdateCrown), так что рисуется на своём месте.
            Vector2 crownScale = new Vector2(NPC.scale) * AnimPose(LayerCrown).Scale;
            float rot;
            // Пивот — в ОСНОВАНИИ короны (низ текстуры) во ВСЕХ режимах: наклоны от клипов
            // раскачивают её на ободе, сидя на панцире, а не отрывают от него, а слетевшая
            // корона вращается вокруг обода. Единый пивот нужен ещё и затем, чтобы возврат
            // на голову не дёргался в момент старта.
            Vector2 origin = new Vector2(tex.Width / 2f, tex.Height);
            Vector2 pos;
            if (_crownMode == CrownMode.Seated)
            {
                pos = CrownSeatWorld(out rot);
            }
            else
            {
                pos = _crownPos; // слетела/лежит/возвращается — позу ведёт UpdateCrown
                rot = _crownRot;
            }

            SpriteEffects fx = NPC.spriteDirection * ClawDirFix > 0f ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            spriteBatch.Draw(tex, pos - screenPos, null, drawColor, rot, origin, crownScale, fx, 0f);

            // Свечение слабого места: слабое в фазе 2, пульс в Desperate и на Crown Command;
            // клипы добавляют своё через канал Aux слоя crown (разгорание на входе в фазу 2)
            float glow = 0f;
            if (State == CrabState.CrownCommand)
                glow = 0.75f + 0.25f * (float)Math.Sin(Main.GameUpdateCount * 0.25f);
            else if (Desperate)
                glow = 0.5f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.15f);
            else if (Phase2)
                glow = 0.3f;
            glow = MathHelper.Clamp(glow + AnimPose(LayerCrown).Aux, 0f, 1f);

            if (glow > 0f && _crownMode == CrownMode.Seated && _crownGlowTex.Value != null)
                spriteBatch.Draw(_crownGlowTex.Value, pos - screenPos, null, Color.White * glow, rot, origin, crownScale, fx, 0f);
        }
    }
}
