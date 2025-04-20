using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Terraria.ObjectData;
using SoA.Content.Dusts;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Tiles.Nature
{
    public class Tidesand_tile : ModTile
    {
        public override void SetStaticDefaults() {
            Main.tileSolid[Type] = true;
            Main.tileMergeDirt[Type] = true;
            Main.tileBlockLight[Type] = true;
            TileID.Sets.Falling[Type] = true;

            DustType = ModContent.DustType<Sparkle>();
            AddMapEntry(new Color(250, 100, 20));
            RegisterItemDrop(ModContent.ItemType<Tidesand>());  
        }
        public override void NumDust(int i, int j, bool fail, ref int num) {
            num = fail ? 1 : 3;
        }
	
        public override void RandomUpdate(int i, int j)
        {
            WorldGen.SpreadGrass(i, j, Type, Type); // не обязательно
            WorldGen.TileFrame(i, j); // обновляем фрейм — заставит проверить "можно ли падать"
        }
    }
}