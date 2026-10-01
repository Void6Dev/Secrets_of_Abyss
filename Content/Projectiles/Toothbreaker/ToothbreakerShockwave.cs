using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Content.Buffs;
using SoA.Content.Items.Weapons;

namespace SoA.Content.Projectiles
{
    // Удар полностью заряженной Зубодробилки об землю: короткий урон по приплюснутой
    // области с оглушением, затем столб света, кольцо пены по грунту и волны в стороны.
    // Отдельный снаряд, а не часть ToothbreakerClub: тот держится через heldProj и рисуется
    // внутри игрока без смены батча, а кольцу нужен шейдер и своя перспектива
    public class ToothbreakerShockwave : ModProjectile
    {
        public const float DamageMult = 0.75f;

        private const float RadiusX = 96f;
        private const float RadiusY = 56f;
        private const int DamageTicks = 3;
        private const int VisualTicks = 30;

        private const float PillarHeight = 220f;
        private const float PillarWidth = 34f;
        private const float GroundFlashWidth = 300f;
        private const float GroundFlashHeight = 60f;
        private const float RippleTravel = 150f;
        private const float RingSize = 300f;
        private const float RingFlatten = 0.28f; // круг, положенный на землю, виден сбоку сплюснутым

        private static readonly Color TideGlow = new(120, 205, 255, 0);
        private static readonly Color CoreGlow = new(225, 245, 255, 0);

        private ref float Age => ref Projectile.localAI[0];
        private float Progress => Age / VisualTicks;

        public override string Texture => Toothbreaker.TexturePath; // сама не рисуется, см. PreDraw

        public override void SetDefaults()
        {
            Projectile.width = (int)(RadiusX * 2f);
            Projectile.height = (int)(RadiusY * 2f);
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.timeLeft = VisualTicks;
        }

        public override void AI()
        {
            if (Age == 0f && !Main.dedServ)
                SpawnImpactFx();
            Age++;
            Lighting.AddLight(Projectile.Center, new Vector3(0.3f, 0.8f, 1.1f) * (1f - Progress));
        }

        public override bool? CanDamage() => Age <= DamageTicks ? null : false;

        // Эллипс шире, чем выше: удар расходится по земле, а не шаром
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 center = Projectile.Center;
            Vector2 nearest = Vector2.Clamp(center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
            Vector2 d = (nearest - center) / new Vector2(RadiusX, RadiusY);
            return d.LengthSquared() <= 1f;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
            => modifiers.HitDirectionOverride = target.Center.X < Projectile.Center.X ? -1 : 1;

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => TideStunDebuff.Apply(target);

        // Звук удара и пыль уже дал сам молот (ClubProjectile); здесь — только приливная часть
        private void SpawnImpactFx()
        {
            Vector2 at = Projectile.Center;

            SoundEngine.PlaySound(SoundID.Splash with { Volume = 0.8f, Pitch = -0.2f }, at);
            SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.45f, Pitch = -0.5f }, at);
            // Все клиенты: соседние игроки тоже чувствуют удар, дальние — нет (затухание по расстоянию)
            ScreenShake.Punch(
                at, Vector2.UnitY, 5f, 7f, 20, 1200f, "ToothbreakerShockwave");

            SoAParticles.AddLight(at, TideGlow, 3f, 24);
            SoAParticles.SpawnGlow(at, Vector2.Zero, CoreGlow, 40f, 180f, 14);

            // Искры столбом вверх
            for (int i = 0; i < 14; i++)
            {
                Vector2 velocity = (-MathHelper.PiOver2 + Main.rand.NextFloat(-0.5f, 0.5f)).ToRotationVector2()
                    * Main.rand.NextFloat(6f, 13f);
                SoAParticles.SpawnStreak(at, velocity, CoreGlow, 2.5f, 0.05f, 26);
            }

            // Брызги веером — падают обратно
            for (int i = 0; i < 22; i++)
            {
                Vector2 velocity = (-MathHelper.PiOver2 + Main.rand.NextFloat(-1.3f, 1.3f)).ToRotationVector2()
                    * Main.rand.NextFloat(4f, 9f);
                SoAParticles.SpawnStreak(at, velocity, TideGlow, 2f, 0.3f, 32);
            }

            for (int i = 0; i < 20; i++)
            {
                Dust d = Dust.NewDustPerfect(at, DustID.Water,
                    new Vector2(Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(0.4f, 1f)) * Main.rand.NextFloat(3f, 8f));
                d.scale = Main.rand.NextFloat(1.1f, 1.7f);
            }

            // Комья грунта
            for (int i = 0; i < 8; i++)
            {
                Vector2 velocity = (-MathHelper.PiOver2 + Main.rand.NextFloat(-1.1f, 1.1f)).ToRotationVector2()
                    * Main.rand.NextFloat(3f, 7f);
                SoAParticles.SpawnDebris(at, velocity, SoAVfx.TideSand, Main.rand.NextFloat(3f, 5f));
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float p = Progress;
            float fade = 1f - p;
            float easeOut = 1f - (1f - p) * (1f - p);
            Vector2 at = Projectile.Center;

            DrawGroundRing(at, easeOut, fade);

            // Дальше — цвет с A = 0 в обычном батче, то есть аддитив без смены режима.
            // Столб света: мгновенно вырастает вверх, затем истончается
            float height = PillarHeight * Math.Min(p * 4f, 1f);
            float width = PillarWidth * (float)Math.Pow(fade, 1.5);
            Vector2 pillarCenter = at - new Vector2(0f, height / 2f);
            SoAVfx.DrawTintedQuad(Main.spriteBatch, pillarCenter, new Vector2(height, width), MathHelper.PiOver2, TideGlow * fade);
            SoAVfx.DrawTintedQuad(Main.spriteBatch, pillarCenter, new Vector2(height * 0.9f, width * 0.4f),
                MathHelper.PiOver2, CoreGlow * fade);

            // Вспышка по грунту и две волны, бегущие в стороны
            SoAVfx.DrawTintedGlow(Main.spriteBatch, at,
                new Vector2(GroundFlashWidth * easeOut, GroundFlashHeight * fade), TideGlow * fade);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 ripple = at + new Vector2(side * (30f + RippleTravel * easeOut), -2f);
                SoAVfx.DrawTintedQuad(Main.spriteBatch, ripple, new Vector2(10f + 70f * fade, 2f + 10f * fade), 0f,
                    TideGlow * (fade * 0.8f));
            }
            return false;
        }

        // Кольцо пены того же шейдера, что у ударной волны Короля-краба, положенное на землю
        private static void DrawGroundRing(Vector2 at, float progress, float fade)
        {
            var sb = Main.spriteBatch;
            SoAVfx.BeginLocalTransform(sb, SoAVfx.Squash(at, new Vector2(1f, RingFlatten)));
            SoAVfx.BeginAdditive(sb);
            SoAVfx.DrawRing(sb, at, RingSize, progress, fade * 0.9f);
            SoAVfx.EndAdditive(sb);
            SoAVfx.EndLocalTransform(sb);
        }
    }
}
