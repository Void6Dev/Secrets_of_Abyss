using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Walls
{
    // Природная стена глубин: задник породы в зонах 3-5 Прилива Теней.
    // Небезопасная — жильём не считается и врагов не глушит: Main.wallHouse
    // у модовых стен по умолчанию false, отдельно выключать не нужно.
    public class Tidestone_wall : ModWall
    {
        // Временная подмена ванильной стеной. Снимается одной строкой,
        // как только появится Content/Walls/Tidestone_wall.png (лист 468x252)
        public override string Texture => "Terraria/Images/Wall_" + WallID.Stone;

        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = false;
            DustType = DustID.Stone;
            HitSound = SoundID.Tink;

            // Темнее и синее ванильного камня: на карте зоны 3-5 должны
            // отличаться от обычных пещер, иначе биом на ней теряется
            AddMapEntry(new Color(46, 54, 74));
        }
    }
}
