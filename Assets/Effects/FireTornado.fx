// Огненное торнадо Scythe of Fire Storm.
// Воронка — цилиндр, видимый сбоку. Форму задают винтовые языки пламени на его поверхности:
// передняя стенка — яркие языки, сквозь просветы видна задняя стенка с витками в обратную
// сторону (тусклее) и раскалённое ядро внутри. Между языками — полупрозрачная тёмно-красная
// толща, в верхней трети остывающая в дым. Шум только рвёт края и тянет волокна вдоль языков.
// Цвет — по «нагреву»: остывший тёмно-красный → оранжевый → жёлтый → белое ядро.
// Рисуется в premultiplied alpha; цвет ярче альфы даёт свечение.
// uProgress = 0..1 жизни вихря (быстрое появление, плавное затухание).

sampler uImage0 : register(s0); // квад (форма процедурная)
sampler uImage1 : register(s1); // тайловый шум (WaveNoise.png, wrap)

float uTime;
float uOpacity;
float uProgress;

static const float PI = 3.14159265;
static const float BandCount = 4.0;   // языков на полный оборот
static const float Twist = 2.6;       // сколько раз фаза проворачивается от устья до земли
static const float SpinSpeed = 1.5;

float3 FireRamp(float h)
{
    float3 ember  = float3(0.30, 0.05, 0.02);
    float3 red    = float3(0.95, 0.20, 0.04);
    float3 orange = float3(1.55, 0.62, 0.10);
    float3 yellow = float3(1.95, 1.30, 0.40);
    float3 white  = float3(2.20, 2.00, 1.55);

    if (h < 0.25)
        return lerp(ember, red, h / 0.25);
    if (h < 0.5)
        return lerp(red, orange, (h - 0.25) / 0.25);
    if (h < 0.75)
        return lerp(orange, yellow, (h - 0.5) / 0.25);
    return lerp(yellow, white, (h - 0.75) / 0.25);
}

// Язык пламени по фазе витка: 1 — середина языка, 0 — просвет. Шум рвёт его края
float Band(float phase, float noise, float width)
{
    float d = abs(frac(phase) - 0.5) * 2.0;
    return 1.0 - smoothstep(width - 0.25, width + 0.2, d + (noise - 0.5) * 0.9);
}

float4 TornadoPS(float2 uv : TEXCOORD0) : COLOR0
{
    float y = uv.y; // 0 — широкое устье, 1 — узкий хобот у земли

    // Профиль воронки; ось слегка гуляет, сильнее у земли
    float halfWidth = lerp(0.46, 0.10, pow(y, 1.1));
    float cx = 0.5 + sin(uTime * 2.5 + y * 5.0) * 0.035 * y;
    float xn = (uv.x - cx) / halfWidth; // -1..1 внутри воронки
    float xd = abs(xn);
    if (xd > 1.35)
        return float4(0, 0, 0, 0);

    // Цилиндр: xn → долгота на передней стенке (-0.25..0.25 оборота) и та же точка экрана
    // на задней стенке. facing — насколько поверхность смотрит на зрителя
    float xc = clamp(xn, -0.999, 0.999);
    float lonF = asin(xc) / (2.0 * PI);
    float lonB = 0.5 - lonF;
    float facing = sqrt(1.0 - xc * xc);
    float t = uTime * SpinSpeed;

    // Фаза витка и координата вдоль языка (перпендикуляр к градиенту фазы)
    float pf = lonF * BandCount + y * Twist - t;
    float pb = lonB * BandCount + y * Twist - t;
    float alongF = lonF * Twist - y * BandCount;
    float alongB = lonB * Twist - y * BandCount;

    float edgeN = tex2D(uImage1, float2(xn * 0.25 + 0.5, y * 0.8 - uTime * 0.4)).r;
    float nF = tex2D(uImage1, float2(pf * 0.35, alongF * 0.5 - uTime * 0.3)).r;
    float nB = tex2D(uImage1, float2(pb * 0.35 + 0.37, alongB * 0.5 - uTime * 0.3)).r;
    float streakF = tex2D(uImage1, float2(alongF * 0.9 - uTime * 0.8, pf * 1.6)).r;
    float streakB = tex2D(uImage1, float2(alongB * 0.9 - uTime * 0.8 + 0.5, pb * 1.6)).r;
    float boil = tex2D(uImage1, float2(xn * 0.5 + uTime * 0.2, y * 1.5 - uTime * 1.2)).r;

    float width = 0.42;
    float bandF = Band(pf, nF, width);
    float bandB = Band(pb, nB, width);

    // Рваный силуэт; устье и основание — мягкие рваные кромки
    float silhouette = 1.0 - smoothstep(0.82, 1.05, xd + (edgeN - 0.5) * 0.35);
    float top = smoothstep(0.0, 0.12, y + (edgeN - 0.5) * 0.12);
    float bottom = 1.0 - smoothstep(0.86, 1.0, y + (boil - 0.5) * 0.3);
    float mask = silhouette * top * bottom;

    // Нагрев: передние языки жарче всего в середине языка и к земле, задние — вполсилы,
    // ядро светит из глубины нижней части воронки
    float lower = lerp(0.75, 1.1, y);
    float front = pow(bandF, 1.6) * (0.5 + 0.55 * streakF) * (0.55 + 0.45 * facing) * lower;
    float back = bandB * (0.35 + 0.35 * streakB) * 0.6 * lower;
    float core = facing * facing * smoothstep(0.2, 0.95, y) * (0.35 + 0.25 * boil);
    float heat = saturate(max(front, max(back, core)));

    float fireA = smoothstep(0.10, 0.38, heat) * mask;
    float3 fire = FireRamp(heat);

    // Толща вихря между языками: тусклое полупрозрачное пламя, кипящее вверх
    float fillHeat = 0.2 + 0.18 * boil * facing;
    float fillA = (0.28 + 0.22 * boil) * mask * lerp(0.6, 1.0, y);
    float3 fill = FireRamp(fillHeat);

    // Дым: в верхней трети просветы между языками темнеют космами
    float smokeZone = 1.0 - smoothstep(0.05, 0.40, y);
    float smokeA = smoothstep(0.35, 0.65, nF) * (1.0 - bandF) * mask * smokeZone * 0.6;
    float3 smoke = float3(0.10, 0.07, 0.06);

    // Появление и затухание по жизни вихря
    float fadeIn  = smoothstep(0.0, 0.06, uProgress);
    float fadeOut = 1.0 - smoothstep(0.55, 1.0, uProgress);
    float fade = uOpacity * fadeIn * fadeOut;

    // Слои спереди назад: языки → толща → дым (premultiplied «поверх»)
    float underA = saturate(fillA + smokeA * (1.0 - fillA));
    float3 under = fill * fillA + smoke * smokeA * (1.0 - fillA);
    float a = saturate(fireA + underA * (1.0 - fireA)) * fade;
    float3 col = (fire * fireA + under * (1.0 - fireA)) * fade;
    return float4(col, a);
}

