using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace SoA.Content.Tiles.Nature
{
    // Тёмный коралл: амбиентная поросль на дне Прилива Теней, срезается ударом.
    // Лист 54x36: три варианта 1x2 по горизонтали
    public class ShadowCoral_tile : ModTile
    {
        private const int StyleCount = 3;
        private static readonly Vector3 LightColor = new(0.22f, 0.08f, 0.34f);

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileCut[Type] = true;
            Main.tileNoFail[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style1x2);
            TileObjectData.newTile.Origin = new Point16(0, 1);
            TileObjectData.newTile.AnchorBottom = new AnchorData(AnchorType.SolidTile, 1, 0);
            TileObjectData.newTile.AnchorValidTiles = new[]
            {
                ModContent.TileType<Tidesand_tile>(),
                ModContent.TileType<Tidestone_tile>()
            };
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaDeath = true;
            TileObjectData.newTile.RandomStyleRange = StyleCount;
            TileObjectData.newTile.StyleHorizontal = true;
            TileObjectData.addTile(Type);

            DustType = DustID.PurpleTorch;
            AddMapEntry(new Color(96, 54, 148), CreateMapEntryName());
        }

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            // Едва заметное свечение, чтобы дно не было чёрной ямой
            float pulse = 0.85f + 0.15f * (float)Math.Sin(Main.GameUpdateCount * 0.03f + i * 0.9f);
            r = LightColor.X * pulse;
            g = LightColor.Y * pulse;
            b = LightColor.Z * pulse;
        }
    }
}
