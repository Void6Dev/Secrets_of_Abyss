using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Интерактивное окно превью постройки: колесо — масштаб к курсору, ЛКМ — двигать,
    // ПКМ — вписать целиком, наведение подсвечивает клетку. Содержимое окна рисуется
    // в свой RenderTarget размером с рамку — так обрезка по краям бесплатна и не надо
    // перезапускать SpriteBatch интерфейса со scissor-тестом.
    public sealed class StructureViewport
    {
        private const int TileSize = 16;
        private const float ZoomStep = 1.2f;
        private const float MaxZoom = 6f;
        private const float FitMargin = 0.92f;
        private const float MinZoomOfFit = 0.5f;
        private const int CheckerSize = 12;
        private const float GridMinTilePixels = 10f;
        private const int KeepVisiblePixels = 24;
        private const int VisibleGraceFrames = 2;

        private static readonly Color Background = new(14, 16, 30);
        private static readonly Color CheckerLight = new(21, 24, 44);
        private static readonly Color GridColor = Color.White * 0.08f;
        private static readonly Color BoundsColor = new Color(110, 130, 210) * 0.6f;
        private static readonly Color HoverColor = new(255, 226, 130);

        public readonly StructureRender Render = new();

        private RenderTarget2D _view;
        private Rectangle _box;
        private float _zoom = 1f;
        private float _minZoom = 0.05f;
        private float _maxZoom = MaxZoom;
        private Vector2 _pan;           // левый верхний угол постройки относительно рамки
        private bool _fitPending = true;
        private bool _panning;
        private Vector2 _lastMouse;
        private int _visibleFrames;

        public StructureViewport() => StructureRenderSystem.Register(this);

        public StructureData Data => Render.Data;
        public Point? HoveredCell { get; private set; }

        // resetView — вписать заново (новая постройка); false — оставить масштаб (сменили галочку)
        public void Show(StructureData data, bool resetView)
        {
            Render.Show(data);
            if (resetView)
                _fitPending = true;
            HoveredCell = null;
        }

        public void Release()
        {
            Render.Release();
            _view?.Dispose();
            _view = null;
            _panning = false;
            _fitPending = true;
            HoveredCell = null;
        }

        // --- Ввод ---

        public void HandleInput(in UiInput input, Rectangle box)
        {
            _box = box;
            if (Data == null)
            {
                HoveredCell = null;
                _panning = false;
                return;
            }

            if (_fitPending)
                Fit();

            var local = new Vector2(input.Mouse.X - box.X, input.Mouse.Y - box.Y);
            bool inside = box.Contains(input.Mouse);

            if (inside && input.Scroll != 0)
                ZoomAt(local, input.Scroll > 0 ? ZoomStep : 1f / ZoomStep);

            if (inside && input.LeftClick)
            {
                _panning = true;
                _lastMouse = local;
            }
            if (!input.LeftHeld)
                _panning = false;
            if (_panning)
            {
                _pan += local - _lastMouse;
                _lastMouse = local;
            }

            if (inside && input.RightClick)
                Fit();

            ClampPan();
            HoveredCell = inside && !_panning ? CellAt(local) : null;
        }

        private void Fit()
        {
            if (_box.Width <= 0 || _box.Height <= 0)
                return;

            float structureWidth = Data.Width * TileSize;
            float structureHeight = Data.Height * TileSize;
            float fit = MathF.Min(_box.Width / structureWidth, _box.Height / structureHeight) * FitMargin;

            _zoom = fit;
            _minZoom = fit * MinZoomOfFit;
            _maxZoom = MathF.Max(MaxZoom, fit);
            _pan = new Vector2((_box.Width - structureWidth * fit) / 2f, (_box.Height - structureHeight * fit) / 2f);
            _fitPending = false;
        }

        // Точка под курсором остаётся на месте — масштаб «к курсору», как в редакторах
        private void ZoomAt(Vector2 local, float factor)
        {
            float zoom = Math.Clamp(_zoom * factor, _minZoom, _maxZoom);
            _pan = local - (local - _pan) * (zoom / _zoom);
            _zoom = zoom;
        }

        // Постройку нельзя утащить за рамку целиком — край всегда виден
        private void ClampPan()
        {
            float width = Data.Width * TileSize * _zoom;
            float height = Data.Height * TileSize * _zoom;
            _pan.X = Math.Clamp(_pan.X, KeepVisiblePixels - width, _box.Width - KeepVisiblePixels);
            _pan.Y = Math.Clamp(_pan.Y, KeepVisiblePixels - height, _box.Height - KeepVisiblePixels);
        }

        private Point? CellAt(Vector2 local)
        {
            Vector2 world = (local - _pan) / (_zoom * TileSize);
            var cell = new Point((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
            return Data.Contains(cell.X, cell.Y) ? cell : null;
        }

        // --- Отрисовка в интерфейсе ---

        public void Draw(SpriteBatch spriteBatch, Rectangle box)
        {
            _box = box;
            _visibleFrames = VisibleGraceFrames;

            SoAHudDraw.Fill(spriteBatch, box, Background);
            if (Data != null && _view != null)
                spriteBatch.Draw(_view, box, Color.White);
            else
                SoAHudDraw.TextCentered(spriteBatch, "...", box, SoAHudDraw.DimText, 0.9f);
            SoAHudDraw.Frame(spriteBatch, box, SoAHudDraw.PanelBorder);
        }

        // Строка про клетку под курсором: координаты, тайл, стена, жидкость
        public string DescribeHover()
        {
            if (Data == null || HoveredCell is not Point cell)
                return null;

            int idx = Data.Index(cell.X, cell.Y);
            var parts = new List<string> { $"{cell.X}, {cell.Y}" };

            if (!Data.InStructure(idx))
            {
                parts.Add(ToolText.Get("Preview.Outside"));
                return string.Join("  ·  ", parts);
            }

            int tile = Data.TileTypes[idx];
            int wall = Data.WallTypes[idx];
            int liquid = Data.Liquids[idx];

            if (tile != StructureData.NoType)
                parts.Add(TileName(tile));
            if (wall != StructureData.NoType)
                parts.Add(ToolText.Get("Preview.Wall", WallName(wall)));
            if ((liquid & 0xFF) > 0)
                parts.Add(ToolText.Get("Preview.Liquid", LiquidName(liquid >> 8), (liquid & 0xFF) * 100 / 255));
            if (parts.Count == 1)
                parts.Add(ToolText.Get("Preview.Empty"));

            return string.Join("  ·  ", parts);
        }

        private static string TileName(int type)
            => type < TileID.Count ? TileID.Search.GetName(type) : TileLoader.GetTile(type)?.Name ?? type.ToString();

        private static string WallName(int type)
            => type < WallID.Count ? WallID.Search.GetName(type) : WallLoader.GetWall(type)?.Name ?? type.ToString();

        private static string LiquidName(int liquidType) => liquidType switch
        {
            LiquidID.Lava => ToolText.Get("Preview.Lava"),
            LiquidID.Honey => ToolText.Get("Preview.Honey"),
            LiquidID.Shimmer => ToolText.Get("Preview.Shimmer"),
            _ => ToolText.Get("Preview.Water")
        };

        // --- Рендер содержимого окна (из Main.OnPreDraw) ---

        internal bool RenderIfVisible(GraphicsDevice device)
        {
            if (_visibleFrames <= 0 || Data == null || Render.Texture == null || _box.Width <= 0 || _box.Height <= 0)
                return false;
            _visibleFrames--;

            // Цель в реальных пикселях экрана, рисуем в неё в единицах интерфейса
            int width = Math.Max(1, (int)(_box.Width * Main.UIScale));
            int height = Math.Max(1, (int)(_box.Height * Main.UIScale));
            if (_view == null || _view.Width != width || _view.Height != height)
            {
                _view?.Dispose();
                _view = new RenderTarget2D(device, width, height, false,
                    device.PresentationParameters.BackBufferFormat, DepthFormat.None);
            }

            device.SetRenderTarget(_view);
            device.Clear(Background);

            // При отдалении пиксельная выборка рябит — сглаживаем, при приближении держим пиксели
            float textureScale = _zoom / Render.Scale;
            SamplerState sampler = textureScale < 1f ? SamplerState.LinearClamp : SamplerState.PointClamp;

            SpriteBatch spriteBatch = Main.spriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, sampler,
                DepthStencilState.None, RasterizerState.CullNone, null, Matrix.CreateScale(Main.UIScale));

            DrawChecker(spriteBatch);
            spriteBatch.Draw(Render.Texture, _pan, null, Color.White, 0f, Vector2.Zero, textureScale,
                SpriteEffects.None, 0f);
            DrawGrid(spriteBatch);
            SoAHudDraw.Frame(spriteBatch, StructureRect(), BoundsColor, 1);
            DrawHover(spriteBatch);

            spriteBatch.End();
            return true;
        }

        private Rectangle StructureRect()
            => new((int)_pan.X, (int)_pan.Y,
                (int)(Data.Width * TileSize * _zoom), (int)(Data.Height * TileSize * _zoom));

        // Шахматка под постройкой: видно, где у неё прозрачные (невыделенные) клетки
        private void DrawChecker(SpriteBatch spriteBatch)
        {
            for (int x = 0; x < _box.Width; x += CheckerSize)
            {
                for (int y = 0; y < _box.Height; y += CheckerSize)
                {
                    if ((x / CheckerSize + y / CheckerSize) % 2 == 0)
                        SoAHudDraw.Fill(spriteBatch, new Rectangle(x, y, CheckerSize, CheckerSize), CheckerLight);
                }
            }
        }

        private void DrawGrid(SpriteBatch spriteBatch)
        {
            float tilePixels = TileSize * _zoom;
            if (tilePixels < GridMinTilePixels)
                return;

            Rectangle area = StructureRect();
            for (int column = 1; column < Data.Width; column++)
            {
                int x = (int)(_pan.X + column * tilePixels);
                if (x >= 0 && x < _box.Width)
                    SoAHudDraw.Fill(spriteBatch, new Rectangle(x, area.Y, 1, area.Height), GridColor);
            }
            for (int row = 1; row < Data.Height; row++)
            {
                int y = (int)(_pan.Y + row * tilePixels);
                if (y >= 0 && y < _box.Height)
                    SoAHudDraw.Fill(spriteBatch, new Rectangle(area.X, y, area.Width, 1), GridColor);
            }
        }

        private void DrawHover(SpriteBatch spriteBatch)
        {
            if (HoveredCell is not Point cell)
                return;

            float tilePixels = TileSize * _zoom;
            var rect = new Rectangle((int)(_pan.X + cell.X * tilePixels), (int)(_pan.Y + cell.Y * tilePixels),
                Math.Max(2, (int)tilePixels), Math.Max(2, (int)tilePixels));
            SoAHudDraw.Frame(spriteBatch, rect, HoverColor, tilePixels >= 8f ? 2 : 1);
        }
    }
}
