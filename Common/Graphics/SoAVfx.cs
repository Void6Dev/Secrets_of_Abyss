using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using SoA.Common.Graphics.SandFormation;

namespace SoA.Common.Graphics
{
    // Общие визуальные примитивы поверх готовых шейдеров мода (SoA:CrabAura / SoA:CrabRing)
    // и переиспользуемых текстур. Инкапсулируют свап спрайтбатча в Immediate+Additive и обратно,
    // чтобы вызывающие (босс, снаряды) не дублировали Begin/End-бойлерплейт.
    public static class SoAVfx
    {
        private static Asset<Texture2D> _blob;
        private static Asset<Texture2D> _noise;
        private static Asset<Texture2D> _softGlow;
        private static Asset<Texture2D> _softStreak;
        private static Asset<Texture2D> _trailNoise;
        private static bool _softGlowChecked;
        private static bool _softStreakChecked;
        private static bool _trailNoiseChecked;
        private static Texture2D _quad;

        // BeamDistortion.png — звезда-вспышка с лучами, а НЕ мягкое пятно. Раньше ею
        // рисовалось любое свечение, отсюда колючий вид эффектов. Теперь она нужна только
        // там, где вспышка задумана (аура SoA:CrabAura крутит именно её, луч Sharded Spear)
        // и как запасной вариант, пока нет текстур из Docs/SpriteSpecs.md, раздел «VFX».
        public static Texture2D Blob =>
            (_blob ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/BeamDistortion", AssetRequestMode.ImmediateLoad)).Value;

        // Мягкое круглое пятно — свечения, капли, ореолы (DrawTintedGlow)
        public static Texture2D SoftGlow =>
            LoadOptional(ref _softGlow, ref _softGlowChecked, "SoA/Assets/Textures/Vfx/SoftGlow");

        // Мягкий вытянутый штрих — линии, блики, ореол древка, дуги (DrawTintedQuad, DrawTravellingArcs)
        public static Texture2D SoftStreak =>
            LoadOptional(ref _softStreak, ref _softStreakChecked, "SoA/Assets/Textures/Vfx/SoftStreak");

        // Шум ленты SoATrail: горизонтальные струи, бесшовный. Серым по чёрному — шейдер
        // читает яркость. Без файла лента берёт общий WaveNoise
        public static Texture2D TrailNoise =>
            LoadOptional(ref _trailNoise, ref _trailNoiseChecked, "SoA/Assets/Textures/Vfx/TrailNoise", Noise);

        // Белый квад для шейдеров с процедурной формой: они текстуру не читают, им нужна
        // только геометрия с uv 0..1 на всю площадь. Создаётся кодом — рисовать тут нечего.
        // MagicPixel не годится: у него uv не на всю текстуру (см. память про source rect)
        public static Texture2D Quad
        {
            get
            {
                if (_quad == null || _quad.IsDisposed)
                {
                    _quad = new Texture2D(Main.graphics.GraphicsDevice, 4, 4);
                    Color[] white = new Color[16];
                    System.Array.Fill(white, Color.White);
                    _quad.SetData(white);
                }
                return _quad;
            }
        }

        // Текстура, которую пользователь ещё может не нарисовать: пока файла нет, рисуем
        // старой звездой, чтобы эффект не пропал. Проверяем один раз за загрузку мода
        private static Texture2D LoadOptional(ref Asset<Texture2D> asset, ref bool checkedOnce, string path,
            Texture2D fallback = null)
        {
            if (!checkedOnce)
            {
                checkedOnce = true;
                ModContent.RequestIfExists(path, out asset, AssetRequestMode.ImmediateLoad);
            }
            return asset?.Value ?? fallback ?? Blob;
        }

        public static Texture2D Noise =>
            (_noise ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/WaveNoise", AssetRequestMode.ImmediateLoad)).Value;

        // Настоящий аддитив для премультиплицированного цвета: источник * 1 + приёмник * 1.
        // Ванильный BlendState.Additive умножает источник на ЕГО ЖЕ альфу (SourceAlpha), поэтому
        // принятое в моде свечение «цвет с A = 0» в нём не рисовалось вообще: вспышки, телеграфы
        // (тень слэма, линия рывка, маркер приземления, столб бреши), глаза, аура ярости, тело
        // ударной волны, дуги броска копья — всё умножалось на ноль. Цвет с альфой здесь тоже
        // корректен: премультиплицированный rgb просто складывается с кадром.
        private static BlendState _glowBlend;
        public static BlendState GlowBlend => _glowBlend ??= new BlendState
        {
            ColorSourceBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            AlphaDestinationBlend = Blend.One,
        };

        // Локальное преобразование поверх камеры: все Begin/End этого класса его соблюдают, поэтому
        // эффекты, которые сами переключают батч посреди отрисовки сущности (глоу, аура), не
        // выпадают из него. Сейчас им пользуется разворот короля (силуэт раскрывается из узкого).
        // Экранные пиксели; по умолчанию Identity. Ставить и снимать только парой
        // BeginLocalTransform / EndLocalTransform
        private static Matrix _localTransform = Matrix.Identity;

        private static Matrix WorldTransform => _localTransform * Main.GameViewMatrix.TransformationMatrix;

        public static void BeginLocalTransform(SpriteBatch sb, Matrix screenSpaceTransform)
        {
            sb.End();
            _localTransform = screenSpaceTransform;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null, WorldTransform);
        }

        public static void EndLocalTransform(SpriteBatch sb)
        {
            sb.End();
            _localTransform = Matrix.Identity;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null, WorldTransform);
        }

