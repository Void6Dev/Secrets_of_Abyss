using Terraria;
using Terraria.ModLoader;

namespace SoA.Content.Buffs
{
    // Лавовая метка Когтей: только маркер «враг раскалён». Продлевается каждым ударом;
    // когда истечёт, метку подрывает LavaExplosionGlobalNPC снарядом LavaClawBurst
    public class LavaExplosionDebuff : ModBuff
    {
        // Спрайт лежит рядом с кодом в папке контента, а не по пути пространства имён
        public override string Texture => "SoA/Content/Buffs/LavaExplosion/LavaExplosionDebuff";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
        }
    }
}
