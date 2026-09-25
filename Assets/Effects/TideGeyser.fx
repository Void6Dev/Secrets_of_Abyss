// Гейзер Королевского трезубца: столб воды, бьющий из земли, и всё, что он с
// этой землёй делает — удар, всплеск, разлетающиеся капли, растекающаяся вода,
// рябь и низкая водяная взвесь.
//
// ГЛАВНОЕ ПРАВИЛО: силуэт струи задаёт СПРАЙТ (Assets/Textures/TideGeyser.png),
// шейдер его только оживляет. ColumnPass не имеет права размывать или ломать
// форму: искажение квантуется в целые пиксели листа и ограничено парой пикселей.
//
// ЛИСТ: 64x1024, 8 кадров 64x128 сверху вниз, НИЗ кадра = устье (линия земли).
// Поэтому все сэмплы спрайта идут через FrameUV(), которая зажимает координату
// внутрь текущего кадра — иначе искажение подтянет соседний кадр листа.
//
// Направление потока задаётся uFlowDir: -1 — вода идёт вверх (гейзер),
// +1 — вода падает сверху вниз (столб, бьющий в землю). Всё остальное —
// удар, брызги, растекание, взвесь — живёт у устья одинаково в обоих случаях.
//
// Слои рисуются отдельными квадами в таком порядке:
//   PuddlePass → MistPass → ColumnPass → SplashPass → DropletPass
//
// Вывод везде premultiplied alpha. Цвет с alpha = 0 складывается с фоном,
// то есть светится — так сделаны блики и микробрызги.

sampler uImage0 : register(s0); // ColumnPass — лист струи; остальные пассы форму считают сами
sampler uImage1 : register(s1); // WaveNoise.png, wrap

// ПАРАМЕТРЫ.
//
// Каждая отдельная float-униформа съедает у ps_3_0 целый константный регистр,
// а их всего 224 — на россыпи из полусотни настроек шейдер просто не собирается.
// Поэтому настройки лежат упакованными по float4, а работает код с читаемыми
// именами через #define. C# сторона (TideGeyserSettings) пишет ровно эти вектора.

float uTime;       // ставит MiscShaderData.Apply()
float uOpacity;    // -//-
float3 uColor;     // -//- тон стиля (обычная вода / горячий источник)
float2 uImageSize0;

float4 uCommon;    // прогресс, высота струи, общая энергия, seed
#define uProgress   uCommon.x
#define uJet        uCommon.y
#define uIntensity  uCommon.z
#define uSeed       uCommon.w

// xy — размер кадра листа в пикселях, zw — сколько «пиксель-артных» клеток
// в кваде: по ним квантуется вся процедурная часть
float4 uGrid;
#define uFramePx    uGrid.xy
#define uPixelGrid  uGrid.zw

// --- Струя ---
float4 uFrame;     // xy — смещение кадра в листе (uv), zw — размер кадра (uv)

float4 uFlow;
#define uFlowSpeed        uFlow.x
#define uFlowDir          uFlow.y
#define uDistortion       uFlow.z   // амплитуда искажения В ПИКСЕЛЯХ, дальше округляется
#define uDistortionScale  uFlow.w

float4 uShine;
#define uHighlightSpeed     uShine.x
#define uHighlightStrength  uShine.y
#define uEdgeTurbulence     uShine.z
#define uGlowStrength       uShine.w

// --- Удар и всплеск ---
float4 uImpact;
#define uImpactStrength     uImpact.x
#define uImpactPulseSpeed   uImpact.y
#define uImpactCompression  uImpact.z
#define uGround             uImpact.w   // v линии земли внутри квада

float4 uSplash;
#define uSplashStrength  uSplash.x
#define uSplashRadius    uSplash.y
#define uSplashHeight    uSplash.z
#define uSplashWidth     uSplash.w

// --- Капли ---
float4 uParticle;
#define uParticleAmount   uParticle.x
#define uParticleSpeed    uParticle.y
#define uParticleSpread   uParticle.z
#define uParticleGravity  uParticle.w

