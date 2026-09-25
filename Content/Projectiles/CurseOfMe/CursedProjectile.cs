using Terraria;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria.Audio;
using Terraria.Graphics.Shaders;
using Terraria.Graphics.CameraModifiers;

namespace SoA.Content.Projectiles
{
    public class CursedProjectile : ModProjectile
    {
        // Спрайт лежит рядом с кодом в папке владельца, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/CurseOfMe/CursedProjectile";

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailingMode[Type] = 2;
            ProjectileID.Sets.TrailCacheLength[Type] = 22;
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.aiStyle = 0;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.friendly = true;
            Projectile.penetrate = 1;
            Projectile.timeLeft = 300;
            Projectile.light = 0.8f;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
        }

        public override void AI()
        {
            NPC target = FindClosestNPC(600f);

            if (target != null)
            {
                Vector2 direction = target.Center - Projectile.Center;
                direction.Normalize();

                Projectile.velocity = Projectile.velocity * 0.95f + direction * 10f * 0.05f;
            }

            // вращение + "дыхание"
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            Projectile.scale = 1f + (float)Math.Sin(Main.GameUpdateCount * 0.25f) * 0.08f;

            // немного частиц
            if (Main.rand.NextBool(3))
            {
                Dust dust = Dust.NewDustDirect(
                    Projectile.position,
                    Projectile.width,
                    Projectile.height,
                    DustID.Shadowflame,
                    Projectile.velocity.X * 0.2f,
                    Projectile.velocity.Y * 0.2f
                );

                dust.noGravity = true;
                dust.scale = Main.rand.NextFloat(0.6f, 1.1f);
            }

            // фиолетовый свет в тон трейлу
            Lighting.AddLight(Projectile.Center, new Vector3(0.5f, 0.15f, 0.9f) * 0.8f);
        }

        // Дымный фиолетовый трейл: процедурные клубы через шейдер SoA:CursedSmoke
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = ModContent.Request<Texture2D>(Texture).Value;
            Vector2 origin = texture.Size() / 2;
            int len = Projectile.oldPos.Length;

            Texture2D blobTex = SoAVfx.Quad;
            Texture2D noiseTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/WaveNoise").Value;
            Vector2 blobOrigin = blobTex.Size() / 2f;

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            var smokeShader = GameShaders.Misc["SoA:CursedSmoke"];
            smokeShader.UseOpacity(0.9f);
            smokeShader.Apply();
            Main.graphics.GraphicsDevice.Textures[1] = noiseTex;
            Main.graphics.GraphicsDevice.SamplerStates[1] = SamplerState.LinearWrap;

            // Хвост рисуем первым, голова ложится поверх; прогресс штампа уходит в шейдер через альфу
            for (int i = len - 1; i >= 0; i--)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;

                float progress = i / (float)len;
                Vector2 pos = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;

                float sizePx = MathHelper.Lerp(52f, 14f, progress) * Projectile.scale;
                Color stampColor = Color.White;
                stampColor.A = (byte)(progress * 255f);

