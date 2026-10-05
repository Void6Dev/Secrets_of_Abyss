// Разлом грунта — ударная волна Короля-краба (KingCrabShockwave).
// Не столб, который едет, а сам грунт: у фронта из земли вздымаются каменные шипы и
// оседают позади. Шипы привязаны к МИРОВОЙ сетке, поэтому стоят на месте, а бежит
// только волна, которая их поднимает. Пиксели 2x2, цвет — грунт под волной; у основания
// фронта тёплая полоса: видно, где сейчас опасно.
//
// Квад заякорен низом на поверхности. Рисовать в Immediate + AlphaBlend (premultiplied).

sampler uImage0 : register(s0); // текстура квада не читается
float uOpacity;
float uTime;
float uProgress;   // 0..1 жизнь волны: проявление и угасание
float3 uColor;     // цвет грунта
float2 uSizePx;    // размер квада в мировых px
float uWorldLeft;  // мировой X левого края квада
float uFront;      // мировой X фронта
float uDir;        // направление хода: +1 вправо, -1 влево

static const float PixelPx = 2.0;
static const float MaxHeight = 64.0;  // высота самого высокого шипа, px
static const float SpikeWidth = 11.0; // шаг шипов по мировой сетке, px

// Хеш без sin: на мировых координатах (десятки тысяч px) sin теряет точность
float Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float4 RupturePS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 cells = max(uSizePx / PixelPx, 1.0);
    float2 cell = floor(uv * cells);
    float worldX = uWorldLeft + (cell.x + 0.5) * PixelPx; // столбец в мире
    float up = uSizePx.y - (cell.y + 0.5) * PixelPx;      // px над грунтом
    float behind = (uFront - worldX) * uDir;               // > 0 — позади фронта

    // Огибающая: впереди резкий подъём, позади — медленное оседание
    float envelope = smoothstep(-16.0, 2.0, behind) * exp(-max(behind - 6.0, 0.0) / 46.0);

    // Шип: своя высота, заострённая макушка, теневая сторона
    float spikeId = floor(worldX / SpikeWidth);
    float rnd = Hash11(spikeId + 17.0);
    float local = frac(worldX / SpikeWidth);
    float tip = 1.0 - pow(abs(local * 2.0 - 1.0), 1.3) * 0.65;
    float height = envelope * MaxHeight * (0.45 + 0.55 * rnd) * tip;

    // Осевший шип ниже 3 px не рисуем: иначе за волной тянулась бы ниточка в пиксель
    float solid = step(up, height) * step(3.0, height);

    // Тон ступенями: светлая кромка сверху, тень у земли и на дальней от света стороне
    float shade = 0.62 + 0.38 * saturate(up / max(height, 1.0)) + 0.12 * (rnd - 0.5);
    shade += step(height - up, PixelPx * 1.5) * 0.25;
    shade -= step(up, 4.0) * 0.18;
    shade -= step(local, 0.3) * 0.14;
    shade = floor(saturate(shade) * 5.0 + 0.5) / 5.0 * 0.85 + 0.25;

    // Крошка над гребнем у самого фронта: отдельные пиксели, мерцают
    float crumb = step(0.985, Hash11(cell.x * 13.1 + cell.y * 3.3 + floor(uTime * 20.0) * 7.7))
                * step(up, height + 22.0) * step(0.5, envelope) * step(behind, 30.0) * (1.0 - solid);

    float alpha = saturate(solid + crumb);
    float3 rock = uColor * lerp(0.9, shade, solid);

    // Тёплая полоса фронта у основания: эмиссия поверх, альфы не добавляет
    float glow = exp(-(behind - 2.0) * (behind - 2.0) / 120.0) * exp(-up / 14.0) * 0.9;

    float fade = smoothstep(0.0, 0.08, uProgress) * (1.0 - smoothstep(0.8, 1.0, uProgress)) * uOpacity;
    float3 color = rock * alpha + float3(1.0, 0.72, 0.32) * glow;
    return float4(color * fade, alpha * fade); // premultiplied под AlphaBlend
}

technique Technique1
{
    pass RupturePass
    {
        PixelShader = compile ps_3_0 RupturePS();
    }
}
