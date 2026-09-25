using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Players;
using SoA.Common.Utils;

namespace SoA.Content.Projectiles
{
    // Выпад Королевского копья. ai[0] — шаг связки (0-2), ai[1] — разброс этого удара
    // (-1..1), брошенный один раз в RoyalSpear.Shoot и потому одинаковый у всех клиентов.
    public class RoyalSpearProjectile : ModProjectile
    {
        // Спрайт лежит рядом с кодом в папке оружия, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        private const float WetTargetDamageMultiplier = 1.25f;
        private const float TipOffset = 10f;

        // Связка: три быстрых укола, последний чуть длиннее и крупнее. Индекс — шаг
        private static readonly float[] StepReach = { 58f, 58f, 72f };
        private static readonly float[] StepScale = { 1.15f, 1.15f, 1.3f };

        private const float ReachVariance = 0.12f;  // насколько разброс тянет вылет

        // Связка идёт быстро, и одиночный спрайт читался бы как мигание. Держим короткую
        // историю положений: из неё рисуются росчерк по острию и затухающие копии древка
        private const int TrailLength = 7;
        private const float StreakWidth = 14f;
        private static readonly Color TideGlow = new(120, 210, 255, 0);

        // С какого вылета острие вспыхивает конусом и сыплет брызги
        private const float PeakFrom = 0.72f;

        // Спрайт 16x88, острие сверху. Точка опоры — не центр текстуры, а пятка древка:
        // при центре половина копья уходила игроку за спину и вперёд торчала только
        // половина длины. GripOffset — сколько рукояти остаётся позади кисти.
        // Публичные: этой же опорой рисует копьё в руке зарядка броска (RoyalSpearCharge)
        public const float SpriteLength = 108f;
        public const float GripOffset = 36f;
        private const float ShaftWidth = 20f;

        // Куда достаёт острие от центра снаряда
        private float TipDistance => (SpriteLength - GripOffset) * Projectile.scale;

        private int ComboStep => Math.Clamp((int)Projectile.ai[0], 0, StepReach.Length - 1);

        private bool IsFinisher => ComboStep == StepReach.Length - 1;

        private float Variance => Projectile.ai[1];

        private readonly Vector2[] tipTrail = new Vector2[TrailLength];
        private readonly Vector2[] centerTrail = new Vector2[TrailLength];
        private readonly float[] rotationTrail = new float[TrailLength];
        private int trailCount;

