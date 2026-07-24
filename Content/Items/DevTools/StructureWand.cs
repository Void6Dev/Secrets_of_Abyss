using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;

namespace SoA.Content.Items.DevTools
{
    public class StructureWand : ModItem
    {
        private Point? _cornerA;

        // Читает превью-рендерер, пока жезл в руке
        internal Point? CornerA => _cornerA;

        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 28;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.autoReuse = false;
            Item.rare = ItemRarityID.Red;
            Item.value = 0;
        }

        public override bool AltFunctionUse(Player player) => true;

        public override bool? UseItem(Player player)
        {
            if (player.whoAmI != Main.myPlayer)
                return true;

            if (player.altFunctionUse == 2)
            {
                _cornerA = null;
                Main.NewText("Selection cleared. 0/2", Color.Orange);
                return true;
            }

            Point tile = Main.MouseWorld.ToTileCoordinates();

            if (_cornerA == null)
            {
                _cornerA = tile;
                Main.NewText($"Corner A: {tile.X}, {tile.Y}. Drag and click B. 1/2.", Color.LightGreen);
                return true;
            }

            // Сохраняем ровно то, что показывало превью — зажатый по пределу прямоугольник
            Rectangle area = SelectionArea(_cornerA.Value, tile, out bool clamped);
            _cornerA = null;

            string name = "struct_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = StructureIO.Export(area, name);
            string note = clamped ? " (clamped to max)" : "";
            Main.NewText($"Saved {area.Width}x{area.Height}{note}: {path}", Color.LightGreen);
            return true;
        }

        // Прямоугольник от угла A к курсору, зажатый по максимальному размеру постройки.
        // Тянется в сторону курсора, но упершись в предел — перестаёт расти.
        // clamped = хотя бы одна ось упёрлась в максимум.
        internal static Rectangle SelectionArea(Point a, Point cursor, out bool clamped)
        {
            int spanX = cursor.X - a.X; // знак = направление растягивания
            int spanY = cursor.Y - a.Y;
            int w = Math.Abs(spanX) + 1;
            int h = Math.Abs(spanY) + 1;

            clamped = w > StructureIO.MaxWidth || h > StructureIO.MaxHeight;
            w = Math.Min(w, StructureIO.MaxWidth);
            h = Math.Min(h, StructureIO.MaxHeight);

            int left = spanX >= 0 ? a.X : a.X - (w - 1);
            int top = spanY >= 0 ? a.Y : a.Y - (h - 1);
            return new Rectangle(left, top, w, h);
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Wood, 1)
                .AddTile(TileID.WorkBenches)
                .Register();
        }
    }

    // Живое превью выделения в мире: пока держим жезл и задан угол A,
    // рисует сетку-поле, тянущуюся за курсором. Упершись в предел — краснеет.
    public class StructureWandRenderer : ModSystem
    {
        private const int TileSize = 16;
        private const int BorderThickness = 2;

        private static readonly Color OkColor = new(80, 220, 120);
        private static readonly Color LimitColor = new(255, 60, 60);

        public override void PostDrawTiles()
        {
            Player player = Main.LocalPlayer;
            if (player?.HeldItem?.ModItem is not StructureWand wand || wand.CornerA is not Point a)
                return;

            Point cursor = Main.MouseWorld.ToTileCoordinates();
            Rectangle area = StructureWand.SelectionArea(a, cursor, out bool clamped);

            Color color = clamped ? LimitColor : OkColor;
            Texture2D px = TextureAssets.MagicPixel.Value;

            SpriteBatch sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                null, null, null, Main.GameViewMatrix.TransformationMatrix);

            int ox = (int)(area.X * TileSize - Main.screenPosition.X);
            int oy = (int)(area.Y * TileSize - Main.screenPosition.Y);
            int pxW = area.Width * TileSize;
            int pxH = area.Height * TileSize;

            // Заливка-поле (premultiplied: множитель гасит и цвет, и альфу)
            sb.Draw(px, new Rectangle(ox, oy, pxW, pxH), color * 0.12f);

            // Сетка по границам клеток
            Color grid = color * 0.3f;
            for (int gx = 0; gx <= area.Width; gx++)
                sb.Draw(px, new Rectangle(ox + gx * TileSize, oy, 1, pxH), grid);
            for (int gy = 0; gy <= area.Height; gy++)
                sb.Draw(px, new Rectangle(ox, oy + gy * TileSize, pxW, 1), grid);

            // Толстая рамка выделения
            sb.Draw(px, new Rectangle(ox, oy, pxW, BorderThickness), color);
            sb.Draw(px, new Rectangle(ox, oy + pxH - BorderThickness, pxW, BorderThickness), color);
            sb.Draw(px, new Rectangle(ox, oy, BorderThickness, pxH), color);
            sb.Draw(px, new Rectangle(ox + pxW - BorderThickness, oy, BorderThickness, pxH), color);

            // Маркер угла A
            var aPos = new Vector2(a.X * TileSize, a.Y * TileSize) - Main.screenPosition;
            sb.Draw(px, new Rectangle((int)aPos.X, (int)aPos.Y, TileSize, TileSize), color * 0.7f);

            sb.End();
        }
    }
}
