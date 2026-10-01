// Screen-фильтр Прилива Теней (TideUnderwaterFx.cs): толща воды вокруг камеры.
//   • оттенок зоны, виньетка и лёгкое колыхание — когда голова игрока под водой;
//   • лучи света и каустики — ниже зеркала воды, гаснут с глубиной, привязаны к миру.
// Кадр не затемняется целиком: яркие пиксели (свечение, планктон) оттенок почти не трогает.
// Стандартный набор параметров ScreenShaderData — объявлены ВСЕ, которые ставит ваниль
// (отсутствие любого из них даёт NRE в ScreenShaderData.Apply).
sampler uImage0 : register(s0); // экран
sampler uImage1 : register(s1);
float3 uColor;          // оттенок толщи воды
float3 uSecondaryColor; // цвет света с поверхности (день тёплый, ночь холодная)
float uOpacity;
float2 uScreenResolution;
float2 uScreenPosition;
float2 uTargetPosition;
float2 uDirection;
float uProgress;
float uTime;
float uIntensity;       // сила оттенка
float2 uImageOffset;
float2 uZoom;
// Свои параметры (ваниль их не трогает, ставим напрямую из C#)
float uSubmerge;        // 0..1 голова под водой
float uVignette;        // 0..1
float uWarp;            // амплитуда колыхания, px экрана
float uShallowLight;    // 0..1 сила лучей и каустик
float uWaterTopY;       // зеркало воды, px мира
// Видимая часть мира и размер кадра. Ставит TideUnderwaterShaderData.Apply в момент
// отрисовки: ванильные uScreenPosition/uScreenResolution сдвинуты на запас за краем
// экрана и поделены на зум, по ним узор ехал вместе с камерой
float2 uWorldTopLeft;   // px мира в левом верхнем углу кадра
float2 uWorldViewSize;  // сколько px мира видно в кадре
float2 uScreenPixels;   // размер кадра в пикселях экрана
// Маска воды в uImage1, тексель на тайл: R — доля тайла под водой, G — верх дна
float2 uMaskOriginTiles;
float2 uMaskSizeTiles;

static const float TilePx = 16.0;
static const float RaySlant = 0.3;          // наклон лучей: столбцов на тайл глубины
static const float RayFalloffTiles = 45.0;  // лучи гаснут в e раз за столько тайлов
static const float CausticFalloffTiles = 10.0;
static const float RayLight = 0.4;          // сила света луча (цвет солнца, не яркость кадра)
static const float RaySceneLift = 0.12;     // доля от яркости самого кадра — лёгкая, без рисунка
static const float CausticVolume = 0.3;     // каустики в толще воды; на дне — в полную силу
static const float3 Luma = float3(0.299, 0.587, 0.114);

float Band(float x, float sharpness)
{
    return pow(sin(x) * 0.5 + 0.5, sharpness);
}

// Столбы света: три набора мягких полос вдоль наклонной оси (шаг ~15-50 тайлов),
// медленно плывут, вся картина дышит, как от волн на поверхности. Всё здесь зависит
// только от r — координаты поперёк луча, поэтому лучи остаются прямыми: свет под водой
// мерцает и сдвигается, но не гнётся
float Rays(float2 world, float depthTiles)
{
    float r = world.x / TilePx + depthTiles * RaySlant;
    float shafts = Band(r * 0.21 + uTime * 0.35, 7.0)
        + Band(r * 0.37 - uTime * 0.27 + 2.1, 10.0) * 0.8
        + Band(r * 0.13 + uTime * 0.18 + 4.3, 6.0) * 0.7;
    float breathe = 0.6 + 0.4 * sin(r * 0.05 + uTime * 0.4);
    float shimmer = 0.85 + 0.15 * sin(r * 0.9 + uTime * 2.3);
    return shafts * breathe * shimmer;
}

