using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Процедурные ноги Короля-краба (прототип).
    // Чистая косметика: считается локально на каждом клиенте из синхронизированных
    // позиции/скорости тела, ничего по сети не шлём и ai[] не тратим.
    // Рисуется в PreDraw ПОЗАДИ панциря.
    public partial class King_crab
    {
        // --- Настройки (крути тут) ---
        private const float BodyLift = 50f;        // на сколько px тело приподнято над землёй (спрайт + крепление ног)
        private const float LegScale = 1.6f;       // размер ног относительно тела (1 = как раньше)
        private const float LegBoneUpperPx = 40f;  // расстояние пивот→колено в KingCrabLegUpper.png (пивот x2 → колено x42)
        private const float LegBoneLowerPx = 44f;  // расстояние колено→лапка в KingCrabLegLower.png (пивот x2 → лапка x46)
        private const int StepDuration = 12;        // тиков на шаг в покое
        private const int StepDurationFast = 6;     // тиков на шаг на полной скорости
        private const float StepTrigger = 30f;      // отход стопы до перестановки в покое
        private const float StepTriggerFast = 18f;  // то же на полной скорости (короче шаг)
        private const float StepLift = 14f;         // высота подъёма стопы в покое
        private const float StepLiftFast = 24f;     // выше на скорости — читаемая рысь
        private const float StepLead = 12f;         // базовый заброс стопы вперёд
        private const float StepLeadPerSpeed = 2.4f;// доп. заброс за каждую единицу скорости
        private const float FullSpeed = 7f;         // при какой |velocity.X| походка «на полной»
        private const float FootProbeDepth = 240f;  // как глубоко искать землю под стопой
        private const float BodySquashAmount = 0.32f; // предел деформации тела (squash & stretch)

        // Пивоты (синяя/зелёная точки из шаблона) в пикселях текстур
        private static readonly Vector2 UpperPivot = new(2f, 8f);
        private static readonly Vector2 LowerPivot = new(2f, 6f);

        // Бёдра относительно центра тела (правая сторона; левая зеркалится по X).
        // 3 ноги на бок: передняя, средняя, задняя.
        private static readonly Vector2[] HipLocal =
        {
            new(34f, 4f),
            new(56f, 14f),
            new(76f, 22f),
        };

        // Насколько стопа стоит наружу от бедра по X (крабья раскоряка)
        private static readonly float[] FootSpread = { 34f, 30f, 40f };

        private Asset<Texture2D> _legUpper;
        private Asset<Texture2D> _legLower;
        private Leg[] _legs;
        private int _airborneTicks;   // гистерезис детекта «в воздухе»
        private bool _wasAirborne;    // для чистой постановки ног при приземлении
        private float _squashPose;    // деформация от текущей стадии (присед/вытяжка)
        private float _squashImpact;  // затухающий импульс сжатия от приземления
        private float _bodySquash;    // итог для DrawBody: >0 сплющен, <0 вытянут

        private class Leg
        {
            public Vector2 Hip;
            public Vector2 Foot;
            public Vector2 StepFrom;
            public Vector2 StepTo;
            public int StepTimer;
        }

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

            UpdateLegs();
            UpdateClaws();
            UpdateCrown();
            UpdateRings();
            UpdateBursts();
            RecordAfterimage();

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
            DrawLegs(spriteBatch, screenPos, drawColor);
            DrawBody(spriteBatch, screenPos, drawColor); // тело рисуем сами — ради squash & stretch
            DrawCrown(spriteBatch, screenPos, drawColor); // корона на панцире, под клешнями («забота» ложится поверх)
            DrawClawBack(spriteBatch, screenPos, drawColor);  // обе клешни перед панцирем; задняя — под передней
            DrawClawFront(spriteBatch, screenPos, drawColor);

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

            // Детект «в воздухе» с гистерезисом + привязкой к явным воздушным стадиям —
            // иначе мелкие подскоки/шаг-апы дёргают лапки между «стоит» и «висит»
            bool stateAir = (State == CrabState.JumpCrush && SubState >= 1f)
                         || (State == CrabState.Burrow && SubState >= 2f)
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

                    float homeX = leg.Hip.X + sign * FootSpread[i] * scale;

                    // Только что приземлились — ставим стопы сразу на место, без рывков-догоняний
                    if (landed)
                    {
                        float g = FindGroundY(homeX, leg.Hip.Y - 8f, FootProbeDepth, true);
                        leg.Foot = float.IsNaN(g) ? new Vector2(homeX, leg.Hip.Y + 46f * scale) : new Vector2(homeX, g);
                        leg.StepTimer = 0;
                        continue;
                    }

                    // Шаг в процессе — ведём стопу по дуге
                    if (leg.StepTimer > 0)
                    {
                        leg.StepTimer--;
                        float t = 1f - leg.StepTimer / (float)stepDur;
                        Vector2 pos = Vector2.Lerp(leg.StepFrom, leg.StepTo, t);
                        pos.Y -= (float)Math.Sin(t * Math.PI) * lift;
                        leg.Foot = leg.StepTimer == 0 ? leg.StepTo : pos;
                        continue;
                    }

                    // Стоим: если стопа уехала слишком далеко от «дома» — шагаем
                    if (Math.Abs(leg.Foot.X - homeX) <= trigger)
                        continue;

                    // Волновая походка: соседняя (ближе к голове) нога того же бока не отрывается одновременно
                    if (i > 0 && _legs[(i - 1) * 2 + s].StepTimer > 0)
                        continue;

                    float targetX = homeX + moveDir * lead;
                    float targetGround = FindGroundY(targetX, leg.Hip.Y - 8f, FootProbeDepth, true);
                    if (float.IsNaN(targetGround))
                        continue; // некуда ставить — стоим на месте

                    leg.StepFrom = leg.Foot;
                    leg.StepTo = new Vector2(targetX, targetGround);
                    leg.StepTimer = stepDur;
                }
            }
        }

        // Squash & stretch тела: пружинит к позе от стадии + затухающий импульс приземления.
        // Считается локально из синхронизированных стадии/скорости — по сети ничего не шлём.
        private void UpdateBodySquash(bool airborne, bool landed)
        {
            float targetPose = 0f;
            if (State == CrabState.JumpCrush && SubState == 0f)
                targetPose = 0.30f * MathHelper.Clamp(1f - Timer / JumpCrouchTicks, 0f, 1f); // присед сплющивает
            else if (State == CrabState.CrushingGrip && SubState == 0f)
                targetPose = 0.20f * MathHelper.Clamp(1f - Timer / GripWindupTicks, 0f, 1f); // изготовка перед выпадом
            else if (State == CrabState.TsunamiClap)
                targetPose = -0.18f * MathHelper.Clamp(1f - Timer / TsunamiWindupTicks, 0f, 1f); // привстаёт для хлопка
            else if (State == CrabState.Dying)
                targetPose = 0.35f; // оседает, «последний взгляд»
            else if (_sulkTimer > 0)
                targetPose = 0.22f; // «недовольство» — опускает корпус
            else if (_proudPose && State == CrabState.Scuttle)
                targetPose = -0.15f; // «гордая поза» — вытягивается
            else if (airborne && NPC.velocity.Y < -1f)
                targetPose = -0.22f; // на взлёте вытягивается
            _squashPose = MathHelper.Lerp(_squashPose, targetPose, 0.2f);

            if (landed)
                _squashImpact = 0.55f; // резкая плюха при приземлении
            _squashImpact = MathHelper.Lerp(_squashImpact, 0f, 0.16f);

            _bodySquash = _squashPose + _squashImpact;
        }

        // Рисуем панцирь с неоднородным масштабом. При _bodySquash == 0 совпадает с ванильной
        // отрисовкой (та же привязка к bodyCenter, что и у ног), поэтому в покое картинка не меняется.
        private void DrawBody(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D tex = TextureAssets.Npc[Type].Value;
            if (tex == null)
                return;

            float sx = 1f + _bodySquash * BodySquashAmount;
            float sy = 1f - _bodySquash * BodySquashAmount;
            Vector2 scale = new Vector2(NPC.scale * sx, NPC.scale * sy);

            // Держим «ноги» на месте: при сжатии центр опускаем на убыль полувысоты
            int frameCount = Math.Max(1, Main.npcFrameCount[Type]);
            float halfHeightWorld = tex.Height / frameCount / 2f * NPC.scale;
            Vector2 bodyCenter = NPC.Center - new Vector2(0f, BodyLift);
            Vector2 drawCenter = bodyCenter + new Vector2(0f, halfHeightWorld * (1f - sy));

            DrawBodySprite(drawCenter, NPC.rotation, scale, drawColor, screenPos);
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
            // Центр приподнятого тела (совпадает со сдвигом спрайта через DrawOffsetY)
            Vector2 bodyCenter = NPC.Center - new Vector2(0f, BodyLift);
            return bodyCenter + new Vector2(sign * HipLocal[i].X * scale, HipLocal[i].Y * scale).RotatedBy(NPC.rotation);
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
