using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.Projectiles
{
    // Водяная лента за игроком во время приливного рывка и пике (RoyalSpearPlayer).
    // Только визуал: урона нет. Путь не синхронизируется — каждый клиент сам пишет,
    // где был хозяин, поэтому ленту видят все, а не только тот, кто рвётся.
    // Пока игрок несётся — лента растёт, встал — хвост догоняет голову и снаряд гаснет.
    public class RoyalSpearDashTrail : ModProjectile
    {
        private const int MaxPoints = 24;

        // Быстрее этого (px/тик) игрок считается «в рывке» и лента растёт
        private const float MovingThreshold = 6f;

        // Сколько точек хвоста съедается за тик, когда игрок остановился
        private const int ShrinkPerTick = 2;

        // Рывок начинается не мгновенно: первые тики живём, даже если хозяин ещё стоит
        private const int StartupTicks = 4;
        private const int MaxLifeTicks = 90;

        private const float HeadHalfWidth = 16f;
        private static readonly Color BodyColor = new(60, 150, 255);

        private readonly Vector2[] points = new Vector2[MaxPoints];
        private int count;
        private Vector2 lastOwnerCenter;
        private int age;

        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        public override void SetDefaults()
        {
            Projectile.width = 2;
            Projectile.height = 2;
            Projectile.aiStyle = -1;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = MaxLifeTicks;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (!owner.active || owner.dead)
            {
                Projectile.Kill();
                return;
            }

            Vector2 center = owner.MountedCenter;
            bool moving = age > 0 && Vector2.Distance(center, lastOwnerCenter) > MovingThreshold;
            lastOwnerCenter = center;
            age++;

            if (moving || age <= StartupTicks)
            {
                Push(center);
                EmitDust(owner);
            }
            else
            {
                count = Math.Max(count - ShrinkPerTick, 0);
                if (count < 2 && age > StartupTicks)
                {
                    Projectile.Kill();
                    return;
                }
            }

            Projectile.Center = center;
            Lighting.AddLight(center, 0.15f, 0.35f, 0.55f);
        }

        // Голова — в начале массива: так SoATrail рисует её широкой, а хвост узким
        private void Push(Vector2 point)
        {
            for (int i = Math.Min(count, MaxPoints - 1); i > 0; i--)
                points[i] = points[i - 1];
            points[0] = point;
            count = Math.Min(count + 1, MaxPoints);
        }

        private void EmitDust(Player owner)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            for (int i = 0; i < 2; i++)
            {
                Dust drop = Dust.NewDustDirect(owner.position, owner.width, owner.height, DustID.Water);
                drop.velocity = Main.rand.NextVector2Circular(1.5f, 1.5f);
                drop.noGravity = true;
                drop.scale = Main.rand.NextFloat(1f, 1.5f);
            }
        }

        // Лента рисуется в проходе снарядов — раньше игроков, поэтому ложится под персонажа
        public override bool PreDraw(ref Color lightColor)
        {
            if (count < 2)
                return false;

            SoATrail.Draw(points.AsSpan(0, count),
                progress => HeadHalfWidth * (float)Math.Pow(1f - progress, 0.8f),
                progress => BodyColor * (1f - progress),
                TrailStyle.Tide);
            return false;
        }
    }
}
