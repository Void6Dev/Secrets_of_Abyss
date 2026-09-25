using Microsoft.Xna.Framework;

namespace SoA.Common.Graphics
{
    // Настройки гейзера. Один в один параметры TideGeyser.fx, только в шейдере они
    // упакованы по float4 (у ps_3_0 всего 224 константных регистра, на россыпи
    // отдельных float шейдер не собирается) — упаковку делает ToXxx() ниже.
    //
    // Единицы: всё процедурное считается в uv квада своего слоя, где 1.0 — вся
    // ширина квада. Поэтому радиусы и разлёты — доли, а не пиксели: слой можно
    // масштабировать под любую ширину струи, и пропорции не поедут.
    public struct TideGeyserSettings
    {
        // Общая энергия эффекта: 1 — как задумано, меньше — вялая струя
        public float Intensity;

        // --- Струя ---
        public float FlowSpeed;
        // -1 — вода идёт вверх (гейзер), +1 — падает сверху вниз (столб в землю)
        public float FlowDirection;
        // Амплитуда искажения спрайта В ПИКСЕЛЯХ. Больше 2 не ставить: силуэт поедет
        public float Distortion;
        public float DistortionScale;
        public float HighlightSpeed;
        public float HighlightStrength;
        public float EdgeTurbulence;
        public float GlowStrength;

        // --- Удар ---
        public float ImpactStrength;
        // Сколько циклов «удар → разлёт → оседание» в секунду
        public float ImpactPulseSpeed;
        public float ImpactCompression;

        // --- Всплеск ---
        public float SplashStrength;
        public float SplashRadius;
        public float SplashHeight;
        public float SplashWidth;

        // --- Капли ---
        public float ParticleAmount;
        public float ParticleSpeed;
        public float ParticleSpread;
        public float ParticleGravity;
        public float ParticleSize;
        public float ParticleLifetime;
        public float ParticleTurbulence;

        // --- Растекание и рябь ---
        public float PuddleSpread;
        public float PuddleSpeed;
        public float PuddleOpacity;
        public float RippleStrength;
        public float RippleSpeed;
        public float RippleBreakup;

        // --- Взвесь ---
        public float MistAmount;
        public float MistSpread;
        public float MistSpeed;
        public float MistNoise;
        public float MistNoiseScale;
        public float MistOpacity;
        public float MistRise;
        public float MistTurbulence;

        public static TideGeyserSettings Default => new()
        {
            Intensity = 1f,

            FlowSpeed = 1f,
            FlowDirection = -1f,
            Distortion = 1.2f,
            DistortionScale = 1f,
            HighlightSpeed = 1.6f,
            HighlightStrength = 0.55f,
            EdgeTurbulence = 1f,
            GlowStrength = 1f,

            ImpactStrength = 1f,
            ImpactPulseSpeed = 2.4f,
            ImpactCompression = 0.35f,

            // Всплеск шире и выше первой прикидки: рядом со струёй в 69 px
            // аккуратная шапка в четверть её ширины просто не читается
            SplashStrength = 1.15f,
            SplashRadius = 0.38f,
            SplashHeight = 0.42f,
            SplashWidth = 0.18f,

            // Капель в проходе всего десять, поэтому все десять всегда живые:
            // плотность добирается вторым слоем (см. TideGeyserFx.MicroSpray)
            ParticleAmount = 1f,
            ParticleSpeed = 0.55f,
            ParticleSpread = 1f,
            ParticleGravity = 1.4f,
            ParticleSize = 1f,
            ParticleLifetime = 0.8f,
            ParticleTurbulence = 1.2f,

            PuddleSpread = 0.42f,
            PuddleSpeed = 1f,
            PuddleOpacity = 0.9f,
            RippleStrength = 0.9f,
            RippleSpeed = 1f,
            RippleBreakup = 0.25f,

            MistAmount = 1.2f,
            MistSpread = 0.38f,
            MistSpeed = 0.35f,
            MistNoise = 1f,
            MistNoiseScale = 2.2f,
            MistOpacity = 0.8f,
            MistRise = 1f,
            MistTurbulence = 0.9f,
        };

        public static TideGeyserSettings For(GeyserStyle style)
        {
            TideGeyserSettings settings = Default;

            switch (style)
            {
                case GeyserStyle.Weak:
                    settings.Intensity = 0.55f;
                    settings.SplashStrength = 0.6f;
                    settings.ImpactStrength = 0.6f;
                    settings.ParticleAmount = 0.5f;
                    settings.MistAmount = 0.6f;
                    settings.PuddleSpread = 0.3f;
                    break;

                // Горячий источник: пара больше, поднимается выше, вода спокойнее
                case GeyserStyle.Hot:
                    settings.MistAmount = 1.5f;
                    settings.MistRise = 1.4f;
                    settings.MistOpacity = 0.7f;
                    settings.MistSpeed = 0.5f;
                    settings.EdgeTurbulence = 0.8f;
                    break;
            }

            return settings;
        }

        // --- Упаковка в вектора шейдера ---

        public Vector4 Flow => new(FlowSpeed, FlowDirection, Distortion, DistortionScale);

        public Vector4 Shine => new(HighlightSpeed, HighlightStrength, EdgeTurbulence, GlowStrength);

        public Vector4 Impact(float ground)
            => new(ImpactStrength, ImpactPulseSpeed, ImpactCompression, ground);

        public Vector4 Splash => new(SplashStrength, SplashRadius, SplashHeight, SplashWidth);

        public Vector4 Particle => new(ParticleAmount, ParticleSpeed, ParticleSpread, ParticleGravity);

        public Vector4 ParticleShape => new(ParticleSize, ParticleLifetime, ParticleTurbulence, 0f);

        public Vector4 Puddle => new(PuddleSpread, PuddleSpeed, PuddleOpacity, RippleStrength);

        public Vector4 Ripple => new(RippleSpeed, RippleBreakup, 0f, 0f);

        public Vector4 Mist => new(MistAmount, MistSpread, MistSpeed, MistNoise);

        public Vector4 MistShape => new(MistNoiseScale, MistOpacity, MistRise, MistTurbulence);
    }
}
