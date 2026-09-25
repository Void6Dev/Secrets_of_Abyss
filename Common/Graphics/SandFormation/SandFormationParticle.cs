using Microsoft.Xna.Framework;

namespace SoA.Common.Graphics.SandFormation
{
    public enum SandFormationGrainKind
    {
        // Садится точно в силуэт — из этих зёрен и складывается предмет
        Core,

        // Остаётся чуть снаружи: пыльная неровная кайма по контуру
        Edge,

        // Кружит вокруг сборки и падает внутрь только на последних процентах
        Orbit,
    }

    // Одно зерно. Struct, живёт в общем массиве эффекта: за сборку не выделяется
    // ни одного объекта, GC в кадре не просыпается.
    public struct SandFormationParticle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 StartPosition;
        public Vector2 TargetPosition;

        // Три предыдущих положения — короткий хвост из отдельных пикселей
        public Vector2 Trail0;
        public Vector2 Trail1;
        public Vector2 Trail2;

        public float Size;
        public float Brightness;

        // Порог пробуждения: зерно трогается с места, когда прогресс перевалит за него
        public float Delay;

        public float Life;

        public int Seed;

        public Color Color;

        // Цвет спрайта в точке назначения: к концу сборки зерно перекрашивается в него
        public Color TargetColor;

        public bool IsGathering;

        public SandFormationGrainKind Kind;

        // Сторона и фаза бокового сноса — от них траектория получается дугой
        public float SwirlSign;
        public float SwirlPhase;

        // Орбита: радиус, угловая скорость и фаза
        public float OrbitRadius;
        public float OrbitSpeed;
        public float OrbitPhase;

        // Фазы шумовой болтанки россыпи
        public float NoisePhaseX;
        public float NoisePhaseY;
    }

    // Палитра песка. Большинство зёрен держится в середине шкалы, ярких единицы —
    // иначе россыпь превращается в равномерное свечение и перестаёт читаться.
    public static class SandFormationPalette
    {
        public static readonly Color[] Ramp =
        {
            new(0x65, 0x45, 0x28), // тёмный песок — хвосты
            new(0x92, 0x5F, 0x2C),
            new(0xC4, 0x7A, 0x2E),
            new(0xE6, 0x9A, 0x35),
            new(0xF5, 0xBC, 0x52), // яркий
            new(0xFF, 0xD8, 0x75), // светлый
            new(0xFF, 0xF0, 0xB0), // блик
        };

        public const int TrailIndex = 0;

        // roll 0..1 → индекс: 70% обычный песок, 20% яркий, 8% светлый, 2% блики
        public static int PickIndex(float roll, float spread)
        {
            if (roll < 0.70f)
                return 1 + (int)(spread * 3f) % 3;
            if (roll < 0.90f)
                return 4;
            if (roll < 0.98f)
                return 5;
            return 6;
        }

        public static Color Pick(float roll, float spread) => Ramp[PickIndex(roll, spread)];
    }
}
