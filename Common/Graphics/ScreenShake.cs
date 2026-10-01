using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.CameraModifiers;
using SoA.Common.Config;

namespace SoA.Common.Graphics
{
    // Единственный вход для тряски экрана в моде: игрок может выключить её в настройках
    // (SoAClientConfig.ScreenShake). Новую тряску добавлять только через Punch
    public static class ScreenShake
    {
        public static void Punch(Vector2 startPosition, Vector2 direction, float strength,
            float vibrationCyclesPerSecond, int frames, float distanceFalloff = -1f, string uniqueIdentity = null)
        {
            if (Main.dedServ || !SoAClientConfig.Instance.ScreenShake)
                return;

            Main.instance.CameraModifiers.Add(new PunchCameraModifier(startPosition, direction, strength,
                vibrationCyclesPerSecond, frames, distanceFalloff, uniqueIdentity));
        }
    }
}
