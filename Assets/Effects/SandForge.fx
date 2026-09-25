sampler uImage0 : register(s0);

float uOpacity;
float uTime;
float uProgress;
float3 uColor;
float2 uImageSize0;
float uPadding;

// Размер отдельной песчинки
static const float GrainPx = 2.0;

// Максимальная дистанция, с которой прилетает песок
static const float FlyReach = 1.35;


// ---------------------------------------------------------
// HASH
// ---------------------------------------------------------

float Hash21(float2 p)
{
    return frac(
        sin(dot(p, float2(41.31, 289.07))) *
        43758.5453
    );
}

float2 Hash22(float2 p)
{
    return float2(
        Hash21(p),
        Hash21(p + 17.31)
    );
}


// ---------------------------------------------------------
// БЕЗОПАСНЫЙ СЭМПЛ СПРАЙТА
// ---------------------------------------------------------

float4 SampleSprite(float2 suv)
{
    float inside =
        step(0.0, suv.x) *
        step(suv.x, 1.0) *
        step(0.0, suv.y) *
        step(suv.y, 1.0);

    return tex2D(
        uImage0,
        saturate(suv)
    ) * inside;
}


// ---------------------------------------------------------
// ПЛАВНОЕ EASE
// ---------------------------------------------------------

float EaseOut(float x)
{
    return 1.0 - pow(1.0 - saturate(x), 3.0);
}

float EaseIn(float x)
{
    return x * x * x;
}


// ---------------------------------------------------------
// PIXEL SHADER
// ---------------------------------------------------------

