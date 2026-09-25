using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.UI;
using SoA.Common.Utils;

namespace SoA.Content.Items.DevTools
{
    // Живое превью выделения в мире: заливка по клеткам маски, контур только по
    // внешним границам и прямоугольник текущего мазка. Геометрия контура кэшируется
    // и пересобирается только при изменении маски (StructureSelection.Version).
    public class StructureWandRenderer : ModSystem
    {
        private const int TileSize = 16;
        private const int BorderThickness = 2;
        private const int CullMargin = 400;

        private static readonly Color MaskColor = new(80, 220, 120);
        private static readonly Color LimitColor = new(255, 60, 60);
        private static readonly Color BoundsColor = new(200, 220, 255);

        private readonly List<Rectangle> _fillRuns = new();   // в тайлах, высота 1
        private readonly List<Rectangle> _edges = new();       // в пикселях мира
        private int _geometryVersion = -1;

        // Маску чистит StructureToolUi — здесь только свой кэш геометрии
        public override void OnWorldUnload() => _geometryVersion = -1;

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            // Убрали жезл из руки посреди протяжки — мазок не должен «залипнуть»
            if (Main.LocalPlayer?.HeldItem?.ModItem is not StructureWand && StructureSelection.Dragging)
                StructureSelection.CancelDrag();
        }

        public override void PostDrawTiles()
        {
            if (Main.LocalPlayer?.HeldItem?.ModItem is not StructureWand)
                return;
            if (StructureSelection.Count == 0 && !StructureSelection.Dragging)
                return;

            if (_geometryVersion != StructureSelection.Version)
                RebuildGeometry();

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            SpriteBatch sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, null, null, Main.GameViewMatrix.TransformationMatrix);

            Color maskColor = StructureSelection.OverLimit ? LimitColor : MaskColor;

            foreach (Rectangle run in _fillRuns)
            {
                var area = new Rectangle(run.X * TileSize, run.Y * TileSize, run.Width * TileSize, TileSize);
                if (!OnScreen(area))
                    continue;
                sb.Draw(pixel, Offset(area), maskColor * 0.14f);
            }

            foreach (Rectangle edge in _edges)
            {
                if (!OnScreen(edge))
                    continue;
                sb.Draw(pixel, Offset(edge), maskColor);
            }

            DrawBoundsOutline(sb, pixel);
            DrawDragPreview(sb, pixel);

            sb.End();
        }

        // Габариты будущего файла — тонкая рамка, чтобы видеть реальный размер постройки
        private static void DrawBoundsOutline(SpriteBatch sb, Texture2D pixel)
        {
            Rectangle bounds = StructureSelection.Bounds;
            if (bounds.Width <= 0)
                return;

            Color color = (StructureSelection.OverLimit ? LimitColor : BoundsColor) * 0.5f;
            var area = new Rectangle(bounds.X * TileSize, bounds.Y * TileSize,
                bounds.Width * TileSize, bounds.Height * TileSize);
            DrawOutline(sb, pixel, area, color, 1);
        }

        private static void DrawDragPreview(SpriteBatch sb, Texture2D pixel)
        {
            if (!StructureSelection.Dragging)
                return;

            Rectangle drag = StructureSelection.DragArea(out bool clamped);
            Color color = clamped ? LimitColor : ModeColor(StructureSelection.Mode);
            var area = new Rectangle(drag.X * TileSize, drag.Y * TileSize,
                drag.Width * TileSize, drag.Height * TileSize);

            sb.Draw(pixel, Offset(area), color * 0.12f);
            DrawOutline(sb, pixel, area, color, BorderThickness);
        }

        private static void DrawOutline(SpriteBatch sb, Texture2D pixel, Rectangle area, Color color, int thickness)
        {
            Rectangle r = Offset(area);
            sb.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, thickness), color);
            sb.Draw(pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
            sb.Draw(pixel, new Rectangle(r.X, r.Y, thickness, r.Height), color);
            sb.Draw(pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
        }

        internal static Color ModeColor(SelectionMode mode) => mode switch
        {
            SelectionMode.Add => new Color(80, 220, 120),
            SelectionMode.Erase => new Color(255, 90, 90),
            _ => new Color(90, 180, 255)
        };

        private static Rectangle Offset(Rectangle worldPixels)
            => new(worldPixels.X - (int)Main.screenPosition.X, worldPixels.Y - (int)Main.screenPosition.Y,
                worldPixels.Width, worldPixels.Height);

        private static bool OnScreen(Rectangle worldPixels)
            => worldPixels.Right > Main.screenPosition.X - CullMargin
            && worldPixels.X < Main.screenPosition.X + Main.screenWidth + CullMargin
            && worldPixels.Bottom > Main.screenPosition.Y - CullMargin
            && worldPixels.Y < Main.screenPosition.Y + Main.screenHeight + CullMargin;

        // --- Сборка геометрии маски ---

        // Клетки собираются в горизонтальные пробеги: одна заливка на пробег вместо
        // отрисовки каждой клетки. Контур ставится только там, где соседа в маске нет.
        private void RebuildGeometry()
        {
            _geometryVersion = StructureSelection.Version;
            _fillRuns.Clear();
            _edges.Clear();

            var rows = new Dictionary<int, List<int>>();
            foreach (Point cell in StructureSelection.Cells)
            {
                if (!rows.TryGetValue(cell.Y, out List<int> columns))
                    rows[cell.Y] = columns = new List<int>();
                columns.Add(cell.X);
            }

            foreach ((int y, List<int> columns) in rows)
            {
                columns.Sort();
                int runStart = columns[0];

                for (int i = 1; i <= columns.Count; i++)
                {
                    bool runEnded = i == columns.Count || columns[i] != columns[i - 1] + 1;
                    if (!runEnded)
                        continue;

                    int runEnd = columns[i - 1];
                    _fillRuns.Add(new Rectangle(runStart, y, runEnd - runStart + 1, 1));

                    // Края пробега по определению не имеют соседа слева/справа
                    _edges.Add(new Rectangle(runStart * TileSize, y * TileSize, BorderThickness, TileSize));
                    _edges.Add(new Rectangle((runEnd + 1) * TileSize - BorderThickness, y * TileSize,
                        BorderThickness, TileSize));

                    AddHorizontalEdges(runStart, runEnd, y, -1);
                    AddHorizontalEdges(runStart, runEnd, y, 1);

                    if (i < columns.Count)
                        runStart = columns[i];
                }
            }
        }

        // Непрерывные отрезки верхней (direction -1) или нижней (+1) границы пробега
        private void AddHorizontalEdges(int fromX, int toX, int y, int direction)
        {
            int neighborY = y + direction;
            int segmentStart = int.MinValue;

            for (int x = fromX; x <= toX + 1; x++)
            {
                bool open = x <= toX && !StructureSelection.Contains(x, neighborY);

                if (open && segmentStart == int.MinValue)
                    segmentStart = x;
                else if (!open && segmentStart != int.MinValue)
                {
                    int pixelY = direction < 0
                        ? y * TileSize
                        : (y + 1) * TileSize - BorderThickness;
                    _edges.Add(new Rectangle(segmentStart * TileSize, pixelY,
                        (x - segmentStart) * TileSize, BorderThickness));
                    segmentStart = int.MinValue;
                }
            }
        }
    }
}