float4 uParticleShape;
#define uParticleSize        uParticleShape.x
#define uParticleLifetime    uParticleShape.y
#define uParticleTurbulence  uParticleShape.z

// --- Растекание и рябь ---
float4 uPuddle;
#define uPuddleSpread   uPuddle.x
#define uPuddleSpeed    uPuddle.y
#define uPuddleOpacity  uPuddle.z
#define uRippleStrength uPuddle.w

float4 uRipple;
#define uRippleSpeed    uRipple.x
#define uRippleBreakup  uRipple.y

// --- Взвесь ---
float4 uMist;
#define uMistAmount  uMist.x
#define uMistSpread  uMist.y
#define uMistSpeed   uMist.z
#define uMistNoise   uMist.w

float4 uMistShape;
#define uMistNoiseScale  uMistShape.x
#define uMistOpacity     uMistShape.y
#define uMistRise        uMistShape.z
#define uMistTurbulence  uMistShape.w

static const float3 WaterDeep = float3(0.05, 0.32, 0.85);
static const float3 WaterCore = float3(0.35, 0.78, 1.30);
static const float3 Foam      = float3(0.92, 1.20, 1.45);
static const float3 Haze      = float3(0.62, 0.86, 1.05);

static const float Tau = 6.28318;


// ---------------------------------------------------------------------------
// ХЕШИ И ШУМ
// ---------------------------------------------------------------------------

// Хеши сначала загоняют аргумент в 0..1 и только потом мешают его. Прежний
// вариант (frac(p * 456.21)) на больших координатах разваливался: у float не
// хватает мантиссы, дробная часть вырождается и «случайность» пропадает
float Hash21(float2 p)
{
    float3 q = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float2 Hash22(float2 p)
{
    float3 q = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.xx + q.yz) * q.zy);
}

// Бегущее смещение для КЛЕТОЧНЫХ хешей — обязательно пила в узком диапазоне.
//
// uTime здесь — Main.GlobalTimeWrappedHourly, он растёт до 3600. Наивное
// floor(y - uTime * 26) улетает в десятки тысяч, хеш от таких чисел вырождается
// в функцию одного X, и вместо россыпи капель по кромке струи получаются
// сплошные вертикальные полосы во всю её высоту.
//
// Заворачивается каждые CellWrap клеток; клетки целые, поэтому шва не видно.
static const float CellWrap = 512.0;

float ScrollCells(float cellsPerSecond)
{
    return floor(frac(uTime * cellsPerSecond / CellWrap) * CellWrap);
}

// Шум из тайла, wrap-сэмплер: дешевле процедурного и уже используется остальными
// эффектами мода
float Noise(float2 p)
{
    return tex2D(uImage1, p).r;
}

// Пиксель-артная сетка: всё процедурное считается по центрам клеток, поэтому
// края получаются рублеными, а не сглаженными
float2 Snap(float2 uv)
{
    return (floor(uv * uPixelGrid) + 0.5) / uPixelGrid;
}

// Ступенчатая непрозрачность вместо плавного градиента.
// Округление, а не отбрасывание: с floor слабая половина каждой ступени
// проваливалась в ноль и слои получались почти невидимыми
float Quantize(float value, float steps)
{
    return floor(saturate(value) * steps + 0.5) / steps;
}

// Короткий резкий импульс в начале каждого цикла: удар → разлёт → оседание → удар
float Pulse(float speed, float sharpness)
{
    float phase = frac(uTime * speed + uSeed * 0.37);
    return pow(1.0 - phase, sharpness);
}

// Асимметрия: одна сторона всегда живее другой
float SideBias(float side)
{
    return 0.78 + 0.44 * Hash21(float2(side * 13.7 + uSeed, ScrollCells(0.7)));
}


// ---------------------------------------------------------------------------
// СТРУЯ. Спрайт — единственный источник силуэта.
// ---------------------------------------------------------------------------

// Локальные координаты кадра (0..1) → uv листа. clamp обязателен: без него
// искажение вылезает в соседний кадр спрайтшита
float2 FrameUV(float2 local)
{
    return uFrame.xy + clamp(local, 0.003, 0.997) * uFrame.zw;
}

