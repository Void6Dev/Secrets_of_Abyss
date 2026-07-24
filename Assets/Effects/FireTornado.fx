// Огненное торнадо Scythe of Fire Storm:
// широкая воронка, тело — пламя с тёмными подпалинами,
// вокруг конуса вьётся непрерывная диагональная спираль-виток (как резьба),
// светящаяся кромка силуэта, у земли — рваные языки.
// Рисуется в premultiplied alpha.
// uProgress = 0..1 жизни вихря (быстрое появление, плавное затухание).

sampler uImage0 : register(s0); // квад (форма процедурная)
sampler uImage1 : register(s1); // тайловый шум (WaveNoise.png, wrap)

float uTime;
float uOpacity;
float uProgress;

float4 TornadoPS(float2 uv : TEXCOORD0) : COLOR0
{
    float y = uv.y; // 0 — широкое устье, 1 — узкий хобот у земли

    // Профиль воронки
    float halfWidth = lerp(0.46, 0.11, pow(y, 1.05));

    // Ось слегка гуляет
    float sway = sin(uTime * 2.5 + y * 5.0) * 0.03 * y;
    float cx = 0.5 + sway;
    float xn = clamp((uv.x - cx) / halfWidth, -1.3, 1.3); // -1..1 внутри воронки
    float xd = abs(xn);

    // Два слоя шума: крупный — силуэт и пятна, мелкий — кипение
    float noiseA = tex2D(uImage1, float2(xn * 0.35 + 0.5 - uTime * 0.55, y * 1.4 - uTime * 1.1)).r;
    float noiseB = tex2D(uImage1, float2(xn * 0.6 + 0.5 + uTime * 0.35, y * 2.6 + uTime * 0.7)).r;

    // Рваный силуэт; устье и основание — мягкие рваные кромки
    float inFunnel = 1.0 - smoothstep(0.90, 1.06, xd + (noiseA - 0.5) * 0.22);
    float aTop = smoothstep(0.0, 0.06, y + (noiseA - 0.5) * 0.05);
    float aBot = 1.0 - smoothstep(0.90, 1.0, y + (noiseB - 0.5) * 0.25);
    inFunnel *= aTop * aBot;

    // 1. Тело: яркое пламя с тёмными подпалинами-«сотами»
    float spots = smoothstep(0.42, 0.62, noiseB);
    float3 bodyBright = float3(1.55, 0.72, 0.12);
    float3 bodyDark   = float3(0.55, 0.16, 0.04);
    float3 col = lerp(bodyBright, bodyDark, spots * 0.85);
    float a = 0.9 * inFunnel;

    // 2. Спираль-виток: диагональные полосы, соединяющиеся на краях конуса
    // (сдвиг по xn = резьба винта; лёгкая парабола — объём обвития)
    float thread = frac(y * 4.5 + xn * 0.5 + xn * xn * 0.20 - uTime * 1.1);
    float threadDist = min(thread, 1.0 - thread);
    float spiral = 1.0 - smoothstep(0.07, 0.17, threadDist);
    float spiralGlow = 1.0 - smoothstep(0.12, 0.42, threadDist);
    col = lerp(col, float3(2.1, 1.35, 0.35), spiral * inFunnel);
    col += float3(1.2, 0.55, 0.08) * spiralGlow * 0.35 * inFunnel;

    // 3. Светящаяся кромка силуэта
    float rim = smoothstep(0.72, 0.94, xd) * inFunnel;
    col = lerp(col, float3(2.0, 1.15, 0.25), rim * 0.8);

    a = max(a, saturate(spiral + rim * 0.8) * inFunnel);

    // Появление и затухание по жизни вихря
    float fadeIn  = smoothstep(0.0, 0.06, uProgress);
    float fadeOut = 1.0 - smoothstep(0.55, 1.0, uProgress);
    a *= uOpacity * fadeIn * fadeOut;

    // Premultiplied alpha
    return float4(col * a, a);
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