        // Сжатие по горизонтали вокруг точки мира: scaleX = 1 — без изменений
        public static Matrix HorizontalSquash(Vector2 worldCenter, float scaleX)
        {
            Vector2 c = worldCenter - Main.screenPosition;
            return Matrix.CreateTranslation(-c.X, -c.Y, 0f) * Matrix.CreateScale(scaleX, 1f, 1f)
                * Matrix.CreateTranslation(c.X, c.Y, 0f);
        }

        // Свап в аддитивный Immediate-режим (для шейдеров/свечения) и обратно в обычную отрисовку.
        // Пара строго симметрична: на каждый BeginAdditive — свой EndAdditive.
        public static void BeginAdditive(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, GlowBlend, null, null, null, null,
                WorldTransform);
        }

        public static void EndAdditive(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                WorldTransform);
        }

        // Свап в Immediate + обычный AlphaBlend — для шейдеров непрозрачной пыли/дыма,
        // где аддитив дал бы «свечение». Закрывать тем же EndAdditive (он восстанавливает Deferred).
        public static void BeginAlphaImmediate(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, null, null, null, null,
                WorldTransform);
        }

        // То же самое, но с точечной фильтрацией и в паре с EndPixelBatch — для
        // пиксель-артных эффектов. Обычная пара Begin/EndAdditive оставляет сэмплер
        // по умолчанию (линейный), от которого мелкие детали мылятся.
        public static void BeginPixelImmediate(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                WorldTransform);
        }

        // Возврат в состояние ванильного батча сущностей
        public static void EndPixelBatch(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                WorldTransform);
        }

        // Приливная аура через SoA:CrabAura (каустика расходящимися кольцами + пузыри).
        // sizePx — диаметр в мире. Вызывать внутри BeginAdditive/EndAdditive.
        // blend: 0 — холодная вода, 1 — мутный песок.
        public static void DrawGlow(SpriteBatch sb, Vector2 worldPos, float sizePx, float opacity, float blend = 0f)
        {
            MiscShaderData shader = GameShaders.Misc["SoA:CrabAura"];
            shader.UseOpacity(opacity);
            shader.Shader.Parameters["uBlend"]?.SetValue(blend);
            shader.Apply();
            Texture2D tex = Blob;
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, Color.White, 0f,
                tex.Size() / 2f, new Vector2(sizePx / tex.Width, sizePx / tex.Height), SpriteEffects.None, 0);
        }

        // Плоский аддитивный блоб произвольного цвета (без шейдера) — для цветных аур
        // (напр. красный пульс «треснувшего панциря»). Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawTintedGlow(SpriteBatch sb, Vector2 worldPos, Vector2 sizePx, Color color)
        {
            Texture2D tex = SoftGlow;
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, color, 0f,
                tex.Size() / 2f, new Vector2(sizePx.X / tex.Width, sizePx.Y / tex.Height), SpriteEffects.None, 0);
        }

        // Плоский квад произвольного цвета С ПОВОРОТОМ — для вытянутых форм: трещин на грунте,
        // линии рывка, столба света. Рисовать можно в любом батче: цвет с альфой даёт
        // непрозрачную форму, цвет с A=0 — аддитивное свечение.
        public static void DrawTintedQuad(SpriteBatch sb, Vector2 worldPos, Vector2 sizePx, float rotation, Color color)
        {
            Texture2D tex = SoftStreak;
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, color, rotation,
                tex.Size() / 2f, new Vector2(sizePx.X / tex.Width, sizePx.Y / tex.Height), SpriteEffects.None, 0);
        }

        // Свап в аддитивный Immediate-режим с наложенным SoA:CrabRage. Шейдер остаётся
        // привязанным на весь батч, поэтому все части босса, нарисованные следом, горят
        // одинаково — одним Apply, без per-part настройки. Закрывать EndAdditive.
        public static void BeginRageOverlay(SpriteBatch sb, float intensity)
        {
            BeginAdditive(sb);
            MiscShaderData shader = GameShaders.Misc["SoA:CrabRage"];
            shader.UseOpacity(intensity);
            shader.Apply();
        }

        // Ударная волна через SoA:CrabRing: фронт с язычками песка, к концу расширения
        // рассыпается в крупинки. progress 0..1 = радиус фронта.
        // blend: 0 — пена, 1 — песок. Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawRing(SpriteBatch sb, Vector2 worldPos, float sizePx, float progress, float opacity, float blend = 0f)
        {
            MiscShaderData shader = GameShaders.Misc["SoA:CrabRing"];
            shader.UseOpacity(opacity);
            shader.Shader.Parameters["uProgress"]?.SetValue(progress);
            shader.Shader.Parameters["uBlend"]?.SetValue(blend);
            shader.Apply();
            Texture2D tex = Quad; // RingPass текстуру не читает
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, Color.White, 0f,
                tex.Size() / 2f, new Vector2(sizePx / tex.Width, sizePx / tex.Height), SpriteEffects.None, 0);
        }

        // Шлейф скорости (SoA:SpeedRush) — конус штрихов позади разогнанного снаряда.
        // worldPos — центр квада, rotation — направление движения (квад «остриём» вперёд),
        // lengthPx/widthPx — габариты шлейфа в мире, intensity 0..1 — превышение порога.
        // Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawSpeedRush(SpriteBatch sb, Vector2 worldPos, float rotation,
            float lengthPx, float widthPx, float intensity, float opacity)
        {
            MiscShaderData rush = GameShaders.Misc["SoA:SpeedRush"];
            rush.UseOpacity(opacity);
            rush.Shader.Parameters["uProgress"]?.SetValue(intensity);
            rush.Apply();

            Texture2D tex = Quad; // RushPass текстуру не читает
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, Color.White, rotation,
                tex.Size() / 2f, new Vector2(lengthPx / tex.Width, widthPx / tex.Height),
                SpriteEffects.None, 0);
        }

        // Две дуги, бегущие вдоль оси: выгибаются в стороны от неё и несут по себе
        // светящуюся голову. Используется как «энергия скорости» на древке копья.
        // center — середина оси, rotation — её направление, lengthPx — длина,
        // bowPx — насколько дуги отходят вбок, phase 0..1 — положение бегущей головы.
        // Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawTravellingArcs(SpriteBatch sb, Vector2 center, float rotation,
            float lengthPx, float bowPx, float phase, Color color, float thickness = 5f)
        {
            const int Segments = 18;

            Vector2 axis = rotation.ToRotationVector2();
            Vector2 normal = new(-axis.Y, axis.X);
            Texture2D tex = SoftStreak;

            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 previous = Vector2.Zero;
                for (int i = 0; i <= Segments; i++)
                {
                    float t = i / (float)Segments;
                    float bow = (float)System.Math.Sin(t * MathHelper.Pi);
                    Vector2 point = center + axis * ((t - 0.5f) * lengthPx)
                        + normal * (side * bow * bowPx);

                    if (i == 0)
                    {
                        previous = point;
                        continue;
                    }

                    // Бегущая голова: яркое пятно едет от пятки к острию, хвост за ним тускнеет
                    float head = t - phase;
                    head -= (float)System.Math.Floor(head);
                    float glow = 0.25f + 0.75f * (float)System.Math.Pow(1f - head, 6f);

                    Vector2 step = point - previous;
                    float segmentLength = step.Length();
                    Vector2 mid = previous + step * 0.5f;
                    previous = point;

                    Main.EntitySpriteDraw(tex, mid - Main.screenPosition, null, color * (glow * bow),
                        step.ToRotation(), tex.Size() / 2f,
                        new Vector2((segmentLength + 2f) / tex.Width, thickness / tex.Height),
                        SpriteEffects.None, 0);
                }
            }
        }

        // Цвет приливного песка: тёплое зерно с холодным отливом, чтобы сборка читалась
        // как часть приливной темы, а не как обычная пустынная пыль
        public static readonly Color TideSand = new(214, 192, 142);

        // Спрайт, собирающийся из песка. progress: 0 — россыпь зёрен, 1 — цельный предмет.
        // Тот же вызов с убывающим progress рассыпает предмет обратно.
        //
        // Зёрна живут в SandFormationEffect: их тикает SandFormationSystem, здесь только
        // выдаётся прогресс и отрисовка. key — стабильный локальный идентификатор владельца
        // (обычно Projectile.whoAmI), seed — синхронное по сети число (Projectile.identity):
        // по нему клиенты собирают одинаковую россыпь, не пересылая ни одной частицы.
        // Батч не свапает: вызывать прямо из PreDraw.
        public static void DrawSandForged(SpriteBatch sb, Texture2D tex, Vector2 drawPos,
            Color color, float rotation, Vector2 origin, float scale, float progress,
            int key = 0, int seed = 0)
        {
            SandFormationEffect effect = SandFormationEffect.Attach(key, tex, seed);
            effect.FormationProgress = progress;
            effect.Draw(sb, drawPos, color, rotation, origin, scale);
        }

        // Шум в слот s1 с wrap-сэмплером. Ставится ПОСЛЕ Apply: шейдер сам слот не занимает,
        // но порядок повторяет FireTornadoProjectile — там это уже проверено в игре
        public static void BindNoise()
        {
            Main.graphics.GraphicsDevice.Textures[1] = Noise;
            Main.graphics.GraphicsDevice.SamplerStates[1] = SamplerState.LinearWrap;
        }
    }
}
