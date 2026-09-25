using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Players;
using SoA.Common.Utils;

namespace SoA.Content.Projectiles
{
    // Один удар Королевского копья. Удары выпускает RoyalSpearFlurry — и короткую
    // связку ЛКМ, и шквал после зажатия.
    //   ai[0] — форма удара (StrikeStyle)
    //   ai[1] — разброс этого удара (-1..1), брошенный владельцем и потому одинаковый
    //           у всех клиентов
    //   ai[2] — сколько тиков живёт удар; 0 — взять длительность формы по умолчанию
    public class RoyalSpearProjectile : ModProjectile
    {
        public enum StrikeStyle
        {
            Jab,     // быстрый прямой укол
            Sweep,   // взмах древком снизу вверх
            Lunge,   // тяжёлый выпад с замахом назад
            Flurry,  // лёгкий укол шквала
        }

        // Спрайт лежит рядом с кодом в папке оружия, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        private const float TipOffset = 10f;

        // По форме удара (индекс — StrikeStyle): вылет, масштаб копья, длительность
        private static readonly float[] StyleReach = { 66f, 56f, 98f, 60f };
        private static readonly float[] StyleScale = { 1.35f, 1.35f, 1.55f, 1.25f };

        // Водяное лезвие, которое выстреливает из острия на выпаде (как энергия у Dark
        // Lance): удлиняет удар и бьёт вместе с древком. Длина и полуширина по форме удара
        private static readonly float[] StyleSpikeLength = { 44f, 30f, 64f, 34f };
        private static readonly float[] StyleSpikeWidth = { 9f, 11f, 12f, 7f };
        private static readonly Color SpikeColor = new(90, 185, 255);

        // Лезвие растёт на отрезке вылета от SpikeFrom до SpikeFull
        private const float SpikeFrom = 0.3f;
        private const float SpikeFull = 0.75f;
        private static readonly int[] StyleDuration = { 6, 8, 12, 7 };

        private const float ReachVariance = 0.12f;  // насколько разброс тянет вылет

        // Взмах проходит дугу от нижнего края к верхнему, половина дуги в радианах
        private const float SweepHalfArc = 0.95f;

        // Выпад сначала отводит копьё назад за кисть, потом выстреливает
        private const float LungeWindupPull = 0.3f;

        // Связка идёт быстро, и одиночный спрайт читался бы как мигание. Держим короткую
        // историю положений: из неё рисуются росчерк по острию и затухающие копии древка
        private const int TrailLength = 10;
        private static readonly Color TideGlow = new(120, 210, 255, 0);

        // Лента за острием (SoATrail): полуширина у головы по форме удара и цвет воды
        private static readonly float[] StyleRibbonWidth = { 10f, 18f, 16f, 7f };
        private static readonly Color RibbonColor = new(60, 150, 255);

        // Копии древка берём только из первых кадров истории: дальше их заменяет лента
        private const int AfterimageCount = 4;

        // С какого вылета острие вспыхивает конусом и сыплет брызги
        private const float PeakFrom = 0.72f;

        // Спрайт 22x100, острие сверху. Точка опоры — не центр текстуры, а пятка древка:
        // при центре половина копья уходила игроку за спину и вперёд торчала только
        // половина длины. GripOffset — сколько рукояти остаётся позади кисти.
        // SpriteLength обязан совпадать с высотой PNG: от него считаются хитбокс древка
        // и всё, что рисуется на острие. Перерисовал спрайт другой высоты — поправь здесь.
        // Публичные: этой же опорой рисуют копьё в руке замахи (RoyalSpearCharge, RoyalSpearFlurry)
        public const float SpriteLength = 100f;
        public const float GripOffset = 36f;
        private const float ShaftWidth = 20f;

        public static int DefaultDuration(StrikeStyle style) => StyleDuration[(int)style];

        // Куда достаёт острие от центра снаряда
        private float TipDistance => (SpriteLength - GripOffset) * Projectile.scale;

        // 0..1 — насколько выросло лезвие; убранное копьё лезвия не несёт
        private float SpikeGrowth => MathHelper.Clamp((extension - SpikeFrom) / (SpikeFull - SpikeFrom), 0f, 1f);

        private float SpikeLength => StyleSpikeLength[(int)Style] * SpikeGrowth * Projectile.scale;

