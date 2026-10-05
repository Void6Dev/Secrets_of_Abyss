// Рисунок печати прилива (TideSeal_tile): двойное кольцо, пентаграмма, пять рун и кольцо
// вокруг знака ступени в центре. Вращается — поэтому считается здесь, а не лежит в листе
// тайла: повёрнутый спрайт пиксель-арта мылится и рябит, а тут линии считаются прямо на
// пиксельной сетке (1 px мира) и остаются чёткими под любым углом.
// Квад 48x48 по центру замка. Рисовать в Immediate + AlphaBlend (premultiplied).

sampler uImage0 : register(s0); // текстура квада не читается
float uOpacity;
float3 uColor;
float uAngle;      // поворот рисунка, рад
float uIntensity;  // 1 — обычный накал; в ритуале растёт

static const float Size = 48.0;
static const float PI = 3.14159265;
static const float TAU = 6.2831853;
static const float StarRadius = 16.0;
static const float RuneRadius = 18.5;
static const float ClearRadius = 10.5; // внутри — только знак ступени

float SegmentDistance(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float t = saturate(dot(p - a, ab) / dot(ab, ab));
    return length(p - a - ab * t);
}

float4 SigilPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = floor(uv * Size) + 0.5 - Size * 0.5; // центр пикселя, от центра печати
    float s, c;
    sincos(-uAngle, s, c);
    float2 q = float2(p.x * c - p.y * s, p.x * s + p.y * c);
    float r = length(p);

    float d = abs(r - 20.0);                 // внешнее кольцо
    d = min(d, abs(r - 17.0) + 0.25);        // внутреннее, тоньше
    d = min(d, abs(r - 9.5) + 0.3);          // кольцо вокруг знака

    float star = 99.0;
    for (int k = 0; k < 5; k++)
    {
        float a0 = -PI * 0.5 + k * TAU / 5.0;
        float a1 = -PI * 0.5 + (k + 2) * TAU / 5.0;
        star = min(star, SegmentDistance(q, float2(cos(a0), sin(a0)) * StarRadius, float2(cos(a1), sin(a1)) * StarRadius));

        float ar = -PI * 0.5 + PI / 5.0 + k * TAU / 5.0;
        d = min(d, length(q - float2(cos(ar), sin(ar)) * RuneRadius) - 0.3);
    }
    star += step(r, ClearRadius) * 99.0;
    d = min(d, star);

    float core = step(d, 0.6);
    float halo = step(d, 1.25) * (1.0 - core);

    float3 hot = lerp(uColor, 1.0, 0.45);
    float alpha = core + halo * 0.22 * min(uIntensity, 1.5);
    float3 color = hot * core * min(uIntensity, 1.3) + uColor * halo * 0.22 * uIntensity;
    color += uColor * 0.25 * halo * uIntensity; // ореол светится сверх альфы
    return float4(color, alpha) * uOpacity;
}

technique Technique1
{
    pass SigilPass
    {
        PixelShader = compile ps_3_0 SigilPS();
    }
}
