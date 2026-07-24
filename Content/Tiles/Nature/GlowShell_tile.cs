using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace SoA.Content.Tiles.Nature
{
    // Светящаяся ракушка: живой огонёк на дне. Лист 54x18: три варианта 1x1
    public class GlowShell_tile : ModTile
    {
        private const int StyleCount = 3;
        private static readonly Vector3 LightColor = new(0.1f, 0.5f, 0.6f);

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileCut[Type] = true;
            Main.tileNoFail[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
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

            DustType = DustID.BlueTorch;
            AddMapEntry(new Color(60, 180, 200), CreateMapEntryName());
        }

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            // Медленное дыхание света, несинхронное между ракушками
            float pulse = 0.7f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.02f + i * 1.7f + j * 0.5f);
            r = LightColor.X * pulse;
            g = LightColor.Y * pulse;
            b = LightColor.Z * pulse;
        }
    }
}
