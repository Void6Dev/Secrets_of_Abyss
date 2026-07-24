using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Клешни Короля-краба — четырёхчастный риг: плечевой сегмент → локоть → предплечье →
    // запястье → клешня (основание + подвижный коготь). Рука решается двухкостным IK
    // (как ноги), поза задаёт ПОЗИЦИЮ запястья, угол клешни и раскрытие пинцера —
    // клешня может вести замах дугой, вытягиваться в выпаде и прижиматься к телу.
    // Чистая косметика: поза считается локально на каждом клиенте из синхронизированных
    // State/SubState/Timer/направления, по сети ничего не шлём и ai[] не тратим.
    // Обе клешни рисуются ПЕРЕД панцирем: задняя — под передней.
    // Индекс: 0 — передняя (со стороны взгляда, активная в атаках), 1 — задняя.
    public partial class King_crab
    {
        // --- Настройки рига (крути тут после добавления спрайта) ---
        private const float ClawScale = 1f;          // клешни нарисованы 1:1 к панцирю
        private const float ArmScale = 1.25f;        // сегменты руки относительно тела
        private const float ClawShoulderX = 102.5f;  // вынос плеча вперёд от центра тела (в px спрайта тела)
        private const float ClawShoulderY = -17.5f;  // высота плеча относительно центра тела (- вверх)
        private const float ClawOpenAngle = 0.5f;    // максимальный разворот когтя при полном раскрытии (рад)
        private const float ClawResponse = 0.3f;     // скорость подстройки позы к цели (0..1)
        private const float ClawDirFix = 1f;         // = -1, если клешни смотрят в противоположную от тела сторону
        private const float ClawBackRestBias = 0.45f; // разворот задней клешни наружу, чтобы не сливалась с передней
        private const float ArmElbowSign = -1f;      // сторона сгиба локтя; поменяй знак, если гнётся не туда
        private const float ClawSquashDip = 12f;     // насколько запястья проседают при плюхе тела (px на единицу squash)

        // Кости руки: пивот→сустав в пикселях текстур сегментов
        private const float ArmBoneUpperPx = 29f;    // плечо→локоть в KingCrabArmUpper.png
        private const float ArmBoneLowerPx = 27f;    // локоть→запястье в KingCrabArmLower.png
        private static readonly Vector2 ArmUpperPivot = new(12f, 13f); // плечевой сустав в KingCrabArmUpper.png
        private static readonly Vector2 ArmLowerPivot = new(10f, 11f); // локтевой сустав в KingCrabArmLower.png

        // Анкеры в пикселях текстур клешни. Авторская ориентация — краб смотрит влево (как King_crab.png).
        // ClawBaseShoulder — «нарост» на клешне, которым она крепится к запястью руки.
        private static readonly Vector2 ClawBaseShoulder = new(57f, 15f); // крепление к запястью (верх плечевого сегмента)
        private static readonly Vector2 ClawBaseHinge = new(66f, 53f);    // шарнир подвижного когтя в base
        private static readonly Vector2 ClawTipHinge = new(10f, 17f);     // тот же шарнир в tip

        // --- Углы клешни (отклонение от авторской позы покоя, рад; + = клешня поднята вверх) ---
        private const float ClawRestPitch = 0.15f;   // покой
        private const float ClawIdleBob = 0.06f;     // амплитуда покачивания при ходьбе
        private const float ClawSlamRaise = 1.1f;    // замах слэма вверх
        private const float ClawSlamStrike = -0.55f; // удар слэма вниз
        private const float ClawSweepRear = 0.8f;    // отвод перед рывком
        private const float ClawSweepThrust = -0.4f; // выпад вперёд на рывке
        private const float ClawTideRaise = 1.2f;    // воздеты при призыве прилива
        private const float ClawTuck = -0.35f;       // поджаты (прыжок / под землёй)
        private const float ClawGripReady = 0.4f;    // изготовка захлопа: приподнята и раскрыта
        private const float ClawGripThrust = -0.35f; // выпад захлопа
        private const float ClawCrownReach = 1.35f;  // задняя клешня тянется к короне
        private const float ClawProudRaise = 0.55f;  // «гордая поза» на дистанции
        private const float ClawSulkDroop = -0.3f;   // «недовольство» после промаха
        private const float ClawDeathDroop = -0.4f;  // бессильно опущены при смерти

        // --- Позиции запястья (от плеча, в px тела; +X = наружу вдоль своей стороны, +Y = вниз) ---
        private static readonly Vector2 WristRest = new(10f, 16f);        // покой: клешня висит у панциря
        private static readonly Vector2 WristSlamRaise = new(-8f, -46f);  // замах слэма: занесена над головой
        private static readonly Vector2 WristSlamStrike = new(36f, 30f);  // удар слэма: вбита вперёд-вниз
        private static readonly Vector2 WristSweepRear = new(-20f, 4f);   // отвод к телу перед рывком
        private static readonly Vector2 WristSweepThrust = new(48f, 12f); // вытянута в рывке
        private static readonly Vector2 WristTideRaise = new(4f, -42f);   // воздета при призыве прилива/рёве
        private static readonly Vector2 WristTuck = new(0f, 20f);         // поджата (прыжок / под землёй)
        private static readonly Vector2 WristGripReady = new(-12f, -14f); // изготовка захлопа: оттянута
        private static readonly Vector2 WristGripThrust = new(54f, 14f);  // выпад захлопа: далеко вперёд
        private static readonly Vector2 WristCrownReach = new(-2f, -40f); // задняя тянется вверх к короне
        private static readonly Vector2 WristClapRaise = new(12f, -46f);  // замах хлопка: разведены вверх
        private static readonly Vector2 WristClapStrike = new(-16f, 10f); // хлопок: сведены к центру перед телом
        private static readonly Vector2 WristProud = new(6f, -22f);       // «гордая поза»
        private static readonly Vector2 WristSulk = new(10f, 28f);        // «недовольство»: обвисла
        private static readonly Vector2 WristDeath = new(16f, 36f);       // смерть: лежит на земле
        private static readonly Vector2 WristVolley = new(14f, -2f);      // залп пузырей: приподнята вперёд
        private static readonly Vector2 WristGuard = new(20f, 6f);        // передняя охраняет при созыве

        // --- Раскрытие пинцера (0 — сомкнут, 1 — раскрыт) ---
        private const float ClawRestOpen = 0.15f;
        private const float ClawOpenMax = 1f;

        private Asset<Texture2D> _clawBase;
        private Asset<Texture2D> _clawTip;
        private Asset<Texture2D> _armUpper;
        private Asset<Texture2D> _armLower;
        private readonly Vector2[] _clawWrist = new Vector2[2];
        private readonly float[] _clawPitch = new float[2];
        private readonly float[] _clawOpen = new float[2];
        private bool _clawsInit;

        private void UpdateClaws()
        {
            if (!_clawsInit)
            {
                _clawsInit = true;
                for (int i = 0; i < 2; i++)
                {
                    _clawWrist[i] = WristRest;
                    _clawPitch[i] = ClawRestPitch;
                    _clawOpen[i] = ClawRestOpen;
                }
            }

            for (int i = 0; i < 2; i++)
            {
                (Vector2 wrist, float pitch, float open) = ClawPose(i);
                wrist.Y += _bodySquash * ClawSquashDip; // плюха тела продавливает и руки
                _clawWrist[i] = Vector2.Lerp(_clawWrist[i], wrist, ClawResponse);
                _clawPitch[i] = MathHelper.Lerp(_clawPitch[i], pitch, ClawResponse);
                _clawOpen[i] = MathHelper.Lerp(_clawOpen[i], open, ClawResponse);
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        // Целевая поза клешни по текущей стадии. Активна (телеграфит атаку) передняя клешня (idx 0);
        // задняя держит опорную позу, только в приливе/прыжке/хлопке двигаются обе.
        private (Vector2 wrist, float pitch, float open) ClawPose(int idx)
        {
            bool front = idx == 0;
            Vector2 wrist = WristRest;
            float pitch = ClawRestPitch;
            float open = ClawRestOpen;

            switch (State)
            {
                case CrabState.Scuttle:
                    // Ходьба: клешни идут в противофазе шагам — лёгкий мах вперёд-назад
                    float phase = _stepCycle * 0.2f + (front ? 0f : 1.5f);
                    wrist = WristRest + new Vector2((float)Math.Sin(phase) * 3f, (float)Math.Cos(phase) * 2f);
                    pitch = ClawRestPitch + (float)Math.Sin(phase) * ClawIdleBob;
                    break;

                case CrabState.ClawSlam:
                    if (front)
                    {
                        float p = 1f - Timer / ClawWindupTicks; // 0→1 за замах
                        if (p < 0.6f) // замах: запястье уводит клешню дугой над головой
                        {
                            float t = Smooth(p / 0.6f);
                            wrist = Vector2.Lerp(WristRest, WristSlamRaise, t);
                            pitch = MathHelper.Lerp(ClawRestPitch, ClawSlamRaise, t);
                            open = MathHelper.Lerp(ClawRestOpen, ClawOpenMax, t);
                        }
                        else if (p < 0.85f) // задержка на пике: дрожит от напряжения — читаемый телеграф
                        {
                            float tremble = Main.GameUpdateCount * 0.9f;
                            wrist = WristSlamRaise + new Vector2((float)Math.Sin(tremble) * 1.5f, (float)Math.Cos(tremble * 1.3f) * 1.5f);
                            pitch = ClawSlamRaise;
                            open = ClawOpenMax;
                        }
                        else // удар: запястье вбивает клешню вперёд-вниз с ускорением + щелчок
                        {
                            float t = (p - 0.85f) / 0.15f;
                            float tt = t * t;
                            wrist = Vector2.Lerp(WristSlamRaise, WristSlamStrike, tt);
                            pitch = MathHelper.Lerp(ClawSlamRaise, ClawSlamStrike, tt);
                            open = MathHelper.Lerp(ClawOpenMax, 0f, tt);
                        }
                    }
                    break;

                case CrabState.ClawSweep:
                    if (front)
                    {
                        if (SubState == 0f) // замах: клешня оттягивается к телу, пинцер раскрывается
                        {
                            float w = Smooth(1f - Timer / ClawSweepWindupTicks);
                            wrist = Vector2.Lerp(WristRest, WristSweepRear, w);
                            pitch = MathHelper.Lerp(ClawRestPitch, ClawSweepRear, w);
                            open = MathHelper.Lerp(ClawRestOpen, ClawOpenMax, w);
                        }
                        else // рывок: выброшена во всю длину, к концу подбирается и смыкается
                        {
                            float t = 1f - Timer / ClawSweepDashTicks;
                            wrist = Vector2.Lerp(WristSweepThrust, WristRest, Smooth(t) * 0.5f);
                            pitch = MathHelper.Lerp(ClawSweepThrust, ClawRestPitch, t);
                            open = MathHelper.Lerp(0f, ClawRestOpen, t);
                        }
                    }
                    break;

                case CrabState.TideCall: // обе воздеты, парят с медленным покачиванием
                    {
                        float hover = (float)Math.Sin(Main.GameUpdateCount * 0.08f + (front ? 0f : 0.9f)) * 3f;
                        wrist = WristTideRaise + new Vector2(0f, hover);
                        pitch = ClawTideRaise;
                        open = ClawOpenMax * 0.6f;
                    }
                    break;

                case CrabState.JumpCrush: // поджаты в прыжке
                    wrist = WristTuck;
                    pitch = ClawTuck;
                    open = 0f;
                    break;

                case CrabState.BubbleVolley: // приподнята вперёд, пинцер «дышит» в такт залпу
                    wrist = WristVolley;
                    open = 0.35f + 0.25f * (float)Math.Sin(Main.GameUpdateCount * 0.35f);
                    break;

                case CrabState.CrushingGrip:
                    if (front)
                    {
                        if (SubState == 0f) // изготовка: оттянута и раскрывается во всю ширь
                        {
                            float w = Smooth(1f - Timer / GripWindupTicks);
                            wrist = Vector2.Lerp(WristRest, WristGripReady, w);
                            pitch = MathHelper.Lerp(ClawRestPitch, ClawGripReady, w);
                            open = MathHelper.Lerp(ClawRestOpen, ClawOpenMax, w);
                        }
                        else // выпад: рука выстреливает вперёд, захлоп ускоряется к щелчку
                        {
                            float t = 1f - Timer / GripLungeTicks;
                            wrist = WristGripThrust;
                            pitch = ClawGripThrust;
                            open = MathHelper.Lerp(ClawOpenMax, 0f, t * t);
                        }
                    }
                    break;

                case CrabState.RoyalRoar: // обе воздеты и раскрыты, как перед приливом
                    {
                        float w = Smooth(1f - Timer / RoarWindupTicks);
                        wrist = Vector2.Lerp(WristRest, WristTideRaise, w);
                        pitch = MathHelper.Lerp(ClawRestPitch, ClawTideRaise, w);
                        open = MathHelper.Lerp(ClawRestOpen, ClawOpenMax, w);
                    }
                    break;

                case CrabState.CrownCommand:
                    if (front) // передняя охраняет: выставлена вперёд
                    {
                        wrist = WristGuard;
                        open = ClawOpenMax * 0.4f;
                    }
                    else // задняя тянется вверх к короне
                    {
                        wrist = WristCrownReach;
                        pitch = ClawCrownReach;
                    }
                    break;

                case CrabState.TsunamiClap:
                    {
                        float w = 1f - Timer / TsunamiWindupTicks;
                        if (w < 0.85f) // замах: обе разведены вверх и раскрыты
                        {
                            float t = Smooth(w / 0.85f);
                            wrist = Vector2.Lerp(WristRest, WristClapRaise, t);
                            pitch = MathHelper.Lerp(ClawRestPitch, ClawTideRaise, t);
                            open = MathHelper.Lerp(ClawRestOpen, ClawOpenMax, t);
                        }
                        else // хлопок: обе сшибаются к центру перед корпусом
                        {
                            float t = (w - 0.85f) / 0.15f;
                            float tt = t * t;
                            wrist = Vector2.Lerp(WristClapRaise, WristClapStrike, tt);
                            pitch = MathHelper.Lerp(ClawTideRaise, ClawSlamStrike, tt);
                            open = 0f;
                        }
                    }
                    break;

                case CrabState.Dying: // бессильно опущены, лежат на земле
                    wrist = WristDeath;
                    pitch = ClawDeathDroop;
                    open = 0.05f;
                    break;

                default: // KnightCourt / Burrow — под землёй, поджаты
                    wrist = WristTuck;
                    pitch = ClawTuck;
                    open = 0f;
                    break;
            }

            // Характер поверх стадийной позы
            if (State == CrabState.Scuttle)
            {
                if (_sulkTimer > 0) // «недовольство»: обвисли
                {
                    wrist = WristSulk;
                    pitch = ClawSulkDroop;
                    open = 0.05f;
                }
                else if (_proudPose) // «гордая поза»: подняты, лёгкое покачивание
                {
                    float sway = (float)Math.Sin(Main.GameUpdateCount * 0.05f + (front ? 0f : 0.8f));
                    wrist = WristProud + new Vector2(0f, sway * 2f);
                    pitch = ClawProudRaise + sway * 0.05f;
                    open = 0.35f;
                }
            }
            if (_crownCareTimer > 0 && !front && State != CrabState.CrownCommand) // придерживает корону
            {
                wrist = WristCrownReach;
                pitch = ClawCrownReach;
                open = 0.15f;
            }

            return (wrist, pitch, open);
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
            float scale = NPC.scale * ClawScale;
            float armScale = NPC.scale * ArmScale;
            int aimSign = idx == 0 ? 1 : -1; // передняя тянется вперёд, задняя — назад
            float dirSign = NPC.spriteDirection * ClawDirFix;
            float pitch = _clawPitch[idx];
            float open = _clawOpen[idx];

            // Экранная сторона клешни: -1 = левая (авторская ориентация текстур),
            // +1 = правая (текстуры зеркалятся горизонтально, как и панцирь)
            bool flip = aimSign * dirSign > 0f;
            float side = flip ? 1f : -1f;

            // Подъём — отклонение от авторской позы; экранный знак зависит от стороны.
            // Задняя клешня чуть развёрнута наружу, чтобы не сливалась с передней
            float raise = pitch - ClawRestPitch;
            if (idx == 1)
                raise += ClawBackRestBias;
            float rot = NPC.rotation - side * raise;
            SpriteEffects fx = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Vector2 shoulderWorld = FacingToWorld(new Vector2(aimSign * ClawShoulderX, ClawShoulderY) * NPC.scale);

            // Запястье из позы: клешне-локальные координаты (+X наружу) → мир
            Vector2 wristLocal = _clawWrist[idx];
            Vector2 wristWorld = shoulderWorld
                + new Vector2(aimSign * wristLocal.X * dirSign, wristLocal.Y).RotatedBy(NPC.rotation) * NPC.scale;

            DrawArm(spriteBatch, shoulderWorld, wristWorld, flip, armScale, screenPos, drawColor);

            // Шарнир когтя едет вместе с клешнёй вокруг запястья
            Vector2 armTex = ClawBaseHinge - ClawBaseShoulder;
            Vector2 hingeWorld = wristWorld
                + new Vector2(flip ? -armTex.X : armTex.X, armTex.Y).RotatedBy(rot) * scale;

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
        private void DrawArm(SpriteBatch spriteBatch, Vector2 shoulder, Vector2 wrist, bool flip, float armScale, Vector2 screenPos, Color drawColor)
        {
            Texture2D upper = _armUpper.Value;
            Texture2D lower = _armLower.Value;
            float l1 = ArmBoneUpperPx * armScale;
            float l2 = ArmBoneLowerPx * armScale;

            float d = MathHelper.Clamp(Vector2.Distance(shoulder, wrist), Math.Abs(l1 - l2) + 0.1f, l1 + l2 - 0.1f);
            float baseAngle = (wrist - shoulder).ToRotation();
            float cosA = MathHelper.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float offset = (float)Math.Acos(cosA);

            // Фиксированная сторона сгиба (зеркалится вместе с клешнёй) — локоть не «щёлкает»
            // между решениями, когда запястье проходит уровень плеча
            float bendSign = ArmElbowSign * (flip ? -1f : 1f);
            Vector2 elbow = shoulder + new Vector2(l1, 0f).RotatedBy(baseAngle + bendSign * offset);

            float upperRot = (elbow - shoulder).ToRotation();
            float lowerRot = (wrist - elbow).ToRotation();

            // Сегмент нарисован вправо; если повёрнут влево — переворачиваем, чтобы блик остался сверху
            SpriteEffects upperFx = Math.Cos(upperRot) < 0f ? SpriteEffects.FlipVertically : SpriteEffects.None;
            SpriteEffects lowerFx = Math.Cos(lowerRot) < 0f ? SpriteEffects.FlipVertically : SpriteEffects.None;

            spriteBatch.Draw(upper, shoulder - screenPos, null, drawColor, upperRot, ArmUpperPivot, armScale, upperFx, 0f);
            spriteBatch.Draw(lower, elbow - screenPos, null, drawColor, lowerRot, ArmLowerPivot, armScale, lowerFx, 0f);
        }

        // «Лицевое» пространство → мир: отражаем X по направлению взгляда, наклоняем на угол тела,
        // сдвигаем к приподнятому центру тела (та же привязка, что у ног и панциря).
        private Vector2 FacingToWorld(Vector2 forwardOffset)
        {
            float fx = forwardOffset.X * (NPC.spriteDirection * ClawDirFix);
            Vector2 bodyCenter = NPC.Center - new Vector2(0f, BodyLift);
            return bodyCenter + new Vector2(fx, forwardOffset.Y).RotatedBy(NPC.rotation);
        }
    }
}