float4 SampleFrame(float2 local)
{
    return tex2D(uImage0, FrameUV(local));
}

// Сдвиг в ПИКСЕЛЯХ спрайта, округлённый до целых — иначе получится мыло
float4 SampleFrameOffset(float2 local, float2 offsetPx)
{
    return SampleFrame(local + round(offsetPx) / uFramePx);
}

float4 ColumnPS(float4 sampleColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    // uv приходит уже в координатах ЛИСТА (кадр вырезан source-прямоугольником),
    // поэтому сначала переводим их в координаты кадра
    float2 local = (uv - uFrame.xy) / uFrame.zw;
    float2 px = local * uFramePx;

    // Ниже к устью вода злее: там же концентрируется вся рваность
    float toMouth = saturate(local.y);
    float turbulence = 0.25 + 0.75 * smoothstep(0.35, 1.0, toMouth);

    float flowT = uTime * uFlowSpeed * uFlowDir;

    // Три слоя течения с разными масштабами и скоростями. Одинаковая скорость
    // читалась бы как ползущая текстура, разная — как турбулентность внутри массы
    float flowA = Noise(float2(local.x * 0.60 * uDistortionScale, local.y * 0.50 + flowT * 0.35));
    float flowB = Noise(float2(local.x * 1.70 * uDistortionScale + 0.37, local.y * 1.10 + flowT * 0.70));
    float micro = Noise(float2(local.x * 3.40 * uDistortionScale + 0.11, local.y * 2.30 + flowT * 1.50));

    // Искажение в целых пикселях и с жёстким потолком: силуэт должен остаться собой
    float2 warp;
    warp.x = (flowB - 0.5) * 2.0 * uDistortion * turbulence;
    warp.y = (flowA - 0.5) * 2.0 * uDistortion * 0.6;
    warp = clamp(warp, -2.0, 2.0);

    float4 src = SampleFrameOffset(local, warp);
    float body = src.a;

    // Внутренняя жизнь: пласты разной плотности, но без дыр в ядре
    float3 col = src.rgb * (0.86 + 0.26 * flowA + 0.14 * flowB);

    // Бегущие блики вдоль оси струи — вода читается как движущаяся масса
    float axis = saturate(1.0 - abs(local.x - 0.5) * 3.0);
    float wave = sin((local.y * 5.0 + uTime * uHighlightSpeed * uFlowDir) * Tau + micro * 5.0);
    float highlight = pow(saturate(wave * 0.5 + 0.5), 7.0) * axis;
    col += Foam * highlight * uHighlightStrength * body;

    // Мелкие колебания плотности
    float flicker = 0.93 + 0.07 * micro;

    // ---- Кромка: капли и обрывки, срывающиеся с боков ----
    //
    // Пиксель ПУСТОЙ, но в паре пикселей в сторону оси вода есть — значит мы
    // прямо у кромки, и здесь может жить сорвавшийся кусочек. Так рваность
    // берётся от самого спрайта и не может разъехаться с его формой.
    float side = sign(local.x - 0.5);
    float2 cell = float2(floor(px.x), floor(px.y) - ScrollCells(26.0) * uFlowDir);
    float2 rnd = Hash22(cell + uSeed);

    float reach = 1.0 + floor(rnd.x * 3.0);
    float inside = SampleFrameOffset(local, float2(-side * reach, 0.0)).a;

    float edgeSpark = step(0.74, rnd.y) * inside * (1.0 - body)
        * turbulence * uEdgeTurbulence * uIntensity;

    // Короткие водяные шипы: тянутся вдоль движения, поэтому проверяем воду ещё
    // и по вертикали — так обрывок выглядит вытянутым, а не точкой
    float spike = step(0.88, rnd.x)
        * SampleFrameOffset(local, float2(-side * reach, reach * uFlowDir)).a
        * (1.0 - body) * turbulence * uEdgeTurbulence;

    float fragments = saturate(edgeSpark + spike * 0.7);

    float alpha = saturate(body * flicker + fragments) * uOpacity;
    col = lerp(col, Foam, saturate(fragments * 1.2));
    col *= uColor;

    // Свечение у самого устья: там струя ярче всего
    float mouthGlow = pow(saturate((local.y - 0.72) / 0.28), 2.0) * body * uGlowStrength;
    col += Foam * mouthGlow * 0.35;

    return float4(col * alpha, alpha) * sampleColor;
}


