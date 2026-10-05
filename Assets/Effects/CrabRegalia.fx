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

// Мягкое насыщение вместо жёсткого клипа аддитива: яркое уходит в белое плавно
float3 Tonemap(float3 c) { return 1.0 - exp(-c * 1.2); }

float ScrollWrap(float unitsPerSecond)
{
    return frac(uTime * unitsPerSecond / CellWrap) * CellWrap;
}

// ---------------------------------------------------------------------------
// Приливная аура: водяная каустика (сетка ярких нитей, как свет на дне),
// расходящаяся рябь и всплывающие пузыри. Строго круглая: форма текстуры
// (звезда-вспышка) больше не проступает шипами по краю.
// ---------------------------------------------------------------------------

// Классическая каустика: несколько итераций искажения координат дают сетку
// светлых нитей. Сдвиг −250 обязателен — без него сумма насыщается в сплошную единицу
float Caustic(float2 uv, float t)
{
    float2 p = uv * 6.2831853 - 250.0;
    float2 i = p;
    float c = 1.0;
    const float inten = 0.005;
    for (int n = 0; n < 4; n++)
    {
        float tt = t * (1.0 - 3.5 / (n + 1.0));
        i = p + float2(cos(tt - i.x) + sin(tt + i.y), sin(tt - i.y) + cos(tt + i.x));
        c += 1.0 / length(float2(p.x / (sin(i.x + tt) / inten), p.y / (cos(i.y + tt) / inten)));
    }
    c /= 4.0;
    c = 1.17 - pow(max(c, 0.0), 1.4);
    return saturate(pow(abs(c), 8.0));
}

float4 AuraPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float r = length(centered) * 2.0;

    float falloff = pow(saturate(1.0 - smoothstep(0.0, 1.0, r)), 1.6);

    float caustic = Caustic(uv * 1.3, frac(uTime / 600.0) * 300.0 + 23.0);
    float ripple = pow(saturate(sin(r * 18.0 - uTime * 4.0) * 0.5 + 0.5), 6.0) * (1.0 - r);

    // Пузыри: круглые, всплывают вверх, чуть виляя
    float2 bub = float2(uv.x * 7.0 + sin(uTime * 1.7 + uv.y * 9.0) * 0.3, uv.y * 7.0 + ScrollWrap(1.3));
    float bubble = step(0.94, Hash21(floor(bub)));
    bubble *= saturate(1.0 - length(frac(bub) - 0.5) / 0.22) * saturate(1.0 - frac(bub.y));

    float3 deepColor = float3(0.04, 0.30, 0.85);
    float3 foamColor = float3(0.55, 0.95, 1.20);
    float3 sandColor = float3(0.85, 0.66, 0.34);
    float3 water = lerp(deepColor, foamColor, saturate(caustic * 1.2 + (1.0 - r) * 0.3));
    float3 color = lerp(water, sandColor, uBlend * 0.75);

    float body = falloff * (0.25 + 1.5 * caustic + 0.35 * ripple);
    float pulse = 0.88 + 0.12 * sin(uTime * 4.0);

    float3 result = color * body * pulse + foamColor * bubble * falloff * 1.1;
    return float4(Tonemap(result * uOpacity), 1.0);
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
// «Ярость океана»: туша раскаляется изнутри. Рисуется ВТОРЫМ проходом поверх
// обычного спрайта в аддитивном батче — только там, где у спрайта есть альфа.
// Светятся контур и тёмные линии рисунка (трещины панциря), освещённые места
// сохраняют свой цвет: раньше проход заливал всю тушу жёлтыми полосами, и спрайт
// превращался в лавовую кляксу. Один проход на все части — туша горит равномерно.
// uOpacity — сила разгорания (0 — ярости нет, 1 — полыхает).
// ---------------------------------------------------------------------------
float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 RagePS(float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(uImage0, uv);

    // Контур: экранная производная альфы — у кромки спрайта она скачет. Считаем ДО любых
    // ветвлений: производные в расходящемся потоке не определены
    float rim = saturate(fwidth(tex.a) * 2.0);

    // Трещины: тёмные линии рисунка (обводка, морщины, швы панциря)
    float lum = dot(tex.rgb, float3(0.3, 0.59, 0.11));
    float crevice = saturate((0.2 - lum) / 0.12);

    // Жар течёт по трещинам, бьётся сердцем: резкий вдох, долгий выдох
    float flow = ValueNoise(float2(uv.x * 9.0 + uTime * 0.6, uv.y * 9.0 - ScrollWrap(1.4)));
    float beat = 0.6 + 0.4 * pow(saturate(sin(uTime * 6.0) * 0.5 + 0.5), 3.0);
    float heat = (crevice * (0.55 + 0.9 * flow) + rim * 0.9) * beat;

    // Угольки: редкие точки на мелкой сетке, мигают
    float cell = Hash21(floor(uv * 70.0));
    float ember = step(0.985, cell) * (0.5 + 0.5 * sin(uTime * 9.0 + cell * 40.0));

    float3 deep = float3(1.0, 0.10, 0.03);  // лёгкий красный налёт по всей туше
    float3 hot = float3(1.5, 0.75, 0.20);   // раскалённые трещины и контур
    float3 white = float3(1.8, 1.40, 0.90); // угольки
    float3 color = deep * 0.07 * beat + hot * heat + white * ember;

    return float4(Tonemap(color * uOpacity) * tex.a, 1.0);
}

// ---------------------------------------------------------------------------
// Рассыпание в сцене смерти: туша сгорает в песок раскалённой кромкой.
//   uMode 0 — панцирь: рваная линия ползёт сверху вниз, отдельные пиксели отрываются
//             раньше неё (раньше спрайт срезался ровной горизонтальной «шторкой»);
//   uMode 1 — ноги и клешни: крошатся по пикселям (раньше просто таяли призраками).
// Пиксели — 2x2 текселя, как в арте. Кромка светится сама, независимо от освещения.
// uTexSize — размер текстуры текущей части, выставляется перед каждым Draw.
// ---------------------------------------------------------------------------
float uMode;
float2 uTexSize;

static const float DissolveBand = 0.05; // ширина раскалённой кромки, доля высоты спрайта

float4 DissolvePS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 tex = tex2D(uImage0, uv) * color;
    float2 cell = floor(uv * uTexSize / 2.0);
    float n = Hash21(cell + 0.5);

    float d;
    if (uMode < 0.5)
    {
        // Та же линия, что DissolveLine в King_crab.Cinematics.cs (по ней сыплется песок)
        float wipeLine = uProgress * (1.0 + 2.0 * DissolveBand + 0.14) - DissolveBand - 0.07;
        float edge = wipeLine + (ValueNoise(float2(cell.x * 0.18, 7.0)) - 0.5) * 0.12 + (n - 0.5) * 0.07;
        d = uv.y - edge;
    }
    else
    {
        d = (n * 1.1 - uProgress * 1.15) * 0.5;
    }

    float keep = step(0.0, d);
    float live = step(0.001, uProgress) * step(uProgress, 0.999);
    float glow = pow(1.0 - saturate(d / DissolveBand), 1.5) * live;
    float3 hot = lerp(float3(1.0, 0.62, 0.22), float3(1.0, 0.92, 0.70), glow * glow);
    float3 rgb = tex.rgb * (1.0 - glow * 0.85) + hot * glow * 1.4 * tex.a;
    return float4(rgb, tex.a) * keep;
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
    pass DissolvePass
    {
        PixelShader = compile ps_3_0 DissolvePS();
    }
}
