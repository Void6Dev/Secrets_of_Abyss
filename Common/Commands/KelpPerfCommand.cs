using Terraria.ModLoader;
using SoA.Common.Graphics.Kelp;

namespace SoA.Common.Commands
{
    // /kelpperf — раз в 2 секунды писать в чат, сколько мс кадра уходит на подводный лес,
    // сколько квадов и сколько стеблей перестроено за кадр. Повторно — выключить
    public class KelpPerfCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "kelpperf";
        public override string Usage => "/kelpperf";
        public override string Description => "Замер подводного леса: мс на кадр, квады, перестроенные стебли. Повторно — выключить";

        public override void Action(CommandCaller caller, string input, string[] args) => KelpForestRenderer.TogglePerf();
    }
}
