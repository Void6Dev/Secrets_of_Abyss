using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Projectiles
{
    public class InfernoShurikenProjectile : ModProjectile
    {
        private int _sourceDamage = 70;

        public override void SetDefaults() {
            Projectile.width = 26;
            Projectile.height = 26;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Ranged;
            Projectile.penetrate = 3;
            Projectile.timeLeft = 600;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = true;
            Projectile.aiStyle = 2; // Shuriken style
        }
        
        public override void AI() {
            // Optional visual effects or movement behavior
            for (int i = 0; i < 2; i++) {
                Dust dust = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Firefly, Projectile.velocity.X * 0.2f, Projectile.velocity.Y * 0.2f);
                dust.scale = 1.2f;
                dust.noGravity = true;
            }

            // Store old positions for the afterimage effect
            const int maxTrails = 5;
            if (Projectile.oldPos.Length != maxTrails) {
                Projectile.oldPos = new Vector2[maxTrails];
            }

            // Shift positions to create a trail effect
            for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
                Projectile.oldPos[i] = Projectile.oldPos[i - 1];
            }
            Projectile.oldPos[0] = Projectile.position;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            Explode();
            target.AddBuff(BuffID.OnFire, 300);
        }

        public override bool OnTileCollide(Vector2 oldVelocity) {
            Explode();
            return true;
        }

        private void Explode() {
            for (int i = 0; i < 40; i++) {
                Vector2 dustPosition = Projectile.position + new Vector2(Main.rand.NextFloat(Projectile.width), Main.rand.NextFloat(Projectile.height));
                Dust.NewDust(dustPosition, 0, 0, DustID.InfernoFork, Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f), 100, default(Color), 1.5f);
            }
            for (int i = 0; i < 20; i++) {
                Vector2 dustPosition = Projectile.position + new Vector2(Main.rand.NextFloat(Projectile.width), Main.rand.NextFloat(Projectile.height));
                Dust.NewDust(dustPosition, 0, 0, DustID.Smoke, Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f), 100, default(Color), 1.5f);
            }

            Lighting.AddLight(Projectile.Center, 1f, 0.5f, 0.3f);
            SoundEngine.PlaySound(SoundID.Item14, Projectile.position); 

            int explosionRadius = 60;
            for (int i = 0; i < 200; i++) {
                NPC npc = Main.npc[i];
                if (npc.active && !npc.friendly && npc.Distance(Projectile.Center) < explosionRadius && !npc.dontTakeDamage) {
                    NPC.HitInfo hitInfo = new NPC.HitInfo() {
                        Damage = _sourceDamage,
                        Knockback = 0f,
                        HitDirection = npc.Center.X < Projectile.Center.X ? -1 : 1,
                        Crit = false
                    };

                    npc.StrikeNPC(hitInfo);
                    npc.AddBuff(BuffID.OnFire, 300);
                }
            }
        }

        // Draw the projectile with afterimage effect
        public override bool PreDraw(ref Color lightColor) {
            Texture2D texture = ModContent.Request<Texture2D>(Texture).Value;

            // Draw the afterimages
            for (int i = 0; i < Projectile.oldPos.Length; i++) {
                Vector2 drawPos = Projectile.oldPos[i] - Main.screenPosition + new Vector2(Projectile.width / 2, Projectile.height / 2);
                Color color = Projectile.GetAlpha(lightColor) * ((float)(Projectile.oldPos.Length - i) / Projectile.oldPos.Length);
                Main.EntitySpriteDraw(texture, drawPos, null, color, Projectile.rotation, texture.Size() / 2, Projectile.scale, SpriteEffects.None, 0);
            }

            // Draw the actual projectile
            Vector2 origin = new Vector2(texture.Width / 2, texture.Height / 2);
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);

            return false; // We handled the drawing
        }
    }
}