// ---------------------------------------------------------------------------
// УДАР. Самая энергичная зона всего эффекта: плотное ядро, боковой выхлоп,
// вертикальный отскок, тонкий слой воды по земле и микробрызги.
// ---------------------------------------------------------------------------

float4 SplashPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 grid = Snap(uv);
    float2 c = float2(grid.x - 0.5, grid.y - uGround);   // 0,0 — точка удара

    // Импульс: удар → разлёт → оседание → удар. Живёт поверх общей огибающей.
    // Пик короткий, поэтому основную яркость даёт постоянная часть — иначе
    // всплеск почти всё время не виден
    float pulse = Pulse(uImpactPulseSpeed, 5.0);
    float energy = uJet * uIntensity * (0.62 + 0.38 * pulse);

    // Сжатие в момент удара: основание на мгновение расплывается вширь
    float compress = 1.0 + pulse * uImpactCompression;

    float x = c.x / max(uSplashWidth * compress, 0.02);
    float up = -c.y;                      // вверх от земли — положительное
    float ax = abs(x);

    // Ядро удара: плотная яркая шапка прямо под струёй
    float core = saturate(1.0 - ax * ax) * saturate(1.0 - up / (uSplashHeight * 0.5 + 0.001));
    core *= step(-0.06, up);              // вниз почти не уходит: вода бьёт из земли, не сквозь неё
    core = pow(saturate(core), 1.4) * uImpactStrength;

    // Боковой выхлоп: два языка воды, разлетающиеся влево-вправо. Их длина
    // растёт рывком на импульсе и опадает
    float sideDir = sign(c.x);
    float bias = SideBias(sideDir);
    float reach = uSplashRadius * bias * (0.35 + 0.65 * pulse);
    float wedge = saturate(1.0 - abs(c.x) / max(reach, 0.02));

    // Язык прижат к земле и приподнят к концу — вода уходит по баллистике
    float lift = up - wedge * wedge * uSplashHeight * 0.55 * bias;
    float wing = wedge * saturate(1.0 - abs(lift) / 0.17);
    float tongues = 0.65 + 0.35 * sin(c.x * 46.0 + uTime * 9.0 * sideDir + uSeed);
    wing *= tongues * uSplashStrength;

    // Вертикальный отскок: короткие струйки вверх у самой оси
    float jetX = saturate(1.0 - ax * 2.2);
    float rebound = jetX * saturate(1.0 - abs(up - pulse * uSplashHeight) / 0.14)
        * (0.4 + 0.6 * Noise(float2(grid.x * 3.0 + uSeed, uTime * 2.5)));

    // Контакт с землёй: тонкая яркая полоса, растекающаяся в стороны
    float groundSpread = uSplashRadius * (0.5 + 0.5 * pulse) * 1.25;
    float ground = saturate(1.0 - abs(c.x) / max(groundSpread, 0.02))
        * saturate(1.0 - abs(up + 0.005) / 0.035);
    ground = pow(ground, 1.3);

    // Микробрызги: десятки мелких капель, разлетающихся у самого удара
    float2 sprayCell = floor(grid * uPixelGrid) - float2(0.0, ScrollCells(34.0));
    float spray = step(0.955, Hash21(sprayCell + uSeed))
        * saturate(1.0 - length(float2(c.x * 1.6, c.y * 2.4)) / 0.7)
        * step(-0.02, up);

    float water = saturate(core + wing * 0.9 + rebound * 0.8 + ground * 0.85);
    float alpha = saturate(water + spray * 0.9) * energy * uOpacity;

    // Цвет: тело всплеска — вода, гребни и брызги — пена
    float3 col = lerp(WaterDeep, WaterCore, saturate(water * 1.3));
    col = lerp(col, Foam, saturate(core * 0.8 + spray + ground * 0.5 + rebound * 0.6));
    col *= uColor;

    // Свечение в эпицентре — единственное место, где эффект реально светит
    float glow = core * pulse * uGlowStrength;

    return float4(col * alpha + Foam * glow * 0.25, alpha);
}


