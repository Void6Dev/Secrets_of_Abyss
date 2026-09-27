using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;

namespace SoA.Content.Projectiles
{
    // Огненный взрыв адского тира как обычный снаряд игрока (Когти, Инферно-сюрикен).
    // Урон через StrikeNPC шёл на каждой машине и мимо защиты с критами; снаряд владельца
    // бьёт по обычным правилам и синхронизируется сам.
    //   damage — урон взрыва, ai[0] — радиус, ai[1] — сила эффектов 0..1 (если наследник не считает её сам)
    public abstract class FireBurstProjectile : ModProjectile
    {
        public override string Texture => "SoA/Assets/Textures/Vfx/SoftGlow";

        private const int Lifetime = 26;
        private const int DamageTicks = 3;            // бьёт в первые тики, дальше только догорает
        private const int RingSegments = 40;
        private const float ShockwaveThreshold = 0.6f;

        protected static readonly Color HotWhite = new(255, 240, 190);
        protected static readonly Color LavaOrange = new(255, 140, 40);
        protected static readonly Color DeepRed = new(170, 30, 12);
        private static readonly Color SmokeColor = new(70, 48, 40);

        protected abstract DamageClass BurstDamageClass { get; }
        protected virtual float DefaultRadius => 90f;

        protected float Radius => Projectile.ai[0] > 0f ? Projectile.ai[0] : DefaultRadius;
        protected virtual float Intensity => MathHelper.Clamp(Projectile.ai[1], 0f, 1f);
        private ref float Age => ref Projectile.localAI[0];

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = BurstDamageClass;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1; // каждого — один раз
            Projectile.timeLeft = Lifetime;
        }

        public override void AI()
        {
            if (Age == 0f)
                OnDetonate();
            Age++;
            Projectile.velocity = Vector2.Zero;
            Projectile.friendly = Age <= DamageTicks;

            if (!Main.dedServ)
                Lighting.AddLight(Projectile.Center, LavaOrange.ToVector3() * (1.6f * (1f - Age / Lifetime)));
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 closest = Vector2.Clamp(Projectile.Center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
            return Vector2.DistanceSquared(closest, Projectile.Center) <= Radius * Radius;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
            => modifiers.HitDirectionOverride = target.Center.X < Projectile.Center.X ? -1 : 1;

        // Настройки под размер взрыва. По умолчанию — как у лавовой метки Когтей
        protected virtual float ParticleSpread => 1f;                    // множитель разлёта и дыма
        protected virtual int SparkCount => 26 + (int)(20 * Intensity);
        protected virtual float CameraPunch => 3f + 5f * Intensity;       // 0 — без тряски

        private void OnDetonate()
        {
            if (Main.dedServ)
                return;

            Vector2 at = Projectile.Center;
            float intensity = Intensity;
            float power = 0.6f + 0.6f * intensity;
            float spread = ParticleSpread;

            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.3f, Volume = 0.8f }, at);
            SoundEngine.PlaySound(SoundID.Item74 with { Pitch = -0.5f, Volume = 0.6f }, at);

            // Радиальный выброс раскалённых брызг
            for (int i = 0; i < SparkCount; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                SoAParticles.SpawnStreak(at + dir * 10f, dir * Main.rand.NextFloat(5f, 14f) * power * spread,
                    Color.Lerp(LavaOrange, HotWhite, Main.rand.NextFloat(0.5f)), Main.rand.NextFloat(2.5f, 4.5f),
                    gravity: 0.18f, life: Main.rand.Next(20, 36), lengthPerSpeed: 2.6f);
            }

            // Тлеющие угольки, которые падают и отскакивают
            for (int i = 0; i < 10 + (int)(8 * intensity); i++)
            {
                SoAParticles.SpawnDebris(at, Main.rand.NextVector2Circular(6f, 6f) * spread + new Vector2(0f, -3f),
                    Color.Lerp(DeepRed, LavaOrange, Main.rand.NextFloat()), Main.rand.NextFloat(2f, 4f), Main.rand.Next(40, 70));
            }

            // Дым поднимается над местом взрыва
            for (int i = 0; i < 8; i++)
            {
                SoAParticles.SpawnSmoke(at + Main.rand.NextVector2Circular(Radius * 0.3f, Radius * 0.3f),
                    new Vector2(Main.rand.NextFloatDirection() * 1.2f, -Main.rand.NextFloat(0.6f, 1.8f)),
                    SmokeColor, 40f * spread, (110f + 60f * intensity) * spread, 0.5f, Main.rand.Next(50, 80));
            }

            SoAParticles.SpawnGlow(at, Vector2.Zero, HotWhite, Radius * 0.6f, Radius * 1.8f, 12);
            SoAParticles.AddLight(at, LavaOrange, 2.2f + 1.2f * intensity, 20);

            float punch = CameraPunch;
            if (punch > 0f && Main.LocalPlayer.Distance(at) < 1000f)
            {
                Main.instance.CameraModifiers.Add(new PunchCameraModifier(at, Main.rand.NextVector2Unit(),
                    punch, 8f, 16, 1000f, "SoA:" + Name));
            }
            if (intensity > ShockwaveThreshold)
                RoarShockwaveFx.Trigger(at, 26f, 0.35f + 0.35f * intensity);
        }

        // Ударная волна кольцом из раскалённых штрихов + гаснущее ядро
        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            float t = Age / Lifetime;
            float grow = 1f - (1f - t) * (1f - t) * (1f - t);
            float ringRadius = Radius * grow;
            float fade = 1f - t;

            SoAVfx.BeginAdditive(sb);

            SoAVfx.DrawTintedGlow(sb, Projectile.Center, new Vector2(Radius * 1.4f * (1f - 0.5f * t)),
                Color.Lerp(HotWhite, DeepRed, t) * (0.8f * fade * fade));

            float segmentLength = MathHelper.TwoPi * ringRadius / RingSegments + 4f;
            float thickness = MathHelper.Lerp(16f, 2f, t) * MathHelper.Clamp(ParticleSpread, 0.6f, 1f);
            Color ring = Color.Lerp(LavaOrange, DeepRed, t) * fade;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = MathHelper.TwoPi * i / RingSegments;
                Vector2 pos = Projectile.Center + angle.ToRotationVector2() * ringRadius;
                SoAVfx.DrawTintedQuad(sb, pos, new Vector2(segmentLength, thickness), angle + MathHelper.PiOver2, ring);
            }

            SoAVfx.EndAdditive(sb);
            return false;
        }
    }
}