        private float extension;
        private bool peakBurstDone;

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 18;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.scale = StepScale[0];
            Projectile.ownerHitCheck = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            Projectile.scale = StepScale[ComboStep];
        }

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];
            // Укол живёт ровно свой отрезок связки, а не всю анимацию: следующий
            // стартует, когда этот уже убран
            int duration = Math.Max(1, player.itemAnimationMax / RoyalSpearPlayer.ComboLength);
            player.heldProj = Projectile.whoAmI;

            if (Projectile.timeLeft > duration)
                Projectile.timeLeft = duration;

            Projectile.velocity = Vector2.Normalize(Projectile.velocity);

            // Держим выпад в сторону прицела: и спрайт, и рука игрока
            Projectile.direction = Projectile.spriteDirection = Projectile.velocity.X >= 0f ? 1 : -1;
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            player.ChangeDir(Projectile.direction);
            player.SetDummyItemTime(2);
            player.itemRotation = Projectile.velocity.ToRotation();
            if (Projectile.spriteDirection == -1)
                player.itemRotation += MathHelper.Pi;

            // Общая длительность приходит из useAnimation, форму удара задаёт шаг связки
            float t = 1f - Projectile.timeLeft / (float)duration;
            float reach = StepReach[ComboStep] * (1f + ReachVariance * Variance);
            extension = Extension(t);

            // Рука идёт за выпадом: копьё держит кисть, а не абстрактный центр игрока.
            // Формула поворота — ванильная (useStyle 13, «рапира»), поэтому работает
            // и на левом повороте, и при перевёрнутой гравитации
            Player.CompositeArmStretchAmount stretch = StretchFor(extension);
            float armRotation = player.itemRotation * player.gravDir
                - MathHelper.PiOver2 * player.direction;
            player.SetCompositeArmFront(true, stretch, armRotation);

            Projectile.Center = player.GetFrontHandPosition(stretch, armRotation)
                + Projectile.velocity * extension * reach;

            // Копьё чуть вытягивается на выпаде — вместе с ним растёт и досягаемость
            Projectile.scale = StepScale[ComboStep] * (0.94f + 0.06f * extension);

            RecordTrail();
            Lighting.AddLight(Projectile.Center + Projectile.velocity * (TipDistance * 0.5f),
                new Vector3(0.2f, 0.4f, 0.55f) * (0.5f + 0.5f * extension));

            if (!peakBurstDone && extension >= PeakFrom)
            {
                peakBurstDone = true;
                OnPeak();
            }

            EmitTipDust();
            return false;
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

            tipTrail[0] = Projectile.Center + Projectile.velocity * TipDistance;
            centerTrail[0] = Projectile.Center;
            rotationTrail[0] = Projectile.rotation;

            if (trailCount < TrailLength)
                trailCount++;
        }

        // Пик выпада: брызги с острия и короткий толчок камеры на последнем уколе связки
        private void OnPeak()
        {
            if (Main.dedServ)
                return;

            Vector2 tip = tipTrail[0];
            int drops = IsFinisher ? 14 : 8;
            for (int i = 0; i < drops; i++)
            {
                Vector2 spray = Projectile.velocity.RotatedByRandom(0.5f)
                    * Main.rand.NextFloat(2f, 6f);
                Dust d = Dust.NewDustPerfect(tip, DustID.Water, spray);
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1f, 1.7f);
            }

            if (IsFinisher && Projectile.owner == Main.myPlayer)
                Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                    Projectile.Center, Projectile.velocity, 3.5f, 6f, 8, 1000f, "RoyalSpearStab"));
        }

        // Насколько копьё вынесено вперёд: 0 — у кисти, 1 — полный вылет.
        // Укол выстреливает и убирается чуть медленнее, чем бьёт
        private static float Extension(float t)
        {
            const float StabOut = 0.4f;

            return t < StabOut
                ? Snap(t / StabOut)
                : 1f - (t - StabOut) / (1f - StabOut);
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

            Vector2 tip = Projectile.Center + Projectile.velocity * (TipDistance - TipOffset);
            Dust d = Dust.NewDustPerfect(tip + Main.rand.NextVector2Circular(4f, 4f), DustID.Water,
                Projectile.velocity.RotatedByRandom(0.35f) * Main.rand.NextFloat(1f, 3.5f));
            d.noGravity = true;
            d.scale = Main.rand.NextFloat(0.9f, 1.4f) * (IsFinisher ? 1.4f : 1f);
        }

        // Копьё бьёт всем древком, а не точкой в центре хитбокса
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Player player = Main.player[Projectile.owner];
            Vector2 shaftStart = player.MountedCenter;
            Vector2 shaftEnd = Projectile.Center + Projectile.velocity * TipDistance;
            float _ = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                shaftStart, shaftEnd, ShaftWidth * Projectile.scale, ref _);
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
                DrawBloom();
                DrawTipStreak();
                DrawAfterimages(tex, origin);
            }

            Main.EntitySpriteDraw(tex, drawPos, null,
                Projectile.GetAlpha(lightColor), Projectile.rotation, origin,
                Projectile.scale, SpriteEffects.None, 0);
            return false;
        }

        // Росчерк по следу острия: свежие отрезки шире и ярче
        private void DrawTipStreak()
        {
            for (int i = 0; i + 1 < trailCount; i++)
            {
                Vector2 from = tipTrail[i];
                Vector2 step = tipTrail[i + 1] - from;
                float length = step.Length();
                if (length < 0.5f)
                    continue;

                float fade = 1f - i / (float)TrailLength;
                SoAVfx.DrawTintedQuad(Main.spriteBatch, from + step * 0.5f,
                    new Vector2(length + 6f, StreakWidth * fade), step.ToRotation(),
                    TideGlow * (fade * fade * 0.9f));
            }
        }

        // Затухающие копии древка: за восемь тиков выпада глазу не за что зацепиться
        private void DrawAfterimages(Texture2D tex, Vector2 origin)
        {
            for (int i = trailCount - 1; i >= 1; i--)
            {
                float fade = 1f - i / (float)TrailLength;
                Main.EntitySpriteDraw(tex, centerTrail[i] - Main.screenPosition, null,
                    TideGlow * (fade * fade * 0.5f), rotationTrail[i], origin,
                    Projectile.scale * (0.88f + 0.12f * fade), SpriteEffects.None, 0);
            }
        }

        // Свечение: вложенные пятна от широкого и тусклого к узкому и яркому, плюс
        // ореол по всему древку — горит копьё целиком, а не одна точка на острие
        private void DrawBloom()
        {
            float power = 0.35f + 0.65f * extension;
            Vector2 tip = tipTrail[0];
            float scale = Projectile.scale;

            SoAVfx.DrawTintedQuad(Main.spriteBatch,
                Projectile.Center + Projectile.velocity * (TipDistance * 0.5f),
                new Vector2(TipDistance + 24f, 30f * scale), Projectile.velocity.ToRotation(),
                TideGlow * (0.22f * power));

            SoAVfx.DrawTintedGlow(Main.spriteBatch, tip, new Vector2(88f, 88f) * scale,
                TideGlow * (0.16f * power));
            SoAVfx.DrawTintedGlow(Main.spriteBatch, tip, new Vector2(50f, 50f) * scale,
                TideGlow * (0.3f * power));
            SoAVfx.DrawTintedGlow(Main.spriteBatch, tip, new Vector2(26f, 26f) * scale,
                TideGlow * (0.6f * power));

            if (extension < PeakFrom)
                return;

            // Вспышка на пике: вытянутое вперёд пятно вместо прежнего шейдерного конуса
            float peak = (extension - PeakFrom) / (1f - PeakFrom);
            SoAVfx.DrawTintedQuad(Main.spriteBatch, tip + Projectile.velocity * 16f,
                new Vector2(120f * scale, 64f * scale), Projectile.velocity.ToRotation(),
                TideGlow * (0.4f * peak));
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (SoACombat.IsSoaked(target))
                modifiers.FinalDamage *= WetTargetDamageMultiplier;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Укол промачивает цель — следующие удары уже идут с водяным бонусом
            SoACombat.Soak(target);

            if (Projectile.owner == Main.myPlayer)
            {
                Main.player[Projectile.owner].GetModPlayer<RoyalSpearPlayer>().AddSlamDamage(damageDone);
            }

            if (Main.dedServ)
                return;

            // Брызги летят по ходу укола, а не облаком вверх: удар должен читаться
            // как пробитие, а не как лопнувший пузырь
            int bubbles = IsFinisher ? 22 : 14;
            for (int i = 0; i < bubbles; i++)
            {
                Vector2 spray = Projectile.velocity.RotatedByRandom(0.8f)
                    * Main.rand.NextFloat(1.5f, 6.5f);
                Dust bubble = Dust.NewDustDirect(target.position, target.width, target.height,
                    DustID.Water, spray.X, spray.Y);
                bubble.noGravity = true;
                bubble.scale = Main.rand.NextFloat(1f, 1.7f);
            }
        }
    }
}
