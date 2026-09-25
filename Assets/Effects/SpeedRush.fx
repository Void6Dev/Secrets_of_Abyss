// Шлейф скорости: включается, когда снаряд разогнался выше порога (заряженный бросок
// Королевского копья). Рисуется ОДНИМ повёрнутым квадом вдоль вектора движения.
//
// Отличие от CursedTrail/BeamDistortion: там дым и луч, здесь — рассекаемая вода:
// узкий конус у острия, разлетающийся к хвосту, набитый продольными штрихами,
// которые уезжают назад быстрее самого снаряда.
//
// uv.x: 0 — хвост шлейфа, 1 — остриё (квад поворачивается по velocity вызывающим)
// uv.y: 0..1 поперёк, центр оси — 0.5
// uProgress: 0..1 — насколько превышен порог скорости (0 — только-только, 1 — предел)
// uOpacity: общая яркость, считает вызывающий код
// Вывод в premultiplied alpha — годится и для аддитивного батча.

sampler uImage0 : register(s0); // квад (форма процедурная)

float uTime;
float uOpacity;
float uProgress;

static const float3 RushDeep = float3(0.04, 0.30, 0.80);
static const float3 RushCore = float3(0.40, 0.82, 1.30);
static const float3 RushFoam = float3(0.95, 1.25, 1.50);

// Число продольных штрихов поперёк конуса
static const float StreakRows = 30.0;

float Hash11(float n)
{
    return frac(sin(n * 78.233) * 43758.5453);
}

float4 RushPS(float2 uv : TEXCOORD0) : COLOR0
{
    float t = 1.0 - uv.x;          // 0 — остриё, 1 — конец хвоста
    float y = (uv.y - 0.5) * 2.0;  // -1..1 поперёк оси

    // Конус: у острия почти схлопнут, к хвосту раскрывается
    float spread = lerp(0.14, 0.90, pow(t, 0.65));
    float across = abs(y) / spread;
    if (across > 1.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    // Мягкие борта конуса и растворение хвоста
    float sides = 1.0 - smoothstep(0.45, 1.0, across);
    float tail = 1.0 - smoothstep(0.55, 1.0, t);
    float nose = smoothstep(0.0, 0.06, t); // у самого острия шлейфа нет
    float cone = sides * tail * nose;
    if (cone <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    // Продольные штрихи: каждый ряд едет назад со своей скоростью, поэтому
    // шлейф не читается расчёской из одинаковых палок
    float rowPos = uv.y * StreakRows;
    float row = floor(rowPos);
    float seed = Hash11(row);
    float thin = 1.0 - smoothstep(0.22, 0.72, abs(frac(rowPos) - 0.5) * 2.0);

    float dashSpeed = 1.5 + seed * 1.6;
    float dash = frac(t * (1.6 + seed * 0.8) - uTime * dashSpeed + seed * 6.3);
    float streak = smoothstep(0.60, 0.88, dash) * (1.0 - smoothstep(0.93, 1.0, dash));

    // Короткие ряды у оси, длинные по краям — иначе штрихи выстраиваются сеткой
    float lines = streak * thin * (0.45 + 0.55 * across);

    // Ось: плотный светлый клин прямо за остриём
    float core = pow(saturate(1.0 - across / 0.42), 3.0) * (1.0 - smoothstep(0.30, 0.85, t));

    // Разгон читается ярче не только по яркости, но и по плотности штрихов
    float drive = 0.35 + 0.65 * uProgress;

    float a = saturate(lines * 0.85 + core * 0.8) * cone * drive * uOpacity;
    if (a <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float3 col = lerp(RushDeep, RushCore, saturate(lines * 1.3 + across * 0.2));
    col = lerp(col, RushFoam, saturate(core * 1.1 + streak * 0.35));

    return float4(col * a, a);
}

technique Technique1
{
    pass RushPass
    {
        PixelShader = compile ps_3_0 RushPS();
    }
}
