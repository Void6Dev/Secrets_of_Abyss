using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Common.Graphics
{
    public enum GeyserStyle
    {
        Strong,
        Weak,
        Hot
    }

    // Гейзер целиком: спрайтовая струя плюс слои эффектов вокруг неё.
    //
    // СИЛУЭТ ЗАДАЁТ СПРАЙТ. TideGeyser.fx его только оживляет — течение внутри
    // массы, бегущие блики, срывающиеся с кромки капли. Искажение квантуется в
    // целые пиксели листа и ограничено парой пикселей, поэтому форма струи
    // остаётся ровно той, что нарисована.
    //
    // ЛИСТ СТРУИ: Assets/Textures/TideGeyser.png — 8 кадров 64x128, кадры сверху вниз,
    // НИЗ кадра = устье (линия земли), струя растёт вверх, центр струи по центру кадра.
    // Кадры — полный цикл: 1 вспучивание, 2-3 подъём, 4 пик, 5-6 спад, 7-8 оседание.
    //
    // Слои рисуются снизу вверх по глубине:
    //   растекание по земле → взвесь → СТРУЯ → удар → капли (два слоя скоростей).
    // Всё это один батч Immediate: свап делает Draw, вызывающему хватает одной строки.
    public static class TideGeyserFx
    {
        public const int Frames = 8;
        public const int FrameWidth = 64;
        public const int FrameHeight = 128;

        // Размер «пикселя» процедурных слоёв в мире. По нему считается сетка
        // квантования: без неё вместо пиксель-арта получается гладкий туман
        private const float PixelSize = 2f;

        // Габариты слоёв в долях от ширины струи и положение линии земли внутри
        // квада (0 — верх квада, 1 — низ). Всё, что выше линии, летит вверх
        private const float PuddleWidth = 5f, PuddleHeight = 0.6f, PuddleGround = 0.30f;
        private const float MistWidth = 4.2f, MistHeight = 1.6f, MistGround = 0.80f;
        private const float SplashWidth = 2.8f, SplashHeight = 1.2f, SplashGround = 0.74f;
        // Квад капель держим тесным: пасс разворачивает цикл в полторы тысячи
        // инструкций, и лишняя площадь стоит дороже всего остального эффекта
        private const float DropWidth = 3f, DropHeight = 1.9f, DropGround = 0.82f;

        // Высота струи по кадру: по ней же считается хитбокс, чтобы урон совпадал
        // с картинкой, а не бил во всю колонну, пока струя ещё вспучивается
        private static readonly float[] FrameJetHeight = { 0.28f, 0.62f, 0.86f, 1f, 0.82f, 0.55f, 0.3f, 0.12f };

        private static Asset<Texture2D> _sheet;

        public static Texture2D Sheet =>
            (_sheet ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/TideGeyser",
                AssetRequestMode.ImmediateLoad)).Value;

        public static int FrameOf(float progress)
            => (int)MathHelper.Clamp(progress * Frames, 0f, Frames - 1);

        public static float JetHeight(float progress) => FrameJetHeight[FrameOf(progress)];

        // basePos — устье (точка удара), widthPx — ширина струи в мире.
        // seed — стабильное по сети число (например Projectile.identity): по нему
        // раскладываются капли и асимметрия, поэтому на всех клиентах картина одна
        public static void Draw(SpriteBatch sb, Vector2 basePos, float progress,
            GeyserStyle style, float widthPx, float opacity = 1f, int seed = 0)
            => Draw(sb, basePos, progress, TideGeyserSettings.For(style), Tint(style), widthPx, opacity, seed);

        public static void Draw(SpriteBatch sb, Vector2 basePos, float progress,
            in TideGeyserSettings settings, Color tint, float widthPx, float opacity, int seed)
        {
            if (progress <= 0f || opacity <= 0f)
                return;

            float jet = JetHeight(progress);
            Vector2 screen = basePos - Main.screenPosition;

            SoAVfx.BeginPixelImmediate(sb);

            DrawLayer("SoA:TidePuddle", screen, new Vector2(widthPx * PuddleWidth, widthPx * PuddleHeight),
                PuddleGround, settings, tint, progress, jet, opacity, seed);

            DrawLayer("SoA:TideMist", screen, new Vector2(widthPx * MistWidth, widthPx * MistHeight),
                MistGround, settings, tint, progress, jet, opacity, seed);

            DrawColumn(screen, widthPx, settings, tint, progress, jet, opacity, seed);

            DrawLayer("SoA:TideSplash", screen, new Vector2(widthPx * SplashWidth, widthPx * SplashHeight),
                SplashGround, settings, tint, progress, jet, opacity, seed);

            // Два слоя капель с разной скоростью: крупные баллистические и мелкая
            // быстрая пыль. Разделение обязательно — на одной скорости всё
            // сваливается в однородный «партикль-систем»
            Vector2 dropSize = new(widthPx * DropWidth, widthPx * DropHeight);
            DrawLayer("SoA:TideDroplets", screen, dropSize, DropGround,
                settings, tint, progress, jet, opacity, seed);
            DrawLayer("SoA:TideDroplets", screen, dropSize, DropGround,
                MicroSpray(settings), tint, progress, jet, opacity, seed + 7);

            SoAVfx.EndPixelBatch(sb);
        }

        // Струя: спрайт с наложенным ColumnPass. Кадр вырезается source-прямоугольником,
        // поэтому шейдеру передаётся его положение в листе — иначе искажение
        // подтянет соседний кадр спрайтшита
        private static void DrawColumn(Vector2 screen, float widthPx, in TideGeyserSettings settings,
            Color tint, float progress, float jet, float opacity, int seed)
        {
            Texture2D sheet = Sheet;
            var frame = new Rectangle(0, FrameOf(progress) * FrameHeight, FrameWidth, FrameHeight);

            MiscShaderData shader = Prepare("SoA:TideColumn", settings, tint, progress, jet, opacity, seed,
                new Vector2(widthPx, widthPx * FrameHeight / FrameWidth), SplashGround);
            shader.Shader.Parameters["uFrame"]?.SetValue(new Vector4(
                frame.X / (float)sheet.Width, frame.Y / (float)sheet.Height,
                frame.Width / (float)sheet.Width, frame.Height / (float)sheet.Height));
            shader.Apply();
            SoAVfx.BindNoise();

            // Тон и прозрачность идут через uColor/uOpacity, поэтому вершинный цвет белый:
            // иначе всё умножится дважды
            Main.EntitySpriteDraw(sheet, screen, frame, Color.White, 0f,
                new Vector2(FrameWidth / 2f, FrameHeight), widthPx / FrameWidth, SpriteEffects.None, 0);
        }

        // Процедурный слой: квад нужного размера, вся форма считается в шейдере.
        // Квад сдвигается так, чтобы линия земли слоя легла ровно на устье
        private static void DrawLayer(string shaderKey, Vector2 screen, Vector2 sizePx, float ground,
            in TideGeyserSettings settings, Color tint, float progress, float jet, float opacity, int seed)
        {
            MiscShaderData shader = Prepare(shaderKey, settings, tint, progress, jet, opacity, seed, sizePx, ground);
            shader.Apply();
            SoAVfx.BindNoise();

            Texture2D quad = SoAVfx.Blob;
            Vector2 center = screen + new Vector2(0f, (0.5f - ground) * sizePx.Y);
            Main.EntitySpriteDraw(quad, center, null, Color.White, 0f, quad.Size() / 2f,
                new Vector2(sizePx.X / quad.Width, sizePx.Y / quad.Height), SpriteEffects.None, 0);
        }

        // Все настройки ставятся всем пассам: лишние они просто не читают, зато
        // не надо держать в голове, какому слою что нужно
        private static MiscShaderData Prepare(string shaderKey, in TideGeyserSettings settings, Color tint,
            float progress, float jet, float opacity, int seed, Vector2 sizePx, float ground)
        {
            MiscShaderData shader = GameShaders.Misc[shaderKey];
            shader.UseColor(tint);
            shader.UseOpacity(opacity);

            EffectParameterCollection p = shader.Shader.Parameters;
            p["uCommon"]?.SetValue(new Vector4(progress, jet, settings.Intensity, SeedValue(seed)));
            p["uGrid"]?.SetValue(new Vector4(FrameWidth, FrameHeight,
                MathHelper.Max(sizePx.X / PixelSize, 1f), MathHelper.Max(sizePx.Y / PixelSize, 1f)));
            p["uFlow"]?.SetValue(settings.Flow);
            p["uShine"]?.SetValue(settings.Shine);
            p["uImpact"]?.SetValue(settings.Impact(ground));
            p["uSplash"]?.SetValue(settings.Splash);
            p["uParticle"]?.SetValue(settings.Particle);
            p["uParticleShape"]?.SetValue(settings.ParticleShape);
            p["uPuddle"]?.SetValue(settings.Puddle);
            p["uRipple"]?.SetValue(settings.Ripple);
            p["uMist"]?.SetValue(settings.Mist);
            p["uMistShape"]?.SetValue(settings.MistShape);

            return shader;
        }

        // Второй слой капель: мельче, быстрее, живут меньше и разлетаются шире
        private static TideGeyserSettings MicroSpray(in TideGeyserSettings settings)
        {
            TideGeyserSettings spray = settings;
            spray.ParticleSize = 1f;
            spray.ParticleSpeed *= 1.7f;
            spray.ParticleLifetime *= 0.5f;
            spray.ParticleSpread *= 1.35f;
            spray.ParticleGravity *= 0.8f;
            return spray;
        }

        // Seed в вид, удобный хешам шейдера: небольшое дробное число
        private static float SeedValue(int seed) => (seed & 1023) * 0.0173f;

        // Свет и звуковой «вес» гейзера. Ванильную пыль отсюда убрали: круглые
        // спрайты дастов ломали пиксельный язык эффекта, а брызги, капли и взвесь
        // теперь целиком живут в шейдере
        public static void Emit(Vector2 basePos, float progress, GeyserStyle style, float widthPx)
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            float jet = JetHeight(progress);

            Vector3 light = style == GeyserStyle.Hot
                ? new Vector3(0.55f, 0.45f, 0.4f)
                : new Vector3(0.25f, 0.45f, 0.6f);
            Lighting.AddLight(basePos - new Vector2(0f, jet * 40f), light * (0.5f + jet * 0.5f));
        }

        private static Color Tint(GeyserStyle style) => style switch
        {
            GeyserStyle.Hot => new Color(255, 235, 225),
            GeyserStyle.Weak => new Color(225, 245, 255),
            _ => Color.White
        };
    }
}
