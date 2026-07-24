// Эффекты Cursed Projectile.
// SmokePass — дымный фиолетовый клуб для трейла (прогресс штампа передаётся через альфу вершинного цвета).
// BlastPass — взрыв: вспышка, рваная ударная волна и лучи (uProgress = 0..1 жизни взрыва).

sampler uImage0 : register(s0); // квад (не используется, форма процедурная)
sampler uImage1 : register(s1); // тайловый шум (WaveNoise.png, wrap)

float uTime;
float uOpacity;
float uProgress;

float4 SmokePS(float4 vcol : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float progress = vcol.a; // 0 = голова трейла, 1 = хвост
    float fade = 1.0 - progress;

    float2 centered = uv - 0.5;
    float dist = length(centered) * 2.0;

    // Шум рвёт край клуба — дымность; каждый штамп сдвинут по прогрессу
    float noise = tex2D(uImage1, uv * 0.9 + float2(progress * 3.7, progress * 1.3 - uTime * 0.6)).r;

    float radius = 0.55 + 0.40 * noise;
    float blob = pow(saturate(1.0 - dist / radius), 1.7);
    float core = pow(saturate(1.0 - dist / 0.35), 2.0);

    float3 coreCol  = float3(1.50, 1.00, 2.20);
    float3 smokeCol = lerp(float3(0.50, 0.12, 1.10), float3(0.18, 0.03, 0.45), progress);

    float3 col = smokeCol * blob + coreCol * core * fade * 0.9;
    col *= 0.85 + 0.15 * sin(uTime * 10.0 + progress * 12.0);
    col *= fade * uOpacity;

    // Альфа = 1: аддитивный бленд, чёрное не светится
    return float4(col, 1.0);
}

float4 BlastPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float dist = length(centered) * 2.0;
    float angNorm = atan2(centered.y, centered.x) / 6.28318 + 0.5;

    // Рваный фронт ударной волны: радиус пляшет от шума по углу
    float edgeNoise = tex2D(uImage1, float2(angNorm * 2.0, uProgress * 0.3)).r;
    float front = uProgress * (0.72 + 0.28 * edgeNoise);
    float ringWidth = 0.10 + 0.12 * uProgress;
    float ring = pow(saturate(1.0 - abs(dist - front) / ringWidth), 1.5);

    // Вспышка в центре, быстро сжимается и гаснет
    float flashRadius = 0.45 * (1.0 - uProgress) + 0.12;
    float flash = pow(saturate(1.0 - dist / flashRadius), 2.0) * pow(1.0 - uProgress, 1.4) * 2.2;

    // Лучи, бьющие из центра
    float rays = pow(tex2D(uImage1, float2(angNorm * 3.0, 0.37)).r, 3.5)
               * saturate(1.0 - dist / max(front + 0.10, 0.001))
               * (1.0 - uProgress);

    float3 hotCol  = float3(1.90, 1.40, 2.40);
    float3 ringCol = float3(0.95, 0.30, 2.10);
    float3 rayCol  = float3(0.60, 0.15, 1.70);

    float3 col = hotCol * flash + ringCol * ring * (1.0 - uProgress * 0.5) + rayCol * rays;
    return float4(col * uOpacity, 1.0);
}

technique Technique1
{
    pass SmokePass
    {
        PixelShader = compile ps_3_0 SmokePS();
    }
    pass BlastPass
    {
        PixelShader = compile ps_3_0 BlastPS();
    }
}
