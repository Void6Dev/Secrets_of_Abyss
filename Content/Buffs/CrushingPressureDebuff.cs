using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Buffs
{
    // Давление глубины. Вешается и снимается каждый тик из TidePressurePlayer:
    // сам по себе ничего не делает, это индикатор для игрока — иконка и подсказка,
    // почему тают жизни. Числа эффекта живут в TidePressurePlayer, чтобы ступени
    // не пришлось править в двух местах.
    public class CrushingPressureDebuff : ModBuff
    {
        public override string Texture => "Terraria/Images/Buff_" + BuffID.Weak;

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = true;   // держится, пока игрок на глубине
        }
    }
}
