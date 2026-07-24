using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using SoA.Assets.Dusts;
using SoA.Content.Items.Placebles;
using SoA.Content.Projectiles;

namespace SoA.Content.Tiles.Nature
{
    public class Tidesand_tile : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileSolid[Type] = true;
            Main.tileMergeDirt[Type] = true;
            Main.tileBlockLight[Type] = true;
            Main.tileMerge[Type][TileID.Sand] = true;
            Main.tileMerge[TileID.Sand][Type] = true;

            // Падает как песок — своим снарядом, чтобы после падения остаться Tidesand
            TileID.Sets.Falling[Type] = true;
            TileID.Sets.FallingBlockProjectile[Type] = new TileID.Sets.FallingBlockProjectileInfo(
                ModContent.ProjectileType<TidesandBallFalling>());

            DustType = ModContent.DustType<Sparkle>();
            HitSound = SoundID.Dig;

            AddMapEntry(new Color(60, 72, 154), CreateMapEntryName());
            RegisterItemDrop(ModContent.ItemType<Tidesand>());
        }

        public override void NumDust(int i, int j, bool fail, ref int num)
        {
            num = fail ? 1 : 3;
        }
    }
}
