// Эффекты боя с Королём-крабом: приливная аура вокруг корпуса и ударная волна
// песка с пеной. Заменяют универсальные GlowPass/RingPass из BeamDistortion.fx —
// те были общемодовыми (спиральный «луч»), а здесь нужен водно-песчаный мотив.
//
// Униформы задаёт MiscShaderData: uOpacity и uTime — автоматически, uProgress и
// uBlend выставляет SoAVfx вручную через Shader.Parameters.
sampler uImage0 : register(s0);
float uOpacity;
float uTime;
float uProgress; // RingPass: радиус фронта 0..1
float uBlend;    // 0 — холодная вода, 1 — мутный песок

float2 Rotate(float2 uv, float angle)
{
    float s, c;
    sincos(angle, s, c);
    uv -= 0.5;
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c) + 0.5;
}

// Дешёвый хеш-шум для крупинок песка и ряби. Аргумент сначала загоняется в
// 0..1 и только потом мешается: прежний вариант (frac(p * 456.21) в конце) на
// больших координатах разваливался — у float не хватало мантиссы, дробная
// часть вырождалась и «случайность» пропадала
float Hash21(float2 p)
{
    float3 q = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// Пила для бегущих координат.
//
// uTime — это Main.GlobalTimeWrappedHourly, он растёт до 3600. Без заворота
// uv.y * 34 - uTime * 9 уходит в десятки тысяч, хеш от таких чисел вырождается
// в функцию одного аргумента, и вместо крупинок и пузырей появляются сплошные
// полосы. Заворот на целом числе клеток: и хеш цел, и дробная фаза внутри
// клетки на стыке не рвётся.
static const float CellWrap = 512.0;

float ScrollWrap(float unitsPerSecond)
{
    return frac(uTime * unitsPerSecond / CellWrap) * CellWrap;
}

// ---------------------------------------------------------------------------
// Приливная аура: каустика расходящимися кольцами + всплывающие пузыри.
// В отличие от старого свирла крутится не вся текстура, а бегут кольца —
// читается как вода вокруг туши, а не как энергетический вихрь.
// ---------------------------------------------------------------------------
float4 AuraPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float dist = length(centered) * 2.0;
    float angle = atan2(centered.y, centered.x);

    // Мягкий спад к краю — держим ауру круглой независимо от текстуры
    float falloff = saturate(1.0 - dist);
    falloff *= falloff;

    // Каустика: два встречных набора колец
    float caustic = sin(dist * 11.0 - uTime * 3.4)
                  + sin(dist * 17.0 + uTime * 2.1 + angle * 2.0) * 0.6;
    caustic = saturate(caustic * 0.5 + 0.5);
    caustic = pow(caustic, 2.2);

    // Пузыри: всплывают вверх, чуть виляя по горизонтали
    float2 bub = float2(uv.x * 6.0 + sin(uTime * 1.7 + uv.y * 9.0) * 0.35,
                        uv.y * 6.0 + ScrollWrap(1.1));
    float bubble = Hash21(floor(bub));
    bubble = step(0.93, bubble) * saturate(1.0 - frac(bub.y));

    // Лёгкая рябь по самой текстуре, чтобы аура не выглядела чисто процедурной
    float4 tex = tex2D(uImage0, Rotate(uv, sin(uTime * 0.5) * 0.15));

    float3 deepColor = float3(0.05, 0.35, 0.9);   // холодная вода по краю
    float3 foamColor = float3(0.55, 0.95, 1.25);  // пена в ядре
    float3 sandColor = float3(0.85, 0.68, 0.35);  // муть, когда uBlend > 0
    float3 water = lerp(deepColor, foamColor, saturate(caustic * (1.0 - dist * 0.7)));
    float3 color = lerp(water, sandColor, uBlend * 0.75);

    float body = falloff * (0.35 + caustic * 0.75) * tex.a;
    float pulse = 0.85 + 0.15 * sin(uTime * 4.0);

    float3 result = color * body * pulse + foamColor * bubble * falloff * 0.9;
    return float4(result * uOpacity, 1.0);
}

