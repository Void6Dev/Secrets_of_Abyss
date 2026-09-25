using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Common.Graphics.SandFormation
{
    // Параметры одной сборки предмета из песка. Система тикает фиксированным шагом
    // 1/60 c, поэтому все силы читаются как «пиксели в секунду» — значения переносимы
    // между предметами разного размера, а ForTexture() докручивает то, что зависит
    // от габаритов спрайта (число зёрен, радиус облака, радиус захвата).
    public struct SandFormationSettings
    {
        public int ParticleCount;

        // Предельная скорость притянутого зерна и то, как быстро оно её набирает
        public float AttractionStrength;
        public float AttractionAcceleration;

        // Пружина к точке назначения: доводит зерно на последних пикселях,
        // основную дорогу делает AttractionStrength
        public float SpringStrength;

        // Затухание скорости за тик: у собирающегося зерна жёстче, чем у россыпи
        public float Damping;
        public float LooseDamping;

        public float MaxSpeed;

        // Сила «шумовой» болтанки россыпи (0..1)
        public float Randomness;

        // Базовый размер зерна в пикселях спрайта (1..3)
        public float ParticleSize;

        public float GlowIntensity;

        // Радиус, внутри которого притяжение считается близким и берёт зерно жёстче
        public float FormationRadius;

        // Радиус облака россыпи: откуда зёрна стартуют
        public float SpreadRadius;

        // Боковой снос траектории (px) и скорость его вращения (рад/с)
        public float SwirlStrength;
        public float SwirlSpeed;

        public float Gravity;
        public float WindStrength;

        // Доли зёрен, которые садятся не в силуэт: кайма по краю и орбита вокруг
        public float EdgeShare;
        public float OrbitShare;

        // Тусклый тёплый ореол под россыпью. Пиксель-арт от него не страдает —
        // он живёт под зёрнами и почти не виден до фазы сборки
        public bool BackgroundGlow;

        public static SandFormationSettings Default => new()
        {
            ParticleCount = 128,
            AttractionStrength = 220f,
            AttractionAcceleration = 7f,
            SpringStrength = 6f,
            Damping = 0.93f,
            LooseDamping = 0.985f,
            MaxSpeed = 420f,
            Randomness = 0.65f,
            ParticleSize = 1.5f,
            GlowIntensity = 1f,
            FormationRadius = 32f,
            SpreadRadius = 48f,
            SwirlStrength = 18f,
            SwirlSpeed = 2.2f,
            Gravity = 26f,
            WindStrength = 22f,
            EdgeShare = 0.18f,
            OrbitShare = 0.10f,
            BackgroundGlow = true,
        };

        // Дефолты под конкретный спрайт: мелкому предмету 48 зёрен хватает,
        // крупному нужно ~200, иначе силуэт не читается
        public static SandFormationSettings ForTexture(Texture2D texture)
        {
            SandFormationSettings settings = Default;
            if (texture == null)
                return settings;

            float longSide = MathHelper.Max(texture.Width, texture.Height);

            settings.ParticleCount = (int)MathHelper.Clamp(texture.Width * texture.Height * 0.055f, 48f, 256f);
            settings.FormationRadius = MathHelper.Clamp(longSide * 0.55f, 16f, 64f);
            settings.SpreadRadius = MathHelper.Max(longSide * 1.15f, 28f);
            settings.SwirlStrength = MathHelper.Clamp(longSide * 0.35f, 10f, 26f);

            return settings;
        }
    }
}
