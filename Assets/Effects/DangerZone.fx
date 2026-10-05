// Зона удара — общий телеграф атак босса. Прямоугольник, стоящий на грунте (низ квада —
// поверхность): ровно та область, куда придёт удар.
//   • рамка и линия уровня — яркие и непрозрачные: видны и на дневном небе, где
//     аддитивное свечение пропадает, и в темноте (рамка ещё и светится);
//   • заливка поднимается снизу вверх с uProgress — зона полная, значит удар сейчас;
//   • диагональные полосы бегут, в последней четверти зона мигает;
//   • uSafe = 1 — безопасная зона (брешь в приливе): без полос, мигания и линии уровня.
// Пиксели 2x2, как у спрайтов. Рисовать в Immediate + AlphaBlend (premultiplied).

sampler uImage0 : register(s0); // текстура квада не читается
float uOpacity;
float uTime;
float uProgress;  // 0..1: доля предупреждения, прошедшая до удара
float3 uColor;
float2 uSizePx;   // размер зоны в мировых px
float uSafe;

static const float PixelPx = 2.0;

float4 ZonePS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 cells = max(uSizePx / PixelPx, 1.0);
    float2 cell = floor(uv * cells);
    float up = uSizePx.y - (cell.y + 0.5) * PixelPx; // px от грунта
    float danger = 1.0 - uSafe;

    // Рамка: бока и верх. Низ — грунт, там её нет
    float edge = min(min(cell.x, cells.x - 1.0 - cell.x), cell.y);
    float border = step(edge, 0.5);

    float level = uProgress * uSizePx.y;
    float charged = step(up, level);
    float levelLine = step(abs(up - level), PixelPx) * step(uProgress, 0.999) * danger;

    float2 px = cell * PixelPx;
    float stripe = step(frac((px.x + uSizePx.y - px.y) / 14.0 - uTime * 1.6), 0.45) * danger;

    float appear = smoothstep(0.0, 0.15, uProgress);
    float late = smoothstep(0.75, 1.0, uProgress);
    float blink = lerp(1.0, 0.8 + 0.2 * sign(sin(uTime * 30.0)) * late, danger);

    float topFade = 1.0 - 0.45 * saturate(up / uSizePx.y);
    float alpha = (0.2 + 0.10 * stripe + 0.3 * charged) * topFade;
    float3 color = uColor * (0.7 + 0.3 * charged);

    float outline = saturate(border + levelLine);
    alpha = lerp(alpha, 0.95, outline);
    color = lerp(color, lerp(uColor, 1.0, 0.5), outline);

    alpha *= appear * blink * uOpacity;
    // Рамка и линия светятся сверх альфы: в темноте они горят, а не тонут
    float3 glow = uColor * 0.35 * outline * appear * uOpacity;
    return float4(color * alpha + glow, alpha); // premultiplied
}

technique Technique1
{
    pass ZonePass
    {
        PixelShader = compile ps_3_0 ZonePS();
    }
}