        private Vector2 SpearTip => Projectile.Center + strikeDir * TipDistance;

        private StrikeStyle Style => (StrikeStyle)Math.Clamp((int)Projectile.ai[0], 0, StyleReach.Length - 1);

        private bool IsLunge => Style == StrikeStyle.Lunge;

        private float Variance => Projectile.ai[1];

        private int Duration => Projectile.ai[2] > 0f ? (int)Projectile.ai[2] : DefaultDuration(Style);

        // Куда смотрит копьё в этом тике. velocity хранит исходный прицел удара, а взмах
        // проворачивает древко вокруг него — поэтому направление считаем отдельно
        private Vector2 strikeDir = Vector2.UnitX;
        private Vector2 previousStrikeDir = Vector2.UnitX;

        private readonly Vector2[] tipTrail = new Vector2[TrailLength];
        private readonly Vector2[] centerTrail = new Vector2[TrailLength];
        private readonly float[] rotationTrail = new float[TrailLength];
        private int trailCount;

        private float extension;
        private bool peakBurstDone;
        private bool started;

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 18;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.scale = StyleScale[0];
            Projectile.ownerHitCheck = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.timeLeft = StyleDuration[0];
        }

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead)
            {
                Projectile.Kill();
                return false;
            }

            if (!started)
                Start();

            player.heldProj = Projectile.whoAmI;
            player.SetDummyItemTime(2);

            Vector2 aim = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float t = 1f - Projectile.timeLeft / (float)Duration;

            previousStrikeDir = strikeDir;
            strikeDir = aim.RotatedBy(SweepOffset(t, aim));
            if (!started)
                previousStrikeDir = strikeDir;

            // Держим удар в сторону прицела: и спрайт, и рука игрока
            Projectile.direction = Projectile.spriteDirection = aim.X >= 0f ? 1 : -1;
            Projectile.rotation = strikeDir.ToRotation() + MathHelper.PiOver2;
            player.ChangeDir(Projectile.direction);
            player.itemRotation = strikeDir.ToRotation();
            if (Projectile.spriteDirection == -1)
                player.itemRotation += MathHelper.Pi;

            float reach = StyleReach[(int)Style] * (1f + ReachVariance * Variance);
            extension = Extension(Style, t);

            // Рука идёт за ударом: копьё держит кисть, а не абстрактный центр игрока.
            // Формула поворота — ванильная (useStyle 13, «рапира»), поэтому работает
            // и на левом повороте, и при перевёрнутой гравитации
            Player.CompositeArmStretchAmount stretch = StretchFor(extension);
            float armRotation = player.itemRotation * player.gravDir
                - MathHelper.PiOver2 * player.direction;
            player.SetCompositeArmFront(true, stretch, armRotation);

            Projectile.Center = player.GetFrontHandPosition(stretch, armRotation)
                + strikeDir * extension * reach;

            // Копьё чуть вытягивается на выпаде — вместе с ним растёт и досягаемость
            Projectile.scale = StyleScale[(int)Style] * (0.94f + 0.06f * MathHelper.Max(extension, 0f));

            RecordTrail();
            Lighting.AddLight(Projectile.Center + strikeDir * (TipDistance * 0.5f),
                new Vector3(0.2f, 0.4f, 0.55f) * (0.5f + 0.5f * MathHelper.Max(extension, 0f)));

            if (!peakBurstDone && extension >= PeakFrom)
            {
                peakBurstDone = true;
                OnPeak();
            }

            EmitTipDust();
            started = true;
            return false;
        }

        // Первый тик удара: длительность и звук. Звук здесь, а не у владельца при
        // спавне, — так его слышат все, кто видит удар
        private void Start()
        {
            Projectile.timeLeft = Duration;
            Projectile.scale = StyleScale[(int)Style];

            if (Main.dedServ)
                return;

            Vector2 at = Main.player[Projectile.owner].Center;
            switch (Style)
            {
                case StrikeStyle.Jab:
                    SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.75f, Pitch = 0.15f }, at);
                    break;
                case StrikeStyle.Sweep:
                    SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.8f, Pitch = -0.1f }, at);
                    SoundEngine.PlaySound(SoundID.Item7 with { Volume = 0.5f, Pitch = 0.3f }, at);
                    break;
                case StrikeStyle.Lunge:
                    SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.95f, Pitch = -0.4f }, at);
                    break;
                case StrikeStyle.Flurry:
                    // В шквале звуки идут очередью — тише и с разбросом тона,
                    // иначе двенадцать одинаковых ударов сливаются в гудок
                    SoundEngine.PlaySound(SoundID.Item1 with
                    {
                        Volume = 0.45f,
                        Pitch = 0.2f + 0.3f * Variance,
                        MaxInstances = 4,
                    }, at);
                    break;
            }
        }

        // Взмах поворачивает древко от нижнего края дуги к верхнему. «Низ» зависит от
        // того, куда смотрит игрок: вправо — поворот по часовой стрелке, влево — против
        private static float SweepOffsetFor(float t, int facing)
        {
            float eased = MathHelper.SmoothStep(0f, 1f, t);
            return MathHelper.Lerp(SweepHalfArc, -SweepHalfArc, eased) * facing;
        }

        private float SweepOffset(float t, Vector2 aim)
        {
            if (Style != StrikeStyle.Sweep)
                return 0f;
            return SweepOffsetFor(t, aim.X >= 0f ? 1 : -1);
        }

        // Рука уходит вперёд вместе с древком: от подобранной на замахе до полностью
        // вытянутой на пике
        private static Player.CompositeArmStretchAmount StretchFor(float extension)
        {
            if (extension < 0.25f)
                return Player.CompositeArmStretchAmount.None;
            if (extension < 0.55f)
                return Player.CompositeArmStretchAmount.Quarter;
            if (extension < 0.8f)
                return Player.CompositeArmStretchAmount.ThreeQuarters;
            return Player.CompositeArmStretchAmount.Full;
        }

        private void RecordTrail()
        {
            for (int i = TrailLength - 1; i > 0; i--)
            {
                tipTrail[i] = tipTrail[i - 1];
                centerTrail[i] = centerTrail[i - 1];
                rotationTrail[i] = rotationTrail[i - 1];
            }

            tipTrail[0] = Projectile.Center + strikeDir * TipDistance;
            centerTrail[0] = Projectile.Center;
            rotationTrail[0] = Projectile.rotation;

            if (trailCount < TrailLength)
                trailCount++;
        }

        // Пик удара: брызги с острия, а у выпада — ещё плеск и толчок камеры
        private void OnPeak()
        {
            if (Main.dedServ)
                return;

            Vector2 tip = SpearTip + strikeDir * SpikeLength;
            int drops = Style switch
            {
                StrikeStyle.Lunge => 18,
                StrikeStyle.Flurry => 4,
                _ => 8,
            };
            for (int i = 0; i < drops; i++)
            {
                Vector2 spray = strikeDir.RotatedByRandom(0.5f) * Main.rand.NextFloat(2f, IsLunge ? 8f : 6f);
                Dust d = Dust.NewDustPerfect(tip, DustID.Water, spray);
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1f, 1.7f) * (IsLunge ? 1.25f : 1f);
            }

            if (!IsLunge)
                return;

            SoundEngine.PlaySound(SoundID.Item21 with { Volume = 0.6f, Pitch = 0.1f }, tip);
            if (Projectile.owner == Main.myPlayer)
                Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                    Projectile.Center, strikeDir, 5f, 6f, 10, 1000f, "RoyalSpearLunge"));
        }

        // Насколько копьё вынесено вперёд: 0 — у кисти, 1 — полный вылет, меньше нуля —
        // отведено назад на замахе
        private static float Extension(StrikeStyle style, float t)
        {
            switch (style)
            {
                case StrikeStyle.Sweep:
                {
                    // Выбрасывается вперёд и проходит дугу вытянутым, убирается в конце
                    const float Out = 0.2f;
                    const float Back = 0.8f;
                    if (t < Out)
                        return Snap(t / Out);
                    if (t > Back)
                        return 1f - (t - Back) / (1f - Back);
                    return 1f;
                }
                case StrikeStyle.Lunge:
                {
                    // Отвёл → выстрелил → задержал на вылете → убрал
                    const float Windup = 0.25f;
                    const float Strike = 0.45f;
                    const float Hold = 0.75f;
                    if (t < Windup)
                        return -LungeWindupPull * (t / Windup);
                    if (t < Strike)
                        return MathHelper.Lerp(-LungeWindupPull, 1f, Snap((t - Windup) / (Strike - Windup)));
                    if (t < Hold)
                        return 1f;
                    return 1f - (t - Hold) / (1f - Hold);
                }
                default:
                {
                    // Укол выстреливает и убирается чуть медленнее, чем бьёт
                    const float StabOut = 0.4f;
                    return t < StabOut
                        ? Snap(t / StabOut)
                        : 1f - (t - StabOut) / (1f - StabOut);
                }
            }
        }

        // Разгон к концу фазы: удар должен выстреливать, а не выезжать
        private static float Snap(float x) => x * x;

        private void EmitTipDust()
        {
            if (Main.dedServ)
                return;

            // Сыплем гуще на самом выпаде, а не всю анимацию поровну
            if (Main.rand.NextFloat() > 0.35f + 0.65f * extension)
                return;

            Vector2 tip = Projectile.Center + strikeDir * (TipDistance - TipOffset);
            Dust d = Dust.NewDustPerfect(tip + Main.rand.NextVector2Circular(4f, 4f), DustID.Water,
                strikeDir.RotatedByRandom(0.35f) * Main.rand.NextFloat(1f, 3.5f));
            d.noGravity = true;
            d.scale = Main.rand.NextFloat(0.9f, 1.4f) * (IsLunge ? 1.4f : 1f);
        }

        // Копьё бьёт всем древком, а не точкой в центре хитбокса. Взмах за тик
        // проворачивается на добрых полтора десятка градусов — проверяем ещё и
        // промежуточное положение, иначе мелкий враг проскакивал бы между кадрами
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Player player = Main.player[Projectile.owner];
            Vector2 shaftStart = player.MountedCenter;
            float shaftLength = Vector2.Distance(shaftStart, SpearTip) + SpikeLength;

            if (ShaftHits(targetHitbox, shaftStart, strikeDir, shaftLength))
                return true;

            if (Style != StrikeStyle.Sweep)
                return false;

            Vector2 midDir = Vector2.Normalize(strikeDir + previousStrikeDir);
            return ShaftHits(targetHitbox, shaftStart, midDir, shaftLength);
        }

        private bool ShaftHits(Rectangle targetHitbox, Vector2 start, Vector2 dir, float length)
        {
            float _ = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                start, start + dir * length, ShaftWidth * Projectile.scale, ref _);
        }

        // Рисуем сами: ванильная отрисовка ставит в кисть центр текстуры, из-за чего
        // половина копья уходит за спину. Опора — пятка древка
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 origin = new(tex.Width / 2f, tex.Height - GripOffset);
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            // Батч не переключаем: копьё держится через heldProj и рисуется внутри
            // прохода игрока, так что End/Begin посреди него сбрасывал бы состояние
            // и самому персонажу, и слоям вроде шкалы слэма — отсюда было мерцание.
            // Аддитив здесь даёт премультиплированная альфа: цвет с A = 0
            if (trailCount > 1)
            {
                DrawRibbon();
                DrawSpike();
                DrawBloom();
                DrawAfterimages(tex, origin);
            }

            Main.EntitySpriteDraw(tex, drawPos, null,
                Projectile.GetAlpha(lightColor), Projectile.rotation, origin,
                Projectile.scale, SpriteEffects.None, 0);
            return false;
        }

        // Водяная лента по следу острия. Тянется, пока копьё идёт вперёд, и гаснет
        // в последние тики удара — у убранного копья хвоста быть не должно
        // Лезвие — та же лента SoATrail, только прямая и сужающаяся в иглу: широкая у
        // острия, на конце в ноль. Струи шейдера бегут вдоль него, и вода «течёт» вперёд
        private void DrawSpike()
        {
            float length = SpikeLength;
            if (length < 2f)
                return;

            Vector2 tip = SpearTip;
            float halfWidth = StyleSpikeWidth[(int)Style] * Projectile.scale * (0.6f + 0.4f * SpikeGrowth);
            float strength = MathHelper.Clamp(Projectile.timeLeft / 3f, 0f, 1f)
                * (Style == StrikeStyle.Flurry ? 0.7f : 1f);

            ReadOnlySpan<Vector2> path = stackalloc Vector2[]
            {
                tip - strikeDir * 6f,
                tip + strikeDir * (length * 0.5f),
                tip + strikeDir * length,
            };
            SoATrail.Draw(path,
                progress => halfWidth * (float)Math.Pow(1f - progress, 1.3f),
                progress => SpikeColor * (strength * (1f - 0.5f * progress)),
                TrailStyle.Tide);
        }

        private void DrawRibbon()
        {
            float headWidth = StyleRibbonWidth[(int)Style] * Projectile.scale;
            float life = MathHelper.Clamp(Projectile.timeLeft / 3f, 0f, 1f);
            float strength = life * (Style == StrikeStyle.Flurry ? 0.65f : 1f);

            SoATrail.Draw(tipTrail.AsSpan(0, trailCount),
                progress => headWidth * (float)Math.Pow(1f - progress, 0.7f),
                progress => RibbonColor * (strength * (1f - progress)),
                TrailStyle.Tide);
        }

        // Затухающие копии древка: за несколько тиков удара глазу не за что зацепиться
        private void DrawAfterimages(Texture2D tex, Vector2 origin)
        {
            for (int i = Math.Min(trailCount, AfterimageCount) - 1; i >= 1; i--)
            {
                float fade = 1f - i / (float)AfterimageCount;
                Main.EntitySpriteDraw(tex, centerTrail[i] - Main.screenPosition, null,
                    TideGlow * (fade * fade * 0.5f), rotationTrail[i], origin,
                    Projectile.scale * (0.88f + 0.12f * fade), SpriteEffects.None, 0);
            }
        }

        // Свечение: узкий ореол по древку и точка на острие. Основную форму удара даёт
        // лента, поэтому пятна здесь небольшие — крупные размывали силуэт копья
        private void DrawBloom()
        {
            float power = 0.35f + 0.65f * MathHelper.Max(extension, 0f);
            if (Style == StrikeStyle.Flurry)
                power *= 0.6f; // в шквале копий много сразу, полная яркость засветила бы экран
            Vector2 tip = tipTrail[0];
            float scale = Projectile.scale;
            float angle = strikeDir.ToRotation();

            SoAVfx.DrawTintedQuad(Main.spriteBatch,
                Projectile.Center + strikeDir * (TipDistance * 0.5f),
                new Vector2(TipDistance + 12f, 18f * scale), angle,
                TideGlow * (0.18f * power));

            SoAVfx.DrawTintedGlow(Main.spriteBatch, tip, new Vector2(34f, 34f) * scale,
                TideGlow * (0.3f * power));
            SoAVfx.DrawTintedGlow(Main.spriteBatch, tip, new Vector2(14f, 14f) * scale,
                new Color(230, 248, 255, 0) * (0.7f * power));

            if (extension < PeakFrom)
                return;

            // Вспышка на пике: узкий блик вперёд по ходу удара, у выпада — длиннее
            float peak = (extension - PeakFrom) / (1f - PeakFrom);
            float flash = IsLunge ? 1.5f : 1f;
            SoAVfx.DrawTintedQuad(Main.spriteBatch, tip + strikeDir * (SpikeLength + 6f),
                new Vector2(70f * scale * flash, 22f * scale), angle,
                TideGlow * (0.45f * peak));
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            SoACombat.ApplySoakBonus(target, ref modifiers);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Удар промачивает цель — следующие уже идут с водяным бонусом
            SoACombat.Soak(target);

            if (Projectile.owner == Main.myPlayer)
            {
                Main.player[Projectile.owner].GetModPlayer<RoyalSpearPlayer>().AddSlamDamage(damageDone);
            }

            if (Main.dedServ)
                return;

            // Брызги летят по ходу удара, а не облаком вверх: удар должен читаться
            // как пробитие, а не как лопнувший пузырь
            int bubbles = Style switch
            {
                StrikeStyle.Lunge => 22,
                StrikeStyle.Flurry => 6,
                _ => 14,
            };
            for (int i = 0; i < bubbles; i++)
            {
                Vector2 spray = strikeDir.RotatedByRandom(0.8f) * Main.rand.NextFloat(1.5f, 6.5f);
                Dust bubble = Dust.NewDustDirect(target.position, target.width, target.height,
                    DustID.Water, spray.X, spray.Y);
                bubble.noGravity = true;
                bubble.scale = Main.rand.NextFloat(1f, 1.7f);
            }
        }
    }
}
