using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;
using SoA.Common.Graphics.Animation;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Клешни Короля-краба — четырёхчастный риг: плечевой сегмент → локоть → предплечье →
    // запястье → клешня (основание + подвижный коготь). Рука решается двухкостным IK
    // (как ноги). ПОЗУ ЦЕЛИКОМ ДИКТУЮТ КЕЙФРЕЙМОВЫЕ КЛИПЫ (King_crab.Animation.cs):
    // Offset слоя — сдвиг запястья от позы покоя, Rotation — подъём клешни, Aux — раскрытие
    // пинцера. Здесь остались только геометрия рига и отрисовка.
    // Чистая косметика: клипы тикаются локально из синхронизированного State/SubState/Timer,
    // по сети ничего не шлём и ai[] не тратим.
    // Обе клешни рисуются ПЕРЕД панцирем: задняя — под передней.
    // Индекс: 0 — передняя (со стороны взгляда, активная в атаках), 1 — задняя.
    public partial class King_crab
    {
        // Все числа рига (плечо, кости руки, стойка клешни, анкеры) — в King_crab.Rig.cs

        private Asset<Texture2D> _clawBase;
        private Asset<Texture2D> _clawTip;
        private Asset<Texture2D> _armUpper;
        private Asset<Texture2D> _armLower;
        private readonly Vector2[] _clawWrist = new Vector2[2];
        private readonly float[] _clawPitch = new float[2];
        private readonly float[] _clawOpen = new float[2];
        private readonly Vector2[] _clawWristWorld = new Vector2[2]; // кэш для шлейфа (см. DrawClawGhost)

        // На резких стадиях запястье ДОГОНЯЕТ целевую точку, а не встаёт в неё мгновенно
        private static bool IsSharpStage(CrabState state, float sub) => state switch
        {
            CrabState.ClawSweep => true,
            CrabState.CrushingGrip => sub >= 1f,
            CrabState.OceanRage => sub is RageSubRam or RageSubCharge,
            _ => false,
        };

        // Поза клешней целиком из кейфреймовых клипов; интерполяцию и сглаживание
        // переходов делает AnimPlayer (easing ключей + кроссфейд при смене клипа).
        // Сверху ложится вторичная физика: отставание запястья по инерции, дрожь после
        // тяжёлого удара и дыхание со своим периодом.
        // До первого тика аниматора (бестиарий) AnimPose даёт Identity = поза покоя.
        private void UpdateClaws()
        {
            bool sharp = IsSharpStage(State, SubState);
            for (int i = 0; i < 2; i++)
            {
                LayerPose pose = AnimPose(i == 0 ? LayerClawFront : LayerClawBack);
                Vector2 wrist = ClawWristRest + pose.Offset; // в координатах от центра тела
                wrist.Y += _bodySquash * ClawSquashDip                  // плюха тела продавливает и руки
                         + Math.Max(0f, _squashImpact) * ClawSquashDipExtra; // на приземлении — сильнее

                // Дыхание клешней: период 76 тиков не совпадает ни с одним клипом, поэтому
                // в стойке рисунок не повторяется. В атаках гасится _calmness.
                wrist.Y += _breathClaw * (1f + i * 0.4f) * _calmness;

                // Отставание на 1–3 тика: рука тяжёлая и на рывке не поспевает за телом
                _clawLag[i] = sharp
                    ? Vector2.Lerp(_clawLag[i], wrist, ClawLagRate)
                    : wrist;
                wrist = _clawLag[i];

                // Дрожь после тяжёлого удара. Ключами такую частоту не записать —
                // интерполяция её съест, поэтому это шум, а не кейфреймы.
                if (_clawShake > 0)
                {
                    float amp = ClawShakeAmp * (_clawShake / (float)ClawShakeTicks);
                    wrist += Main.rand.NextVector2Circular(amp, amp);
                }

                _clawWrist[i] = wrist;
                _clawPitch[i] = ClawStanceRaise + pose.Rotation; // подъём от авторской позы текстуры
                _clawOpen[i] = MathHelper.Clamp(ClawRestOpen + pose.Aux, 0f, 1f);
            }
        }

        // Силуэт клешни для шлейфа: только основание пинцера в сохранённой мировой точке
        // запястья. Полный риг руки шлейфу не нужен — важен читаемый контур целой туши,
        // а не рой оторванных панцирей (именно так шлейф и выглядел раньше).
        private void DrawClawGhost(SpriteBatch spriteBatch, int idx, Vector2 wristWorld, float bodyRot,
            Vector2 screenPos, Color color)
        {
            if (_clawBase?.Value == null)
                return;

            Texture2D baseTex = _clawBase.Value;
            int aimSign = idx == 0 ? 1 : -1;
            bool flip = aimSign * (NPC.spriteDirection * ClawDirFix) > 0f;
            float side = flip ? 1f : -1f;
            float raise = _clawPitch[idx] + (idx == 1 ? ClawBackRestBias : 0f);
            float rot = bodyRot - side * raise;

            Vector2 origin = flip ? new Vector2(baseTex.Width - ClawBaseShoulder.X, ClawBaseShoulder.Y) : ClawBaseShoulder;
            SpriteEffects fx = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(baseTex, wristWorld - screenPos, null, color, rot, origin,
                NPC.scale * ClawScale, fx, 0);
        }

        // Задняя клешня. Зовётся после DrawBody, до передней (лежит под ней).
        private void DrawClawBack(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
            => DrawClaw(spriteBatch, 1, screenPos, drawColor);

        // Передняя клешня. Зовётся последней — поверх тела и задней клешни.
        private void DrawClawFront(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
            => DrawClaw(spriteBatch, 0, screenPos, drawColor);

        private void DrawClaw(SpriteBatch spriteBatch, int idx, Vector2 screenPos, Color drawColor)
        {
            if (_clawBase?.Value == null || _clawTip?.Value == null || _armUpper?.Value == null || _armLower?.Value == null)
                return;

            Texture2D baseTex = _clawBase.Value;
            Texture2D tipTex = _clawTip.Value;
            Vector2 scale = new Vector2(NPC.scale * ClawScale) * AnimPose(idx == 0 ? LayerClawFront : LayerClawBack).Scale;
            float armScale = NPC.scale * ArmScale;
            int aimSign = idx == 0 ? 1 : -1; // передняя тянется вперёд, задняя — назад
            float dirSign = NPC.spriteDirection * ClawDirFix;
            float pitch = _clawPitch[idx];
            float open = _clawOpen[idx];
            float bodyRot = AnimatedBodyRotation();

            // Экранная сторона клешни: -1 = левая (авторская ориентация текстур),
            // +1 = правая (текстуры зеркалятся горизонтально, как и панцирь)
            bool flip = aimSign * dirSign > 0f;
            float side = flip ? 1f : -1f;

            // Подъём — отклонение от авторской позы; экранный знак зависит от стороны.
            // Задняя клешня чуть развёрнута наружу, чтобы не сливалась с передней
            float raise = pitch;
            if (idx == 1)
                raise += ClawBackRestBias;
            float rot = bodyRot - side * raise;
            SpriteEffects fx = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            // Плечо приклеено к панцирю, поэтому берём его через BodyAnchorToWorld — оно обязано
            // деформироваться вместе с телом, иначе рука отваливается на каждой плюхе
            Vector2 shoulderLocal = new Vector2(ClawShoulderX, ClawShoulderY);
            Vector2 shoulderWorld = BodyAnchorToWorld(new Vector2(aimSign * shoulderLocal.X, shoulderLocal.Y));

            // Запястье задано от центра тела, поэтому от плеча его отделяет разность. Саму руку
            // держим ЖЁСТКОЙ: с деформацией тела едет только точка крепления (плечо), иначе на
            // плюхе клешню отбрасывало бы от корпуса на треть ширины панциря.
            Vector2 wristFromShoulder = _clawWrist[idx] - shoulderLocal;
            Vector2 wristWorld = shoulderWorld
                + new Vector2(aimSign * wristFromShoulder.X * dirSign, wristFromShoulder.Y).RotatedBy(bodyRot) * NPC.scale;

            _clawWristWorld[idx] = wristWorld; // шлейфу нужна мировая точка запястья
            DrawArm(spriteBatch, shoulderWorld, wristWorld, flip, armScale, screenPos, drawColor);

            // Шарнир когтя едет вместе с клешнёй вокруг запястья
            Vector2 armTex = ClawBaseHinge - ClawBaseShoulder;
            Vector2 hingeWorld = wristWorld
                + (new Vector2(flip ? -armTex.X : armTex.X, armTex.Y) * scale).RotatedBy(rot);

            // При зеркалировании XNA не отражает origin — отражаем сами, чтобы пивот остался на суставе
            Vector2 baseOrigin = flip ? new Vector2(baseTex.Width - ClawBaseShoulder.X, ClawBaseShoulder.Y) : ClawBaseShoulder;
            Vector2 tipOrigin = flip ? new Vector2(tipTex.Width - ClawTipHinge.X, ClawTipHinge.Y) : ClawTipHinge;

            // Коготь раскрывается, отходя вверх от неподвижной половины пинцера.
            // Рисуем его ПОД базой: в авторском спрайте кромка серпа перекрывает коготь.
            float tipRot = rot + side * open * ClawOpenAngle;
            spriteBatch.Draw(tipTex, hingeWorld - screenPos, null, drawColor, tipRot, tipOrigin, scale, fx, 0f);
            spriteBatch.Draw(baseTex, wristWorld - screenPos, null, drawColor, rot, baseOrigin, scale, fx, 0f);
        }

        // Рука плечо→запястье: двухкостный IK, локоть через теорему косинусов (как у ног).
        // Рисуется ПОД клешнёй — её «нарост» ClawBaseShoulder накрывает запястный сустав.
        // Сегменты круглые, поэтому при выносе запястья дальше суммы костей рука не клампится,
        // а РАСТЯГИВАЕТСЯ: шары просто расходятся, и связка с клешнёй не рвётся на выпадах
        // (вытянутые сегменты раньше маскировали разрыв собой, круглые — не маскируют).
        private void DrawArm(SpriteBatch spriteBatch, Vector2 shoulder, Vector2 wrist, bool flip, float armScale, Vector2 screenPos, Color drawColor)
        {
            Texture2D upper = _armUpper.Value;
            Texture2D lower = _armLower.Value;
            float l1 = ArmBoneUpperPx * armScale;
            float l2 = ArmBoneLowerPx * armScale;

            float need = Vector2.Distance(shoulder, wrist);
            float stretch = Math.Max(1f, need / (l1 + l2 - 0.1f));
            l1 *= stretch;
            l2 *= stretch;

            float d = MathHelper.Clamp(need, Math.Abs(l1 - l2) + 0.1f, l1 + l2 - 0.1f);
            float baseAngle = (wrist - shoulder).ToRotation();
            float cosA = MathHelper.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float offset = (float)Math.Acos(cosA);

            // Фиксированная сторона сгиба (зеркалится вместе с клешнёй) — локоть не «щёлкает»
            // между решениями, когда запястье проходит уровень плеча
            float bendSign = ArmElbowSign * (flip ? -1f : 1f);
            Vector2 elbow = shoulder + new Vector2(l1, 0f).RotatedBy(baseAngle + bendSign * offset);

            // Сегменты круглые: доворачивать их вдоль руки незачем — поворот гонял бы блик по кругу.
            // А вот зеркалить надо ВМЕСТЕ С КЛЕШНЁЙ (тот же flip): блик на шарах направленный,
            // и на отражённой стороне он обязан смотреть в ту же сторону, что и на клешне.
            SpriteEffects fx = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 upperOrigin = flip ? new Vector2(upper.Width - ArmUpperPivot.X, ArmUpperPivot.Y) : ArmUpperPivot;
            Vector2 lowerOrigin = flip ? new Vector2(lower.Width - ArmLowerPivot.X, ArmLowerPivot.Y) : ArmLowerPivot;

            spriteBatch.Draw(upper, shoulder - screenPos, null, drawColor, 0f, upperOrigin, armScale, fx, 0f);
            spriteBatch.Draw(lower, elbow - screenPos, null, drawColor, 0f, lowerOrigin, armScale, fx, 0f);
        }

        // «Лицевое» пространство → мир: отражаем X по направлению взгляда, наклоняем на угол тела,
        // сдвигаем к приподнятому центру тела (та же привязка, что у ног и панциря).
        // БОЕВАЯ версия — без смещений клипов (зона короны, точки для пыли).
        private Vector2 FacingToWorld(Vector2 forwardOffset)
        {
            float fx = forwardOffset.X * (NPC.spriteDirection * ClawDirFix);
            Vector2 bodyCenter = NPC.Center - new Vector2(0f, BodyLift);
            return bodyCenter + new Vector2(fx, forwardOffset.Y).RotatedBy(NPC.rotation);
        }
    }
}
