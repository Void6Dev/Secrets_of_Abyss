using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace SoA.Common.Config
{
    // Клиентские настройки игрока: у каждого свои, на других игроков не влияют
    public class SoAClientConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        public static SoAClientConfig Instance => ModContent.GetInstance<SoAClientConfig>();

        // Все толчки камеры мода идут через ScreenShake.Punch и смотрят на этот флаг
        [DefaultValue(true)]
        public bool ScreenShake { get; set; }
    }
}
