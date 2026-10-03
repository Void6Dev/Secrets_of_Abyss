using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Players;

namespace SoA.Content.Buffs
{
    // Давление глубины. Вешается и снимается каждый тик из TidePressurePlayer:
    // сам по себе ничего не делает, это индикатор для игрока — иконка и подсказка,
    // почему уходит воздух и тают жизни. Числа эффекта живут в TidePressurePlayer.
    public class CrushingPressureDebuff : ModBuff
    {
        public override string Texture => "Terraria/Images/Buff_" + BuffID.Weak;

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = true;   // держится, пока игрок на глубине
        }

        // В подсказке — текущая сила давления, чтобы было видно, что дают жемчужины
        public override void ModifyBuffText(ref string buffName, ref string tip, ref int rare)
        {
            float pressure = Main.LocalPlayer.GetModPlayer<TidePressurePlayer>().Pressure;
            tip = string.Format(tip, pressure.ToString("0.00"));
        }
    }
}
