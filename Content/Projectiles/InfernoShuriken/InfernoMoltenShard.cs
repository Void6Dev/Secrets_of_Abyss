using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Расплавленный осколок: разлетается из взрыва сюрикена на полном Жаре, падает
    // и растекается по земле короткой огненной лужей. Лужа жжёт всех, кто в ней стоит.
    //   ai[0] — 0 падает, 1 лужа
    public class InfernoMoltenShard : ModProjectile
    {
        public override string Texture => "SoA/Assets/Textures/Vfx/SoftGlow";

        private const float Gravity = 0.3f;
        private const float MaxFallSpeed = 14f;
        private const int FallMaxTicks = 150;
        private const int PuddleTicks = 120;
        private const int PuddleFadeTicks = 30;
        private const int PuddleWidth = 52;
        private const int PuddleHeight = 12;
        private const int HitCooldown = 20;
        private const int HellFireTicks = 120;

        private static readonly Color HotWhite = new(255, 236, 170);
        private static readonly Color Lava = new(255, 120, 30);
        private static readonly Color DeepRed = new(170, 30, 12);
        private static readonly Color Crust = new(60, 16, 8);

        private bool IsPuddle => Projectile.ai[0] == 1f;
        private ref float Age => ref Projectile.localAI[0];

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Ranged;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = HitCooldown;
            Projectile.timeLeft = FallMaxTicks;
        }

        // Ложится и на платформы, а не проваливается сквозь них
        public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac)
        {
            fallThrough = false;
            return true;
        }

        public override void AI()
        {
            Age++;
            if (IsPuddle)
            {
                Projectile.velocity = Vector2.Zero;
                if (!Main.dedServ)
                    PuddleEffects();
                return;
            }

            Projectile.velocity.Y = System.Math.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);
            Projectile.rotation = Projectile.velocity.ToRotation();

            if (!Main.dedServ)
            {
                SoAParticles.SpawnStreak(Projectile.Center, -Projectile.velocity * 0.1f, Color.Lerp(Lava, DeepRed, Main.rand.NextFloat()),
                    1.6f, gravity: 0.05f, life: 10, lengthPerSpeed: 2f);
                Lighting.AddLight(Projectile.Center, Lava.ToVector3() * 0.6f);
            }
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            if (IsPuddle)
                return false;

            if (Projectile.velocity.Y != oldVelocity.Y && oldVelocity.Y > 0f)
            {
                BecomePuddle();
                return false;
            }
            if (Projectile.velocity.X != oldVelocity.X)
                Projectile.velocity.X = -oldVelocity.X * 0.3f;   // от стены — сползает вниз
            if (Projectile.velocity.Y != oldVelocity.Y)
                Projectile.velocity.Y = 0f;                       // о потолок
            return false;
        }

        // Все клиенты приходят сюда одной и той же физикой, поэтому превращение не пересылаем
        private void BecomePuddle()
        {
            Projectile.ai[0] = 1f;
            Vector2 bottom = Projectile.Bottom;
            Projectile.width = PuddleWidth;
            Projectile.height = PuddleHeight;
            Projectile.Bottom = bottom;
            Projectile.velocity = Vector2.Zero;
            Projectile.tileCollide = false;
            Projectile.timeLeft = PuddleTicks;
            Age = 0f;

            if (Main.dedServ)
                return;

            for (int i = 0; i < 8; i++)
            {
                SoAParticles.SpawnStreak(bottom, new Vector2(Main.rand.NextFloatDirection() * 4f, -Main.rand.NextFloat(1f, 4f)),
                    Color.Lerp(Lava, HotWhite, Main.rand.NextFloat(0.5f)), 1.8f, gravity: 0.2f, life: 16, lengthPerSpeed: 2f);
            }
            SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.35f, Pitch = 0.6f }, bottom);
        }

        private float PuddleLife => System.Math.Min(1f, Projectile.timeLeft / (float)PuddleFadeTicks);

        private void PuddleEffects()
        {
            float life = PuddleLife;
            if (Main.rand.NextFloat() < 0.5f * life)
            {
                Vector2 at = new(Projectile.position.X + Main.rand.NextFloat(Projectile.width), Projectile.Bottom.Y - 4f);
                SoAParticles.SpawnStreak(at, new Vector2(Main.rand.NextFloatDirection() * 0.4f, -Main.rand.NextFloat(1f, 2.4f)),
                    Color.Lerp(Lava, DeepRed, Main.rand.NextFloat()), 2f, gravity: -0.03f, life: Main.rand.Next(16, 28), lengthPerSpeed: 2.2f);
            }
            Lighting.AddLight(Projectile.Center, Lava.ToVector3() * (0.8f * life));
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
            => target.AddBuff(ModContent.BuffType<HellFireDebuff>(), HellFireTicks);

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D glow = SoAVfx.SoftGlow;
            Texture2D streak = SoAVfx.SoftStreak;

            if (!IsPuddle)
            {
                Vector2 center = Projectile.Center - Main.screenPosition;
                Main.EntitySpriteDraw(glow, center, null, SoAVfx.Additive(Lava * 0.7f), 0f, glow.Size() / 2f,
                    26f / glow.Width, SpriteEffects.None, 0);
                float length = 10f + Projectile.velocity.Length() * 2f;
                Main.EntitySpriteDraw(streak, center, null, SoAVfx.Additive(HotWhite), Projectile.rotation, streak.Size() / 2f,
                    new Vector2(length / streak.Width, 5f / streak.Height), SpriteEffects.None, 0);
                return false;
            }

            // Лужа: тёмная корка по краю, светящийся расплав и горячее ядро, которое мерцает
            float life = PuddleLife;
            float spread = System.Math.Min(1f, Age / 8f);   // растекается за несколько тиков
            float flicker = 0.85f + 0.15f * (float)System.Math.Sin(Age * 0.5f + Projectile.whoAmI);
            Vector2 surface = new Vector2(Projectile.Center.X, Projectile.Bottom.Y - 3f) - Main.screenPosition;
            float width = PuddleWidth * 1.4f * spread;

            Main.EntitySpriteDraw(glow, surface, null, Crust * (0.7f * life), 0f, glow.Size() / 2f,
                new Vector2(width * 1.1f / glow.Width, 16f / glow.Height), SpriteEffects.None, 0);
            Main.EntitySpriteDraw(glow, surface, null, SoAVfx.Additive(Lava * (0.8f * life * flicker)), 0f, glow.Size() / 2f,
                new Vector2(width / glow.Width, 12f / glow.Height), SpriteEffects.None, 0);
            Main.EntitySpriteDraw(glow, surface, null, SoAVfx.Additive(HotWhite * (0.5f * life * flicker)), 0f, glow.Size() / 2f,
                new Vector2(width * 0.5f / glow.Width, 5f / glow.Height), SpriteEffects.None, 0);
            return false;
        }
    }
}
