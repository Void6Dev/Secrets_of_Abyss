// Универсальная лента-трейл (SoATrail.cs). Геометрию строит C#: полоса треугольников
// вдоль пути, uv.x — от головы (0) к хвосту (1), uv.y — поперёк ленты (0..1).
// Цвет тела ленты приходит вершинным цветом (им вызывающий красит и гасит хвост),
// параметры ниже задают характер: яркую жилу по центру, рваный шумом край и
// бегущие вдоль ленты струи. Бленд аддитивный — чёрное не светится.

float4x4 uWorldViewProjection;

float uTime;          // Main.GlobalTimeWrappedHourly
float uOpacity;
float3 uCoreColor;    // цвет жилы по центру ленты
float uCoreWidth;     // ширина жилы, доля от полуширины (0..1)
float uNoiseScale;    // сколько раз шум укладывается вдоль ленты
float uScrollSpeed;   // скорость бега струй к хвосту, долей текстуры в секунду
float uEdgeTear;      // насколько шум рвёт край ленты (0 — ровная)

sampler uNoise : register(s1); // тайловый шум, wrap (SoAVfx.Noise)

struct VertexInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
};

VertexOutput TrailVS(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, uWorldViewProjection);
    output.Color = input.Color;
    output.Uv = input.Uv;
    return output;
}

float4 TrailPS(VertexOutput input) : COLOR0
{
    float along = input.Uv.x;
    float across = abs(input.Uv.y - 0.5) * 2.0; // 0 — центр, 1 — край

    // Время заворачиваем в frac: большие uTime съедают точность (см. TideGeyser.fx)
    float scroll = frac(uTime * uScrollSpeed);
    float drift = frac(uTime * 0.37);

    float streakNoise = tex2D(uNoise, float2(along * uNoiseScale - scroll, input.Uv.y * 0.55 + drift)).r;
    float tearNoise = tex2D(uNoise, float2(along * uNoiseScale * 0.6 + drift, input.Uv.y * 1.4 - scroll)).r;

    // Край ленты рвётся шумом, к хвосту сильнее — трейл распадается, а не обрезается
    float edge = 1.0 - uEdgeTear * tearNoise * (0.4 + 0.6 * along);
    float body = pow(saturate(1.0 - across / max(edge, 0.05)), 1.6);

    // Струи: светлые прожилки шума, бегущие к хвосту
    float streaks = smoothstep(0.5, 0.85, streakNoise) * body;

    // Жила: узкая и горячая у головы, гаснет к хвосту быстрее тела
    float core = pow(saturate(1.0 - across / max(uCoreWidth, 0.01)), 2.0) * pow(1.0 - along, 2.0);

    float3 color = input.Color.rgb * (body * (0.55 + 0.45 * streakNoise) + streaks * 0.8)
                 + uCoreColor * core;
    color *= input.Color.a * uOpacity;

    return float4(color, 1.0);
}

technique Trail
{
    pass TrailPass
    {
        VertexShader = compile vs_2_0 TrailVS();
        PixelShader = compile ps_2_0 TrailPS();
    }
}
