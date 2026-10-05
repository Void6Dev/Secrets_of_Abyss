using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace SoA.Common.Commands
{
    // /soa — список команд мода с описаниями. Встроенная /help tModLoader выводит команды
    // всех модов вперемешку; эта — только наши. Список собирается сам: новая ModCommand
    // с Description попадает сюда без правок
    public class SoaHelpCommand : ModCommand
    {
        private static readonly Color Title = new(150, 220, 255);
        private static readonly Color Line = new(200, 210, 230);

        public override CommandType Type => CommandType.Chat;
        public override string Command => "soa";
        public override string Usage => "/soa";
        public override string Description => "Список команд Secrets of Abyss";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            caller.Reply("Команды Secrets of Abyss:", Title);
            foreach (ModCommand command in Mod.GetContent<ModCommand>())
            {
                if (command == this)
                    continue;
                string usage = string.IsNullOrEmpty(command.Usage) ? "/" + command.Command : command.Usage;
                caller.Reply(string.IsNullOrEmpty(command.Description) ? usage : $"{usage} — {command.Description}", Line);
            }
        }
    }
}