float4 ForgePS(
    float4 sampleColor : COLOR0,
    float2 uv : TEXCOORD0
) : COLOR0
{
    float progress = saturate(uProgress);

    // Координаты относительно настоящего спрайта.
    //
    // Например:
    // uPadding = 3
    //
    // Тогда центральная треть квада = настоящий спрайт,
    // а остальное пространство используется для летящего песка.

    float2 suv =
        (uv - 0.5) * uPadding + 0.5;


    // -----------------------------------------------------
    // НАСТОЯЩИЙ СПРАЙТ
    // -----------------------------------------------------

    float4 target = SampleSprite(suv);

    float targetAlpha = target.a;


    // -----------------------------------------------------
    // КООРДИНАТЫ В ПИКСЕЛЯХ
    // -----------------------------------------------------

    float2 px = suv * uImageSize0;


    // -----------------------------------------------------
    // КЛЕТКА ПЕСЧИНКИ
    // -----------------------------------------------------

    float2 cell =
        floor(px / GrainPx);

    float rnd1 = Hash21(cell);
    float rnd2 = Hash21(cell + 31.71);
    float rnd3 = Hash21(cell + 71.23);


    // -----------------------------------------------------
    // СЛУЧАЙНАЯ ТОЧКА НАЗНАЧЕНИЯ
    //
    // Каждая песчинка выбирает место внутри спрайта.
    // -----------------------------------------------------

    float2 destination =
        float2(
            rnd2,
            rnd3
        );


    // -----------------------------------------------------
    // ПРОВЕРЯЕМ, ПОПАЛА ЛИ ТОЧКА В СПРАЙТ
    // -----------------------------------------------------

    float4 destinationPixel =
        SampleSprite(destination);

    float validParticle =
        step(0.15, destinationPixel.a);


    // -----------------------------------------------------
    // СЛУЧАЙНОЕ ВРЕМЯ ПОСАДКИ
    // -----------------------------------------------------

    //
    // Сначала собирается низ,
    // затем середина,
    // затем наконечник.
    //

    float verticalOrder =
        1.0 - destination.y;

    float order =
        verticalOrder * 0.72 +
        rnd1 * 0.28;


    // Окно появления частицы
    float t =
        saturate(
            (progress - order) / 0.28
        );


    // -----------------------------------------------------
    // ТРАЕКТОРИЯ
    // -----------------------------------------------------

    float angle =
        rnd1 * 6.28318;

    float2 direction =
        float2(
            cos(angle),
            sin(angle)
        );


    // Дальность старта
    float reach =
        min(
            uImageSize0.x,
            uImageSize0.y
        )
        *
        FlyReach
        *
        lerp(
            0.45,
            1.0,
            rnd2
        );


    // Позиция, где частица начинает движение.
    float2 start =
        destination +
        direction * reach;


    // -----------------------------------------------------
    // ЗАКРУЧИВАЕМ ТРАЕКТОРИЮ
    // -----------------------------------------------------

    float2 perpendicular =
        float2(
            -direction.y,
            direction.x
        );


    float swirl =
        sin(
            t * 3.14159 +
            rnd3 * 6.28318
        )
        *
        (1.0 - t)
        *
        lerp(
            2.0,
            9.0,
            rnd2
        );


    float2 position =
        lerp(
            start,
            destination,
            EaseOut(t)
        );


    position +=
        perpendicular *
        swirl;


    // -----------------------------------------------------
    // НАСКОЛЬКО МЫ БЛИЗКО К ТОЧКЕ НАЗНАЧЕНИЯ
    // -----------------------------------------------------

    float arrival =
        smoothstep(
            0.70,
            1.0,
            t
        );


    // Перед самым попаданием песчинку
    // начинает засасывать сильнее.

    position =
        lerp(
            position,
            destination,
            arrival * arrival
        );


    // -----------------------------------------------------
    // ТЕКУЩАЯ ПОЗИЦИЯ ПЕСЧИНКИ
    // -----------------------------------------------------

    float2 currentPx =
        position * uImageSize0;


    float2 pixelDelta =
        px - currentPx;


    // -----------------------------------------------------
    // ФОРМА ПЕСЧИНКИ
    // -----------------------------------------------------

    float distanceFromParticle =
        length(pixelDelta);


    float grain =
        1.0 -
        smoothstep(
            0.25,
            GrainPx * 0.55,
            distanceFromParticle
        );


    // Песчинка уменьшается прямо перед поглощением.
    float shrink =
        lerp(
            1.0,
            0.15,
            arrival
        );

    grain *= shrink;


    // -----------------------------------------------------
    // ПОЯВЛЕНИЕ ПЕСЧИНКИ
    // -----------------------------------------------------

    float particleVisible =
        smoothstep(
            0.0,
            0.08,
            t
        )
        *
        (1.0 - smoothstep(
            0.90,
            1.0,
            t
        ));


    // -----------------------------------------------------
    // ЦВЕТ ПЕСКА
    // -----------------------------------------------------

    float3 sand =
        uColor *
        (
            0.65 +
            rnd1 * 0.45
        );


    // -----------------------------------------------------
    // ВСПЫШКА В МОМЕНТ ПОГЛОЩЕНИЯ
    // -----------------------------------------------------

    float impact =
        smoothstep(
            0.78,
            0.94,
            t
        )
        *
        (1.0 - smoothstep(
            0.94,
            1.0,
            t
        ));


    // -----------------------------------------------------
    // ПРОЯВЛЕНИЕ НАСТОЯЩЕГО СПРАЙТА
    // -----------------------------------------------------

    //
    // Не показываем его сразу целиком.
    // Он постепенно проявляется после того,
    // как песчинки достигают своих мест.
    //

    float reveal =
        smoothstep(
            0.65,
            1.0,
            progress
        );


    // Дополнительная маска:
    // объект проявляется снизу вверх.
    float revealWave =
        smoothstep(
            0.0,
            0.35,
            progress -
            (1.0 - suv.y) * 0.55
        );


    float objectAlpha =
        targetAlpha *
        reveal *
        revealWave;


    // -----------------------------------------------------
    // ПЕСЧИНКА
    // -----------------------------------------------------

    float particleAlpha =
        grain *
        particleVisible *
        validParticle;


    // -----------------------------------------------------
    // ФИНАЛЬНЫЙ ЦВЕТ
    // -----------------------------------------------------

    float3 color =
        sand *
        particleAlpha;


    // Настоящий пиксель предмета.
    color +=
        target.rgb *
        objectAlpha;


    // Маленькая вспышка при попадании.
    color +=
        uColor *
        impact *
        validParticle *
        0.65;


    float alpha =
        particleAlpha +
        objectAlpha;


    alpha = saturate(alpha);


    return float4(
        color,
        alpha
    )
    *
    uOpacity
    *
    sampleColor;
}


technique Technique1
{
    pass ForgePass
    {
        PixelShader =
            compile ps_3_0 ForgePS();
    }
}