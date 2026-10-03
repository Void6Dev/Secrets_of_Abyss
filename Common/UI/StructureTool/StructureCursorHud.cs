using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using SoA.Common.Utils;

namespace SoA.Common.UI
{
    // Подсказка у курсора: размер тянущегося мазка или сведения о призраке постройки
    public static class StructureCursorHud
    {
        private const int OffsetX = 20;
        private const int OffsetY = 22;
        private const int Padding = 8;
        private const int LineHeight = 20;
        private const float TextScale = 0.8f;
        private const int ScreenMargin = 8;

        public static void Draw(SpriteBatch spriteBatch)
        {
            Point mouse = Main.MouseScreen.ToPoint();
            if (StructureToolbar.Bounds().Contains(mouse))
                return;

            var lines = new List<(string Text, Color Color)>();
            if (StructurePlacement.Active)
                AddPlacementLines(lines);
            else if (StructureSelection.Dragging)
                AddDragLines(lines);

            if (lines.Count == 0)
                return;

            float width = 0f;
            foreach ((string text, _) in lines)
                width = MathF.Max(width, SoAHudDraw.Measure(text, TextScale).X);

            var panel = new Rectangle(mouse.X + OffsetX, mouse.Y + OffsetY,
                (int)width + Padding * 2, lines.Count * LineHeight + Padding * 2 - 4);
            panel.X = Math.Min(panel.X, Main.screenWidth - panel.Width - ScreenMargin);
            panel.Y = Math.Min(panel.Y, Main.screenHeight - panel.Height - ScreenMargin);

            SoAHudDraw.Panel(spriteBatch, panel, SoAHudDraw.PanelFill * 0.9f, SoAHudDraw.PanelBorder);
            int y = panel.Y + Padding;
            foreach ((string text, Color color) in lines)
            {
                SoAHudDraw.Text(spriteBatch, text, new Vector2(panel.X + Padding, y), color, TextScale);
                y += LineHeight;
            }
        }

        private static void AddDragLines(List<(string, Color)> lines)
        {
            Rectangle drag = StructureSelection.DragArea(out bool clamped);
            lines.Add((ToolText.Get("Hud.DragSize", drag.Width, drag.Height),
                StructureToolbar.ModeColor(StructureSelection.Mode)));
            if (clamped)
                lines.Add((ToolText.Get("Hud.Clamped", StructureIO.MaxWidth, StructureIO.MaxHeight),
                    StructureToolbar.LimitColor));
        }

        private static void AddPlacementLines(List<(string, Color)> lines)
        {
            StructureData data = StructurePlacement.Data;
            string title = ToolText.Get("Hud.PlaceTitle", StructurePlacement.Name, data.Width, data.Height);
            if (StructurePlacement.Mirror)
                title += ToolText.Get("Hud.Mirrored");
            lines.Add((title, SoAHudDraw.TitleText));

            if (Main.netMode != NetmodeID.SinglePlayer)
                lines.Add((ToolText.Get("Hud.SingleplayerOnly"), StructureToolbar.LimitColor));
            else if (StructurePlacement.ConflictCount > 0)
                lines.Add((ToolText.Get("Hud.Conflicts", StructurePlacement.ConflictCount), Color.Orange));

            lines.Add((ToolText.Get("Hud.PlaceHint"), SoAHudDraw.DimText));
        }
    }
}
