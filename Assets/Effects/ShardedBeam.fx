// Полностью процедурный шейдер луча Sharded Spear.
// Луч рисуется ОДНИМ растянутым квадом: uv.y = 0..1 вдоль луча, uv.x = 0..1 поперёк.
// Форма (ядро/тело/свечение) считается математикой и не зависит от текстуры квада.

sampler uImage0 : register(s0); // текстура квада (не используется, форма процедурная)
sampler uImage1 : register(s1); // тайловый шум (WaveNoise.png, wrap)

float uTime;
float uOpacity;
float uBlend;  // 0 = ледяной синий, 1 = фиолетовый (вода)
float uLength; // текущая длина луча в пикселях
float uFade;   // 1 = луч жив, к 0 при погасании

float4 BeamPS(float2 uv : TEXCOORD0) : COLOR0
{
    float alongPx = uv.y * uLength; // расстояние от начала луча, px

    // Изгиб луча: две волны разной частоты, гасятся у наконечника
    float wobble = sin(alongPx * 0.045 - uTime * 8.0) * 0.10
                 + sin(alongPx * 0.012 + uTime * 3.0) * 0.06;
    wobble *= saturate(alongPx / 150.0) * uFade;

    float across = uv.x * 2.0 - 1.0 + wobble; // -1..1 поперёк квада

    // Шум в мировом масштабе (не растягивается с длиной луча)
    float n1 = tex2D(uImage1, float2(uv.x * 0.5 + 0.13, alongPx / 620.0 - uTime * 0.65)).r;
    float n2 = tex2D(uImage1, float2(uv.x * 1.1 + 0.57, alongPx / 240.0 - uTime * 1.70)).r;
    float noise = saturate(n1 * 0.62 + n2 * 0.55);

    // Гауссовы профили: ядро (с хроматическим сдвигом R/B), тело, внешнее свечение
    float d  = abs(across);
    float dR = abs(across + 0.05);
    float dB = abs(across - 0.05);

    float coreW = 0.11 + 0.03 * (noise - 0.5); // ядро "дышит" от шума
    float core  = exp(-d  * d  / (coreW * coreW));
    float coreR = exp(-dR * dR / (coreW * coreW));
    float coreB = exp(-dB * dB / (coreW * coreW));
    float body  = exp(-d * d / (0.28 * 0.28)) * (0.65 + 0.70 * noise);
    float glow  = exp(-d * d / (0.75 * 0.75)) * 0.30;

    float3 coreCol = lerp(float3(1.60, 1.90, 2.10), float3(2.10, 1.40, 2.30), uBlend);
    float3 bodyCol = lerp(float3(0.30, 0.90, 1.90), float3(1.00, 0.25, 2.10), uBlend);
    float3 glowCol = lerp(float3(0.10, 0.40, 1.50), float3(0.50, 0.10, 1.60), uBlend);

    float3 col = bodyCol * body + glowCol * glow;
    col += float3(coreCol.r * coreR, coreCol.g * core, coreCol.b * coreB);

    // Бегущие сгустки энергии
    float packet = pow(saturate(sin(alongPx * 0.02 - uTime * 14.0) * 0.5 + 0.5), 3.0);
    col += bodyCol * packet * body * 0.9;

    // Плавное появление у наконечника и затухание у стены
    float headFade = saturate(alongPx / 70.0);
    float tailFade = saturate((uLength - alongPx) / 40.0);
    float flicker  = 0.92 + 0.08 * sin(uTime * 24.0 + alongPx * 0.05);

    col *= headFade * tailFade * flicker * uFade * uOpacity;

    // Альфа = 1: аддитивный бленд, яркость линейная, чёрное не светится
    return float4(col, 1.0);
}

technique Technique1
{
    pass BeamPass
    {
        PixelShader = compile ps_3_0 BeamPS();
    }
}