// ---------------------------------------------------------------------------
// Ударная волна: фронт на радиусе uProgress с радиальными язычками песка,
// которые тянутся наружу и рассыпаются в крупинки к концу расширения.
// ---------------------------------------------------------------------------
float4 RingPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float dist = length(centered) * 2.0;
    float angle = atan2(centered.y, centered.x);

    // Фронт истончается по мере расширения — волна выдыхается
    float thickness = 0.16 - 0.08 * uProgress;
    float front = saturate(1.0 - abs(dist - uProgress) / max(thickness, 0.02));
    front = pow(front, 1.5);

    // Язычки: неровный гребень по окружности
    float tongues = 0.72 + 0.28 * sin(angle * 13.0 + uTime * 2.0)
                         * sin(angle * 5.0 - uTime * 1.3);
    front *= tongues;

    // Крупинки песка на гребне, всё заметнее к концу
    float grit = Hash21(floor(float2(angle * 42.0, dist * 34.0 - ScrollWrap(4.0))));
    front *= lerp(1.0, 0.45 + 0.85 * step(0.55, grit), uProgress);

    // Внутренняя «промоина» — лёгкое затемнение за фронтом даёт объём
    float wake = saturate((uProgress - dist) / max(uProgress, 0.01));
    front += wake * wake * 0.12;

    float3 foam = float3(0.75, 1.15, 1.6);
    float3 sand = float3(1.05, 0.78, 0.42);
    float3 color = lerp(foam, sand, uBlend);

    // Гребень выцветает в белую пену
    color = lerp(color, float3(1.3, 1.35, 1.4), saturate(front - 0.65) * 1.6);

    return float4(color * front * uOpacity * (1.0 - uProgress * 0.35), 1.0);
}

// ---------------------------------------------------------------------------
// «Ярость океана»: раскалённая красная энергия, разлитая по силуэту сегмента.
// Рисуется ВТОРЫМ проходом поверх обычного спрайта в аддитивном батче, поэтому
// работает только там, где у спрайта есть альфа — форма панциря, ног и клешней
// сохраняется. Один и тот же проход применяется ко всем частям, чтобы туша
// горела равномерно, а не пятнами.
// uOpacity — сила разгорания (0 — ярости нет, 1 — полыхает).
// ---------------------------------------------------------------------------
float4 RagePS(float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(uImage0, uv);
    if (tex.a < 0.02)
        return float4(0.0, 0.0, 0.0, 0.0);

    // Волокна энергии, бегущие вдоль сегмента и виляющие по ширине
    float fiber = sin(uv.y * 22.0 - uTime * 7.0 + sin(uv.x * 13.0 + uTime * 2.0) * 1.6);
    fiber = pow(saturate(fiber * 0.5 + 0.5), 2.5);

    // Зерно-разряды: рвут волокна, иначе читается как полосатая ткань
    float grain = Hash21(floor(float2(uv.x * 34.0, uv.y * 34.0 - ScrollWrap(9.0))));
    float spark = step(0.86, grain);

    // Сердцебиение: резкий вдох, долгий выдох
    float beat = 0.65 + 0.35 * pow(saturate(sin(uTime * 6.0) * 0.5 + 0.5), 3.0);

    float3 deep = float3(1.35, 0.10, 0.05);  // густой красный по всей туше
    float3 hot = float3(1.90, 0.75, 0.35);   // раскалённые прожилки и искры
    float3 color = lerp(deep, hot, saturate(fiber + spark * 0.8));

    float energy = (0.45 + fiber * 0.85 + spark * 0.6) * beat;
    return float4(color * energy * uOpacity * tex.a, 1.0);
}

technique Technique1
{
    pass AuraPass
    {
        PixelShader = compile ps_3_0 AuraPS();
    }
    pass RingPass
    {
        PixelShader = compile ps_3_0 RingPS();
    }
    pass RagePass
    {
        PixelShader = compile ps_3_0 RagePS();
    }
}
