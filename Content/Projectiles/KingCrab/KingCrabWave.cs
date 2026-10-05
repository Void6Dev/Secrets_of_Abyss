using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Utils;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Content.Projectiles
{
    // Водяной вал Короля-краба. Режимы через ai[0]:
    //   0 — бежит по земле (Tsunami Clap): прижат к полу, гаснет об стены
    //   1 — «прилив»: летит по прямой сквозь тайлы (стена Tide Call)
    public class KingCrabWave : ModProjectile
    {
        private const int FrameTicks = 5;
        private const float MaxStepUp = 36f; // вал по грунту взбирается на уступ до двух блоков
        private const int Lifetime = 240;
        private const int RiseTicks = 10;     // вал вырастает из грунта, а не появляется готовым
        private const int SinkTicks = 16;     // и оседает в конце, а не исчезает
        private const int TrailCopies = 2;

        private static readonly Color FoamColor = new(190, 240, 255);

        public override string Texture => "SoA/Content/NPCs/Bosses/KingCrab/KingCrabWave";

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 4;
            ProjectileID.Sets.TrailCacheLength[Type] = 6;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override void SetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 56;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.tileCollide = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = Lifetime;
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

        // Ранит только выросший вал: пока он поднимается из грунта или оседает, картинка
        // ниже хитбокса — бить невидимой частью нечестно
        public override bool CanHitPlayer(Player target)
            => Lifetime - Projectile.timeLeft >= RiseTicks / 2 && Projectile.timeLeft > SinkTicks / 2;

        // Достал игрока — атака короля засчитана, оглушения за промах не будет
        public override void OnHitPlayer(Player target, Player.HurtInfo info) => King_crab.ReportAttackLanded();

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Упёрся в невысокий уступ — взбирается; в настоящую стену — гаснет;
            // коснулся пола — продолжает катиться
            if (Projectile.velocity.X != oldVelocity.X)
                return !SoAPhysics.TryStepUp(Projectile, oldVelocity, MaxStepUp);
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

        // Низ спрайта на низ хитбокса: гребень возвышается над «телом» волны.
        // Кадры спрайта пока одинаковые, поэтому жизнь вала даёт отрисовка: растёт из грунта,
        // дышит гребнем, клонится вперёд по ходу, тянет за собой след и пену, оседает в конце
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            int frameHeight = tex.Height / Main.projFrames[Type];
            Rectangle frame = new(0, frameHeight * Projectile.frame, tex.Width, frameHeight);
            SpriteEffects fx = Projectile.spriteDirection >= 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Vector2 origin = new(tex.Width / 2f, frameHeight);

            float age = Lifetime - Projectile.timeLeft;
            float rise = MathHelper.Clamp(age / RiseTicks, 0f, 1f);
            float sink = MathHelper.Clamp(Projectile.timeLeft / (float)SinkTicks, 0f, 1f);
            float height = (1f - (1f - rise) * (1f - rise)) * sink;
            float breathe = 1f + 0.06f * (float)Math.Sin(age * 0.35f);
            Vector2 scale = new Vector2(1.08f - 0.08f * height, height * breathe); // низкий вал шире
            float lean = Projectile.spriteDirection * 0.07f * (float)Math.Sin(age * 0.2f + 1f);
            Color body = Projectile.GetAlpha(lightColor);

            // След: вал оставляет за собой гаснущие копии — скорость читается без пыли
            for (int i = TrailCopies; i >= 1; i--)
            {
                Vector2 old = Projectile.oldPos[i * 2];
                if (old == Vector2.Zero)
                    continue;
                Vector2 oldBase = new Vector2(old.X + Projectile.width / 2f, old.Y + Projectile.height + 2f);
                Main.EntitySpriteDraw(tex, oldBase - Main.screenPosition, frame, body * (0.25f / i),
                    lean, origin, scale * (1f - 0.08f * i), fx, 0);
            }

            Vector2 basePos = new Vector2(Projectile.Center.X, Projectile.position.Y + Projectile.height + 2f);
            Main.EntitySpriteDraw(tex, basePos - Main.screenPosition, frame, body, lean, origin, scale, fx, 0);

            // Пена: свечение на гребне (передняя кромка) и полоса у подножия
            SoAVfx.BeginAdditive(Main.spriteBatch);
            float foamPulse = 0.75f + 0.25f * (float)Math.Sin(age * 0.5f);
            Color crest = FoamColor * (0.45f * foamPulse * height);
            crest.A = 0;
            Vector2 crestPos = basePos + new Vector2(Projectile.spriteDirection * tex.Width * 0.18f, -frameHeight * 0.8f * scale.Y);
            SoAVfx.DrawTintedGlow(Main.spriteBatch, crestPos, new Vector2(34f, 24f) * scale.Y, crest);
            Color foot = FoamColor * (0.35f * height);
            foot.A = 0;
            SoAVfx.DrawTintedGlow(Main.spriteBatch, basePos - new Vector2(0f, 4f), new Vector2(tex.Width * 1.3f, 14f), foot);
            SoAVfx.EndAdditive(Main.spriteBatch);
            return false;
        }
    }
}
