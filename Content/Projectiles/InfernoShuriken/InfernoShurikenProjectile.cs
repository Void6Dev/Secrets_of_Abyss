using System;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Microsoft.Xna.Framework.Graphics;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    public class InfernoShurikenProjectile : ModProjectile
    {
        // Спрайт лежит рядом с кодом в папке владельца, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/InfernoShuriken/InfernoShurikenProjectile";

        // ai[0]: current bounce count
        // ai[1]: signed gravity — negative = perfect shot, Math.Abs = actual gravity
        // localAI[0/1]: spawn X/Y
        // _phase: 0 = flying, >0 = hover countdown, -1 = returning to target
        private const float MaxRange = 1500f;
        private bool _initialized = false;
        private float _phase = 0f;
        private int _returnTargetNPC = -1;
        private int _maxBounces = 3;     
        private int _hitsRemaining = 2;  

        public override void SetDefaults()
        {
            Projectile.width = 26;
            Projectile.height = 26;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Ranged;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = true;
            Projectile.aiStyle = 0;
        }

        public override void AI()
        {
            if (!_initialized)
            {
                Projectile.localAI[0] = Projectile.Center.X;
                Projectile.localAI[1] = Projectile.Center.Y;
                float grav = Math.Abs(Projectile.ai[1]);
                // Scale bounces and hits with charge: low gravity = high charge = more bounces/hits
                _maxBounces    = grav > 0.25f ? 1 : grav > 0.12f ? 2 : 3;
                // Perfect shot uses hover+return mechanic so only needs 1 hit-remaining for the return
                _hitsRemaining = Projectile.ai[1] < 0f ? 1 : _maxBounces;
                _initialized = true;
            }

            bool perfect = Projectile.ai[1] < 0f;
            float gravity = Math.Abs(Projectile.ai[1]);

            // Range kill only while flying forward
            if (_phase >= 0f)
            {
                var spawnPos = new Vector2(Projectile.localAI[0], Projectile.localAI[1]);
                if (Vector2.Distance(Projectile.Center, spawnPos) > MaxRange)
                {
                    Explode();
                    Projectile.Kill();
                    return;
                }
            }

            if (perfect && _phase != 0f)
            {
                if (_phase > 0f)
                {
                    // Hover: decelerate, spin, emit sparks, no NPC collision
                    Projectile.friendly = false;
                    _phase--;
                    Projectile.velocity *= 0.82f;
                    Projectile.rotation += 0.55f;

                    if (_phase <= 0f)
                        _phase = -1f;

                    if (Main.rand.NextBool(2))
                    {
                        Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                            DustID.InfernoFork, Main.rand.NextFloat(-1.5f, 1.5f), Main.rand.NextFloat(-1.5f, 1.5f));
                        d.scale = 1.3f;
                        d.noGravity = true;
                    }
                }
                else
                {
                    // Return: fly toward the NPC that was hit
                    Projectile.friendly = true;
                    if (_returnTargetNPC >= 0 && _returnTargetNPC < Main.maxNPCs && Main.npc[_returnTargetNPC].active)
                    {
                        Vector2 toTarget = Main.npc[_returnTargetNPC].Center - Projectile.Center;
                        float dist = toTarget.Length();
                        float returnSpeed = MathHelper.Clamp(dist * 0.15f + 10f, 10f, 22f);
                        Projectile.velocity = Vector2.Normalize(toTarget) * returnSpeed;
                    }
                    else
                    {
                        Explode();
                        Projectile.Kill();
                        return;
                    }
                    Projectile.rotation += 0.55f;
                }
            }
            else
            {
                Projectile.friendly = true;

                if (gravity > 0f)
                {
                    Projectile.velocity.Y += gravity;
                    if (Projectile.velocity.Y > 20f) Projectile.velocity.Y = 20f;
                }

                Projectile.rotation += 0.25f * Math.Sign(Projectile.velocity.X);
            }

            // Fire trail
            bool hotTrail = perfect && _phase <= 0f;
            int trailCount = hotTrail ? 3 : 2;
            for (int i = 0; i < trailCount; i++)
            {
                Dust dust = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    DustID.InfernoFork, Projectile.velocity.X * 0.3f, Projectile.velocity.Y * 0.3f);
                dust.scale = Main.rand.NextFloat(0.9f, 1.6f);
                dust.noGravity = false;
            }
            if (Main.rand.NextBool(3))
            {
                Dust ember = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    DustID.Torch, Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(-1f, 1f));
                ember.scale = Main.rand.NextFloat(0.6f, 1f);
                ember.noGravity = false;
            }

            Lighting.AddLight(Projectile.Center, perfect ? 1.8f : 0.8f, perfect ? 0.9f : 0.3f, 0f);

            const int maxTrails = 5;
            if (Projectile.oldPos.Length != maxTrails)
                Projectile.oldPos = new Vector2[maxTrails];
            for (int i = Projectile.oldPos.Length - 1; i > 0; i--)
                Projectile.oldPos[i] = Projectile.oldPos[i - 1];
            Projectile.oldPos[0] = Projectile.position;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            if (Projectile.ai[0] >= _maxBounces)
            {
                Explode();
                Projectile.Kill();
                return false;
            }

            float damping = 1f - Projectile.ai[0] * 0.1f;
            if (Projectile.velocity.X != oldVelocity.X)
                Projectile.velocity.X = -oldVelocity.X * damping;
            if (Projectile.velocity.Y != oldVelocity.Y)
                Projectile.velocity.Y = -oldVelocity.Y * damping;

            Projectile.ai[0] += 1f;

            int sparkCount = 8 + (int)(Projectile.ai[0] * 6);
            for (int i = 0; i < sparkCount; i++)
            {
                Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    DustID.InfernoFork, Main.rand.NextFloat(-4f, 4f), Main.rand.NextFloat(-4f, 4f));
                d.noGravity = true;
                d.scale = 1.2f + Projectile.ai[0] * 0.2f;
            }
            SoundEngine.PlaySound(SoundID.Tink, Projectile.position);

            return false;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            bool perfect = Projectile.ai[1] < 0f;
            bool returning = _phase < 0f;

            target.AddBuff(ModContent.BuffType<HellFireDebuff>(), 300);

            if (perfect && !returning)
            {
                // First hit on perfect shot: hover then return
                _returnTargetNPC = target.whoAmI;
                Projectile.tileCollide = false;
                _phase = 25f;
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.4f }, Projectile.position);
                return;
            }

            _hitsRemaining--;
            if (_hitsRemaining <= 0)
            {
                Explode();
                Projectile.Kill();
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(_returnTargetNPC);
            writer.Write(_phase);
            writer.Write(_hitsRemaining);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _returnTargetNPC = reader.ReadInt32();
            _phase = reader.ReadSingle();
            _hitsRemaining = reader.ReadInt32();
        }

        private void Explode()
        {
            bool perfect = Projectile.ai[1] < 0f;
            int dustCount = perfect ? 60 : 30;
            float dustSpeed = perfect ? 5f : 3f;
            float dustScale = perfect ? 2f : 1.5f;

            for (int i = 0; i < dustCount; i++)
            {
                Vector2 pos = Projectile.position + new Vector2(Main.rand.NextFloat(Projectile.width), Main.rand.NextFloat(Projectile.height));
                Dust.NewDust(pos, 0, 0, DustID.InfernoFork,
                    Main.rand.NextFloat(-dustSpeed, dustSpeed), Main.rand.NextFloat(-dustSpeed, dustSpeed), 100, default, dustScale);
            }
            for (int i = 0; i < (perfect ? 35 : 20); i++)
            {
                Vector2 pos = Projectile.position + new Vector2(Main.rand.NextFloat(Projectile.width), Main.rand.NextFloat(Projectile.height));
                Dust.NewDust(pos, 0, 0, DustID.Smoke,
                    Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f), 100, default, 1.5f);
            }

            Lighting.AddLight(Projectile.Center, perfect ? 2f : 1f, perfect ? 1f : 0.5f, 0.3f);
            SoundEngine.PlaySound(SoundID.Item14, Projectile.position);

            int explosionRadius = perfect ? 100 : 50;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.Distance(Projectile.Center) >= explosionRadius)
                    continue;

                npc.StrikeNPC(new NPC.HitInfo
                {
                    Damage = Projectile.damage,
                    Knockback = 0f,
                    HitDirection = npc.Center.X < Projectile.Center.X ? -1 : 1,
                    Crit = false
                });
                npc.AddBuff(ModContent.BuffType<HellFireDebuff>(), 300);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            bool perfect = Projectile.ai[1] < 0f;
            Texture2D texture = perfect
                ? ModContent.Request<Texture2D>("SoA/Content/Projectiles/InfernoShuriken/InfernoShurikenProjectile_active").Value
                : ModContent.Request<Texture2D>(Texture).Value;
            Vector2 origin = texture.Size() / 2;

            for (int i = 0; i < Projectile.oldPos.Length; i++)
            {
                Vector2 drawPos = Projectile.oldPos[i] - Main.screenPosition + new Vector2(Projectile.width / 2, Projectile.height / 2);
                float trailAlpha = (float)(Projectile.oldPos.Length - i) / Projectile.oldPos.Length;
                Color color = Projectile.GetAlpha(lightColor) * trailAlpha;
                Main.EntitySpriteDraw(texture, drawPos, null, color, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            }

            if (perfect)
            {
                float pulse = 0.4f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
                Color glowColor = new Color(255, 160, 40) * pulse;
                Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null, glowColor,
                    Projectile.rotation, origin, Projectile.scale * 1.3f, SpriteEffects.None, 0);
            }

            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null, lightColor,
                Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);

            return false;
        }
    }
}
