using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Utils;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Content.Projectiles
{
    // Ударная волна Короля-краба: бежит по земле, гаснет об стены.
    // Рисуется разломом грунта (шейдер SoA:GroundRupture): у фронта из земли вздымаются
    // каменные шипы цвета грунта и оседают позади. Раньше это были два светящихся пятна —
    // полупрозрачный столб, который просто ехал по полу.
    public class KingCrabShockwave : ModProjectile
    {
        // Спрайт лежит рядом с кодом в папке контента, а не по пути пространства имён
        public override string Texture => "SoA/Content/Projectiles/KingCrab/KingCrabShockwave";

        private const int Lifetime = 90;
        private const float MaxStepUp = 36f; // волна по грунту взбирается на уступ до двух блоков

        // Квад разлома: шлейф оседающих шипов тянется за фронтом
        private const float RuptureWidth = 200f;
        private const float RuptureHeight = 96f;
        private const float RuptureAhead = 0.15f; // доля квада перед фронтом
        private const float SurfaceSearchUp = 48f;   // поверхность ищем выше уровня волны (уступ)
        private const float SurfaceSearchDown = 64f; // и ниже (спуск); дальше — яма, там пусто

        private Vector3 _groundColor = new(0.82f, 0.72f, 0.45f);

        public override void SetDefaults()
        {
            // Хитбокс — передний шип: он и опасен, шлейф позади только оседает
            Projectile.width = 34;
            Projectile.height = 46;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.tileCollide = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = Lifetime;
        }

        private int Dir => Projectile.velocity.X >= 0f ? 1 : -1;

        public override void AI()
        {
            Projectile.velocity.Y += 0.5f; // прижимаем волну к земле
            if (Projectile.velocity.X != 0f)
                Projectile.direction = Dir; // у стены скорость обнуляется — направление помним
            Lighting.AddLight(Projectile.Bottom - new Vector2(0f, 12f), 0.45f, 0.3f, 0.12f);

            if (Main.netMode == NetmodeID.Server)
                return;

            // Цвет грунта подхватываем на ходу: волна может перейти с песка на камень
            if (Projectile.timeLeft % 6 == 0)
                _groundColor = King_crab.GetGroundTint(Projectile.Bottom - new Vector2(0f, 8f));

            // Крошка из-под фронта: шипы несут картинку, пыль только подчёркивает
            if (Main.rand.NextBool(2))
            {
                Vector2 front = Projectile.Bottom + new Vector2(Projectile.direction * Projectile.width * 0.5f, -4f);
                Dust grit = Dust.NewDustPerfect(front, King_crab.GroundDustType(Projectile.Bottom - new Vector2(0f, 8f)),
                    new Vector2(Projectile.direction * Main.rand.NextFloat(0.5f, 2.5f), -Main.rand.NextFloat(2f, 5f)));
                grit.scale = Main.rand.NextFloat(1f, 1.5f);
            }
        }

        // Достал игрока — атака короля засчитана, оглушения за промах не будет
        public override void OnHitPlayer(Player target, Player.HurtInfo info) => King_crab.ReportAttackLanded();

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Упёрлась в невысокий уступ — взбирается; в настоящую стену — гаснет;
            // коснулась пола — продолжает катиться
            if (Projectile.velocity.X != oldVelocity.X)
                return !SoAPhysics.TryStepUp(Projectile, oldVelocity, MaxStepUp);
            Projectile.velocity.Y = 0f;
            return false;
        }

        // Верх грунта в столбце тайлов рядом с уровнем волны: первый твёрдый тайл, над которым
        // пусто. NaN — в окне поиска поверхности нет (яма глубже или сплошная стена)
        private static float SurfaceNear(int tx, float refY)
        {
            int from = (int)((refY - SurfaceSearchUp) / 16f);
            int to = (int)((refY + SurfaceSearchDown) / 16f);
            for (int ty = from; ty <= to; ty++)
            {
                Tile tile = Framing.GetTileSafely(tx, ty);
                if (!IsGround(tile) || IsGround(Framing.GetTileSafely(tx, ty - 1)))
                    continue;
                return ty * 16f + (tile.IsHalfBlock ? 8f : 0f);
            }
            return float.NaN;
        }

        private static bool IsGround(Tile tile)
            => tile.HasUnactuatedTile && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]);

        public override bool PreDraw(ref Color lightColor)
        {
            int dir = Projectile.direction == 0 ? Dir : Projectile.direction;
            float frontX = Projectile.Center.X + dir * Projectile.width * 0.5f;
            float left = dir > 0 ? frontX - RuptureWidth * (1f - RuptureAhead) : frontX - RuptureWidth * RuptureAhead;
            float right = left + RuptureWidth;
            float life = 1f - Projectile.timeLeft / (float)Lifetime;

            SoAVfx.BeginAlphaImmediate(Main.spriteBatch);
            MiscShaderData shader = GameShaders.Misc["SoA:GroundRupture"];
            shader.UseOpacity(1f);
            shader.Shader.Parameters["uProgress"]?.SetValue(life);
            shader.Shader.Parameters["uFront"]?.SetValue(frontX);
            shader.Shader.Parameters["uDir"]?.SetValue((float)dir);
            Texture2D quad = SoAVfx.Quad;

            // Шлейф режется на столбцы по тайлам, и каждый садится на СВОЮ поверхность:
            // одним квадом шипы над ямой висели в воздухе, а на подъёме уходили в склон.
            // Шейдер считает шипы по мировому X, поэтому на стыках столбцов узор не рвётся
            for (int tx = (int)(left / 16f); tx * 16f < right; tx++)
            {
                float segLeft = Math.Max(tx * 16f, left);
                float segWidth = Math.Min(tx * 16f + 16f, right) - segLeft;
                float surface = SurfaceNear(tx, Projectile.Bottom.Y);
                if (segWidth <= 0f || float.IsNaN(surface))
                    continue; // яма или стена — тут грунту вздыматься неоткуда

                // Свет: шипы — грунт, темнеют в тени, как и тайлы вокруг
                Vector3 light = Lighting.GetColor(tx, (int)(surface / 16f) - 1).ToVector3();
                shader.UseColor(_groundColor * Vector3.Clamp(light * 1.15f + new Vector3(0.15f), Vector3.Zero, Vector3.One));
                shader.Shader.Parameters["uSizePx"]?.SetValue(new Vector2(segWidth, RuptureHeight));
                shader.Shader.Parameters["uWorldLeft"]?.SetValue(segLeft);
                shader.Apply();

                Main.EntitySpriteDraw(quad, new Vector2(segLeft, surface - RuptureHeight) - Main.screenPosition, null,
                    Color.White, 0f, Vector2.Zero, new Vector2(segWidth / quad.Width, RuptureHeight / quad.Height),
                    SpriteEffects.None, 0);
            }
            SoAVfx.EndAdditive(Main.spriteBatch);
            return false;
        }
    }
}
