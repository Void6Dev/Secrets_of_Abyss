using Terraria;
using Terraria.ModLoader;
using Terraria.ID;

namespace SoA.Content.Buffs
{
    public class CoM_debaff : ModBuff
    {
        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true; // Дебафф, а не бафф
            Main.buffNoSave[Type] = true; // Бафф не сохраняется при выходе из игры.
        }// Cursed Proj cooldown, Cursed Projectile on cooldown.
    }
}