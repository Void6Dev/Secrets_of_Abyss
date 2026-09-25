using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace SoA.Common.Config
{
    // Клиентские настройки разработки: сюда попадает всё, что нужно только автору
    // и не должно влиять на игру у других.
    public class SoADevConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        public static SoADevConfig Instance => ModContent.GetInstance<SoADevConfig>();

        [DefaultValue(false)]
        public bool EnableDevMenu { get; set; }
    }
}
