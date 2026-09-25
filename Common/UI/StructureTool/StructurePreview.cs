using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Превью выделенной постройки: рисуем настоящие спрайты тайлов и стен в
    // RenderTarget, диалог сохранения потом просто масштабирует готовую текстуру.
    // Собирается в фазе обновления — там нет активного SpriteBatch и цели рисования,
    // так что переключение render target безопасно.
    // Ограничения превью: без освещения, анимаций и склонов; жидкости — цветной блок.
    public class StructurePreview : ModSystem
    {
        private const int TileSize = 16;
        private const int MaxTargetDimension = 2048;

        private static readonly Color WaterColor = new(30, 90, 210, 190);
        private static readonly Color LavaColor = new(240, 110, 30, 210);
        private static readonly Color HoneyColor = new(230, 180, 40, 210);
        private static readonly Color ShimmerColor = new(200, 120, 220, 200);

        private static RenderTarget2D _target;
        private static Rectangle _area;
        private static IReadOnlySet<Point> _mask;
        private static bool _buildRequested;

        public static Texture2D Texture => _target;

        // Просим пересобрать превью: при открытии диалога и при смене галочек
        public static void Request(Rectangle tileArea, IReadOnlySet<Point> mask)
        {
            _area = tileArea;
            _mask = mask;
            _buildRequested = true;
        }

        public static void Release()
        {
            _buildRequested = false;
            _mask = null;
            _target?.Dispose();
            _target = null;
        }

        public override void OnWorldUnload() => Release();

        public override void PostUpdateEverything()
        {
            if (Main.dedServ || !_buildRequested)
                return;

            _buildRequested = false;
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                _target?.Dispose();
                _target = null;
                Mod.Logger.Warn("Structure preview render failed: " + exception);
            }
        }

        private void Build()
        {
            if (_mask == null || _area.Width <= 0 || _area.Height <= 0)
                return;

            int fullWidth = _area.Width * TileSize;
            int fullHeight = _area.Height * TileSize;
            float scale = MathF.Min(1f, MaxTargetDimension / (float)Math.Max(fullWidth, fullHeight));

            GraphicsDevice device = Main.graphics.GraphicsDevice;
            _target?.Dispose();
            _target = new RenderTarget2D(device,
                Math.Max(1, (int)(fullWidth * scale)), Math.Max(1, (int)(fullHeight * scale)),
                false, device.PresentationParameters.BackBufferFormat, DepthFormat.None);

            RenderTargetBinding[] previousTargets = device.GetRenderTargets();
            device.SetRenderTarget(_target);
            device.Clear(Color.Transparent);

            SpriteBatch spriteBatch = Main.spriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);

            StructureFilter filters = StructureSelection.Filters;
            bool skipWalls = (filters & StructureFilter.SkipWalls) != 0;
            bool skipLiquids = (filters & StructureFilter.SkipLiquids) != 0;

            foreach (Point cell in _mask)
            {
                if (!WorldGen.InWorld(cell.X, cell.Y, 1))
                    continue;

                Tile tile = Main.tile[cell.X, cell.Y];
                Vector2 position = new Vector2((cell.X - _area.X) * TileSize, (cell.Y - _area.Y) * TileSize) * scale;

                if (!skipWalls && tile.WallType != WallID.None)
                    DrawWall(spriteBatch, tile, position, scale);
                if (!skipLiquids && tile.LiquidAmount > 0)
                    DrawLiquid(spriteBatch, tile, position, scale);
                if (tile.HasTile && !StructureFilters.Skips(tile.TileType, filters))
                    DrawTile(spriteBatch, tile, position, scale);
            }

            spriteBatch.End();

            if (previousTargets.Length > 0)
                device.SetRenderTargets(previousTargets);
            else
                device.SetRenderTarget(null);
        }

        // Стены рисуются кадром 32x32 с нахлёстом 8 пикселей — как в ванильном DrawWalls
        private static void DrawWall(SpriteBatch spriteBatch, Tile tile, Vector2 position, float scale)
        {
            Main.instance.LoadWall(tile.WallType);
            Texture2D texture = TextureAssets.Wall[tile.WallType].Value;
            if (texture == null)
                return;

            var source = new Rectangle(tile.WallFrameX, tile.WallFrameY, 32, 32);
            spriteBatch.Draw(texture, position - new Vector2(8f, 8f) * scale, source,
                Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private static void DrawTile(SpriteBatch spriteBatch, Tile tile, Vector2 position, float scale)
        {
            Main.instance.LoadTiles(tile.TileType);
            Texture2D texture = TextureAssets.Tile[tile.TileType].Value;
            if (texture == null)
                return;

            var source = new Rectangle(tile.TileFrameX, tile.TileFrameY, TileSize, TileSize);
            spriteBatch.Draw(texture, position, source, Color.White, 0f, Vector2.Zero, scale,
                SpriteEffects.None, 0f);
        }

        // Жидкость — цветной блок по высоте заполнения, спрайты воды тут не нужны
        private static void DrawLiquid(SpriteBatch spriteBatch, Tile tile, Vector2 position, float scale)
        {
            Color color = tile.LiquidType switch
            {
                LiquidID.Lava => LavaColor,
                LiquidID.Honey => HoneyColor,
                LiquidID.Shimmer => ShimmerColor,
                _ => WaterColor
            };

            float fill = tile.LiquidAmount / 255f;
            int height = Math.Max(1, (int)(TileSize * fill * scale));
            int width = Math.Max(1, (int)(TileSize * scale));
            spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle((int)position.X, (int)position.Y + (int)(TileSize * scale) - height, width, height),
                color);
        }
    }
}