// ---------------------------------------------------------------------------
// КАПЛИ. Пять типов движения, у каждой капли своя баллистика, срок жизни и
// форма. Ни одной круглой частицы: только прямоугольные пиксельные сгустки.
// ---------------------------------------------------------------------------

// Капель в ОДНОМ проходе. Пасс рисуется дважды с разными seed и настройками
// (быстрая мелкая пыль и медленные крупные капли) — так набирается и плотность,
// и разница скоростей между слоями. Больше десяти в проходе не влезает:
// развёрнутый цикл упирается в лимит инструкций ps_3_0.
static const int DropletCount = 10;

float4 DropletPS(float2 uv : TEXCOORD0) : COLOR0
{
    // Работаем в пикселях квада — так капля получается ровным пиксельным блоком
    float2 pos = floor(uv * uPixelGrid);
    float2 ground = float2(uPixelGrid.x * 0.5, uPixelGrid.y * uGround);

    // Пасс дорогой (цикл разворачивается в полторы тысячи инструкций), поэтому
    // всё, что заведомо пусто, отсекаем до цикла: ниже земли капель не бывает —
    // там уже растекание
    if (pos.y > ground.y + 2.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float water = 0.0;
    float foam = 0.0;

    [unroll]
    for (int i = 0; i < DropletCount; i++)
    {
        float fi = (float)i;

        // Всё про каплю выводится из (seed, id): картинка случайная, но
        // воспроизводимая и одинаковая на всех клиентах.
        //
        // id складывается с uSeed СРАЗУ, до всякой арифметики: иначе компилятор
        // свернёт половину хеша в константы на каждую итерацию цикла и шейдер
        // не влезет в константные регистры
        float2 id = fi + float2(uSeed, uSeed * 1.7 + 0.31);
        float2 r1 = Hash22(id);
        float2 r2 = Hash22(id + 37.0);
        float2 r3 = Hash22(id + 91.0);

        // Живых капель ровно столько, сколько просит uParticleAmount
        float active = step(fi, uParticleAmount * (float)DropletCount);
        float dir = sign(r2.y - 0.5);

        // Типы движения ветками не разделены — они дороги, разворачиваются в
        // каждую итерацию и выносят лимит инструкций. Вместо этого непрерывный
        // спектр: mix = 0 — свеча строго вверх, mix = 1 — микробрызги, стелющиеся
        // вбок у самой земли; между ними весь боковой веер
        float mix = r1.x;
        float2 vel = float2(dir * lerp(0.18, 1.70, mix) * uParticleSpread,
                            -lerp(1.25, 0.16, mix)) * uParticleSpeed;

        // Часть капель стекает с кромки столба: старт выше по струе, скорости почти нет
        float drip = step(0.72, r1.y);
        float2 start = ground + float2(dir * uPixelGrid.x * 0.13,
                                       -uPixelGrid.y * (0.15 + 0.45 * r3.y) * uJet) * drip;
        vel = lerp(vel, float2(dir * 0.12 * uParticleSpread, -0.10) * uParticleSpeed, drip);

        // Отскок: капля уходит не из оси, а из стороны от точки удара
        float bounce = step(0.85, r2.x);
        start.x += dir * uPixelGrid.x * 0.20 * bounce;

        // Быстрые боковые живут меньше медленных вертикальных
        float lifetime = uParticleLifetime * lerp(1.0, 0.45, mix) * (0.7 + 0.6 * r1.y);
        float age = frac(uTime / max(lifetime, 0.05) + r2.x);

        // Баллистика + лёгкая турбулентность, чтобы дуги не были одинаковыми
        float2 drift = float2(sin(age * 7.0 + r3.x * Tau), cos(age * 5.0 + r3.y * Tau))
            * uParticleTurbulence * age;

        float2 p = start + vel * age * uPixelGrid.y
            + float2(0.0, uParticleGravity * age * age * uPixelGrid.y)
            + drift;

        // Долетела до земли или дожила своё — гаснет
        float alive = step(p.y, ground.y + 1.0) * (1.0 - smoothstep(0.78, 1.0, age));

        // Форма: каплю вытягивает ВДОЛЬ движения, а не всегда по вертикали.
        // Иначе быстрая боковая капля превращается в высокий столбик — на экране
        // это читается как одинокий прямоугольник, а не как брызги
        float2 heading = abs(vel) / max(length(vel), 0.001);
        float stretch = min(length(vel) * 1.4, 2.0);
        float2 size = max(round(uParticleSize + heading * stretch), 1.0);
        size = min(size, 3.0);

        float2 delta = abs(pos - floor(p));
        float hit = step(delta.x, size.x - 0.5) * step(delta.y, size.y - 0.5) * alive * active;

        water += hit;
        foam += hit * step(0.7, r1.y);   // самые яркие капли идут пеной
    }

    float alpha = saturate(water) * uIntensity * uOpacity;
    float3 col = lerp(WaterCore, Foam, saturate(foam));
    col *= uColor;

    return float4(col * alpha, alpha);
}


// ---------------------------------------------------------------------------
// РАСТЕКАНИЕ И РЯБЬ. Тонкий слой воды, расходящийся от точки удара, с рваным
// пиксельным краем и несколькими волнами разной скорости.
// ---------------------------------------------------------------------------

float4 PuddlePS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 grid = Snap(uv);
    float2 c = float2(grid.x - 0.5, grid.y - uGround);

    // Лужа живёт ниже линии земли: сверху её перекрывает всплеск
    if (c.y < -0.04)
        return float4(0.0, 0.0, 0.0, 0.0);

    // Разгон и торможение: рывок наружу, дальше медленное расползание
    float spread = uPuddleSpread * (1.0 - exp(-uProgress * uPuddleSpeed * 4.0));

    // Рваный край: радиус гуляет по углу, одна сторона всегда дальше другой
    float side = sign(c.x);
    float edge = Noise(float2(grid.x * 2.4 + uSeed, uTime * 0.25)) - 0.5;
    float radius = spread * SideBias(side) * (1.0 + edge * 0.28);

    float dist = abs(c.x);

    // Слой тонкий, но не в один пиксель: на 0.055 высоты квада он читался как
    // случайная горизонтальная черта, а не как вода на земле
    float depth = saturate(1.0 - abs(c.y) / 0.17);
    float sheet = saturate(1.0 - dist / max(radius, 0.02)) * depth;

    // Ступенчатая непрозрачность вместо градиента — это пиксель-арт, а не туман
    sheet = Quantize(sheet * 1.15, 4.0);

    // Рябь: несколько колец разной скорости, часть рвётся по дороге
    float ripple = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float fi = (float)i;
        float speed = uRippleSpeed * (0.7 + 0.45 * fi);
        float phase = frac(uTime * speed * 0.5 + Hash21(float2(fi, uSeed)));
        float ringRadius = phase * radius * 1.15;

        // Кольцо тоньше и слабее по мере ухода наружу, часть колец не доживает
        float ring = saturate(1.0 - abs(dist - ringRadius) / (0.035 + 0.02 * fi));
        float breakup = step(uRippleBreakup, Hash21(floor(float2(grid.x * uPixelGrid.x * 0.5, fi * 7.0 + uSeed))));
        ripple += ring * (1.0 - phase) * breakup;
    }
    ripple = saturate(ripple) * uRippleStrength * depth;

    // Капли, срывающиеся с внешней кромки.
    //
    // В хеш ОБЯЗАТЕЛЬНО входит и строка клетки. Если там только X (а время —
    // величина, общая для всего квада), значение одинаково для всего столбца,
    // и капля растягивается в вертикальную иглу во всю высоту квада.
    // По той же причине drip обязан быть прижат к слою воды через depth
    float rim = saturate(1.0 - abs(dist - radius) / 0.05);
    float2 dripCell = floor(grid * uPixelGrid) - float2(0.0, ScrollCells(8.0));
    float drip = step(0.94, Hash21(dripCell + uSeed)) * rim * depth;

    float alpha = saturate(sheet * uPuddleOpacity + ripple * 0.5 + drip * 0.7)
        * uOpacity * uIntensity;

    float3 col = lerp(WaterDeep, WaterCore, saturate(sheet * 0.8 + ripple));
    col = lerp(col, Foam, saturate(ripple * 0.7 + drip));
    col *= uColor;

    return float4(col * alpha, alpha);
}


