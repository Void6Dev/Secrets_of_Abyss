using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Common.Systems
{
    // /soaseal <ступень> — вернуть печать в «готовое» состояние, чтобы проверить сцену снятия.
    // Только одиночная игра: в мультиплеере это был бы чит, доступный всем
    public class TideSealTestCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "soaseal";
        public override string Usage => "/soaseal <1-4>";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                caller.Reply("Single player only.");
                return;
            }
            if (args.Length != 1 || !int.TryParse(args[0], out int step) || step < 1 || step > 4)
            {
                caller.Reply(Usage);
                return;
            }

            TideSealSystem.ResetForTesting(step);
            caller.Reply($"Seal {step} reset. Ready: {TideSealSystem.IsReady(step)}");
        }
    }
}