// Паровой гейзер (steam-вариация): НЕ залитый конус, а рыхлые полупрозрачные
// клубы. Форму колонны задаёт мягкая маска, рваность и «дыры» между клубами —
// многослойный ползущий вверх шум (fbm) с мягким порогом по плотности.
// Узкая струя у воды, кверху раздувается плюмажем и тает. premultiplied alpha.
float4 SteamPS(float2 uv : TEXCOORD0) : COLOR0
{
    float y = uv.y; // 0 — верх (широкий тающий плюмаж), 1 — узкая струя у воды

    float t = 1.0 - y; // 0 — струя у воды, 1 — макушка

    // Ширина: тонкая струя у воды растёт в стебель и закругляется куполом
    // у макушки (дуга окружности сворачивает силуэт, как у цветной капусты)
    float grow = smoothstep(0.0, 0.70, t);
    float capT = saturate((t - 0.68) / 0.27);       // верхние ~30% — купол
    float cap  = sqrt(saturate(1.0 - capT * capT));  // окружность: 1 → 0 к макушке
    float width = lerp(0.15, 0.54, grow) * cap;

    float cx = 0.5 + sin(uTime * 1.3 + y * 3.0) * 0.03 * t;
    float dx = (uv.x - cx) / max(width, 0.02); // 0 в центре, ±1 у номинального края

    // Мягкая гауссова маска по горизонтали — никакой жёсткой кромки
    float radial = exp(-dx * dx * 2.0);

    // Мягкое устье у воды + лёгкое утоньшение кверху; купол закрывает верх сам
    float envelope = (1.0 - smoothstep(0.97, 1.0, y)) * lerp(1.0, 0.85, t);

    // fbm из одного шумового тайла: три октавы, ползущие вверх с разной скоростью.
    // Доменный варп первой октавой даёт клубящееся вихрение
    float texX = dx * 0.35 + 0.5;
    float warp = tex2D(uImage1, float2(texX * 0.8 + uTime * 0.05, y * 0.9 - uTime * 0.45)).r - 0.5;
    float n1 = tex2D(uImage1, float2(texX * 0.9 + warp * 0.3, y * 1.1 - uTime * 0.60)).r;
    float n2 = tex2D(uImage1, float2(texX * 1.9 + warp * 0.4 - uTime * 0.08, y * 2.4 - uTime * 1.10)).r;
    float n3 = tex2D(uImage1, float2(texX * 3.8 + warp * 0.2 + uTime * 0.12, y * 4.6 - uTime * 1.80)).r;
    float fbm = n1 * 0.55 + n2 * 0.30 + n3 * 0.15;

    // Плотность = шум * маска колонны; мягкий порог рвёт её на клубы с дырами
    float density = fbm * radial * envelope;
    float a = smoothstep(0.24, 0.60, density);

    // Цвет: плотные клубы — тёплый белый, тонкие края — серо-голубые
    float3 dense = float3(0.98, 1.02, 1.10);
    float3 thin  = float3(0.52, 0.62, 0.76);
    float3 col = lerp(thin, dense, smoothstep(0.30, 0.72, fbm));

    // Едва заметный тёплый отсвет ошпаренной воды у самой струи
    float hot = saturate((y - 0.86) / 0.14) * radial;
    col += float3(0.5, 0.24, 0.07) * hot * 0.5;

    // Появление и затухание по жизни гейзера
    float fadeIn  = smoothstep(0.0, 0.10, uProgress);
    float fadeOut = 1.0 - smoothstep(0.50, 1.0, uProgress);
    a *= uOpacity * fadeIn * fadeOut * 0.85;

    // Premultiplied alpha
    return float4(col * a, a);
}

technique Technique1
{
    pass TornadoPass
    {
        PixelShader = compile ps_3_0 TornadoPS();
    }
    pass SteamPass
    {
        PixelShader = compile ps_3_0 SteamPS();
    }
}
