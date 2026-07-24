using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.Projectiles
{
    // Пузырь Короля-краба. Режимы через ai[0]:
    //   0 — медленный дрейф со всплытием (лопается об тайлы)
    //   1 — слабое самонаведение первые 60 тиков (фаза 2)
    //   2 — «прилив»: летит по прямой сквозь тайлы
    public class KingCrabBubble : ModProjectile
    {
        private const int HomingTicks = 60;
        private const int PopTicks = 14; // длительность анимации лопания (шейдер PopPass)

        private float Mode => Projectile.ai[0];
        private ref float HomingTimer => ref Projectile.ai[1];

        // Таймер лопания — локальная косметика, по сети не шлём:
        // условия старта (тайл/конец жизни) детерминированы на всех клиентах
        private ref float PopTimer => ref Projectile.localAI[0];
        private bool Popping => PopTimer > 0f;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailingMode[Type] = 2;
            ProjectileID.Sets.TrailCacheLength[Type] = 12;
        }

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 18;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.tileCollide = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 300;
            Projectile.alpha = 60;
        }

        public override void AI()
        {
            if (Popping)
            {
                PopTimer++;
                Projectile.velocity *= 0.85f;
                if (PopTimer > PopTicks)
                    Projectile.Kill();
                return;
            }

            // Жизнь кончается — лопаемся заранее, чтобы успеть проиграть анимацию
            if (Projectile.timeLeft <= PopTicks)
            {
                StartPop();
                return;
            }

            if (Mode == 2f)
            {
                Projectile.tileCollide = false;
            }
            else if (Mode == 1f && HomingTimer < HomingTicks)
            {
                HomingTimer++;
                Player target = Main.player[Player.FindClosest(Projectile.position, Projectile.width, Projectile.height)];
                if (target.active && !target.dead)
                {
                    Vector2 desired = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX)
                        * Projectile.velocity.Length();
                    Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.03f);
                }
            }
            else if (Mode == 0f)
            {
                Projectile.velocity.Y -= 0.015f; // пузырь медленно всплывает
                Projectile.velocity.X += (float)Math.Sin(Projectile.timeLeft * 0.08f) * 0.02f;
            }

            Projectile.rotation += 0.04f * Math.Sign(Projectile.velocity.X == 0f ? 1f : Projectile.velocity.X);
            Lighting.AddLight(Projectile.Center, 0.1f, 0.25f, 0.4f);

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(12))
            {
                Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                d.noGravity = true;
                d.velocity *= 0.3f;
                d.scale = 0.9f;
            }
        }

        // Столкновение с тайлом = «точка окончания»: не умираем сразу, а лопаемся
        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            StartPop();
            return false;
        }

        // Вход в лопание: гасим урон, шейдер PopPass дорисует разлёт оболочки
        private void StartPop()
        {
            if (Popping)
                return;
            PopTimer = 1f;
            Projectile.hostile = false;
            Projectile.velocity *= 0.2f;

            SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.4f, Pitch = 0.8f }, Projectile.Center);
            Lighting.AddLight(Projectile.Center, 0.2f, 0.4f, 0.6f);
            if (Main.netMode != NetmodeID.Server)
            {
                // Немного брызг — основной визуал разлёта делает шейдер
                for (int i = 0; i < 6; i++)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Water, Main.rand.NextVector2Circular(3f, 3f));
                    d.noGravity = true;
                    d.scale = Main.rand.NextFloat(1f, 1.5f);
                }
            }
        }

        public override Color? GetAlpha(Color lightColor)
        {
            return new Color(180, 220, 255, 120);
        }

        // Водяной трейл, затем процедурный пузырь (SoA:CrabBubble) или разлёт (SoA:BubblePop)
        public override bool PreDraw(ref Color lightColor)
        {
            float seed = Projectile.whoAmI * 0.613f; // пер-снарядная фаза колыхания/перелива

            if (Popping)
            {
                SoAVfx.BeginAdditive(Main.spriteBatch);
                MiscShaderData pop = GameShaders.Misc["SoA:BubblePop"];
                pop.UseOpacity(1f);
                pop.Shader.Parameters["uProgress"]?.SetValue(PopTimer / PopTicks);
                pop.Shader.Parameters["uSeed"]?.SetValue(seed);
                pop.Apply();
                Texture2D blob = SoAVfx.Blob;
                float popSize = Projectile.width * 4f;
                Main.EntitySpriteDraw(blob, Projectile.Center - Main.screenPosition, null, Color.White, 0f,
                    blob.Size() / 2f, new Vector2(popSize / blob.Width, popSize / blob.Height), SpriteEffects.None, 0);
                SoAVfx.EndAdditive(Main.spriteBatch);
                return false;
            }

            SoAVfx.BeginAdditive(Main.spriteBatch);

            int len = Projectile.oldPos.Length;
            for (int i = len - 1; i >= 0; i--)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;
                float p = 1f - i / (float)len; // 1 у головы, 0 у хвоста
                Vector2 pos = Projectile.oldPos[i] + Projectile.Size / 2f;
                Color c = new Color(120, 200, 255) * (p * 0.35f);
                c.A = 0;
                SoAVfx.DrawTintedGlow(Main.spriteBatch, pos, new Vector2(Projectile.width * 1.4f * p + 6f), c);
            }

            SoAVfx.EndAdditive(Main.spriteBatch);

            // Сам пузырь: отдельный Immediate-батч, чтобы Apply не задел трейл
            SoAVfx.BeginAdditive(Main.spriteBatch);
            MiscShaderData bubble = GameShaders.Misc["SoA:CrabBubble"];
            bubble.UseOpacity(1f);
            bubble.Shader.Parameters["uSeed"]?.SetValue(seed);
            bubble.Apply();
            Texture2D tex = SoAVfx.Blob;
            float size = Projectile.width * 2.6f;
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition, null, Color.White, 0f,
                tex.Size() / 2f, new Vector2(size / tex.Width, size / tex.Height), SpriteEffects.None, 0);
            SoAVfx.EndAdditive(Main.spriteBatch);

            return false;
        }
    }
}
