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
    // Картинка постройки в RenderTarget: настоящие спрайты стен и тайлов рисуются
    // один раз при смене данных, дальше её только масштабируют — в окнах превью
    // и призраком в мире. 1 пиксель цели = 1 пиксель мира, пока влезает в 4096.
    // Ограничения: без освещения, анимаций, склонов и покраски; жидкости — цветной блок.
    public sealed class StructureRender
    {
        private const int TileSize = 16;
        private const int WallFrameSize = 32;
        private const int WallOverhang = 8;
        private const int MaxTargetDimension = 4096;

        // Средний кадр стандартного листа блока: у блоков из скрипта кадров нет
        private const int CenterFrame = 18;

        private static readonly Color WaterColor = new(30, 90, 210, 190);
        private static readonly Color LavaColor = new(240, 110, 30, 210);
        private static readonly Color HoneyColor = new(230, 180, 40, 210);
        private static readonly Color ShimmerColor = new(200, 120, 220, 200);

        private RenderTarget2D _target;
        private bool _dirty;

        public StructureRender() => StructureRenderSystem.Register(this);

        public StructureData Data { get; private set; }
        public Texture2D Texture => _target;

        // Пикселей цели на пиксель мира (меньше 1 только у огромных построек)
        public float Scale { get; private set; } = 1f;

        public void Show(StructureData data)
        {
            if (ReferenceEquals(data, Data))
                return;
            Data = data;
            _dirty = true;
        }

        public void Release()
        {
            Data = null;
            _dirty = false;
            _target?.Dispose();
            _target = null;
        }

        // Вызывается из Main.OnPreDraw — кадр ещё не начат, переключать цели безопасно.
        // true — цель рендера переключалась
        internal bool BuildIfDirty(GraphicsDevice device)
        {
            if (!_dirty)
                return false;
            _dirty = false;

            if (Data == null)
            {
                Release();
                return false;
            }

            int fullWidth = Data.Width * TileSize;
            int fullHeight = Data.Height * TileSize;
            Scale = MathF.Min(1f, MaxTargetDimension / (float)Math.Max(fullWidth, fullHeight));
            int width = Math.Max(1, (int)(fullWidth * Scale));
            int height = Math.Max(1, (int)(fullHeight * Scale));

            if (_target == null || _target.Width != width || _target.Height != height)
            {
                _target?.Dispose();
                _target = new RenderTarget2D(device, width, height, false,
                    device.PresentationParameters.BackBufferFormat, DepthFormat.None);
            }

            device.SetRenderTarget(_target);
            device.Clear(Color.Transparent);

            SpriteBatch spriteBatch = Main.spriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Matrix.CreateScale(Scale));

            // Слоями, а не по клеткам: иначе нахлёст стены соседней клетки ляжет поверх тайла
            DrawCells(spriteBatch, DrawWall);
            DrawCells(spriteBatch, DrawLiquid);
            DrawCells(spriteBatch, DrawTile);

            spriteBatch.End();
            return true;
        }

        private void DrawCells(SpriteBatch spriteBatch, Action<SpriteBatch, int, Vector2> drawCell)
        {
            for (int x = 0; x < Data.Width; x++)
            {
                for (int y = 0; y < Data.Height; y++)
                {
                    int idx = Data.Index(x, y);
                    if (Data.InStructure(idx))
                        drawCell(spriteBatch, idx, new Vector2(x * TileSize, y * TileSize));
                }
            }
        }

        private void DrawWall(SpriteBatch spriteBatch, int idx, Vector2 position)
        {
            int type = Data.WallTypes[idx];
            if (type == StructureData.NoType)
                return;

            Main.instance.LoadWall(type);
            Texture2D texture = TextureAssets.Wall[type].Value;
            if (texture == null)
                return;

            if (Data.WallFrames != null)
            {
                // Кадр 32x32 с нахлёстом 8 пикселей — как в ванильном DrawWalls
                int packed = Data.WallFrames[idx];
                var source = new Rectangle(packed & 0xFFFF, packed >> 16, WallFrameSize, WallFrameSize);
                spriteBatch.Draw(texture, position - new Vector2(WallOverhang), source, Color.White);
            }
            else
            {
                // Кадров нет — берём середину любого кадра: там тело стены без краёв
                var source = new Rectangle(WallOverhang, WallOverhang, TileSize, TileSize);
                spriteBatch.Draw(texture, position, source, Color.White);
            }
        }

        private void DrawTile(SpriteBatch spriteBatch, int idx, Vector2 position)
        {
            int type = Data.TileTypes[idx];
            if (type == StructureData.NoType)
                return;

            Main.instance.LoadTiles(type);
            Texture2D texture = TextureAssets.Tile[type].Value;
            if (texture == null)
                return;

            int frameX = Data.FrameX[idx];
            int frameY = Data.FrameY[idx];
            if (!Data.TilesFramed && frameX == 0 && frameY == 0 && !Main.tileFrameImportant[type])
                frameX = frameY = CenterFrame;

            bool halfBlock = (Data.Slopes[idx] & 8) != 0;
            int height = halfBlock ? TileSize / 2 : TileSize;
            var source = new Rectangle(frameX, frameY, TileSize, height);
            spriteBatch.Draw(texture, position + new Vector2(0f, TileSize - height), source, Color.White);
        }

        // Жидкость — цветной блок по высоте заполнения, спрайты воды тут не нужны
        private void DrawLiquid(SpriteBatch spriteBatch, int idx, Vector2 position)
        {
            int liquid = Data.Liquids[idx];
            int amount = liquid & 0xFF;
            if (amount == 0)
                return;

            Color color = (liquid >> 8) switch
            {
                LiquidID.Lava => LavaColor,
                LiquidID.Honey => HoneyColor,
                LiquidID.Shimmer => ShimmerColor,
                _ => WaterColor
            };

            int height = Math.Max(1, TileSize * amount / 255);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle((int)position.X, (int)position.Y + TileSize - height, TileSize, height), color);
        }
    }

    // Рисует все отложенные картинки построек и окна превью до начала кадра.
    // Main.OnPreDraw срабатывает и на паузе, в отличие от фазы обновления мира
    public class StructureRenderSystem : ModSystem
    {
        private static readonly List<StructureRender> _renders = new();
        private static readonly List<StructureViewport> _viewports = new();
        private static bool _failureLogged;

        internal static void Register(StructureRender render) => _renders.Add(render);

        internal static void Register(StructureViewport viewport) => _viewports.Add(viewport);

        public override void Load()
        {
            if (!Main.dedServ)
                Main.OnPreDraw += RenderPending;
        }

        public override void Unload()
        {
            Main.OnPreDraw -= RenderPending;

            // Текстуры освобождаем в главном потоке — выгрузка может идти в другом
            var renders = new List<StructureRender>(_renders);
            var viewports = new List<StructureViewport>(_viewports);
            Main.QueueMainThreadAction(() =>
            {
                foreach (StructureRender render in renders)
                    render.Release();
                foreach (StructureViewport viewport in viewports)
                    viewport.Release();
            });
        }

        private void RenderPending(GameTime gameTime)
        {
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            RenderTargetBinding[] previousTargets = device.GetRenderTargets();
            bool switched = false;

            try
            {
                // Сначала картинки построек: окна превью рисуют их уже в этом кадре
                foreach (StructureRender render in _renders)
                    switched |= render.BuildIfDirty(device);
                foreach (StructureViewport viewport in _viewports)
                    switched |= viewport.RenderIfVisible(device);
            }
            catch (Exception exception)
            {
                // Окно превью перерисовывается каждый кадр — в лог пишем только первый сбой
                if (!_failureLogged)
                    Mod.Logger.Warn("Structure preview render failed: " + exception);
                _failureLogged = true;
                try
                {
                    Main.spriteBatch.End();
                }
                catch (InvalidOperationException)
                {
                    // SpriteBatch не был начат — закрывать нечего
                }
                switched = true;
            }

            if (!switched)
                return;

            if (previousTargets.Length > 0)
                device.SetRenderTargets(previousTargets);
            else
                device.SetRenderTarget(null);
        }
    }
}
