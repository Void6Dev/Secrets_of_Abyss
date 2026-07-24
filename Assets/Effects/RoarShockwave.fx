// Screen-фильтр рёва: круговая звуковая волна от босса, расходится до ~половины экрана.
// Стандартный набор параметров ScreenShaderData — объявлены ВСЕ, которые ставит ваниль
// (отсутствие любого из них даёт NRE в ScreenShaderData.Apply).
sampler uImage0 : register(s0); // экран
sampler uImage1 : register(s1);
float3 uColor;
float3 uSecondaryColor;
float uOpacity;
float2 uScreenResolution;
float2 uScreenPosition;
float2 uTargetPosition; // центр волны в экранных пикселях (ставим из C# с учётом зума)
float2 uDirection;
float uProgress;        // 0..1 жизнь волны
float uTime;
float uIntensity;       // сила искажения
float2 uImageOffset;
float2 uZoom;
// Свои параметры (ваниль их не трогает, ставим напрямую из C#)
float uWaveCount;   // число последовательных фронтов (1..3)
float uWaveSpacing; // сдвиг прогресса между фронтами

float4 RoarPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 pixel = uv * uScreenResolution;
    float2 delta = pixel - uTargetPosition;
    float d = length(delta);
    float2 dir = delta / max(d, 0.001);

    float p = saturate(uProgress);
    float maxRadius = uScreenResolution.x * 0.5; // фронт доходит до половины экрана

    // До 3 последовательных фронтов, каждый стартует с задержкой uWaveSpacing
    // (без ветвлений: активность/старт волны — через step-множители)
    float wave = 0.0;
    float leadRadius = 0.0;
    float leadBand = 1.0;
    for (int i = 0; i < 3; i++)
    {
        float fi = (float)i;
        float active = step(fi + 0.5, uWaveCount);
        float pi = p - fi * uWaveSpacing;
        float live = active * step(0.001, pi);
        pi = saturate(pi);
        float radius = pi * maxRadius;
        float bandWidth = 26.0 + 90.0 * pi;
        wave += exp(-pow((d - radius) / bandWidth, 2.0)) * live * pow(0.78, fi);
        // параметры головного фронта — для эхо-колец одиночного рёва
        leadRadius = lerp(leadRadius, radius, step(fi, 0.5));
        leadBand = lerp(leadBand, bandWidth, step(fi, 0.5));
    }

    // Эхо-кольца за фронтом — только в одиночном режиме (в каскаде их роль играют сами волны)
    float echo = saturate(2.0 - uWaveCount);
    wave += exp(-pow((d - leadRadius * 0.72) / (leadBand * 0.8), 2.0)) * 0.5 * echo;
    wave += exp(-pow((d - leadRadius * 0.45) / (leadBand * 0.7), 2.0)) * 0.25 * echo;

    // Волна слабеет к концу жизни
    float strength = uIntensity * (1.0 - p) * uOpacity;
    float2 offset = dir * (wave * strength * 30.0) / uScreenResolution;

    // Рефракция экрана + лёгкая хроматическая аберрация на фронте
    float3 col;
    col.r = tex2D(uImage0, uv - offset * 1.15).r;
    col.g = tex2D(uImage0, uv - offset).g;
    col.b = tex2D(uImage0, uv - offset * 0.85).b;

    // Едва заметная голубая подсветка фронтов
    col += float3(0.35, 0.6, 0.9) * saturate(wave) * 0.18 * strength;

    return float4(col, 1.0);
}

technique Technique1
{
    pass RoarPass
    {
        PixelShader = compile ps_3_0 RoarPS();
    }
}
