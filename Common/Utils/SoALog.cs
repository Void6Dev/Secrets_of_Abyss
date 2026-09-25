using Terraria.ModLoader;

namespace SoA.Common.Utils
{
    // Диагностика генерации уходит в client.log / server.log рядом с логами tModLoader.
    // Своя обёртка, потому что достать логгер мода из генпасса иначе — три строки на месте
    public static class SoALog
    {
        public static void Info(string tag, string message)
            => ModContent.GetInstance<global::SoA.SoA>()?.Logger.Info("[" + tag + "] " + message);
    }
}
