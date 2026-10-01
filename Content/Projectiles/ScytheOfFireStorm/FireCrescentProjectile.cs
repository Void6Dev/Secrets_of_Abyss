using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Огненный серп Косы огненной бури — ответ на короткий клик без заряда.
    // Короткий бросок: быстро тормозит и гаснет, прошивает врагов и поджигает.
    // Форма — лента SoATrail в стиле расплава, выгнутая дугой навстречу полёту
    public class FireCrescentProjectile : ModProjectile
    {
        private const int Lifetime = 26;
        private const int FadeTicks = 10;
        private const float Drag = 0.92f;
        private const float ArcRadius = 34f;
        private const float ArcHalfAngle = 1.15f;
        private const float ArcThickness = 11f;
        private const float HitRadius = 40f;
        private const int ArcPoints = 9;
        private const int BurnTicks = 90;

        private float Fade => Math.Min(Projectile.timeLeft / (float)FadeTicks, 1f);

        public override string Texture => "SoA/Content/Items/Weapons/ScytheOfFireStorm"; // сам не рисуется, см. PreDraw

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Lifetime;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            Projectile.velocity *= Drag;
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, new Vector3(1.1f, 0.5f, 0.1f) * Fade);

            if (!Main.dedServ && Main.rand.NextBool(2))
            {
                Vector2 edge = ArcPoint(Main.rand.NextFloat(-ArcHalfAngle, ArcHalfAngle));
                Dust d = Dust.NewDustPerfect(edge, DustID.Torch, -Projectile.velocity * 0.2f);
                d.noGravity = true;
                d.scale = 1.2f * Fade;
            }
        }

        // Точка дуги: angle 0 — вершина серпа по ходу полёта
        private Vector2 ArcPoint(float angle)
        {
            Vector2 forward = Projectile.rotation.ToRotationVector2();
            return Projectile.Center - forward * (ArcRadius * 0.5f) + forward.RotatedBy(angle) * ArcRadius;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 nearest = Vector2.Clamp(Projectile.Center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
            return Vector2.DistanceSquared(nearest, Projectile.Center) <= HitRadius * HitRadius;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
            => target.AddBuff(ModContent.BuffType<HellFireDebuff>(), BurnTicks);

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            for (int i = 0; i < 10; i++)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Torch, Main.rand.NextVector2Circular(4f, 4f));
                d.noGravity = true;
                d.scale = 1.4f;
            }
            return true;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Span<Vector2> points = stackalloc Vector2[ArcPoints];
            for (int i = 0; i < ArcPoints; i++)
                points[i] = ArcPoint(MathHelper.Lerp(-ArcHalfAngle, ArcHalfAngle, i / (float)(ArcPoints - 1)));

            float fade = Fade;
            // Толще и горячее в вершине, к рогам серп истончается и краснеет
            SoATrail.Draw(points,
                progress => ArcThickness * (float)Math.Sin(MathHelper.Pi * progress) * fade,
                progress => TrailStyle.MagmaBody(Math.Abs(progress - 0.5f) * 1.4f, 0.6f) * fade,
                TrailStyle.Magma);

            SoAVfx.DrawTintedGlow(Main.spriteBatch, ArcPoint(0f), new Vector2(60f), new Color(255, 140, 40, 0) * (0.5f * fade));
            return false;
        }
    }
}
