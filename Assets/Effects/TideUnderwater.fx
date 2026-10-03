// Screen-фильтр Прилива Теней (TideUnderwaterFx.cs): толща воды вокруг камеры.
//   • оттенок зоны, виньетка и лёгкое колыхание — когда голова игрока под водой;
//   • лучи света и каустики — ниже зеркала воды, гаснут с глубиной, привязаны к миру.
// Кадр не затемняется целиком: яркие пиксели (свечение, планктон) оттенок почти не трогает.
// Стандартный набор параметров ScreenShaderData — объявлены ВСЕ, которые ставит ваниль
// (отсутствие любого из них даёт NRE в ScreenShaderData.Apply).
sampler uImage0 : register(s0); // экран
sampler uImage1 : register(s1);  // маска воды, точечно
sampler uImage2 : register(s2);  // та же маска со сглаживанием
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
// Маска воды в uImage1, тексель на тайл: R — доля тайла под водой, G — верх дна,
// B — дошедший до тайла свет с поверхности (под крышей и сводом 0). Точные границы воды
// читаются точечно, плавные величины (свет, близость воды) — из uImage2 со сглаживанием
float2 uMaskOriginTiles;
float2 uMaskSizeTiles;

static const float TilePx = 16.0;
static const float RaySlant = 0.3;          // наклон лучей: столбцов на тайл глубины
static const float RayFalloffTiles = 45.0;  // лучи гаснут в e раз за столько тайлов
static const float CausticFalloffTiles = 10.0;
static const float RayLight = 0.55;         // сила света луча (цвет солнца, не яркость кадра)
static const float RaySceneLift = 0.2;     // доля от яркости самого кадра — лёгкая, без рисунка
static const float CausticVolume = 0.3;     // каустики в толще воды; на дне — в полную силу
static const float SurfaceSlack = 0.15;     // доля тайла над уровнем воды: игра рисует гребни волн выше него
static const float SurfaceBandPx = 3.0;     // толщина светлой полосы под кромкой воды
static const float SurfaceBandLight = 0.45;
static const float BlockRayShare = 0.35;    // блоки в толще воды тоже освещены лучом, но слабее воды
static const float ShadeFloor = 0.2;        // под крышей не ночь: рассеянный свет остаётся
static const int ReflectScanTiles = 3;      // глубже кромки отражения уже не видно
static const float ReflectStrength = 0.32;
static const float ReflectFadePx = 18.0;    // отражение гаснет вглубь за столько px
static const float ReflectWobblePx = 1.6;   // рябь искажает отражение
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

float4 MaskAt(float2 cell)
{
    return tex2D(uImage1, (cell - uMaskOriginTiles + 0.5) / uMaskSizeTiles);
}

// Плавная маска в точке мира: между центрами тайлов значения перетекают
float4 SmoothMaskAt(float2 world)
{
    return tex2D(uImage2, (world / TilePx - uMaskOriginTiles) / uMaskSizeTiles);
}

// x — пиксель в воде (вода в тайле стоит снизу, как её рисует игра), y — пиксель на верху дна,
// z — свет с поверхности, дошедший до тайла
float3 WaterAt(float2 world)
{
    float2 tile = world / TilePx;
    float2 cell = floor(tile);
    float4 mask = MaskAt(cell);
    float inWater = step(0.004, mask.r) * step(1.0 - mask.r - SurfaceSlack, frac(tile.y));
    return float3(inWater, mask.g, mask.b);
}

// Поверхность снизу: под самой кромкой воды светлая полоса — отражение и преломлённое
// небо, гребни волн бегут вдоль неё. 0..1, гаснет за SurfaceBandPx под кромкой
float SurfaceBand(float2 world)
{
    float2 tile = world / TilePx;
    float2 cell = floor(tile);
    float4 here = MaskAt(cell);
    float4 above = MaskAt(cell - float2(0.0, 1.0));

    // Кромка — в тайле с водой, над которым воды нет. Под потолком света нет — нет и блика
    float isSurface = step(0.004, here.r) * (1.0 - step(0.004, above.r)) * SmoothMaskAt(world).b;
    float waterTopTiles = cell.y + 1.0 - here.r;
    float wave = sin(world.x * 0.09 + uTime * 2.2) * 1.2 + sin(world.x * 0.23 - uTime * 1.7) * 0.6;
    float belowPx = (tile.y - waterTopTiles) * TilePx - wave;
    return isSurface * step(-1.0, belowPx) * exp(-max(belowPx, 0.0) / SurfaceBandPx);
}

