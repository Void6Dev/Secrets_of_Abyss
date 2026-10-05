// Вспышки и кольца Копья волн (Sharded Spear).
//   GlowPass — звезда-вспышка: орб зарядки, выстрел, вспышка у наконечника и в точке попадания.
//   RingPass — ударная волна в точке попадания.
// Оба прохода круглые и гаснут до краёв квада: углы текстуры не проступают.

sampler uImage0 : register(s0); // BeamDistortion.png — звезда-вспышка
float uOpacity;
float uTime;
float uProgress; // RingPass: радиус кольца 0..1
float uBlend;    // 0 = морской синий, 1 = фиолетовый (вода). Эффект общий на оба прохода —
                 // выставлять перед каждым Apply, иначе останется значение от прошлой отрисовки

float3 CoreColor(float b) { return lerp(float3(0.85, 1.00, 1.00), float3(1.00, 0.85, 1.00), b); }
float3 BodyColor(float b) { return lerp(float3(0.10, 0.62, 1.00), float3(0.62, 0.18, 1.00), b); }
float3 FoamColor(float b) { return lerp(float3(0.65, 0.95, 1.00), float3(1.00, 0.70, 1.00), b); }

float3 Tonemap(float3 c) { return 1.0 - exp(-c * 1.25); }

float2 Rotate(float2 uv, float angle)
{
    float s, c;
    sincos(angle, s, c);
    uv -= 0.5;
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c) + 0.5;
}

// Гладкий 1D-шум по углу: рвёт кольцо на брызги и колышет лучи
float Hash1(float x) { return frac(sin(x * 127.1) * 43758.5453); }
float Noise1(float x)
{
    float i = floor(x);
    float f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(Hash1(i), Hash1(i + 1.0), f);
}

float4 GlowPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float r = length(centered) * 2.0; // 0 в центре, 1 у края квада
    float angle = atan2(centered.y, centered.x);
    float window = 1.0 - smoothstep(0.55, 1.0, r);

    // Мягкое ядро и широкий ореол
    float bloom = exp(-r * r / 0.035) * 1.4 + exp(-r * r / 0.18) * 0.35;

    // Лучи: четыре главных и восемь тонких, крутятся навстречу друг другу
    float mainRays = pow(abs(cos(angle * 2.0 + uTime * 0.9)), 24.0) * exp(-r / 0.32);
    float thinRays = pow(abs(cos(angle * 4.0 - uTime * 1.6 + 0.4)), 40.0) * exp(-r / 0.22) * 0.6;
    float flicker = 0.85 + 0.15 * Noise1(angle * 3.0 + uTime * 6.0);

    // Рваная фактура звезды поверх — только ближе к центру
    float star = tex2D(uImage0, Rotate(uv, uTime * 0.7)).r * 0.5 * (1.0 - smoothstep(0.3, 0.7, r));

    float hot = exp(-r * r / 0.012);
    float amount = (bloom + (mainRays + thinRays) * flicker + star) * window;
    float pulse = 0.85 + 0.15 * sin(uTime * 9.0);

    float3 col = (BodyColor(uBlend) * amount + CoreColor(uBlend) * hot * 1.5) * pulse * uOpacity;

    // Альфа = 1: аддитивный бленд берёт цвет как есть, uOpacity гасит линейно
    return float4(Tonemap(col), 1.0);
}

float4 RingPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float r = length(centered) * 2.0;
    float angle = atan2(centered.y, centered.x);
    float radius = uProgress * 0.85; // не упирается в край квада

    // Ударная волна: резкий передний край и мягкий след внутрь, расплывается с расширением
    float x = r - radius;
    float front = x > 0.0
        ? exp(-x * x / 0.0016)
        : exp(-x * x / (0.006 + 0.025 * uProgress));

    // Рвётся на брызги: чем шире кольцо, тем сильнее
    float breakup = smoothstep(0.25 + 0.4 * uProgress, 0.9,
        Noise1(angle * 5.0 + 11.0) * 0.6 + Noise1(angle * 13.0 - uTime) * 0.4);
    float ring = front * lerp(1.0, breakup, 0.4 + 0.5 * uProgress);

    // Хроматический край: красный канал чуть снаружи, синий чуть внутри
    float xr = x - 0.012;
    float xb = x + 0.012;
    float3 foam = FoamColor(uBlend);
    float3 col = BodyColor(uBlend) * ring * 2.2;
    col.r += foam.r * exp(-xr * xr / 0.0015) * 0.9 * breakup;
    col.b += foam.b * exp(-xb * xb / 0.0015) * 0.9 * breakup;

    float window = 1.0 - smoothstep(0.9, 1.0, r);
    return float4(Tonemap(col * uOpacity * window), 1.0);
}

technique Technique1
{
    pass GlowPass
    {
        PixelShader = compile ps_3_0 GlowPS();
    }
    pass RingPass
    {
        PixelShader = compile ps_3_0 RingPS();
    }
}
