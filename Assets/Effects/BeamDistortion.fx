sampler uImage0 : register(s0);
float uOpacity;
float uTime;
float uProgress; // RingPass: радиус кольца 0..1
float uBlend;    // 0 = синий, 1 = фиолетовый (вода)

float2 Rotate(float2 uv, float angle)
{
    float s, c;
    sincos(angle, s, c);
    uv -= 0.5;
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c) + 0.5;
}

float4 GlowPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float dist = length(centered);

    // Swirl deformation near center
    float swirl = sin(dist * 14.0 - uTime * 6.0) * 0.05 * saturate(1.0 - dist * 2.5);
    float2 distortedUV = Rotate(uv, swirl);

    // Layer 1: slow spin
    float4 layer1 = tex2D(uImage0, Rotate(distortedUV, uTime * 0.7));

    // Layer 2: faster reverse spin, slightly zoomed in
    float2 uv2 = Rotate(distortedUV, -uTime * 1.3);
    float4 layer2 = tex2D(uImage0, saturate(uv2 * 0.65 + 0.175));

    float4 combined = layer1 * 0.65 + layer2 * 0.5;

    // White core -> blue outer glow
    float3 coreColor  = float3(1.3, 1.3, 1.5);
    float3 outerColor = float3(0.1, 0.45, 1.6);
    float3 color = lerp(coreColor, outerColor, saturate(dist * 2.8));

    float pulse = 0.78 + 0.22 * sin(uTime * 9.0);

    // Альфа = 1: аддитивный бленд берёт цвет как есть, uOpacity гасит линейно
    return float4(combined.rgb * color * pulse * uOpacity, 1.0);
}

float4 RingPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 centered = uv - 0.5;
    float dist = length(centered) * 2.0;

    // Тонкое кольцо на радиусе uProgress, слегка утолщается при расширении
    float thickness = 0.10 + 0.06 * uProgress;
    float ring = saturate(1.0 - abs(dist - uProgress) / thickness);
    ring = pow(ring, 1.6);

    // Мерцание по окружности
    float angle = atan2(centered.y, centered.x);
    ring *= 0.8 + 0.2 * sin(angle * 9.0 - uTime * 7.0);

    float3 ringColor = lerp(float3(0.5, 1.1, 2.2), float3(1.4, 0.6, 2.4), uBlend);
    return float4(ringColor * ring * uOpacity, 1.0);
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