// Кромка воды над пикселем в том же столбце, не дальше ReflectScanTiles: Y кромки в тайлах.
// found = 0 — кромки рядом нет или между ней и пикселем блок
float SurfaceAbove(float2 tile, out float found)
{
    float2 cell = floor(tile);
    float surface = 0.0;
    float alive = 1.0;
    found = 0.0;
    [unroll] for (int i = 0; i < ReflectScanTiles; i++)
    {
        float2 c = cell - float2(0.0, i);
        float4 here = MaskAt(c);
        float4 above = MaskAt(c - float2(0.0, 1.0));
        alive *= step(0.004, here.r);
        float isTop = alive * (1.0 - step(0.004, above.r));
        surface += isTop * (c.y + 1.0 - here.r);
        found += isTop;
        alive *= 1.0 - isTop;
    }
    return surface;
}

// Отражение того, что над водой: зеркалим пиксель относительно кромки и берём кадр оттуда
float3 Reflection(float2 world, float3 col, out float weight)
{
    weight = 0.0;
    float found;
    float surfaceTiles = SurfaceAbove(world / TilePx, found);
    float depthPx = world.y - surfaceTiles * TilePx;
    if (found < 0.5 || depthPx < 0.0)
        return col;

    float2 wobble = float2(sin(world.y * 0.35 + uTime * 3.1) + sin(world.x * 0.05 - uTime * 1.3) * 0.5,
                           sin(world.x * 0.11 + uTime * 2.4) * 0.5) * ReflectWobblePx;
    float2 mirrored = float2(world.x, surfaceTiles * TilePx - depthPx) + wobble;
    float2 mirroredUv = (mirrored - uWorldTopLeft) / uWorldViewSize;

    // У верхнего края кадра отражать нечего — гасим, а не тянем край
    float onScreen = saturate(mirroredUv.y * 20.0);
    weight = exp(-depthPx / ReflectFadePx) * onScreen;
    return tex2D(uImage0, clamp(mirroredUv, 0.0, 1.0)).rgb;
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

    // Свет с поверхности — только в воде ниже зеркала, куда он доходит сверху
    // (под палубой, аркой, сводом пещеры его нет), и только на освещённом.
    // Каустики — в воде и на верху дна
    float3 water = WaterAt(world);
    float4 smoothMask = SmoothMaskAt(world);
    float sunlight = lerp(ShadeFloor, 1.0, smoothMask.b);
    float shallow = uShallowLight * uOpacity * step(0.0, depthTiles) * sunlight;
    // Луч идёт по воде; блоки, окружённые водой, он тоже освещает, только слабее —
    // иначе свет обрывается ступенькой на каждом блоке
    float rayGate = max(water.x, BlockRayShare * saturate(smoothMask.r * 3.0));
    float lit = saturate(lum * 6.0);
    float rayFade = saturate(depthTiles / 2.0) * exp(-max(depthTiles, 0.0) / RayFalloffTiles);
    float rays = Rays(world, depthTiles) * rayFade * shallow * rayGate;
    // Луч — это свет солнца, ровный по всей полосе. Раньше он умножал сам кадр (col * 0.6),
    // и внутри луча проступал кривой рисунок воды и каустик — луч выглядел изогнутым
    col += rays * (uSecondaryColor * RayLight * lit + col * RaySceneLift);

    // Каустики собираются на дне; в толще воды от них остаётся лишь слабый отсвет
    float causticFade = saturate(depthTiles / 1.5) * exp(-max(depthTiles, 0.0) / CausticFalloffTiles);
    float causticSurface = max(water.x * CausticVolume, water.y);
    col += col * Caustics(world) * causticFade * shallow * causticSurface * 0.35;

    // Отражение надводного: сверху заметно, из-под воды почти нет, под крышей тусклее
    float reflectWeight;
    float3 reflected = Reflection(world, col, reflectWeight);
    reflectWeight *= water.x * ReflectStrength * uOpacity * (1.0 - 0.7 * uSubmerge) * sunlight;
    col = lerp(col, reflected, reflectWeight);

    // Кромка воды светится: снизу сильнее, сверху лёгким бликом
    float band = SurfaceBand(world) * uOpacity * (0.35 + 0.65 * uSubmerge);
    col += band * (uSecondaryColor * SurfaceBandLight * (0.4 + 0.6 * uShallowLight) + col * 0.35);

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
