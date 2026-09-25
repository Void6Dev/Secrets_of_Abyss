using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Animation;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Процедурные ноги Короля-краба (прототип).
    // Чистая косметика: считается локально на каждом клиенте из синхронизированных
    // позиции/скорости тела, ничего по сети не шлём и ai[] не тратим.
    // Рисуется в PreDraw ПОЗАДИ панциря.
    public partial class King_crab
    {
        // Все числа рига (BodyLift, LegScale, кости, бёдра, походка) — в King_crab.Rig.cs

        private Asset<Texture2D> _legUpper;
        private Asset<Texture2D> _legLower;
        private Leg[] _legs;
        private int _airborneTicks;   // гистерезис детекта «в воздухе»
        private bool _wasAirborne;    // для чистой постановки ног при приземлении
        private float _squashPose;      // деформация от текущей стадии (присед/вытяжка)
        private float _squashImpact;    // импульс сжатия от приземления (пружина, не экспонента)
        private float _squashImpactVel;
        private float _bodySquash;      // итог для DrawBody: >0 сплющен, <0 вытянут
        private float _gaitBob;         // 0..1: доля лап в фазе переноса — оседание корпуса
        private int _idleStepTimer;     // до следующего одиночного переступа в покое

        private class Leg
        {
            public Vector2 Hip;
            public Vector2 Foot;
            public Vector2 StepFrom;
            public Vector2 StepTo;
            public int StepTimer;
            public int StepSpan = StepDuration; // длина ИМЕННО ЭТОГО шага: скорость могла
        }                                       // измениться, пока стопа летит по дуге

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            _legUpper ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabLegUpper", AssetRequestMode.ImmediateLoad);
            _legLower ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabLegLower", AssetRequestMode.ImmediateLoad);
            _clawBase ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabClawBase", AssetRequestMode.ImmediateLoad);
            _clawTip ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabClawTip", AssetRequestMode.ImmediateLoad);
            _armUpper ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabArmUpper", AssetRequestMode.ImmediateLoad);
            _armLower ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabArmLower", AssetRequestMode.ImmediateLoad);

            // Манекен бестиария не стоит на земле — процедурные ноги и подъём тела там
            // разъезжаются. Рисуем статичную позу покоя без симуляции и эффектов.
            if (NPC.IsABestiaryIconDummy)
            {
                DrawBestiary(spriteBatch, screenPos, drawColor);
                return false;
            }

            UpdateAnimation(); // кейфреймовый слой первым: риг ниже читает его позы
            UpdateLegs();
            UpdateClaws();
            UpdateCrown();
            UpdateRings();
            UpdateBursts();
            UpdateRageAura();
            UpdateDelayedFx();
            UpdateFlashesAndCracks();
            UpdateLiveliness();
            RecordAfterimage();

            // Трещины на грунте — единственное тёмное, что рисует босс: это повреждение
            // поверхности, оно обязано быть темнее грунта. Всё остальное СВЕТИТСЯ:
            // затемнять картинку в бою нечем.
            DrawCracks(spriteBatch);

            SoAVfx.BeginAdditive(spriteBatch);
            DrawLandingMarker(spriteBatch);
            DrawSlamTelegraph(spriteBatch);
            DrawSweepTelegraph(spriteBatch);
            DrawTideGapLight(spriteBatch);
            DrawGripGlow(spriteBatch);
            DrawCrownGroundLight(spriteBatch);
            SoAVfx.EndAdditive(spriteBatch);

            // Аддитивные слои позади тела. Раздельные батчи: шейдерная аура и плоские ауры/кольца
            // не смешиваются в одном Immediate-батче (иначе последний Apply испортит следующий Draw).
            SoAVfx.BeginAdditive(spriteBatch);
            DrawWaterAura(spriteBatch);
            SoAVfx.EndAdditive(spriteBatch);

            SoAVfx.BeginAdditive(spriteBatch);
            DrawTintAuras(spriteBatch);
            SoAVfx.EndAdditive(spriteBatch);

            if (_rings.Count > 0)
            {
                SoAVfx.BeginAdditive(spriteBatch);
                DrawRings(spriteBatch);
                SoAVfx.EndAdditive(spriteBatch);
            }

            // Пылевые всплески — непрозрачные, рисуем в AlphaBlend-батче
            if (_bursts.Count > 0)
            {
                SoAVfx.BeginAlphaImmediate(spriteBatch);
                DrawBursts(spriteBatch);
                SoAVfx.EndAdditive(spriteBatch);
            }

            DrawAfterimages(spriteBatch, screenPos); // шлейф позади всего

            // Под толщей грунта самого короля не видно — на поверхности остаются только пыль,
            // бугор и слетевшая корона (их рисуют блоки выше и DrawCrown ниже)
            if (!BurrowBuried)
            {
                DrawLegs(spriteBatch, screenPos, drawColor);
                DrawBody(spriteBatch, screenPos, drawColor); // тело рисуем сами — ради squash & stretch
                DrawEyesGlow(spriteBatch); // свечение глаз на морде, под короной и клешнями
            }

            DrawCrown(spriteBatch, screenPos, drawColor); // корона на панцире, под клешнями («забота» ложится поверх)

            if (!BurrowBuried)
            {
                DrawClawBack(spriteBatch, screenPos, drawColor);  // обе клешни перед панцирем; задняя — под передней
                DrawClawFront(spriteBatch, screenPos, drawColor);
            }

            if (!BurrowBuried)
                DrawShellGrains(spriteBatch); // налипший песок поверх панциря, под аурой ярости

            // «Ярость океана» — вторым проходом поверх всей туши
            DrawRageOverlay(spriteBatch, screenPos, drawColor);

            // Блик на мокром хитине и вспышки импактов — самым верхним слоем
            SoAVfx.BeginAdditive(spriteBatch);
            DrawShellGlint(spriteBatch);
            DrawFlashes(spriteBatch);
            SoAVfx.EndAdditive(spriteBatch);

            return false;
        }

        private void UpdateLegs()
        {
            float bodyScale = NPC.scale;        // крепление бёдер к телу
            float scale = NPC.scale * LegScale; // размер самих ног
            int moveDir = Math.Sign(NPC.velocity.X);
            float speed = Math.Abs(NPC.velocity.X);

            // Адаптивная походка: чем быстрее едем, тем короче и чаще шаги
            float speedT = MathHelper.Clamp(speed / FullSpeed, 0f, 1f);
            int stepDur = (int)MathHelper.Lerp(StepDuration, StepDurationFast, speedT);
            float trigger = MathHelper.Lerp(StepTrigger, StepTriggerFast, speedT) * scale;
            float lift = MathHelper.Lerp(StepLift, StepLiftFast, speedT) * scale;
            float lead = (StepLead + speed * StepLeadPerSpeed) * scale;

            // Слой legs авторит не только сдвиг стойки: Aux расширяет расстановку стоп
            // (приседы, упоры, рёв — «стойка шире»), Aux2 вообще запрещает переставлять лапы
            // (под землёй и после обрушения в смерти краб больше не шагает).
            LayerPose legsPose = AnimPose(LayerLegs);
            float spreadMul = 1f + legsPose.Aux * LegSpreadPerAux;
            bool stepLocked = legsPose.Aux2 > 0.5f;

            // Бобб корпуса от РЕАЛЬНОЙ фазы походки: корпус оседает ровно тогда, когда лапы
            // под ним переставляются. Раньше клип подпрыгивал в своём ритме (36 тиков), а ноги
            // шагали в своём (12→6) — отсюда и брался эффект «плывёт, а не идёт».
            if (_legs != null)
            {
                int swinging = 0;
                foreach (Leg l in _legs)
                {
                    if (l.StepTimer > 0)
                        swinging++;
                }
                _gaitBob = MathHelper.Lerp(_gaitBob, swinging / (float)_legs.Length, 0.25f);
            }

            // Детект «в воздухе» с гистерезисом + привязкой к явным воздушным стадиям —
            // иначе мелкие подскоки/шаг-апы дёргают лапки между «стоит» и «висит»
            bool stateAir = (State == CrabState.JumpCrush && SubState >= 1f)
                         || (State == CrabState.Burrow && SubState >= BurrowSubSink) // с провала лапы уже не на грунте
                         || (State == CrabState.KnightCourt && SubState >= 2f);
            bool groundedNow = NPC.velocity.Y == 0f || NPC.collideY;
            if (!groundedNow || stateAir)
                _airborneTicks++;
            else
                _airborneTicks = 0;
            bool airborne = stateAir || _airborneTicks > 4;
            bool landed = _wasAirborne && !airborne;
            _wasAirborne = airborne;

            UpdateBodySquash(airborne, landed);

            if (_legs == null)
            {
                _legs = new Leg[HipLocal.Length * 2];
                for (int i = 0; i < HipLocal.Length; i++)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        int sign = s == 0 ? -1 : 1;
                        Vector2 hip = HipWorld(i, sign, bodyScale);
                        float homeX = hip.X + sign * FootSpread[i] * scale;
                        float groundY = FindGroundY(homeX, hip.Y - 8f, FootProbeDepth, true);
                        _legs[i * 2 + s] = new Leg
                        {
                            Hip = hip,
                            Foot = float.IsNaN(groundY) ? hip + new Vector2(sign * 12f * scale, 46f * scale)
                                                        : new Vector2(homeX, groundY),
                        };
                    }
                }
            }

            for (int i = 0; i < HipLocal.Length; i++)
            {
                for (int s = 0; s < 2; s++)
                {
                    int sign = s == 0 ? -1 : 1;
                    Leg leg = _legs[i * 2 + s];
                    leg.Hip = HipWorld(i, sign, bodyScale);

                    if (airborne)
                    {
                        // Лапки свисают, отстают по ходу движения (инерция) и слегка покачиваются
                        float sway = (float)Math.Sin(Main.GameUpdateCount * 0.15f + i * 1.3f) * 4f * scale;
                        Vector2 hang = leg.Hip + new Vector2(
                            sign * 14f * scale - NPC.velocity.X * 1.6f + sway,
                            (42f + i * 3f) * scale);
                        leg.Foot = Vector2.Lerp(leg.Foot, hang, 0.2f);
                        leg.StepTimer = 0;
                        continue;
                    }

                    float homeX = leg.Hip.X + sign * FootSpread[i] * spreadMul * scale;

                    // Только что приземлились — ставим стопы сразу на место, без рывков-догоняний
                    if (landed)
                    {
                        float g = FindGroundY(homeX, leg.Hip.Y - 8f, FootProbeDepth, true);
                        leg.Foot = ClampToLegReach(leg.Hip,
                            float.IsNaN(g) ? new Vector2(homeX, leg.Hip.Y + 46f * scale) : new Vector2(homeX, g),
                            scale);
                        leg.StepTimer = 0;
                        continue;
                    }

                    // Шаг в процессе — ведём стопу по дуге. Передняя пара поднимает стопу выше
                    // задней: краб тащит зад, и это читается сразу.
                    if (leg.StepTimer > 0)
                    {
                        leg.StepTimer--;
                        float t = 1f - leg.StepTimer / (float)leg.StepSpan;
                        Vector2 pos = Vector2.Lerp(leg.StepFrom, leg.StepTo, t);
                        pos.Y -= (float)Math.Sin(t * Math.PI) * lift * (1f - i * StepLiftRearBias);
                        leg.Foot = leg.StepTimer == 0 ? leg.StepTo : pos;
                        if (leg.StepTimer == 0)
                            OnFootPlanted(i, leg.Foot, speedT); // постановка стопы — звук, пыль, микро-тряска
                        continue;
                    }

                    if (stepLocked)
                        continue;

                    // Стоим: если стопа уехала слишком далеко от «дома» — шагаем
                    if (Math.Abs(leg.Foot.X - homeX) <= trigger)
                        continue;

                    // Волновая походка: соседняя (ближе к голове) нога того же бока не отрывается одновременно
                    if (i > 0 && _legs[(i - 1) * 2 + s].StepTimer > 0)
                        continue;

                    // Осторожность у обрыва: если впереди пола нет, передняя пара «щупает» край
                    // укороченным забросом, а не замирает совсем
                    float targetX = homeX + moveDir * lead;
                    float targetGround = FindGroundY(targetX, leg.Hip.Y - 8f, FootProbeDepth, true);
                    if (float.IsNaN(targetGround))
                    {
                        targetX = homeX + moveDir * lead * CliffProbeLeadCut;
                        targetGround = FindGroundY(targetX, leg.Hip.Y - 8f, FootProbeDepth, true);
                        if (float.IsNaN(targetGround))
                            continue; // некуда ставить — стоим на месте
                    }

                    leg.StepFrom = leg.Foot;
                    leg.StepTo = ClampToLegReach(leg.Hip, new Vector2(targetX, targetGround), scale);
                    leg.StepTimer = stepDur;
                    leg.StepSpan = stepDur;
                }
            }

            TryIdleMicroStep(scale, spreadMul, stepLocked, speed, airborne);
            ScrapeFeetOnCoil(airborne);
        }

        // На взводе рывка стопы ПРОСКАЛЬЗЫВАЮТ назад по 1–2 px за тик: краб упирается и
        // скребёт грунт. Пыль от этого сыплется по метке sweep_coil.
        private void ScrapeFeetOnCoil(bool airborne)
        {
            if (airborne || _legs == null)
                return;
            bool coiling = State == CrabState.ClawSweep && SubState == 0f && Timer <= 10f;
            if (!coiling)
                return;

            float slip = 1.5f * -NPC.spriteDirection;
            foreach (Leg leg in _legs)
            {
                if (leg.StepTimer > 0)
                    continue;
                leg.Foot.X += slip;
            }
        }

        // В покое стопы стояли намертво. Раз в 3–5 секунд одна случайная нога делает короткий
        // переступ — стойка перестаёт быть мебелью, а стоит это одного таймера.
        private void TryIdleMicroStep(float scale, float spreadMul, bool stepLocked, float speed, bool airborne)
        {
            if (stepLocked || airborne || speed > 0.5f || _legs == null)
                return;

            if (_idleStepTimer > 0)
            {
                _idleStepTimer--;
                return;
            }
            _idleStepTimer = Main.rand.Next(IdleStepMinDelay, IdleStepMaxDelay);

            Leg leg = _legs[Main.rand.Next(_legs.Length)];
            if (leg.StepTimer > 0)
                return;

            float shift = Main.rand.NextFloatDirection() * IdleStepDistance * scale;
            float targetX = leg.Foot.X + shift;
            float ground = FindGroundY(targetX, leg.Hip.Y - 8f, FootProbeDepth, true);
            if (float.IsNaN(ground))
                return;

            leg.StepFrom = leg.Foot;
            leg.StepTo = ClampToLegReach(leg.Hip, new Vector2(targetX, ground), scale);
            leg.StepTimer = leg.StepSpan = 16;
        }

        // Стопу нельзя ставить дальше, чем нога физически достаёт: иначе двухкостный IK
        // клампится, колено распрямляется в спичку и лапка отрывается от грунта.
        // Сначала поджимаем заброс по X, оставив стопу на найденной земле; если земля сама
        // по себе ниже досягаемости (обрыв) — подтягиваем цель радиально к бедру.
        private static Vector2 ClampToLegReach(Vector2 hip, Vector2 foot, float scale)
        {
            float max = (LegBoneUpperPx + LegBoneLowerPx) * scale * LegReachSafety;
            Vector2 delta = foot - hip;
            if (delta.LengthSquared() <= max * max)
                return foot;

            float dy = Math.Abs(delta.Y);
            if (dy < max)
            {
                float maxDx = (float)Math.Sqrt(max * max - dy * dy);
                return new Vector2(hip.X + Math.Sign(delta.X) * maxDx, foot.Y);
            }
            return hip + delta * (max / delta.Length());
        }

        // Squash & stretch тела: позу диктуют кейфреймовые клипы (канал Aux слоя body),
        // здесь остаётся только физика — вытяжка на взлёте и импульс сжатия от приземления.
        private void UpdateBodySquash(bool airborne, bool landed)
        {
            float targetPose = AnimPose(LayerBody).Aux;
            if (airborne && NPC.velocity.Y < -1f)
                targetPose -= 0.22f; // на взлёте вытягивается
            _squashPose = MathHelper.Lerp(_squashPose, targetPose, 0.2f);

            // Плюха приземления — ДЕМПФИРОВАННАЯ ПРУЖИНА, а не чистая экспонента: после сжатия
            // тело обязано один раз перелететь в вытяжку, иначе приземление «садится» мёртво.
            if (landed)
            {
                _squashImpact = 0.55f;
                _squashImpactVel = 0f;
            }
            _squashImpactVel += -SquashSpringK * _squashImpact - SquashSpringDamp * _squashImpactVel;
            _squashImpact += _squashImpactVel;

            _bodySquash = _squashPose + _squashImpact;
        }

        // Рисуем панцирь с неоднородным масштабом. При _bodySquash == 0 совпадает с ванильной
        // отрисовкой (та же привязка к bodyCenter, что и у ног), поэтому в покое картинка не меняется.
        private void DrawBody(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D tex = TextureAssets.Npc[Type].Value;
            if (tex == null)
                return;

            LayerPose bodyPose = AnimPose(LayerBody);
            float sx = 1f + _bodySquash * BodySquashAmount;
            float sy = 1f - _bodySquash * BodySquashAmount;
            // «Дыхание жабр»: период 77 тиков не совпадает ни с одним клипом, амплитуда 0.008 —
            // сознательно незаметно, подсознательно заметно
            float gills = 1f + _breathBody * BreathBodyAmp;
            Vector2 scale = new Vector2(NPC.scale * sx, NPC.scale * sy) * bodyPose.Scale * gills;

            // Держим «ноги» на месте: при сжатии центр опускаем на убыль полувысоты
            int frameCount = Math.Max(1, Main.npcFrameCount[Type]);
            float halfHeightWorld = tex.Height / frameCount / 2f * NPC.scale;
            Vector2 drawCenter = AnimatedBodyCenter() + new Vector2(0f, halfHeightWorld * (1f - sy));

            DrawBodySprite(drawCenter, AnimatedBodyRotation(), scale, drawColor, screenPos);
        }

        // Точка, ПРИКЛЕЕННАЯ к панцирю, в мировых координатах — с той же поправкой на squash &
        // stretch и на scale-канал клипа, с какими рисуется само тело (см. DrawBody выше).
        // Всё, что крепится к панцирю, обязано ехать вместе с его деформацией: при
        // BodySquashAmount = 0.32 панцирь на плюхе раздаётся почти на треть ширины и проглатывает
        // руки, а на вытяжке — отрывается от них. В покое (_bodySquash == 0, Scale == 1) даёт
        // ровно то же, что AnimatedFacingToWorld, поэтому статичная поза не меняется.
        private Vector2 BodyAnchorToWorld(Vector2 localOffset)
        {
            Texture2D tex = TextureAssets.Npc[Type].Value;
            LayerPose bodyPose = AnimPose(LayerBody);
            float sx = 1f + _bodySquash * BodySquashAmount;
            float sy = 1f - _bodySquash * BodySquashAmount;

            int frameCount = Math.Max(1, Main.npcFrameCount[Type]);
            float halfHeightWorld = tex == null ? 0f : tex.Height / frameCount / 2f * NPC.scale;
            Vector2 center = AnimatedBodyCenter() + new Vector2(0f, halfHeightWorld * (1f - sy));

            float dirSign = NPC.spriteDirection * ClawDirFix;
            Vector2 local = new Vector2(localOffset.X * dirSign * sx, localOffset.Y * sy)
                            * NPC.scale * bodyPose.Scale;
            return center + local.RotatedBy(AnimatedBodyRotation());
        }

        // Статичный портрет для бестиария: тело с позой покоя, ноги расставлены под бёдрами.
        // Композиция привязана к NPC.Center - BodyLift, поэтому манекен временно опускаем,
        // чтобы портрет оказался по центру рамки.
        private void DrawBestiary(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            NPC.position.Y += BodyLift * 0.6f;

            UpdateClaws(); // State манекена — Scuttle по умолчанию: клешни сходятся к позе покоя

            _legs ??= new Leg[HipLocal.Length * 2];
            float legScale = NPC.scale * LegScale;
            for (int i = 0; i < HipLocal.Length; i++)
            {
                for (int s = 0; s < 2; s++)
                {
                    int sign = s == 0 ? -1 : 1;
                    Vector2 hip = HipWorld(i, sign, NPC.scale);
                    Vector2 foot = new(hip.X + sign * FootSpread[i] * legScale, NPC.Center.Y + BodyLift * 0.55f);
                    _legs[i * 2 + s] = new Leg { Hip = hip, Foot = foot, StepFrom = foot, StepTo = foot };
                }
            }

            DrawLegs(spriteBatch, screenPos, drawColor);
            DrawBody(spriteBatch, screenPos, drawColor);
            DrawCrown(spriteBatch, screenPos, drawColor);
            DrawClawBack(spriteBatch, screenPos, drawColor);
            DrawClawFront(spriteBatch, screenPos, drawColor);

            NPC.position.Y -= BodyLift * 0.6f;
        }

        private void DrawLegs(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (_legUpper?.Value == null || _legLower?.Value == null)
                return;

            Texture2D upper = _legUpper.Value;
            Texture2D lower = _legLower.Value;
            float scale = NPC.scale * LegScale;
            float l1 = LegBoneUpperPx * scale;
            float l2 = LegBoneLowerPx * scale;

            foreach (Leg leg in _legs)
            {
                Vector2 hip = leg.Hip;
                Vector2 foot = leg.Foot;

                // Двухкостный IK: колено через теорему косинусов
                float d = MathHelper.Clamp(Vector2.Distance(hip, foot), Math.Abs(l1 - l2) + 0.1f, l1 + l2 - 0.1f);
                float baseAngle = (foot - hip).ToRotation();
                float cosA = MathHelper.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
                float offset = (float)Math.Acos(cosA);

                Vector2 kneeA = hip + new Vector2(l1, 0f).RotatedBy(baseAngle + offset);
                Vector2 kneeB = hip + new Vector2(l1, 0f).RotatedBy(baseAngle - offset);
                Vector2 knee = kneeA.Y < kneeB.Y ? kneeA : kneeB; // колено гнём вверх (крабья стойка)

                float upperRot = (knee - hip).ToRotation();
                float lowerRot = (foot - knee).ToRotation();

                // Сегмент смотрит вправо; если повёрнут влево — переворачиваем, чтобы «верх» ноги остался сверху
                SpriteEffects upperFx = Math.Cos(upperRot) < 0f ? SpriteEffects.FlipVertically : SpriteEffects.None;
                SpriteEffects lowerFx = Math.Cos(lowerRot) < 0f ? SpriteEffects.FlipVertically : SpriteEffects.None;

                spriteBatch.Draw(upper, hip - screenPos, null, drawColor, upperRot, UpperPivot, scale, upperFx, 0f);
                spriteBatch.Draw(lower, knee - screenPos, null, drawColor, lowerRot, LowerPivot, scale, lowerFx, 0f);
            }
        }

        private Vector2 HipWorld(int i, int sign, float scale)
        {
            // Центр приподнятого тела со смещением кейфреймового слоя body;
            // слой legs добавляет сдвиг стойки (стопы держит на земле IK)
            LayerPose legsPose = AnimPose(LayerLegs);
            Vector2 local = new Vector2(
                sign * HipLocal[i].X * scale + legsPose.Offset.X * AnimDirSign * NPC.scale,
                HipLocal[i].Y * scale + legsPose.Offset.Y * NPC.scale);
            return AnimatedBodyCenter() + local.RotatedBy(AnimatedBodyRotation());
        }

        // Ищем верх первого «пола» под точкой. NaN — пола нет (воздух/вода/обрыв).
        // includePlatforms=true — считать полом и платформы (для ног), false — только сплошные блоки (для закапывания).
        private float FindGroundY(float worldX, float startY, float maxDist, bool includePlatforms)
        {
            int tileX = (int)(worldX / 16f);
            if (tileX < 0 || tileX >= Main.maxTilesX)
                return float.NaN;

            int startTileY = (int)(startY / 16f);
            int endTileY = (int)((startY + maxDist) / 16f);
            for (int ty = startTileY; ty <= endTileY; ty++)
            {
                if (ty < 0 || ty >= Main.maxTilesY)
                    continue;
                Tile t = Main.tile[tileX, ty];
                if (t == null || !t.HasUnactuatedTile)
                    continue;

                bool solid = Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType];
                bool platform = Main.tileSolidTop[t.TileType];
                if (solid || (includePlatforms && platform))
                    return ty * 16f;
            }
            return float.NaN;
        }
    }
}
