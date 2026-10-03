using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace SoA.Content.Tiles.Nature
{
    // Печатный камень: кладка древних по стенке чаши со стороны суши, ниже первой
    // печати. Держит биом от подкопа из пещер джунглей — без него любую зону
    // за печатью можно открыть сбоку, и печати превращаются в декорацию.
    // Ни одна ванильная кирка его не берёт (у лунных 225), взрывы тоже.
    // Текстура — ванильный обсидиан (placeholder до собственного арта)
    public class Sealstone_tile : ModTile
    {
        private const int RequiredPickPower = 230;

        public override string Texture => "Terraria/Images/Tiles_" + TileID.Obsidian;

        public override void SetStaticDefaults()
        {
            Main.tileSolid[Type] = true;
            Main.tileBlockLight[Type] = true;
            Main.tileMerge[Type][ModContent.TileType<Tidestone_tile>()] = true;
            Main.tileMerge[ModContent.TileType<Tidestone_tile>()][Type] = true;
            Main.tileMerge[Type][TileID.Stone] = true;
            Main.tileMerge[TileID.Stone][Type] = true;

            MinPick = RequiredPickPower;
            MineResist = 4f;
            DustType = DustID.Obsidian;
            HitSound = SoundID.Tink;

            AddMapEntry(new Color(52, 46, 74), CreateMapEntryName());
        }

        public override bool CanExplode(int i, int j) => false;
    }
}
