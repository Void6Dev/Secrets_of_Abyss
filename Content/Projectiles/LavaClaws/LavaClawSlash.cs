using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Удар Когтей Лавовой Тени: коготь рывком проходит дугу к курсору, попеременно правой
    // и левой рукой, и оставляет три параллельных раскалённых следа. Каждый четвёртый удар —
    // тяжёлый: дуга шире, дальше и ярче. Попадание ставит лавовую метку (LavaExplosionGlobalNPC).
    //   ai[0] — сторона взмаха (±1), ai[1] — 1 для тяжёлого, ai[2] — угол прицела
    public class LavaClawSlash : ModProjectile
    {
        public override string Texture => "SoA/Content/Items/Weapons/ClawsOfLavaShadow";

        // ---------- ДУГА ----------
        private const int SlashUpdates = 24;          // 12 тиков × 2 подшага: дуга без «лесенки»
        private const float ArcHalf = 1.0f;           // полураствор дуги, рад
        private const float ArcHalfHeavy = 1.3f;
        private const float Reach = 60f;              // от центра игрока до кончиков когтей
        private const float ReachHeavy = 78f;
        private const float HandOffset = 14f;         // где кисть: от центра к кончику
        private const float ActiveFrom = 0.12f;       // доля дуги, на которой коготь режет
        private const float ActiveTo = 0.8f;
        private const float HitLineWidth = 22f;

        // ---------- ВИД ----------
        private const int TrailLength = 14;
        private const float MarkSpacing = 9f;         // расстояние между тремя следами когтей
        private const float ClawScale = 1.45f;
        private const float ClawScaleHeavy = 1.75f;
        private static readonly Vector2 WristOrigin = new(6f, 22f); // запястье на спрайте; когти смотрят вправо-вверх
        private const float SpriteForwardAngle = -MathHelper.PiOver4;

        private static readonly Color HotWhite = new(255, 240, 190);
        private static readonly Color LavaOrange = new(255, 140, 40);
        private static readonly Color DeepRed = new(180, 30, 15);

        // ---------- МЕТКА ----------
        public const int MarkTicks = 30; // сколько после последнего удара метка ждёт взрыва

        private float Side => Projectile.ai[0] >= 0f ? 1f : -1f;
        private bool Heavy => Projectile.ai[1] == 1f;
        private float Aim => Projectile.ai[2];
        private ref float Age => ref Projectile.localAI[0];

        // След: (угол, радиус) последних подшагов, [0] — самый свежий. Точки считаем от
        // текущего центра игрока, поэтому след едет вместе с ним и не отрывается на беге
        private readonly Vector2[] _trail = new Vector2[TrailLength];
        private int _trailCount;

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.ownerHitCheck = true;       // сквозь стену не режет
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;   // один взмах — одно попадание по каждому
            Projectile.extraUpdates = 1;
            Projectile.timeLeft = SlashUpdates;
        }

        private float Progress => MathHelper.Clamp(Age / SlashUpdates, 0f, 1f);

        // Медленный старт, быстрая середина, мягкий дохлёст — взмах, а не поворот стрелки
        private static float EaseSlash(float t) => t * t * t * (t * (6f * t - 15f) + 10f);

        private float AngleAt(float t)
        {
            float arc = Heavy ? ArcHalfHeavy : ArcHalf;
            return Aim + Side * arc * (1f - 2f * EaseSlash(t));
        }

        // Рука выбрасывается вперёд в середине взмаха и подбирается к концу
        private float ReachAt(float t) => (Heavy ? ReachHeavy : Reach) * (0.82f + 0.28f * (float)Math.Sin(t * MathHelper.Pi));

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (!owner.active || owner.dead)
            {
                Projectile.Kill();
                return;
            }

            if (Age == 0f)
                OnSlashStart();
            Age++;

            float t = Progress;
            float angle = AngleAt(t);
            float reach = ReachAt(t);

            Projectile.Center = owner.MountedCenter;
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation = angle;
            Projectile.friendly = t >= ActiveFrom && t <= ActiveTo;

            // Игрок смотрит туда, куда бьёт; руки чередуются вместе со стороной взмаха
            owner.heldProj = Projectile.whoAmI;
            owner.ChangeDir(Math.Cos(Aim) >= 0.0 ? 1 : -1);
            float armRotation = angle - MathHelper.PiOver2;
            if (Side > 0f)
                owner.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRotation);
            else
                owner.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Full, armRotation);

            PushTrail(angle, reach);

            if (Main.dedServ)
                return;

            Vector2 tip = owner.MountedCenter + angle.ToRotationVector2() * reach;
            Lighting.AddLight(tip, LavaOrange.ToVector3() * (Heavy ? 0.9f : 0.6f));
            if (Projectile.friendly && Main.rand.NextBool(Heavy ? 1 : 2))
            {
                Vector2 sweep = (angle - Side * MathHelper.PiOver2).ToRotationVector2();
                SoAParticles.SpawnStreak(tip + Main.rand.NextVector2Circular(8f, 8f),
                    sweep * Main.rand.NextFloat(1.5f, 4f) + new Vector2(0f, -Main.rand.NextFloat(0.5f, 1.5f)),
                    Color.Lerp(LavaOrange, HotWhite, Main.rand.NextFloat(0.4f)), Main.rand.NextFloat(1.8f, 3f),
                    gravity: -0.03f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.2f);
            }
        }

        private void OnSlashStart()
        {
            if (Main.dedServ)
                return;
            SoundEngine.PlaySound((Heavy ? SoundID.Item74 : SoundID.Item71) with
            {
                Pitch = Heavy ? -0.35f : 0.15f + Side * 0.12f,
                PitchVariance = 0.1f,
                Volume = Heavy ? 0.7f : 0.55f,
                MaxInstances = 3,
            }, Projectile.Center);
        }

        private void PushTrail(float angle, float reach)
        {
            for (int i = Math.Min(_trailCount, TrailLength - 1); i > 0; i--)
                _trail[i] = _trail[i - 1];
            _trail[0] = new Vector2(angle, reach);
            _trailCount = Math.Min(_trailCount + 1, TrailLength);
        }

        // Режет всё, через что прошли когти: текущее положение, прошлое и середина между ними
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (!Projectile.friendly || _trailCount == 0)
                return false;

            Vector2 center = Main.player[Projectile.owner].MountedCenter;
            float previous = _trailCount > 1 ? _trail[1].X : _trail[0].X;
            float[] angles = { _trail[0].X, previous, (_trail[0].X + previous) * 0.5f };
            float collisionPoint = 0f;
            foreach (float angle in angles)
            {
                Vector2 tip = center + angle.ToRotationVector2() * _trail[0].Y;
                if (Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), center, tip,
                        HitLineWidth, ref collisionPoint))
                    return true;
            }
            return false;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
            => modifiers.HitDirectionOverride = Main.player[Projectile.owner].direction;

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            target.AddBuff(ModContent.BuffType<LavaExplosionDebuff>(), MarkTicks);
            target.GetGlobalNPC<LavaExplosionGlobalNPC>().AddMark(Projectile.owner, Projectile.damage);
            HitSparks(target);
        }

        // Искры по ходу взмаха, вспышка и шипение раскалённого металла
        private void HitSparks(NPC target)
        {
            if (Main.dedServ)
                return;

            Vector2 at = Vector2.Lerp(target.Center, Projectile.Center, 0.25f);
            Vector2 sweep = (Projectile.rotation - Side * MathHelper.PiOver2).ToRotationVector2();
            int count = Heavy ? 16 : 10;
            for (int i = 0; i < count; i++)
            {
                SoAParticles.SpawnStreak(at, sweep.RotatedByRandom(0.8f) * Main.rand.NextFloat(4f, 11f),
                    Color.Lerp(LavaOrange, HotWhite, Main.rand.NextFloat(0.6f)), Main.rand.NextFloat(2f, 3.2f),
                    gravity: 0.15f, life: Main.rand.Next(14, 26), lengthPerSpeed: 2.4f);
            }
            SoAParticles.SpawnGlow(at, Vector2.Zero, LavaOrange * 0.8f, 30f, Heavy ? 120f : 85f, 10);
            SoAParticles.AddLight(at, LavaOrange, Heavy ? 1.8f : 1.2f, 8);
            SoundEngine.PlaySound(SoundID.Item20 with { Pitch = 0.2f, PitchVariance = 0.2f, Volume = 0.45f, MaxInstances = 4 }, at);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (_trailCount == 0)
                return false;

            SpriteBatch sb = Main.spriteBatch;
            Player owner = Main.player[Projectile.owner];
            Vector2 center = owner.MountedCenter;
            float t = Progress;
            float lifeFade = t < 0.85f ? 1f : (1f - t) / 0.15f;
            float spacing = MarkSpacing * (Heavy ? 1.25f : 1f);

            SoAVfx.BeginAdditive(sb);

            // Три следа когтей: раскалённая голова, остывающий красный хвост
            for (int mark = -1; mark <= 1; mark++)
            {
                for (int i = 0; i < _trailCount - 1; i++)
                {
                    Vector2 a = TrailPoint(center, _trail[i], mark * spacing);
                    Vector2 b = TrailPoint(center, _trail[i + 1], mark * spacing);
                    float along = i / (float)(TrailLength - 1); // 0 — голова, 1 — хвост
                    Color color = TrailColor(along) * ((1f - along) * lifeFade);
                    float width = MathHelper.Lerp(Heavy ? 9f : 7f, 1.5f, along) * (mark == 0 ? 1.15f : 0.85f);
                    Vector2 segment = b - a;
                    SoAVfx.DrawTintedQuad(sb, (a + b) * 0.5f, new Vector2(segment.Length() + 3f, width),
                        segment.ToRotation(), color);
                }
            }

            // Жар вокруг когтя и его светящийся двойник под спрайтом
            Vector2 tip = TrailPoint(center, _trail[0], 0f);
            SoAVfx.DrawTintedGlow(sb, tip, new Vector2(Heavy ? 120f : 85f), LavaOrange * (0.55f * lifeFade));
            DrawClaw(sb, center, LavaOrange * (0.5f * lifeFade), 1.12f);

            SoAVfx.EndAdditive(sb);

            // Сам коготь — раскалённый, свет мира ему не нужен
            DrawClaw(sb, center, Color.White * lifeFade, 1f);
            return false;
        }

        private static Vector2 TrailPoint(Vector2 center, Vector2 sample, float radiusOffset)
            => center + sample.X.ToRotationVector2() * (sample.Y + radiusOffset);

        private static Color TrailColor(float along) => along < 0.35f
            ? Color.Lerp(HotWhite, LavaOrange, along / 0.35f)
            : Color.Lerp(LavaOrange, DeepRed, (along - 0.35f) / 0.65f);

        // Коготь в кисти: запястье на руке, кончики — по направлению взмаха
        private void DrawClaw(SpriteBatch sb, Vector2 center, Color color, float scaleMult)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            float angle = _trail[0].X;
            bool flipped = Main.player[Projectile.owner].direction == -1;

            // Отражённый спрайт смотрит вверх-влево — доворот считаем от его «вперёд»
            float forward = flipped ? MathHelper.Pi - SpriteForwardAngle : SpriteForwardAngle;
            float rotation = angle - forward;
            Vector2 origin = flipped ? new Vector2(tex.Width - WristOrigin.X, WristOrigin.Y) : WristOrigin;
            float scale = (Heavy ? ClawScaleHeavy : ClawScale) * scaleMult;
            Vector2 wrist = center + angle.ToRotationVector2() * HandOffset;

            Main.EntitySpriteDraw(tex, wrist - Main.screenPosition, null, color, rotation, origin, scale,
                flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }
    }
}
