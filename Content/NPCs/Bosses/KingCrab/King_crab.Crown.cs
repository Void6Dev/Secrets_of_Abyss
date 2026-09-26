using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Particles;
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
        // Момент, когда корона слетает в сцене смерти, — метка crown_falls клипа death
        private const int DeathCrownFallGrace = 30; // на сколько тиков клип может отстать от часов сцены

        // ---------- КОРОНА В СЦЕНАХ ----------
        private const float CrownRollFriction = 0.965f;   // смерть: корона катится к ногам игрока
        private const float CrownRollStopNearPlayer = 36f;
        private const float CrownRollMinSpeed = 1.2f;
        private const float CrownRollMaxSpeed = 9f;
        private const float CrownRollWobble = 0.4f;       // покачивание на ребре, пока катится

        private Asset<Texture2D> _crownTex;
        private Asset<Texture2D> _crownGlowTex;

        // Слетевшая корона: локальная симуляция; старт детерминирован по синхронизированному Timer
        // Корона живёт отдельным маленьким автоматом: пока король под землёй (или умирает)
        // она не исчезает, а сваливается с головы и остаётся лежать на грунте; когда король
        // возвращается наверх, она сама плавно взлетает обратно и садится на панцирь.
        // Hidden — корона ещё под песком (появление), Emerging — встаёт из грунта,
        // Rolling — после смерти катится к ногам игрока
        private enum CrownMode { Seated, Falling, Landed, Returning, Hidden, Emerging, Rolling }

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
        private float _crownEmergeSurfaceY; // поверхность, из которой встаёт корона: ниже неё не рисуем
        private bool _crownEmergedFully;
        private float _crownRollTraveled;
        private float _crownRollStartSpeed;

        private void UpdateCrown()
        {
            // Чистая косметика: считается на клиентах из синхронизированных State/Timer.
            // На сервере нет текстур, а они нужны для посадки короны на панцирь.
            if (Main.dedServ)
                return;

            EnsureCrownTextures();
            if (_crownTex?.Value == null)
                return;

            // Появление: пока король идёт под землёй, короны нет; в тишине она встаёт из песка
            if (State == CrabState.Intro && SubState < IntroSubErupt)
            {
                _crownMode = SubState == IntroSubCrown ? CrownMode.Emerging : CrownMode.Hidden;
                if (_crownMode == CrownMode.Emerging)
                    UpdateCrownEmerging();
                return;
            }
            if (_crownMode == CrownMode.Hidden)
                _crownMode = CrownMode.Seated;
            else if (_crownMode == CrownMode.Emerging)
                BeginCrownReturn(); // король вылетел из песка — корона взлетает к нему на голову

            // Смерть — корона слетает НАСОВСЕМ. Уход под землю — временно, потом вернётся.
            // По метке клипа; часы сцены — страховка, если клип сбит паузами удара
            bool dying = State == CrabState.Dying
                && (_deathCrownReleased || DeathElapsed >= DeathCrownFallTick + DeathCrownFallGrace);
            bool underground = (State == CrabState.Burrow && SubState < 2f)
                            || (State == CrabState.KnightCourt && SubState < 2f)
                            || (State == CrabState.CourtDuel && SubState < 2f);

            if (_crownMode == CrownMode.Seated && (dying || underground))
                DropCrown();

            switch (_crownMode)
            {
                case CrownMode.Falling:
                    UpdateCrownFall();
                    break;

                case CrownMode.Rolling:
                    UpdateCrownRoll();
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

            // Конец сцены смерти: корона у ног игрока рассыпается золотыми искрами последней
            if (State == CrabState.Dying && DeathElapsed >= DeathCrownFadeStart && _crownMode != CrownMode.Seated
                && Main.rand.NextBool(2))
            {
                Dust d = Dust.NewDustPerfect(_crownPos + new Vector2(Main.rand.NextFloatDirection() * 16f, -12f),
                    DustID.GoldCoin, new Vector2(Main.rand.NextFloatDirection() * 0.6f, -Main.rand.NextFloat(0.5f, 2f)));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(0.8f, 1.2f);
            }
        }

        // Появление: корона медленно встаёт из песка над точкой выхода и загорается
        private void UpdateCrownEmerging()
        {
            float surfaceY = SurfaceAbove(StateData, NPC.Center.Y);
            float progress = 1f - Timer / Math.Max(1f, IntroHush);
            float rise = MathHelper.Clamp(progress / IntroCrownRiseShare, 0f, 1f);
            rise = 1f - (1f - rise) * (1f - rise); // выходит быстро, у поверхности замедляется

            float height = _crownTex.Value.Height * NPC.scale;
            _crownEmergeSurfaceY = surfaceY;
            _crownPos = new Vector2(StateData, surfaceY + CrownRestOffset + (height - CrownRestOffset) * (1f - rise));
            _crownRot = 0f;

            // Перед выходом корона дрожит: под ней шевелится сам король
            float tremble = IntroTremble;
            if (tremble > 0f)
                _crownPos += new Vector2((float)Math.Sin(Main.GameUpdateCount * 1.9f) * 2.2f,
                    (float)Math.Sin(Main.GameUpdateCount * 2.7f) * 1.2f) * tremble;

            // Корона светится в темноте сама — видно, что она живая, а не клад на дне
            SoAParticles.AddLight(_crownPos - new Vector2(0f, height * 0.5f), CrownGoldColor, 0.9f + 0.6f * rise + 0.8f * tremble, 2);

            if (Main.rand.NextBool(4))
            {
                Dust d = Dust.NewDustPerfect(_crownPos + new Vector2(Main.rand.NextFloatDirection() * 14f, -height * rise),
                    DustID.Sand, new Vector2(Main.rand.NextFloatDirection() * 0.4f, Main.rand.NextFloat(0.3f, 1.2f)));
                d.scale = Main.rand.NextFloat(0.7f, 1.1f);
            }

            // Вышла целиком — одинокий блик и тонкий звон в полной тишине
            if (rise >= 1f && !_crownEmergedFully)
            {
                _crownEmergedFully = true;
                SpawnFlash(_crownPos - new Vector2(0f, height * 0.6f), 140f, CrownGoldColor, 6);
                SoundEngine.PlaySound(SoundID.Item29 with { Pitch = 0.6f, Volume = 0.5f }, _crownPos);
            }
            else if (rise < 1f)
            {
                _crownEmergedFully = false;
            }
        }

        // Смерть: корона катится по грунту к ногам игрока и ложится там
        private void UpdateCrownRoll()
        {
            _crownPos.X += _crownVel.X;
            _crownVel.X *= CrownRollFriction;
            _crownRollTraveled += Math.Abs(_crownVel.X);

            float ground = FindGroundY(_crownPos.X, _crownPos.Y - 24f, 80f, true);
            if (float.IsNaN(ground))
            {
                // Край уступа — дальше падает, а там ляжет где упала
                _crownMode = CrownMode.Falling;
                return;
            }
            _crownPos.Y = ground - CrownRestOffset;

            float speedT = _crownRollStartSpeed <= 0f ? 0f : Math.Abs(_crownVel.X) / _crownRollStartSpeed;
            _crownRot = (float)Math.Sin(_crownRollTraveled * 0.12f) * CrownRollWobble * speedT;

            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustPerfect(_crownPos + new Vector2(0f, 4f), DustID.Sand,
                    new Vector2(-_crownVel.X * 0.2f, -Main.rand.NextFloat(0.2f, 0.9f)));
                d.scale = 0.9f;
            }

            if (Math.Abs(_crownVel.X) > 0.25f)
                return;

            _crownRot *= 0.4f;
            _crownMode = CrownMode.Landed;
            SoundEngine.PlaySound(SoundID.Tink with { Pitch = 0.3f, Volume = 0.45f }, _crownPos);
        }

        // Срыв с головы: соскальзывает назад и вверх
        private void DropCrown()
        {
            _crownMode = CrownMode.Falling;
            _crownPos = CrownSeatWorld(out _crownRot);
            int dir = NPC.spriteDirection;
            _crownVel = new Vector2(-dir * 1.5f, -3f);
            _crownRotVel = -dir * 0.05f;

            // В смерти корона не падает назад, а соскальзывает к игроку: дальше она докатится
            if (State == CrabState.Dying)
            {
                int toPlayer = Math.Sign(Main.player[NPC.target].Center.X - _crownPos.X);
                if (toPlayer == 0)
                    toPlayer = dir;
                _crownVel = new Vector2(toPlayer * 2f, -4f);
                _crownRotVel = toPlayer * 0.06f;
            }
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

            // Смерть: скорость подбираем так, чтобы трение остановило корону у ног игрока
            if (State == CrabState.Dying)
            {
                Player player = Main.player[NPC.target];
                float distance = Math.Abs(player.Center.X - _crownPos.X) - CrownRollStopNearPlayer;
                if (distance > 8f)
                {
                    int toPlayer = Math.Sign(player.Center.X - _crownPos.X);
                    float speed = MathHelper.Clamp(distance * (1f - CrownRollFriction), CrownRollMinSpeed, CrownRollMaxSpeed);
                    _crownVel = new Vector2(toPlayer * speed, 0f);
                    _crownRollStartSpeed = speed;
                    _crownRollTraveled = 0f;
                    _crownMode = CrownMode.Rolling;
                }
            }
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
            if (_crownMode == CrownMode.Hidden)
                return;
            if (_crownMode == CrownMode.Emerging)
            {
                DrawEmergingCrown(spriteBatch, screenPos, tex);
                return;
            }

            // Конец сцены смерти: корона рассыпается последней
            if (State == CrabState.Dying && DeathElapsed > DeathCrownFadeStart)
                drawColor *= 1f - MathHelper.Clamp((DeathElapsed - DeathCrownFadeStart) / (float)(DyingTicks - DeathCrownFadeStart), 0f, 1f);

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

        // Встающая из песка корона: рисуем только часть над поверхностью, иначе закопанный
        // низ просвечивал бы сквозь грунт. Свет берём в точке короны, а не у короля под землёй
        private void DrawEmergingCrown(SpriteBatch spriteBatch, Vector2 screenPos, Texture2D tex)
        {
            float height = tex.Height * NPC.scale;
            float topY = _crownPos.Y - height;
            int visibleRows = (int)(MathHelper.Clamp(_crownEmergeSurfaceY - topY, 0f, height) / NPC.scale);
            if (visibleRows <= 0)
                return;

            Rectangle src = new Rectangle(0, 0, tex.Width, visibleRows);
            Vector2 origin = new Vector2(tex.Width / 2f, 0f);
            Vector2 top = new Vector2(_crownPos.X, topY);
            Color light = Lighting.GetColor(top.ToTileCoordinates());
            SpriteEffects fx = NPC.spriteDirection * ClawDirFix > 0f ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            spriteBatch.Draw(tex, top - screenPos, src, light, 0f, origin, NPC.scale, fx, 0f);
            if (_crownGlowTex.Value != null)
            {
                float glow = 0.55f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.12f) + 0.35f * IntroTremble;
                spriteBatch.Draw(_crownGlowTex.Value, top - screenPos, src, Color.White * glow, 0f, origin, NPC.scale, fx, 0f);
            }
        }
    }
}
