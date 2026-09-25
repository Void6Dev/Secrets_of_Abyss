using System;

namespace SoA.Common.Utils
{
    // Детерминированный шум для процедурной генерации Прилива Теней.
    // Один сид мира — один и тот же рельеф; разные миры получают разные фазы,
    // поэтому одинаковых очертаний между мирами не бывает.
    public static class TideNoise
    {
        // Не 2.0: целая кратность выстраивает октавы в сетку и даёт заметные повторы
        private const float Lacunarity = 2.17f;
        private const float Gain = 0.5f;

        private static int _seed = 1;

        public static void Reseed(int seed) => _seed = seed == 0 ? 1 : seed;

        private static float Hash(int x, int y, int channel)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + channel * 1274126177 + _seed * 1103515245;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFF) / (float)0x7FFFFFF;
            }
        }

        private static float Fade(float t) => t * t * (3f - 2f * t);

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);

        public static float SmoothStep(float edge0, float edge1, float value)
        {
            if (edge1 - edge0 == 0f)
                return value < edge0 ? 0f : 1f;
            return Fade(Clamp01((value - edge0) / (edge1 - edge0)));
        }

        // Мягкий предел: значение асимптотически подходит к границе, но никогда
        // не ложится на неё. Жёсткий Math.Min давал длинные идеально прямые участки
        // там, где профиль упирался в кламп — самый заметный дефект рельефа
        public static float SoftMin(float value, float limit, float softness)
        {
            float knee = limit - softness;
            if (value <= knee)
                return value;
            return knee + softness * (1f - MathF.Exp(-(value - knee) / softness));
        }

        public static float SoftMax(float value, float limit, float softness)
            => -SoftMin(-value, -limit, softness);

        public static float Value(float x, int channel)
        {
            int ix = (int)MathF.Floor(x);
            return Lerp(Hash(ix, 0, channel), Hash(ix + 1, 0, channel), Fade(x - ix));
        }

        public static float Value(float x, float y, int channel)
        {
            int ix = (int)MathF.Floor(x);
            int iy = (int)MathF.Floor(y);
            float fx = Fade(x - ix);
            float fy = Fade(y - iy);
            float top = Lerp(Hash(ix, iy, channel), Hash(ix + 1, iy, channel), fx);
            float bottom = Lerp(Hash(ix, iy + 1, channel), Hash(ix + 1, iy + 1, channel), fx);
            return Lerp(top, bottom, fy);
        }

        public static float Fbm(float x, int channel, int octaves = 4)
        {
            float sum = 0f, amplitude = 1f, frequency = 1f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * frequency, channel + i * 31) * amplitude;
                total += amplitude;
                amplitude *= Gain;
                frequency *= Lacunarity;
            }
            return sum / total;
        }

        public static float Fbm(float x, float y, int channel, int octaves = 4)
        {
            float sum = 0f, amplitude = 1f, frequency = 1f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * frequency, y * frequency, channel + i * 31) * amplitude;
                total += amplitude;
                amplitude *= Gain;
                frequency *= Lacunarity;
            }
            return sum / total;
        }

        // Гребневой шум: острые хребты и щели вместо плавных холмов.
        // Основа для каньонов, разломов и рваных стен траншеи
        public static float Ridged(float x, int channel, int octaves = 3)
            => 1f - MathF.Abs(Fbm(x, channel, octaves) * 2f - 1f);

        // Искажение области: координата смещается вторым шумом, чтобы формы
        // не выстраивались по вертикалям и не читались как «сгенерировано формулой»
        public static float WarpedFbm(float x, int channel, float warpStrength, int octaves = 4)
            => Fbm(x + (Fbm(x * 0.31f, channel + 977, 2) - 0.5f) * warpStrength, channel, octaves);

        // Симметричный вариант в диапазоне [-1; 1] — удобно для смещений
        public static float Signed(float x, int channel, int octaves = 4)
            => Fbm(x, channel, octaves) * 2f - 1f;
    }
}