                Main.EntitySpriteDraw(blobTex, pos, null, stampColor, 0f, blobOrigin,
                    new Vector2(sizePx / blobTex.Width, sizePx / blobTex.Height), SpriteEffects.None, 0);
            }

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            // основной спрайт
            Main.EntitySpriteDraw(texture,
                Projectile.Center - Main.screenPosition,
                null,
                Color.White,
                Projectile.rotation,
                origin,
                Projectile.scale,
                SpriteEffects.None, 0);

            return false;
        }

        // 💀 ВЗРЫВ "КРИК ДУШИ"
        public override void OnKill(int timeLeft)
        {
            Vector2 center = Projectile.Center;

            // 🔊 крик души: спектральный вопль + рёв
            SoundEngine.PlaySound(SoundID.NPCDeath52, center);
            SoundEngine.PlaySound(SoundID.DD2_BetsyScream with { Volume = 0.8f, Pitch = -0.25f }, center);

            // 💡 вспышка
            Lighting.AddLight(center, new Vector3(0.7f, 0.2f, 1f));

            // 📳 тряска камеры (гаснет с расстоянием)
            if (Main.netMode != NetmodeID.Server)
                Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                    center, Main.rand.NextVector2Unit(), 10f, 9f, 22, 800f, "SoA:CursedBlast"));

            // 💥 визуальный взрыв: вспышка + рваная ударная волна (шейдер SoA:CursedBlast)
            if (Main.myPlayer == Projectile.owner)
                Projectile.NewProjectile(Projectile.GetSource_Death(), center, Vector2.Zero,
                    ModContent.ProjectileType<CursedExplosion>(), 0, 0f, Projectile.owner);

            // 💣 урон по области
            int radius = 80;

            foreach (NPC npc in Main.npc)
            {
                if (npc.active && !npc.friendly && !npc.dontTakeDamage)
                {
                    float dist = Vector2.Distance(center, npc.Center);
                    if (dist < radius)
                    {
                        npc.StrikeNPC(new NPC.HitInfo { Damage = Projectile.damage});
                    }
                }
            }

            // 🌌 кольцо энергии
            for (int i = 0; i < 30; i++)
            {
                float angle = MathHelper.TwoPi * i / 30f;
                Vector2 velocity = angle.ToRotationVector2() * Main.rand.NextFloat(3f, 7f);

                Dust dust = Dust.NewDustPerfect(center, DustID.Shadowflame, velocity);
                dust.noGravity = true;
                dust.scale = 1.5f;
            }

            // 💀 души
            for (int i = 0; i < 12; i++)
            {
                Vector2 vel = Main.rand.NextVector2Circular(4f, 4f);

                Dust soul = Dust.NewDustPerfect(center, DustID.SpectreStaff, vel);
                soul.noGravity = true;
                soul.scale = 1.8f;
                soul.fadeIn = 1.2f;
            }

            // ⚡ центральный взрыв
            for (int i = 0; i < 20; i++)
            {
                Vector2 vel = Main.rand.NextVector2Circular(2f, 2f);

                Dust core = Dust.NewDustPerfect(center, DustID.GemAmethyst, vel);
                core.scale = 2f;
            }
        }

        private NPC FindClosestNPC(float maxDistance)
        {
            NPC closest = null;
            float dist = maxDistance;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];

                if (!npc.active || npc.friendly || npc.dontTakeDamage)
                    continue;

                float d = Vector2.Distance(Projectile.Center, npc.Center);

                if (d < dist)
                {
                    dist = d;
                    closest = npc;
                }
            }

            return closest;
        }
    }

    // Чисто визуальный взрыв: живёт полсекунды, рисует вспышку, рваную ударную волну и лучи
    public class CursedExplosion : ModProjectile
    {
        public override string Texture => "SoA/Content/Projectiles/CurseOfMe/CursedProjectile";

        private const int Lifetime = 26;
        private const float BlastSizePx = 340f;

        private float Progress => 1f - Projectile.timeLeft / (float)Lifetime;

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 8;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = Lifetime;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;
            float fade = 1f - Progress;
            Lighting.AddLight(Projectile.Center, new Vector3(0.8f, 0.25f, 1.4f) * fade * 1.5f);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D quadTex = SoAVfx.Quad;
            Texture2D noiseTex = ModContent.Request<Texture2D>("SoA/Assets/Textures/WaveNoise").Value;
            Vector2 pos = Projectile.Center - Main.screenPosition;

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            var blastShader = GameShaders.Misc["SoA:CursedBlast"];
            blastShader.UseOpacity(1f);
            blastShader.Shader.Parameters["uProgress"]?.SetValue(Progress);
            blastShader.Apply();
            Main.graphics.GraphicsDevice.Textures[1] = noiseTex;
            Main.graphics.GraphicsDevice.SamplerStates[1] = SamplerState.LinearWrap;

            Main.EntitySpriteDraw(quadTex, pos, null, Color.White, 0f, quadTex.Size() / 2f,
                new Vector2(BlastSizePx / quadTex.Width, BlastSizePx / quadTex.Height),
                SpriteEffects.None, 0);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            return false;
        }
    }
}