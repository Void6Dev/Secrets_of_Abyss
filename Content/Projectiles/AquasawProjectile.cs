using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Microsoft.Xna.Framework;

namespace SoA.Content.Projectiles
{
    internal class AquasawProjectile : ModProjectile
    {
        private const float WetTargetDamageMultiplier = 1.25f;

        public override void SetDefaults()
        {
            Projectile.width = 60;
            Projectile.height = 32;
            Projectile.scale = 1;
            Projectile.friendly = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.ownerHitCheck = true;
            Projectile.extraUpdates = 1;
            Projectile.timeLeft = 300;

            Projectile.aiStyle = ProjAIStyleID.Drill;
        }

        public override void AI()
        {
            base.AI();
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2 - MathHelper.PiOver4 * Projectile.spriteDirection;

            int halfProjWidth = Projectile.width / 2;
            int halfProjHeight = Projectile.height / 2;
            DrawOriginOffsetX = 0;
            DrawOffsetX = -((50 / 2) - halfProjWidth);
            DrawOriginOffsetY = -((45 / 2) - halfProjHeight);

            // Water spray from blade tip
            if (Main.rand.NextBool(2))
            {
                Vector2 tipPos = Projectile.Center + Projectile.velocity.SafeNormalize(Vector2.Zero) * (Projectile.width * 0.4f);
                Dust spray = Dust.NewDustDirect(tipPos - new Vector2(4), 8, 8, DustID.Water,
                    Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 0.5f));
                spray.scale = Main.rand.NextFloat(0.8f, 1.4f);
                spray.noGravity = true;
            }

            // Water mist along the body
            if (Main.rand.NextBool(3))
            {
                Dust mist = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    DustID.Water, Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(-1.5f, 0f));
                mist.scale = Main.rand.NextFloat(0.4f, 0.8f);
                mist.noGravity = true;
                mist.alpha = 130;
            }

            // Rising bubbles
            if (Main.rand.NextBool(7))
            {
                Dust bubble = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
                    DustID.IceTorch, Main.rand.NextFloat(-0.4f, 0.4f), -Main.rand.NextFloat(0.5f, 2f));
                bubble.scale = Main.rand.NextFloat(0.3f, 0.65f);
                bubble.noGravity = true;
                bubble.alpha = 90;
            }

            Lighting.AddLight(Projectile.Center, 0f, 0.28f, 0.55f);

            // Sparks when blade tip touches a solid tile (tree, wall, etc.)
            Vector2 _tipPos = Projectile.Center + Projectile.velocity.SafeNormalize(Vector2.Zero) * (Projectile.width * 0.42f);
            int tileX = (int)(_tipPos.X / 16);
            int tileY = (int)(_tipPos.Y / 16);
            Tile tile = Framing.GetTileSafely(tileX, tileY);
            if (tile.HasTile && Main.tileSolid[tile.TileType] && Main.rand.NextBool(4))
            {
                Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.Zero);
                for (int i = 0; i < 3; i++)
                {
                    Dust spark = Dust.NewDustDirect(_tipPos - new Vector2(4), 8, 8, DustID.IceTorch,
                        back.X * Main.rand.NextFloat(5f, 13f) + Main.rand.NextFloat(-2f, 2f),
                        back.Y * Main.rand.NextFloat(5f, 13f) + Main.rand.NextFloat(-2f, 2f));
                    spark.scale = Main.rand.NextFloat(0.3f, 0.8f);
                    spark.noGravity = false;
                }
            }
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Пила питается водой: по врагам, находящимся в жидкости, урон выше
            if (target.wet)
                modifiers.FinalDamage *= WetTargetDamageMultiplier;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // По мокрой цели — фонтан брызг вместо обычных искр
            if (target.wet)
            {
                for (int i = 0; i < 12; i++)
                {
                    Dust splash = Dust.NewDustDirect(target.position, target.width, target.height, DustID.Water,
                        Main.rand.NextFloat(-4f, 4f), -Main.rand.NextFloat(2f, 6f));
                    splash.scale = Main.rand.NextFloat(1f, 1.6f);
                    splash.noGravity = true;
                }
            }

            Vector2 back = (Main.player[Projectile.owner].Center - Projectile.Center).SafeNormalize(Vector2.Zero);
            for (int i = 0; i < 10; i++)
            {
                Dust spark = Dust.NewDustDirect(Projectile.Center - new Vector2(10), 20, 20, DustID.IceTorch,
                    back.X * Main.rand.NextFloat(4f, 12f) + Main.rand.NextFloat(-3f, 3f),
                    back.Y * Main.rand.NextFloat(4f, 12f) + Main.rand.NextFloat(-3f, 3f));
                spark.scale = Main.rand.NextFloat(0.4f, 1f);
                spark.noGravity = false;
            }
        }
    }
}
