using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Players;
using SoA.Common.Utils;

namespace SoA.Content.Projectiles
{
    // Вал, который королевский выпад срывает с острия трезубца. Спрайт тот же, что у вала
    // Короля-краба: оружие выковано из его клешни, значит и волну поднимает его.
    // В отличие от боссовой волны летит строго по прицелу и не цепляется за тайлы —
    // игроку нужна предсказуемая дистанция, а не рельеф.
    public class RoyalTideWave : ModProjectile
    {
        private const int FrameTicks = 5;
        private const int LifeTicks = 55;
        private const int FadeTicks = 18;
        private const float WaveScale = 0.7f;
        private const float Drag = 0.985f;

        public override string Texture => "SoA/Content/NPCs/Bosses/KingCrab/KingCrabWave";

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 4;
        }

        public override void SetDefaults()
        {
            Projectile.width = 38;
            Projectile.height = 42;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 3;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = LifeTicks;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
        }

        public override void AI()
        {
            Projectile.velocity *= Drag;

            // Сохраняем гребень вверху при горизонтальном прицеле и наклоняем на диагонали
            Projectile.spriteDirection = Projectile.velocity.X >= 0f ? 1 : -1;
            Projectile.rotation = Projectile.velocity.ToRotation()
                + (Projectile.spriteDirection == -1 ? MathHelper.Pi : 0f);

            if (++Projectile.frameCounter >= FrameTicks)
            {
                Projectile.frameCounter = 0;
                Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];
            }

            Lighting.AddLight(Projectile.Center, 0.2f, 0.35f, 0.5f);

            if (Main.netMode == NetmodeID.Server)
                return;

            if (Main.rand.NextBool(2))
            {
                Dust spray = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                spray.velocity = Projectile.velocity * 0.3f - new Vector2(0f, Main.rand.NextFloat(0.5f, 2f));
                spray.noGravity = true;
                spray.scale = Main.rand.NextFloat(1f, 1.5f);
            }
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (SoACombat.IsSoaked(target))
                modifiers.FinalDamage *= 1.25f;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            SoACombat.Soak(target);

            if (Projectile.owner == Main.myPlayer)
                Main.player[Projectile.owner].GetModPlayer<RoyalSpearPlayer>().AddSlamDamage(damageDone);
        }

        public override Color? GetAlpha(Color lightColor)
        {
            float fade = MathHelper.Clamp(Projectile.timeLeft / (float)FadeTicks, 0f, 1f);
            return new Color(200, 230, 255, (int)(170 * fade));
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            int frameHeight = tex.Height / Main.projFrames[Type];
            Rectangle frame = new(0, frameHeight * Projectile.frame, tex.Width, frameHeight);
            SpriteEffects fx = Projectile.spriteDirection >= 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Vector2 origin = new(tex.Width / 2f, frameHeight / 2f);
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition, frame,
                Projectile.GetAlpha(lightColor), Projectile.rotation, origin, WaveScale, fx, 0);
            return false;
        }
    }
}
