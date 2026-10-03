// Луч маяка. Один квад от лампы вдоль луча: uv.y = 0..1 вдоль, uv.x = 0..1 поперёк.
// Квад шириной с раструб на дальнем конце, сам конус вырезает шейдер: у лампы он
// сужается до uStartWidth от полной ширины. Форма процедурная, текстура квада не читается.

sampler uImage0 : register(s0); // квад (не используется)
sampler uImage1 : register(s1); // тайловый шум (WaveNoise.png, wrap)

float uTime;
float uOpacity;
float3 uColor;
float uLength;     // длина луча в пикселях: до первой стены или до предела
float uMaxLength;  // предельная дальность: от неё, а не от uLength, считается затухание
float uStartWidth; // ширина конуса у лампы, доля от ширины квада

float4 BeamPS(float2 uv : TEXCOORD0) : COLOR0
{
    float along = uv.y;
    float alongPx = along * uLength;

    // Поперёк: -1..1 внутри конуса, за ним больше 1
    float halfWidth = lerp(uStartWidth, 1.0, along);
    float d = abs(uv.x * 2.0 - 1.0) / halfWidth;

    // Мягкий край конуса и плотная середина
    float cone = saturate(1.0 - d);
    cone = cone * cone * (3.0 - 2.0 * cone);
    float core = exp(-d * d * 9.0);

    // Пыль и водяная взвесь в свете: шум в мировом масштабе, медленно плывёт,
    // чтобы луч не читался ровной заливкой
    float n1 = tex2D(uImage1, float2(uv.x * 0.7 + uTime * 0.02, alongPx / 520.0 - uTime * 0.05)).r;
    float n2 = tex2D(uImage1, float2(uv.x * 1.9 - uTime * 0.03, alongPx / 170.0 + uTime * 0.04)).r;
    float dust = 0.8 + 0.6 * (n1 * 0.6 + n2 * 0.4 - 0.5);

    // Ярко у лампы, к пределу дальности сходит на нет. Считается по расстоянию,
    // а не по доле длины: луч, упёршийся в близкую скалу, у скалы ещё яркий
    float falloff = pow(saturate(1.0 - alongPx / uMaxLength), 1.2);
    float headFade = saturate(alongPx / 20.0);
    float tailFade = saturate((uLength - alongPx) / 48.0);

    float intensity = (cone * 0.55 + core * 0.45) * dust * falloff * headFade * tailFade;

    // Альфа = 1: аддитивный бленд, чёрное не светится
    return float4(uColor * intensity * uOpacity, 1.0);
}

technique Technique1
{
    pass BeamPass
    {
        PixelShader = compile ps_3_0 BeamPS();
    }
}
