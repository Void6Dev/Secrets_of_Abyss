sampler uImage0 : register(s0); // WaveNoise: клубящийся шум
float uOpacity;
float uTime;
float uProgress; // 0..1 жизнь всплеска
float3 uColor;   // цвет грунта (песок/снег/земля), задаётся из C#
float2 uSizePx;  // размер квада в мировых px — облако собирается из пикселей 2x2, как спрайты

static const float PixelPx = 2.0;

// Порог дизеринга (interleaved gradient noise): ровное зерно без повторяющегося узора
float Dither(float2 cell)
{
    return frac(52.9829189 * frac(dot(cell, float2(0.06711056, 0.00583715))));
}

// Облако грунта, вырывающееся из земли. Квад заякорен низом на поверхности:
// uv.y = 1 у земли, 0 наверху. Рисовать в Immediate + AlphaBlend (не аддитив).
// Облако пиксельное: плотные комья с тенью снизу и ступенями тона, рваный край —
// дизерингом. Раньше было мягким мыльным пятном и спорило с пиксель-артом вокруг.
float4 BurstPS(float2 uv : TEXCOORD0) : COLOR0
{
    float p = saturate(uProgress);

    // Пиксель-снап: всё ниже считается в центре своего «пикселя» 2x2
    float2 cells = max(uSizePx / PixelPx, 1.0);
    float2 cell = floor(uv * cells);
    float2 suv = (cell + 0.5) / cells;

    // Локальные координаты: x -1..1 от центра, y 0 у земли, 1 наверху
    float2 q = float2(suv.x * 2.0 - 1.0, 1.0 - suv.y);

    // Купол растёт вверх и расширяется по мере жизни
    float height = 0.25 + 0.75 * smoothstep(0.0, 0.45, p);
    float width = 0.45 + 0.55 * p;
    float dome = length(float2(q.x / width, q.y / height));

    // Два слоя шума ползут вверх с разной скоростью — клубление
    float noiseA = tex2D(uImage0, frac(float2(suv.x * 1.6 + uTime * 0.05, suv.y * 1.1 + uTime * 0.35))).r;
    float noiseB = tex2D(uImage0, frac(float2(suv.x * 0.9 - uTime * 0.07, suv.y * 0.7 + uTime * 0.22))).r;
    float noise = noiseA * 0.6 + noiseB * 0.5;

    // Плотность: ядро сплошное, край рваный (шум съедает границу)
    float body = saturate(1.0 - dome);
    float density = saturate(body * (0.6 + noise * 1.1) - (1.0 - body) * 0.45);
    density *= smoothstep(0.0, 0.1, q.y + 0.04); // ниже уровня земли не рисуем

    // Радиальные струи-комья из основания
    float ang = atan2(q.y + 0.08, q.x);
    float jets = pow(abs(sin(ang * 5.0 + uTime * 1.5)), 6.0);
    float jetReach = smoothstep(0.9, 0.0, dome / (0.4 + p * 0.8));
    density += jets * jetReach * 0.4 * saturate(noiseA + 0.3);

    // Конверт: быстро проявляется, рассыпается в зерно к концу
    float fade = smoothstep(0.0, 0.1, p) * (1.0 - smoothstep(0.6, 1.0, p));
    float amount = saturate(density * 2.4) * fade;

    // Пиксель либо есть, либо нет: порог с дизерингом вместо полупрозрачного мыла
    float alpha = step(Dither(cell) * 0.6 + 0.2, amount) * 0.92 * uOpacity;

    // Тон ступенями: тень снизу и с подветренной стороны, светлые гребни сверху
    float shade = saturate(0.55 + 0.35 * q.y + 0.35 * (noise - 0.5) - 0.15 * q.x);
    shade = floor(shade * 4.0 + 0.5) / 4.0 * 0.6 + 0.55;

    return float4(uColor * shade * alpha, alpha); // premultiplied под AlphaBlend
}

technique Technique1
{
    pass BurstPass
    {
        PixelShader = compile ps_3_0 BurstPS();
    }
}
