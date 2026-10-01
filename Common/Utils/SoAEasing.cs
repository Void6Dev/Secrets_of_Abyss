using System;

namespace SoA.Common.Utils
{
    // Кривые плавности: t 0..1 → 0..1. In — медленный старт, Out — медленный финиш
    public static class SoAEasing
    {
        public static float QuadIn(float t) => t * t;
        public static float QuadOut(float t) => 1f - (1f - t) * (1f - t);
        public static float CubicIn(float t) => t * t * t;
        public static float CircIn(float t) => 1f - (float)Math.Sqrt(1f - t * t);
        public static float CircOut(float t) => (float)Math.Sqrt(1f - (t - 1f) * (t - 1f));

        // Проскакивает за 1 и возвращается: «выпрыгивание» предмета
        public static float BackOut(float t, float overshoot = 1.7f)
        {
            float u = t - 1f;
            return 1f + u * u * ((overshoot + 1f) * u + overshoot);
        }
    }
}
