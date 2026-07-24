using Terraria;
using Terraria.ModLoader;

namespace SoA.Content.Buffs
{
    public class CoMDebuff : ModBuff
    {
        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true; // Дебафф, а не бафф
            Main.buffNoSave[Type] = true; // Бафф не сохраняется при выходе из игры.
        }// Cursed Proj cooldown, Cursed Projectile on cooldown.
    }
}