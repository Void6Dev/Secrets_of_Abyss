// Полностью процедурный шейдер луча Копья волн (Sharded Spear).
// Луч рисуется ОДНИМ растянутым квадом: uv.y = 0..1 вдоль луча, uv.x = 0..1 поперёк.
// Форма считается математикой и не зависит от текстуры квада:
//   ядро    — прямое и тонкое: копьё пронзает, а не машет;
//   тело    — поток воды, бегущий от копья (вытянутый шум);
//   волны   — два витка, закрученные вокруг ядра: ближний ярче и толще, дальний тусклее,
//             на гребнях — пена;
//   осколки — короткие светлые щепы внутри тела (порог по мелкому шуму).
// Погасание: луч рассыпается по шуму, а не просто тускнеет.

sampler uImage0 : register(s0); // текстура квада (не используется, форма процедурная)
sampler uImage1 : register(s1); // тайловый шум (WaveNoise.png, wrap)

float uTime;
float uOpacity;
float uBlend;  // 0 = морской синий, 1 = фиолетовый (вода)
float uLength; // текущая длина луча в пикселях
float uFade;   // 1 = луч жив, к 0 при погасании (и при появлении)

static const float PI = 3.14159265;

// Палитра: сухой луч / луч сквозь воду
float3 CoreColor(float b) { return lerp(float3(0.85, 1.00, 1.00), float3(1.00, 0.85, 1.00), b); }
float3 BodyColor(float b) { return lerp(float3(0.10, 0.62, 1.00), float3(0.62, 0.18, 1.00), b); }
float3 DeepColor(float b) { return lerp(float3(0.02, 0.16, 0.62), float3(0.20, 0.03, 0.55), b); }
float3 FoamColor(float b) { return lerp(float3(0.65, 0.95, 1.00), float3(1.00, 0.70, 1.00), b); }

// Мягкое насыщение вместо жёсткого клипа аддитива: яркое уходит в белое плавно,
// без плоской пересвеченной полосы
float3 Tonemap(float3 c) { return 1.0 - exp(-c * 1.25); }

float Gauss(float x, float w) { return exp(-x * x / (w * w)); }

// Виток волны вокруг ядра. phaseShift разводит витки на полпериода
void Strand(float across, float alongPx, float phaseShift, float settle, float flow,
            float3 bodyCol, float3 foamCol, inout float3 col)
{
    float phase = alongPx * 0.032 - uTime * 9.0 + phaseShift;
    float offset = sin(phase) * 0.30 * settle;
    float depth = cos(phase) * 0.5 + 0.5;               // 1 — виток перед ядром
    float strand = Gauss(across - offset, 0.035 + 0.03 * depth) * (0.25 + 0.85 * depth) * settle;
    float crest = smoothstep(0.55, 0.85, flow) * strand; // гребень пены
    col += bodyCol * strand * 0.9 + foamCol * crest * 0.8;
}

float4 BeamPS(float2 uv : TEXCOORD0) : COLOR0
{
    float alongPx = uv.y * uLength; // расстояние от начала луча, px
    float across = uv.x * 2.0 - 1.0;
    float d = abs(across);
    float settle = saturate(alongPx / 120.0); // у наконечника волны ещё не раскрутились

    // Поток: шум в мировом масштабе (не растягивается с длиной луча), бежит от копья
    float n1 = tex2D(uImage1, float2(uv.x * 0.35 + 0.13, alongPx / 900.0 - uTime * 1.10)).r;
    float n2 = tex2D(uImage1, float2(uv.x * 0.90 + 0.57, alongPx / 300.0 - uTime * 2.30)).r;
    float n3 = tex2D(uImage1, float2(uv.x * 2.20 + 0.31, alongPx / 140.0 - uTime * 3.60)).r;
    float flow = n1 * 0.5 + n2 * 0.35 + n3 * 0.15;

    float3 coreCol = CoreColor(uBlend);
    float3 bodyCol = BodyColor(uBlend);
    float3 deepCol = DeepColor(uBlend);
    float3 foamCol = FoamColor(uBlend);

    float core = Gauss(d, 0.055 + 0.015 * (n2 - 0.5));
    float body = Gauss(d, 0.26) * (0.65 + 0.9 * flow);
    float deep = Gauss(d, 0.50) * 0.6;

    float3 col = 0;
    Strand(across, alongPx, 0.0, settle, flow, bodyCol, foamCol, col);
    Strand(across, alongPx, PI, settle, flow, bodyCol, foamCol, col);

    // Осколки и бегущие импульсы с неровным шагом
    float shard = smoothstep(0.78, 0.92, n3) * Gauss(d, 0.16);
    float pulse = smoothstep(0.6, 1.0, sin(alongPx * 0.018 - uTime * 16.0 + n1 * 3.0)) * Gauss(d, 0.3);

    col += deepCol * deep + bodyCol * body * 1.05 + coreCol * core * 1.8;
    col += foamCol * shard * 1.1 + bodyCol * pulse * 0.7;

    // Края квада гарантированно гаснут — прямоугольник не проступает на тёмном фоне
    float edge = 1.0 - smoothstep(0.75, 1.0, d);
    float head = smoothstep(0.0, 24.0, alongPx);
    float tail = smoothstep(0.0, 36.0, uLength - alongPx);

    // Рассыпание: при uFade = 1 порог ниже любого шума, к 0 съедает луч клочьями
    float dissolveLo = (1.0 - uFade) * 1.1 - 0.12;
    float dissolve = smoothstep(dissolveLo, dissolveLo + 0.12, flow);

    col *= edge * head * tail * dissolve * uFade * uOpacity;

    // Альфа = 1: аддитивный бленд, яркость линейная, чёрное не светится
    return float4(Tonemap(col), 1.0);
}

technique Technique1
{
    pass BeamPass
    {
        PixelShader = compile ps_3_0 BeamPS();
    }
}
