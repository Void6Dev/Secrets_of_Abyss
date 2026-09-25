using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Utils;

namespace SoA.Content.Projectiles
{
    // Приливный удар: взрыв воды в точке, где игрок приземлился после полёта на
    // верёвке за брошенным трезубцем. Бьёт по кругу один раз за цель и расходится
    // двумя валами по земле — теми же RoyalTideWave, что срывает королевский выпад.
    // Формы своей не имеет: фронт рисует уже готовый шейдер SoA:CrabRing.
    public class RoyalTideBurst : ModProjectile
    {
        private const int LifeTicks = 42;
        private const float Radius = 200f;
        private const float WaveSpeed = 9f;
        private const float WaveDamageMultiplier = 0.35f;
        private const float SoakedDamageMultiplier = 1.25f;

        public override string Texture => "SoA/Assets/Textures/BeamDistortion";

        private float LifeProgress => 1f - Projectile.timeLeft / (float)LifeTicks;

        public override void SetDefaults()
        {
            Projectile.width = (int)(Radius * 2f);
            Projectile.height = (int)(Radius * 2f);
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = LifeTicks;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.scale = 1f;
        }

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            if (Main.myPlayer == Projectile.owner)
            {
                // Два вала расходятся по земле в обе стороны
                for (int side = -1; side <= 1; side += 2)
                {
                    Projectile.NewProjectile(Projectile.GetSource_FromThis(),
                        Projectile.Center + new Vector2(side * 24f, -8f),
                        new Vector2(side * WaveSpeed, 0f), ModContent.ProjectileType<RoyalTideWave>(),
                        (int)(Projectile.damage * WaveDamageMultiplier), Projectile.knockBack, Projectile.owner);
                }
            }
        }

        public override void AI()
        {
            Lighting.AddLight(Projectile.Center, 0.5f, 0.8f, 1.1f);

            if (Main.netMode == NetmodeID.Server)
                return;

            // Стена брызг по фронту расширения
            float front = Radius * LifeProgress;
            for (int i = 0; i < 8; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                Dust splash = Dust.NewDustPerfect(Projectile.Center + dir * front, DustID.Water,
                    dir * Main.rand.NextFloat(3f, 9f) - Vector2.UnitY * 2f);
                splash.noGravity = true;
                splash.scale = Main.rand.NextFloat(1.3f, 2.2f);
            }
            for (int i = 0; i < 3; i++)
            {
                Dust foam = Dust.NewDustPerfect(
                    Projectile.Center + Main.rand.NextVector2Circular(Radius * 0.5f, Radius * 0.35f),
                    DustID.BreatheBubble, -Vector2.UnitY * Main.rand.NextFloat(2f, 6f));
                foam.noGravity = true;
                foam.scale = Main.rand.NextFloat(1f, 1.6f);
            }
        }

        // Взрыв круглый, а не квадратный на 400 пикселей
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float reach = Radius * MathHelper.Clamp(LifeProgress * 1.6f, 0.2f, 1f);
            Vector2 closest = Vector2.Clamp(Projectile.Center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
            return Vector2.Distance(closest, Projectile.Center) <= reach;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (SoACombat.IsSoaked(target))
                modifiers.FinalDamage *= SoakedDamageMultiplier;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            SoACombat.Soak(target);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SoAVfx.BeginAdditive(Main.spriteBatch);
            SoAVfx.DrawRing(Main.spriteBatch, Projectile.Center, Radius * 2.2f, LifeProgress,
                1f - LifeProgress * 0.7f);
            SoAVfx.DrawGlow(Main.spriteBatch, Projectile.Center, Radius * 1.4f,
                (1f - LifeProgress) * 0.8f);
            SoAVfx.EndAdditive(Main.spriteBatch);
            return false;
        }
    }
}
