sampler uImage0 : register(s0); // текстура сегмента луча (маска формы)
sampler uImage1 : register(s1); // тайловый шум для энергетических потоков

float uTime;
float uOpacity;
float uBlend = 0.0; // 0 = ледяной синий, 1 = фиолетовый (вода)

float4 PixelShaderFunction(float2 uv : TEXCOORD0) : COLOR0
{
    // === ДВОЙНАЯ ВОЛНА ПО ДЛИНЕ ЛУЧА ===
    float wobble = sin(uv.y * 16.0 + uTime * 7.0) * 0.030
                 + sin(uv.y * 37.0 - uTime * 11.0) * 0.015;

    float2 buv = uv;
    buv.x += wobble;
    buv.y = frac(buv.y - uTime * 0.55); // течение вдоль луча (frac = зацикливание, текстура бесшовная)

    // === ХРОМАТИЧЕСКИЕ КРАЯ (RGB-сдвиг формы) ===
    float aR = tex2D(uImage0, buv + float2(0.06, 0.0)).a;
    float aC = tex2D(uImage0, buv).a;
    float aB = tex2D(uImage0, buv - float2(0.06, 0.0)).a;

    // === ПРОФИЛЬ ЛУЧА ===
    float centerDist = saturate(abs(uv.x + wobble - 0.5) * 2.0);
    float core = pow(1.0 - centerDist, 4.0); // бело-горячее ядро
    float body = pow(1.0 - centerDist, 1.3); // основное тело

    // === БЕГУЩИЕ ЭНЕРГЕТИЧЕСКИЕ ПОТОКИ (2 слоя шума) ===
    float n1 = tex2D(uImage1, float2(uv.x * 0.6, uv.y * 2.5 - uTime * 0.9)).r;
    float n2 = tex2D(uImage1, float2(uv.x * 1.3 + 0.37, uv.y * 5.0 - uTime * 2.2)).r;
    float streaks = pow(saturate(n1 * 0.75 + n2 * 0.65), 2.0) * 2.4;

    // === ЦВЕТА (синий <-> фиолетовый в воде) ===
    float3 coreCol = lerp(float3(1.6, 1.9, 2.2), float3(2.1, 1.2, 2.3), uBlend);
    float3 bodyCol = lerp(float3(0.25, 0.85, 1.9), float3(0.95, 0.20, 2.0), uBlend);
    float3 edgeCol = lerp(float3(0.05, 0.35, 1.4), float3(0.50, 0.05, 1.5), uBlend);

    float3 col = lerp(edgeCol, bodyCol, body);
    col = lerp(col, coreCol, core);
    col += bodyCol * streaks * body;        // потоки светятся в теле луча
    col += coreCol * streaks * core * 0.8;  // и вспыхивают в ядре

    // Струи из текстуры добавляют мелкую деталь
    float texDetail = tex2D(uImage0, buv).b;
    col *= lerp(0.8, 1.25, texDetail);

    // === ЭЛЕКТРИЧЕСКОЕ МЕРЦАНИЕ ===
    float flicker = 0.9 + 0.1 * sin(uTime * 23.0 + uv.y * 40.0);
    col *= flicker;

    // Каналы маскируются сдвинутыми альфами -> цветная кайма по краям
    float3 masked = float3(col.r * aR, col.g * aC, col.b * aB);
    float alpha = max(aC, max(aR, aB));

    return float4(masked, alpha * uOpacity);
}

technique Technique1
{
    pass WavePass
    {
        PixelShader = compile ps_3_0 PixelShaderFunction();
    }
}