// Каустики: сетка светлых прожилок из суммы искривлённых синусов, два слоя
float CausticLayer(float2 p, float t)
{
    float2 q = p + float2(sin(p.y * 1.3 + t), cos(p.x * 1.1 - t * 0.8)) * 0.55;
    float s = sin(q.x) + sin(q.y) + sin((q.x + q.y) * 0.72 + t * 0.5);
    return pow(saturate(1.0 - abs(s) * 0.9), 5.0);
}

// Ячейка сетки ~1.5 тайла
float Caustics(float2 world)
{
    float2 p = world / (TilePx * 0.9);
    float layerA = CausticLayer(p, uTime * 0.9);
    float layerB = CausticLayer(p * 1.37 + float2(3.1, 1.7), -uTime * 0.7);
    return max(layerA, layerB);
}

// x — пиксель в воде (вода в тайле стоит снизу, как её рисует игра), y — пиксель на верху дна
float2 WaterAt(float2 world)
{
    float2 tile = world / TilePx;
    float2 cell = floor(tile);
    float4 mask = tex2D(uImage1, (cell - uMaskOriginTiles + 0.5) / uMaskSizeTiles);
    float inWater = step(0.004, mask.r) * step(1.0 - mask.r, frac(tile.y));
    return float2(inWater, mask.g);
}

float4 TidePS(float2 uv : TEXCOORD0) : COLOR0
{
    float submerge = uSubmerge * uOpacity;

    // Колыхание: мелкая рябь на весь кадр, по краям чуть сильнее
    float2 wobble = float2(sin(uv.y * 17.0 + uTime * 1.6), cos(uv.x * 13.0 + uTime * 1.25));
    float2 offset = wobble * uWarp * submerge / uScreenPixels;
    float2 sampleUv = clamp(uv + offset, 0.0, 1.0);
    float3 col = tex2D(uImage0, sampleUv).rgb;

    float2 world = uWorldTopLeft + uv * uWorldViewSize;
    float depthTiles = (world.y - uWaterTopY) / TilePx;
    float lum = dot(col, Luma);

    // Свет с поверхности — только в воде ниже зеркала и только на освещённом:
    // в тёмной пещере на той же глубине лучей нет. Каустики — в воде и на верху дна
    float2 water = WaterAt(world);
    float shallow = uShallowLight * uOpacity * step(0.0, depthTiles);
    float lit = saturate(lum * 6.0);
    float rayFade = saturate(depthTiles / 2.0) * exp(-max(depthTiles, 0.0) / RayFalloffTiles);
    float rays = Rays(world, depthTiles) * rayFade * shallow * water.x;
    // Луч — это свет солнца, ровный по всей полосе. Раньше он умножал сам кадр (col * 0.6),
    // и внутри луча проступал кривой рисунок воды и каустик — луч выглядел изогнутым
    col += rays * (uSecondaryColor * RayLight * lit + col * RaySceneLift);

    // Каустики собираются на дне; в толще воды от них остаётся лишь слабый отсвет
    float causticFade = saturate(depthTiles / 1.5) * exp(-max(depthTiles, 0.0) / CausticFalloffTiles);
    float causticSurface = max(water.x * CausticVolume, water.y);
    col += col * Caustics(world) * causticFade * shallow * causticSurface * 0.35;

    // Оттенок толщи. Яркое почти не трогаем — свечение остаётся живым
    float3 tinted = lerp(col, lum * uColor * 1.9, 0.6);
    float keepBright = smoothstep(0.55, 0.95, lum);
    col = lerp(col, tinted, uIntensity * submerge * (1.0 - keepBright * 0.65));

    // Виньетка по краям, круглая независимо от пропорций экрана
    float2 centered = (uv - 0.5) * float2(uScreenPixels.x / uScreenPixels.y, 1.0);
    float edge = smoothstep(0.35, 1.05, length(centered));
    col *= 1.0 - edge * uVignette * submerge;

    return float4(col, 1.0);
}

technique Technique1
{
    pass TidePass
    {
        PixelShader = compile ps_3_0 TidePS();
    }
}
