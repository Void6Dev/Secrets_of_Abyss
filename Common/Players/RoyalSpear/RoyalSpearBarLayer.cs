using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using SoA.Content.Items.Weapons;

namespace SoA.Common.Players
{
    // Полоска приливного удара под ногами игрока: копится с урона, на полной шкале
    // светится и следующий бросок ПКМ уносит игрока на верёвке.
    //
    // ТЕКСТУРЫ (Assets/Textures/):
    //   RoyalTideBar.png        — рамка, задаёт размер всей полоски
    //   RoyalTideBar_Fill.png   — заливка: кадры сверху вниз, кадр = размер рамки
    //   RoyalTideBar_Ready.png  — вид полной шкалы, тоже кадрами
    //
    // Порядок отрисовки код выбирает сам по рамке: жёлоб залит непрозрачным — рамка
    // идёт первой (иначе она спрячет воду целиком), жёлоб прозрачный — рамка ложится
    // поверх, и её орнамент оказывается над водой.
    //
    // Ничего из геометрии в коде не зашито:
    //   размер полоски      — из рамки;
    //   число кадров        — высота листа заливки / высота рамки;
    //   диапазон заполнения — по непрозрачным пикселям первого кадра заливки.
    // То есть воду достаточно нарисовать там, где у рамки жёлоб: код сам увидит её
    // границы и будет обрезать по ним, а не по всей ширине текстуры.
    public class RoyalSpearBarLayer : PlayerDrawLayer
    {
        private const int BarOffsetY = 18;
        private const int FrameTicks = 6;

        private static Rectangle? _trough;
        private static bool? _frameHidesFill;

        private static Asset<Texture2D> _frame;
        private static Asset<Texture2D> _fill;
        private static Asset<Texture2D> _ready;

        private static Texture2D FrameTexture =>
            (_frame ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/RoyalTideBar",
                AssetRequestMode.ImmediateLoad)).Value;

        private static Texture2D FillTexture =>
            (_fill ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/RoyalTideBar_Fill",
                AssetRequestMode.ImmediateLoad)).Value;

        private static Texture2D ReadyTexture =>
            (_ready ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/RoyalTideBar_Ready",
                AssetRequestMode.ImmediateLoad)).Value;

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            if (player.dead || player.HeldItem.type != ModContent.ItemType<RoyalSpear>())
                return false;
            return player.GetModPlayer<RoyalSpearPlayer>().SlamDamage > 0;
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            var spearPlayer = player.GetModPlayer<RoyalSpearPlayer>();
            float progress = spearPlayer.SlamProgress;
            bool ready = spearPlayer.SlamReady;

            Texture2D frameTex = FrameTexture;
            int barWidth = frameTex.Width;
            int barHeight = frameTex.Height;

            Vector2 anchor = new(player.Center.X, player.Bottom.Y + BarOffsetY + player.gfxOffY);
            Vector2 topLeft = anchor - new Vector2(barWidth * 0.5f, barHeight * 0.5f) - Main.screenPosition;
            topLeft = new Vector2((int)topLeft.X, (int)topLeft.Y);

            Texture2D contentTex = ready ? ReadyTexture : FillTexture;
            Rectangle trough = Trough(FillTexture, barHeight);
            int filled = ready ? trough.Width : (int)(trough.Width * progress);

            DrawData frameData = new(frameTex, topLeft, new Rectangle(0, 0, barWidth, barHeight),
                Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None);

            if (filled <= 0)
            {
                drawInfo.DrawDataCache.Add(frameData);
                return;
            }

            // Кадр гоняется по времени, а не по прогрессу: вода в жёлобе живёт всегда
            int frames = System.Math.Max(1, contentTex.Height / barHeight);
            int frame = (int)(Main.GameUpdateCount / FrameTicks) % frames;

            // На полной шкале пульсируем яркостью — заметно, не глядя на цифры
            Color tint = Color.White;
            if (ready)
                tint *= 0.8f + 0.2f * (float)System.Math.Sin(Main.GlobalTimeWrappedHourly * 8f);

            DrawData fillData = new(contentTex, topLeft + new Vector2(trough.X, trough.Y),
                new Rectangle(trough.X, frame * barHeight + trough.Y, filled, trough.Height),
                tint, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None);

            // Порядок решает сама рамка: если её жёлоб залит непрозрачным, положенная
            // сверху она спрячет воду — тогда рисуем её первой. Если жёлоб прозрачный,
            // рамка идёт поверх и её орнамент ложится на воду
            if (FrameHidesFill(frameTex, trough))
            {
                drawInfo.DrawDataCache.Add(frameData);
                drawInfo.DrawDataCache.Add(fillData);
            }
            else
            {
                drawInfo.DrawDataCache.Add(fillData);
                drawInfo.DrawDataCache.Add(frameData);
            }
        }

        // Жёлоб = непрозрачные пиксели первого кадра заливки. Считаем один раз:
        // так художник задаёт диапазон заполнения прямо в спрайте, без констант в коде
        private static Rectangle Trough(Texture2D fill, int barHeight)
        {
            if (_trough.HasValue)
                return _trough.Value;

            Color[] pixels = new Color[fill.Width * fill.Height];
            fill.GetData(pixels);

            int minX = fill.Width, minY = barHeight, maxX = -1, maxY = -1;
            for (int y = 0; y < barHeight; y++)
            {
                for (int x = 0; x < fill.Width; x++)
                {
                    if (pixels[y * fill.Width + x].A < 8)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            _trough = maxX < 0
                ? new Rectangle(0, 0, fill.Width, barHeight) // пустой кадр — берём всё
                : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return _trough.Value;
        }

        // Непрозрачен ли жёлоб рамки: смотрим три точки по его середине
        private static bool FrameHidesFill(Texture2D frameTex, Rectangle trough)
        {
            if (_frameHidesFill.HasValue)
                return _frameHidesFill.Value;

            Color[] pixels = new Color[frameTex.Width * frameTex.Height];
            frameTex.GetData(pixels);

            int y = System.Math.Clamp(trough.Y + trough.Height / 2, 0, frameTex.Height - 1);
            int maxAlpha = 0;
            for (int i = 1; i <= 3; i++)
            {
                int x = System.Math.Clamp(trough.X + trough.Width * i / 4, 0, frameTex.Width - 1);
                int alpha = pixels[y * frameTex.Width + x].A;
                if (alpha > maxAlpha)
                    maxAlpha = alpha;
            }

            _frameHidesFill = maxAlpha > 128;
            return _frameHidesFill.Value;
        }
    }
}
