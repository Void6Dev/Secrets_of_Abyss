using Microsoft.Xna.Framework;

namespace SoA.Common.Graphics.SandFormation
{
    // Детерминированный «рандом» сборки: чистая хеш-функция от (seed, зерно, канал).
    // Main.rand здесь использовать нельзя — на разных клиентах он разойдётся, и одна
    // и та же сборка будет выглядеть по-разному; а так достаточно синхронизировать seed.
    // channel — номер параметра зерна (угол, задержка, размер...), чтобы одно и то же
    // зерно получало независимые значения.
    public static class SandFormationRandom
    {
        public static float Value(int seed, int index, int channel)
        {
            unchecked
            {
                uint hash = (uint)seed * 747796405u
                    + (uint)index * 2891336453u
                    + (uint)channel * 668265263u
                    + 1442695040u;

                hash ^= hash >> 15;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                hash *= 3266489917u;
                hash ^= hash >> 16;

                return (hash & 0xFFFFFFu) / 16777216f;
            }
        }

        public static float Range(int seed, int index, int channel, float min, float max)
            => MathHelper.Lerp(min, max, Value(seed, index, channel));

        // -1..1
        public static float Signed(int seed, int index, int channel)
            => Value(seed, index, channel) * 2f - 1f;

        public static int Index(int seed, int index, int channel, int count)
        {
            if (count <= 0)
                return 0;
            int picked = (int)(Value(seed, index, channel) * count);
            return picked >= count ? count - 1 : picked;
        }

        // +1 или -1: сторона закрутки траектории
        public static float Sign(int seed, int index, int channel)
            => Value(seed, index, channel) < 0.5f ? -1f : 1f;
    }
}
