using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Walls
{
    // Растрескавшийся вариант приливного камня. Работа у него декоративная:
    // им набраны рукотворные силуэты — коралловая арка, кладка руин, —
    // и он должен читаться как обветшавший, а не как свежая порода.
    public class Tidestone_wall_cracked : ModWall
    {
        public override string Texture => "Terraria/Images/Wall_" + WallID.BlueDungeonSlabUnsafe;

        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = false;
            DustType = DustID.Stone;
            HitSound = SoundID.Tink;

            AddMapEntry(new Color(62, 72, 92));
        }
    }
}
