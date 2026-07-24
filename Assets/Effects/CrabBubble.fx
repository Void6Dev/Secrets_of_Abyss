sampler uImage0 : register(s0);
float uOpacity;
float uTime;
float uProgress; // PopPass: 0..1 прогресс лопания
float uSeed;     // пер-снарядная вариация (задаётся из C#)

static const float TAU = 6.2831853;

// Мыльный пузырь: тонкая переливающаяся оболочка, желейное колыхание, блики
float4 BubblePS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 c = uv - 0.5;
    float ang = atan2(c.y, c.x);
    float dist = length(c) * 2.0;

    // Желеобразное колыхание радиуса оболочки
    float wobble = sin(ang * 3.0 + uTime * 2.6 + uSeed) * 0.035
                 + sin(ang * 5.0 - uTime * 3.4 + uSeed * 2.0) * 0.02;
    float radius = 0.72 + wobble;

    // Тонкая оболочка
    float shell = saturate(1.0 - abs(dist - radius) / 0.10);
    shell = pow(shell, 1.8);

    // Радужный перелив по окружности, утянутый в сине-бирюзовую гамму
    float hue = ang / TAU + uTime * 0.15 + uSeed;
    float3 irid = 0.5 + 0.5 * cos(TAU * (hue + float3(0.0, 0.33, 0.67)));
    float3 shellColor = lerp(float3(0.35, 0.75, 1.4), irid * float3(0.7, 0.9, 1.5), 0.55);

    // Едва заметное внутреннее заполнение
    float fill = saturate(1.0 - dist / radius) * 0.10;
    float3 fillColor = float3(0.3, 0.6, 1.1);

    // Блик-полумесяц сверху-слева и малый снизу-справа (только внутри пузыря)
    float inside = saturate(1.0 - (dist - radius) / 0.05);
    float spec = pow(saturate(1.0 - length(c - float2(-0.16, -0.18)) / 0.16), 2.0) * 1.3;
    float spec2 = pow(saturate(1.0 - length(c - float2(0.13, 0.15)) / 0.09), 2.0) * 0.5;

    float3 col = shellColor * shell + fillColor * fill
               + float3(1.1, 1.2, 1.35) * (spec + spec2) * inside;

    // Альфа = 1: аддитивный бленд, uOpacity гасит линейно
    return float4(col * uOpacity, 1.0);
}

// Лопание: оболочка разлетается, рвётся на дуги, капли летят наружу
float4 PopPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 c = uv - 0.5;
    float ang = atan2(c.y, c.x);
    float dist = length(c) * 2.0;
    float p = saturate(uProgress);

    // Фронт разлетающейся оболочки
    float radius = 0.5 + p * 0.7;
    float shell = saturate(1.0 - abs(dist - radius) / 0.09);
    shell = pow(shell, 1.6);

    // Разрыв на дуги: угловой хэш сегмента, порог растёт с прогрессом
    float seg = floor((ang / TAU + 0.5) * 12.0);
    float h = frac(sin(seg * 12.9898 + uSeed * 78.233) * 43758.5453);
    shell *= step(p * 1.15, h);

    float3 col = float3(0.5, 0.85, 1.5) * shell;

    // Капли по фиксированным углам, летят чуть впереди фронта
    for (int i = 0; i < 8; i++)
    {
        float a = (i / 8.0) * TAU + uSeed;
        float rr = radius * (1.05 + 0.15 * frac(sin(i * 7.13 + uSeed) * 917.31));
        float2 dropPos = float2(cos(a), sin(a)) * rr * 0.5;
        float drop = pow(saturate(1.0 - length(c - dropPos) / 0.05), 2.0);
        col += float3(0.8, 1.0, 1.4) * drop;
    }

    // Короткая вспышка в центре в первый момент
    float flash = saturate(1.0 - dist / 0.5) * saturate(1.0 - p * 3.0) * 0.8;
    col += float3(0.9, 1.1, 1.4) * flash;

    return float4(col * (1.0 - p) * uOpacity, 1.0);
}

technique Technique1
{
    pass BubblePass
    {
        PixelShader = compile ps_3_0 BubblePS();
    }
    pass PopPass
    {
        PixelShader = compile ps_3_0 PopPS();
    }
}
