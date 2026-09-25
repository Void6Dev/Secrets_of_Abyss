using Terraria.GameInput;
using Terraria.ModLoader;
using SoA.Common.UI;

namespace SoA.Common.Players
{
    public class DevMenuPlayer : ModPlayer
    {
        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            if (DevMenu.ToggleKeybind?.JustPressed == true)
                DevMenu.Toggle();
        }
    }
}
