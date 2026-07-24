sampler uImage0 : register(s0); // WaveNoise: клубящийся шум
float uOpacity;
float uTime;
float uProgress; // 0..1 жизнь всплеска
float3 uColor;   // цвет грунта (песок/снег/земля), задаётся из C#

// Облако пыли, вырывающееся из земли. Квад заякорен низом на поверхности:
// uv.y = 1 у земли, 0 наверху. Рисовать в Immediate + AlphaBlend (не аддитив).
float4 BurstPS(float2 uv : TEXCOORD0) : COLOR0
{
    float p = saturate(uProgress);
    // Локальные координаты: x -1..1 от центра, y 0 у земли, 1 наверху
    float2 q = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y);

    // Купол растёт вверх и расширяется по мере жизни
    float height = 0.25 + 0.75 * smoothstep(0.0, 0.45, p);
    float width = 0.45 + 0.55 * p;
    float dome = length(float2(q.x / width, q.y / height));

    // Два слоя шума ползут вверх с разной скоростью — клубление
    float noiseA = tex2D(uImage0, frac(float2(uv.x * 1.6 + uTime * 0.05, uv.y * 1.1 + uTime * 0.35))).r;
    float noiseB = tex2D(uImage0, frac(float2(uv.x * 0.9 - uTime * 0.07, uv.y * 0.7 + uTime * 0.22))).r;
    float noise = noiseA * 0.6 + noiseB * 0.5;

    // Плотность: ядро плотное, край рваный (шум съедает границу)
    float body = saturate(1.0 - dome);
    float density = saturate(body * (0.55 + noise) - (1.0 - body) * 0.35);
    density *= smoothstep(0.0, 0.12, q.y + 0.05); // ниже уровня земли не рисуем

    // Радиальные струи-комья из основания
    float ang = atan2(q.y + 0.08, q.x);
    float jets = pow(abs(sin(ang * 5.0 + uTime * 1.5)), 6.0);
    float jetReach = smoothstep(0.9, 0.0, dome / (0.4 + p * 0.8));
    density += jets * jetReach * 0.35 * saturate(noiseA + 0.3);

    // Конверт: быстро проявляется, плавно рассеивается
    float fade = smoothstep(0.0, 0.12, p) * (1.0 - smoothstep(0.55, 1.0, p));

    // Грунтовый цвет: у основания темнее, верх подсвечен, шум даёт объём
    float3 col = uColor * (0.75 + 0.45 * q.y + 0.25 * noise);

    float alpha = saturate(density) * fade * uOpacity;
    return float4(col * alpha, alpha); // premultiplied под AlphaBlend
}

technique Technique1
{
    pass BurstPass
    {
        PixelShader = compile ps_3_0 BurstPS();
    }
}
