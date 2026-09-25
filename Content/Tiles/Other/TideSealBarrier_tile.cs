using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Tiles.Other
{
    // Мембрана печати: ею затыкается проход между зонами, пока ступень не пройдена.
    // Не ломается ничем — ни киркой, ни взрывчаткой, ни жидкостью: единственный способ
    // её убрать — выполнить условие ступени, тогда TideSealSystem снимет её сам.
    //
    // Свет пропускает намеренно: глухая пробка в шахте читалась бы как тупик,
    // а игрок должен видеть, что ход есть, просто заперт.
    public class TideSealBarrier_tile : ModTile
    {
        // Временная подмена: своего спрайта нет. Стекло выбрано за читаемость —
        // сквозь пробку видно продолжение шахты
        public override string Texture => "Terraria/Images/Tiles_" + TileID.Glass;

        public override void SetStaticDefaults()
        {
            Main.tileSolid[Type] = true;
            Main.tileBlockLight[Type] = false;
            Main.tileNoSunLight[Type] = false;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = false;
            Main.tileNoFail[Type] = false;

            TileID.Sets.DisableSmartCursor[Type] = true;

            DustType = DustID.Glass;
            HitSound = SoundID.Item27;

            AddMapEntry(new Color(96, 178, 210), CreateMapEntryName());
        }

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            blockDamaged = false;
            return false;
        }

        public override bool CanExplode(int i, int j) => false;

        public override bool Slope(int i, int j) => false;
    }
}
