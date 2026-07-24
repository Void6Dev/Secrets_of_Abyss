using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.Projectiles
{
    // Ударная волна Короля-краба: бежит по земле, гаснет об стены.
    // Тело — светящийся песчано-пенный гребень (аддитив) + разлёт пыли.
    public class KingCrabShockwave : ModProjectile
    {
        private const int Lifetime = 90;
        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 38;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.tileCollide = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = Lifetime;
        }

        public override void AI()
        {
            Projectile.velocity.Y += 0.5f; // прижимаем волну к земле
            Lighting.AddLight(Projectile.Center, 0.45f, 0.3f, 0.12f); // тёплое песочное свечение

            if (Main.netMode != NetmodeID.Server)
            {
                for (int i = 0; i < 2; i++)
                {
                    Dust sand = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Sand);
                    sand.velocity = new Vector2(Projectile.velocity.X * 0.2f, -Main.rand.NextFloat(2f, 5f));
                    sand.scale = Main.rand.NextFloat(1.2f, 1.8f);
                }
                if (Main.rand.NextBool(2))
                {
                    Dust splash = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                    splash.velocity = new Vector2(Main.rand.NextFloatDirection(), -Main.rand.NextFloat(3f, 6f));
                    splash.scale = 1.4f;
                    splash.noGravity = true;
                }
            }
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Врезалась в стену — гаснет; коснулась пола — продолжает катиться
            if (Projectile.velocity.X != oldVelocity.X)
                return true;
            Projectile.velocity.Y = 0f;
            return false;
        }

        // Светящийся гребень: тёплое песчаное ядро + холодная водяная пена сверху, с пульсом.
        public override bool PreDraw(ref Color lightColor)
        {
            float life = Projectile.timeLeft / (float)Lifetime;      // 1 → 0
            float fade = MathHelper.Clamp(life * 1.5f, 0f, 1f);       // держим яркость, гасим в конце
            float age = Lifetime - Projectile.timeLeft;
            float grow = MathHelper.Clamp(age / 8f, 0.25f, 1f);       // короткое проявление на старте
            float pulse = 0.72f + 0.28f * (float)Math.Sin(age * 0.4f);
            Vector2 basePos = Projectile.Center;

            SoAVfx.BeginAdditive(Main.spriteBatch);

            Color sand = new Color(255, 208, 120) * (fade * 0.9f * pulse);
            sand.A = 0;
            SoAVfx.DrawTintedGlow(Main.spriteBatch, basePos - new Vector2(0f, 6f),
                new Vector2(48f * grow, 82f * grow), sand);

            Color foam = new Color(140, 220, 255) * (fade * 0.7f * pulse);
            foam.A = 0;
            SoAVfx.DrawTintedGlow(Main.spriteBatch, basePos - new Vector2(0f, 22f),
                new Vector2(34f * grow, 56f * grow), foam);

            SoAVfx.EndAdditive(Main.spriteBatch);
            return false;
        }
    }
}
