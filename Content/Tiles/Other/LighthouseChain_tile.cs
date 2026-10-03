using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Common.Players;
using SoA.Common.Systems;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Tiles.Other
{
    // Маячная цепь: висит под лампой (или под полом её комнаты) и поворачивает луч.
    // Наводишь вблизи — курсор цепи; зажал ПКМ — луч тянется за мышью, пока держишь.
    // Вешается сверху на блок или на другое звено, как гирлянда.
    // Рисуется не тайлами, а целиком от верхнего звена по физике
    // (LighthouseChainPhysics): держишь — цепь тянется за курсором и раскачивается.
    // Текстура — ванильная цепь (placeholder до собственного арта)
    public class LighthouseChain_tile : ModTile
    {
        // Насколько выше верхнего звена ищется лампа: цепь может висеть
        // под полом лампового помещения, а не прямо под лампой
        private const int LampSearchHeight = 8;
        private const int LampSearchHalfWidth = 2;
        private const int MaxLinks = 64;

        public override string Texture => "Terraria/Images/Tiles_" + TileID.Chain;

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.DisableSmartCursor[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
            TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
            TileObjectData.newTile.AnchorTop = new AnchorData(
                AnchorType.SolidTile | AnchorType.SolidBottom | AnchorType.AlternateTile, 1, 0);
            TileObjectData.newTile.AnchorAlternateTiles = new[] { (int)Type };
            TileObjectData.newTile.LavaDeath = true;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.addTile(Type);

            DustType = DustID.Iron;
            HitSound = SoundID.Tink;
            AddMapEntry(new Color(130, 130, 140), CreateMapEntryName());
        }

        public override void MouseOver(int i, int j)
        {
            if (!TryFindLamp(i, j, out _))
                return;

            LighthouseChainPlayer.ShowGrabCursor(Main.LocalPlayer);
        }

        public override bool RightClick(int i, int j)
        {
            if (!TryFindLamp(i, j, out LighthouseLampEntity lamp))
                return false;

            Main.LocalPlayer.GetModPlayer<LighthouseChainPlayer>().Grab(lamp, new Point(i, j), TopLinkY(i, j));
            SoundEngine.PlaySound(SoundID.Unlock with { Volume = 0.5f, Pitch = -0.4f }, new Vector2(i, j) * 16f);
            return true;
        }

        // Ищем лампу над верхним звеном в узком окне
        public static bool TryFindLamp(int i, int j, out LighthouseLampEntity lamp)
        {
            lamp = null;
            int topY = TopLinkY(i, j);

            for (int y = topY - 1; y >= topY - LampSearchHeight && y > 1; y--)
            {
                for (int x = i - LampSearchHalfWidth; x <= i + LampSearchHalfWidth; x++)
                {
                    if (WorldGen.InWorld(x, y) && LighthouseLampEntity.TryGetAt(x, y, out lamp))
                        return true;
                }
            }
            return false;
        }

        private static bool IsLink(int i, int j)
        {
            Tile tile = Main.tile[i, j];
            return tile.HasTile && tile.TileType == ModContent.TileType<LighthouseChain_tile>();
        }

        public static int TopLinkY(int i, int j)
        {
            int topY = j;
            while (topY > 1 && IsLink(i, topY - 1))
                topY--;
            return topY;
        }

        private static int LinkCount(int i, int topY)
        {
            int count = 0;
            while (count < MaxLinks && topY + count < Main.maxTilesY - 1 && IsLink(i, topY + count))
                count++;
            return count;
        }

        #region Отрисовка

        // Тайлы цепи сами не рисуются: кэш тайлов обновляется раз в несколько кадров,
        // а качающейся цепи нужен каждый кадр. Точку регистрирует только верхнее звено —
        // оно рисует всю цепь
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            if (!IsLink(i, j - 1))
                Main.instance.TilesRenderer.AddSpecialPoint(i, j, TileDrawing.TileCounterType.CustomNonSolid);
            return false;
        }

        public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
        {
            if (!IsLink(i, j) || IsLink(i, j - 1))
                return;

            int links = LinkCount(i, j);
            LighthouseChainPhysics.Rope rope = LighthouseChainPhysics.Get(i, j, links);

            Texture2D texture = TextureAssets.Tile[Type].Value;
            var frame = new Rectangle(0, 0, 16, 16);
            var origin = new Vector2(8f, 8f);

            for (int k = 0; k < links; k++)
            {
                Vector2 from = rope.Points[k];
                Vector2 to = rope.Points[k + 1];
                Vector2 middle = (from + to) / 2f;
                float rotation = (to - from).ToRotation() - MathHelper.PiOver2;

                Point lightAt = middle.ToTileCoordinates();
                Color light = Lighting.GetColor(lightAt.X, lightAt.Y);
                spriteBatch.Draw(texture, middle - Main.screenPosition, frame, light, rotation, origin, 1f,
                    SpriteEffects.None, 0f);
            }
        }

        #endregion
    }
}