// ---------------------------------------------------------------------------
// ВЗВЕСЬ. Не дым: мельчайшая водяная пыль у земли. Четыре слоя с разными
// скоростями — плотное ядро, стелющаяся дымка, рваные клочья, микроблёстки.
// ---------------------------------------------------------------------------

float4 MistPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 grid = Snap(uv);
    float2 c = float2(grid.x - 0.5, grid.y - uGround);

    // Взвесь стелется по земле и лишь немного поднимается
    float up = -c.y;
    if (up < -0.08)
        return float4(0.0, 0.0, 0.0, 0.0);

    float pulse = Pulse(uImpactPulseSpeed, 5.0);

    // Ширина растёт по мере жизни гейзера, но никогда не заливает экран
    float spread = uMistSpread * (0.45 + 0.55 * uProgress);
    float lateral = saturate(1.0 - abs(c.x) / max(spread, 0.02));

    // 1. Плотное ядро у самого удара
    float coreMist = pow(saturate(1.0 - length(float2(c.x * 2.6, up * 3.4)) / 0.55), 1.6)
        * (0.7 + 0.5 * pulse);

    // 2. Стелющаяся дымка: ползёт вбок медленнее всего
    float drift = uTime * uMistSpeed;
    float hazeN = Noise(float2(grid.x * uMistNoiseScale + drift * 0.12, up * 1.4 + uSeed));
    float haze = lateral * saturate(1.0 - up / 0.26) * (0.35 + 0.9 * hazeN);

    // 3. Рваные клочья: поднимаются и распадаются
    float riseN = Noise(float2(grid.x * uMistNoiseScale * 1.7 + 0.23,
                               up * 2.2 - drift * uMistRise));
    float wisps = lateral * saturate(1.0 - up / (0.34 * uMistRise + 0.05))
        * step(0.52, riseN) * (0.4 + 0.6 * riseN) * uMistTurbulence;

    // 4. Микроблёстки внутри дымки: отдельные капли, поймавшие свет
    float2 sparkCell = floor(grid * uPixelGrid) - float2(0.0, ScrollCells(6.0));
    float sparks = step(0.975, Hash21(sparkCell + uSeed)) * lateral * saturate(1.0 - up / 0.3);

    float density = (coreMist * 1.1 + haze * 0.75 + wisps * 0.5) * uMistAmount * uMistNoise;

    // Ступени и пиксельные сгустки: гладкого объёмного тумана здесь быть не должно
    density = Quantize(density, 5.0);

    float alpha = saturate(density * uMistOpacity + sparks * 0.55) * uOpacity * uIntensity;

    float3 col = lerp(Haze, Foam, saturate(coreMist * 0.6 + sparks));
    col *= uColor;

    return float4(col * alpha, alpha);
}


technique Technique1
{
    pass ColumnPass
    {
        PixelShader = compile ps_3_0 ColumnPS();
    }
    pass SplashPass
    {
        PixelShader = compile ps_3_0 SplashPS();
    }
    pass DropletPass
    {
        PixelShader = compile ps_3_0 DropletPS();
    }
    pass PuddlePass
    {
        PixelShader = compile ps_3_0 PuddlePS();
    }
    pass MistPass
    {
        PixelShader = compile ps_3_0 MistPS();
    }
}
