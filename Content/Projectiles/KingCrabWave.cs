using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Projectiles
{
    // Водяной вал Короля-краба. Режимы через ai[0]:
    //   0 — бежит по земле (Tsunami Clap): прижат к полу, гаснет об стены
    //   1 — «прилив»: летит по прямой сквозь тайлы (стена Tide Call)
    public class KingCrabWave : ModProjectile
    {
        private const int FrameTicks = 5;

        public override string Texture => "SoA/Content/NPCs/Bosses/KingCrab/KingCrabWave";

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 4;
        }

        public override void SetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 56;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.tileCollide = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 240;
        }

        public override void AI()
        {
            if (Projectile.ai[0] == 1f)
                Projectile.tileCollide = false;
            else
                Projectile.velocity.Y += 0.5f; // прижимаем вал к земле

            if (++Projectile.frameCounter >= FrameTicks)
            {
                Projectile.frameCounter = 0;
                Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];
            }

            Projectile.spriteDirection = Projectile.velocity.X >= 0f ? 1 : -1;
            Lighting.AddLight(Projectile.Center, 0.15f, 0.3f, 0.45f);

            if (Main.netMode != NetmodeID.Server)
            {
                // Брызги с гребня и пена у подножия
                if (Main.rand.NextBool(2))
                {
                    Dust spray = Dust.NewDustDirect(Projectile.position, Projectile.width, 12, DustID.Water);
                    spray.velocity = new Vector2(Projectile.velocity.X * 0.3f, -Main.rand.NextFloat(1.5f, 4f));
                    spray.noGravity = true;
                    spray.scale = Main.rand.NextFloat(1.1f, 1.6f);
                }
                if (Main.rand.NextBool(3))
                {
                    Dust foam = Dust.NewDustDirect(
                        Projectile.position + new Vector2(0f, Projectile.height - 10f), Projectile.width, 10, DustID.BreatheBubble);
                    foam.velocity *= 0.3f;
                    foam.noGravity = true;
                }
            }
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Врезался в стену — гаснет; коснулся пола — продолжает катиться
            if (Projectile.velocity.X != oldVelocity.X)
                return true;
            Projectile.velocity.Y = 0f;
            return false;
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.netMode == NetmodeID.Server)
                return;
            for (int i = 0; i < 10; i++)
            {
                Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 2f, -Main.rand.NextFloat(1f, 4f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1.2f, 1.8f);
            }
        }

        public override Color? GetAlpha(Color lightColor)
        {
            return new Color(200, 230, 255, 180);
        }

        // Низ спрайта на низ хитбокса: гребень возвышается над «телом» волны
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            int frameHeight = tex.Height / Main.projFrames[Type];
            Rectangle frame = new(0, frameHeight * Projectile.frame, tex.Width, frameHeight);
            SpriteEffects fx = Projectile.spriteDirection >= 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Vector2 origin = new(tex.Width / 2f, frameHeight);
            Vector2 drawPos = new Vector2(Projectile.Center.X, Projectile.position.Y + Projectile.height + 2f)
                - Main.screenPosition;
            Main.EntitySpriteDraw(tex, drawPos, frame, Projectile.GetAlpha(lightColor),
                Projectile.rotation, origin, 1f, fx, 0);
            return false;
        }
    }
}
