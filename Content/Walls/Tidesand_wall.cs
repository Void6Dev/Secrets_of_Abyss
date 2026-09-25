using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Walls
{
    // Задник песчаных наносов глубины. Ставится там, где порода в клетке — Tidesand,
    // чтобы стена за спиной совпадала с тем, во что упирается кирка.
    public class Tidesand_wall : ModWall
    {
        public override string Texture => "Terraria/Images/Wall_" + WallID.HardenedSand;

        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = false;
            DustType = DustID.Sand;
            HitSound = SoundID.Dig;

            AddMapEntry(new Color(74, 76, 96));
        }
    }
}
