using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Tiles.Nature
{
    // Приливный камень: скальное основание Прилива Теней.
    // Текстура — программная перекраска Tidesand_tile (можно заменить своим артом)
    public class Tidestone_tile : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileSolid[Type] = true;
            Main.tileMergeDirt[Type] = true;
            Main.tileBlockLight[Type] = true;
            Main.tileMerge[Type][TileID.Stone] = true;
            Main.tileMerge[TileID.Stone][Type] = true;
            Main.tileMerge[Type][ModContent.TileType<Tidesand_tile>()] = true;
            Main.tileMerge[ModContent.TileType<Tidesand_tile>()][Type] = true;

            MineResist = 1.2f;
            DustType = DustID.Stone;
            HitSound = SoundID.Tink;

            // Контрастный серо-стальной цвет карты: скалы должны отличаться от Tidesand
            AddMapEntry(new Color(108, 116, 134), CreateMapEntryName());
            RegisterItemDrop(ModContent.ItemType<Tidestone>());
        }

        public override void NumDust(int i, int j, bool fail, ref int num)
        {
            num = fail ? 1 : 3;
        }
    }
}
