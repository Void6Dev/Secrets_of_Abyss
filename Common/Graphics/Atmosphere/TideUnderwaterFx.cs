using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Worldgen;

namespace SoA.Common.Graphics.Atmosphere
{
    // Экранный фильтр толщи воды (TideUnderwater.fx). Параметры берёт из TideAtmosphere,
    // включён, пока игрок в биоме. Чистая косметика на клиенте
    public class TideUnderwaterFx : ModSystem
    {
        public const string FilterKey = "SoA:TideUnderwater";

        // Свет с поверхности: днём тёплый и сильный, ночью слабый лунный
        private static readonly Color DaylightRayColor = new(255, 244, 214);
        private static readonly Color MoonlightRayColor = new(130, 160, 230);
        private const float NightShallowLight = 0.22f;
        private const float DawnShallowLight = 0.45f;   // у кромки дня солнце низко

        private static TideUnderwaterShaderData _shaderData;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _shaderData = new TideUnderwaterShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/TideUnderwater", AssetRequestMode.ImmediateLoad),
                "TidePass");
            Filters.Scene[FilterKey] = new Filter(_shaderData, EffectPriority.Medium);
        }

        public override void Unload()
        {
            TideUnderwaterShaderData shaderData = _shaderData;
            _shaderData = null;
            if (shaderData != null)
                Main.QueueMainThreadAction(shaderData.DisposeMask);
        }

        // Иначе фильтр переезжает включённым в следующий загруженный мир
        public override void OnWorldUnload()
        {
            if (!Main.dedServ && Filters.Scene[FilterKey]?.IsActive() == true)
                Filters.Scene.Deactivate(FilterKey);
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            Filter filter = Filters.Scene[FilterKey];
            if (!TideAtmosphere.IsActive)
            {
                if (filter.IsActive())
                    Filters.Scene.Deactivate(FilterKey);
                return;
            }

            if (!filter.IsActive())
                Filters.Scene.Activate(FilterKey);

            TideAtmosphereProfile profile = TideAtmosphere.Profile;
            float daylight = SurfaceDaylight();

            ScreenShaderData shader = filter.GetShader()
                .UseColor(profile.Tint)
                .UseSecondaryColor(Color.Lerp(MoonlightRayColor, DaylightRayColor, daylight).ToVector3())
                .UseIntensity(profile.TintStrength)
                .UseOpacity(TideAtmosphere.Presence);

            EffectParameterCollection parameters = shader.Shader.Parameters;
            parameters["uSubmerge"]?.SetValue(TideAtmosphere.Submersion);
            parameters["uVignette"]?.SetValue(profile.Vignette);
            parameters["uWarp"]?.SetValue(profile.Warp);
            parameters["uShallowLight"]?.SetValue(MathHelper.Lerp(NightShallowLight, 1f, daylight));
            parameters["uWaterTopY"]?.SetValue(TideOfShadowsWorldData.WaterTopY * 16f);
        }

        // 0 — ночь, 1 — полдень. У рассвета и заката лучи слабее
        private static float SurfaceDaylight()
        {
            if (!Main.dayTime)
                return 0f;

            float dayProgress = (float)(Main.time / Main.dayLength);
            float sunHeight = (float)Math.Sin(dayProgress * Math.PI);
            return MathHelper.Lerp(DawnShallowLight, 1f, sunHeight);
        }
    }

    // Видимую часть мира ставим в момент отрисовки, а не в тике: камера двигается уже
    // после обновления, и параметр из тика отставал бы на кадр — узор съезжал бы на ходу
    //
    // Заодно собирает маску воды: тексель на тайл видимой области. Лучи идут только в воде,
    // а уровень воды в мире — не зеркало с генерации: вода оседает, есть суша и пещеры
    public class TideUnderwaterShaderData : ScreenShaderData
    {
        private const int MaskMarginTiles = 2;
        private const int MaskSampler = 1;      // uImage1 в шейдере
        private const int SmoothMaskSampler = 2; // uImage2 — та же маска со сглаживанием

        // Свет с поверхности: сверху вниз под наклоном лучей шейдера (RaySlant в .fx),
        // гаснет на сплошных блоках. Чтобы крыша за верхним краем кадра тоже давала тень,
        // свет начинают вести с запасом выше видимого
        private const int SunlightLookAboveTiles = 48;
        private const float RaySlant = 0.3f;
        private const float SunlightSideSpread = 0.12f;   // доля света от соседей: мягкий край тени

        private Texture2D _waterMask;
        private Color[] _maskPixels = Array.Empty<Color>();
        private float[] _sunlight = Array.Empty<float>();
        private float[] _sunlightNext = Array.Empty<float>();

        public TideUnderwaterShaderData(Asset<Effect> shader, string passName) : base(shader, passName)
        {
        }

        public override void Apply()
        {
            Vector2 screenPixels = new(Main.screenWidth, Main.screenHeight);
            Vector2 worldViewSize = screenPixels / Main.GameViewMatrix.Zoom;
            Vector2 worldTopLeft = Main.screenPosition + (screenPixels - worldViewSize) / 2f;

            Point maskOrigin = new((int)(worldTopLeft.X / 16f) - MaskMarginTiles, (int)(worldTopLeft.Y / 16f) - MaskMarginTiles);
            int maskWidth = (int)(worldViewSize.X / 16f) + MaskMarginTiles * 2 + 1;
            int maskHeight = (int)(worldViewSize.Y / 16f) + MaskMarginTiles * 2 + 1;
            FillWaterMask(maskOrigin, maskWidth, maskHeight);

            EffectParameterCollection parameters = Shader.Parameters;
            parameters["uWorldTopLeft"]?.SetValue(worldTopLeft);
            parameters["uWorldViewSize"]?.SetValue(worldViewSize);
            parameters["uScreenPixels"]?.SetValue(screenPixels);
            parameters["uMaskOriginTiles"]?.SetValue(maskOrigin.ToVector2());
            parameters["uMaskSizeTiles"]?.SetValue(new Vector2(maskWidth, maskHeight));
            base.Apply();

            // Одна и та же маска дважды: точечно — где ровно кончается вода (граница тайла
            // настоящая), со сглаживанием — для плавных величин: света сверху, близости воды.
            // По точечной маске они шли бы ступеньками в тайл
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            device.Textures[MaskSampler] = _waterMask;
            device.SamplerStates[MaskSampler] = SamplerState.PointClamp;
            device.Textures[SmoothMaskSampler] = _waterMask;
            device.SamplerStates[SmoothMaskSampler] = SamplerState.LinearClamp;
        }

        // R — доля тайла, залитая водой (у сплошного блока 0: вода за ним не видна;
        //     у скоса и половинки вода видна в пустой части, поэтому они считаются водой).
        // G — твёрдый блок, над которым вода: верх дна, на нём играют каустики.
        // B — сколько солнечного света дошло до тайла: под крышей, палубой, сводом пещеры 0
        private void FillWaterMask(Point origin, int width, int height)
        {
            if (_waterMask == null || _waterMask.IsDisposed || _waterMask.Width != width || _waterMask.Height != height)
            {
                _waterMask?.Dispose();
                _waterMask = new Texture2D(Main.graphics.GraphicsDevice, width, height);
                _maskPixels = new Color[width * height];
                _sunlight = new float[width];
                _sunlightNext = new float[width];
            }

            Array.Fill(_sunlight, 1f);
            for (int tileY = origin.Y - SunlightLookAboveTiles; tileY < origin.Y; tileY++)
                PropagateSunlight(origin.X, tileY, width);

            for (int y = 0; y < height; y++)
            {
                int tileY = origin.Y + y;
                PropagateSunlight(origin.X, tileY, width);

                for (int x = 0; x < width; x++)
                {
                    int tileX = origin.X + x;
                    Tile tile = Framing.GetTileSafely(tileX, tileY);
                    byte water = 0;
                    byte seabed = 0;

                    if (IsFullBlock(tile))
                    {
                        Tile above = Framing.GetTileSafely(tileX, tileY - 1);
                        if (!IsSolid(above) && WaterAmount(above) > 0)
                            seabed = 255;
                    }
                    else
                    {
                        water = WaterAmount(tile);
                    }

                    byte sunlight = (byte)(_sunlight[x] * 255f);
                    _maskPixels[y * width + x] = new Color(water, seabed, sunlight, (byte)255);
                }
            }

            _waterMask.SetData(_maskPixels);
        }

        // Ряд света из предыдущего: каждый тайл берёт свет с той точки выше, откуда к нему
        // приходит наклонный луч, и чуть-чуть от соседей — за краем крыши тень мягкая.
        // Сплошной блок свет обрезает
        private void PropagateSunlight(int originX, int tileY, int width)
        {
            for (int x = 0; x < width; x++)
            {
                float source = SampleSunlight(x + RaySlant, width);
                float sides = (SampleSunlight(x - 1, width) + SampleSunlight(x + 1, width)) * 0.5f;
                float light = MathHelper.Lerp(source, sides, SunlightSideSpread);

                if (IsSolid(Framing.GetTileSafely(originX + x, tileY)))
                    light = 0f;
                _sunlightNext[x] = light;
            }
            (_sunlight, _sunlightNext) = (_sunlightNext, _sunlight);
        }

        private float SampleSunlight(float x, int width)
        {
            float clamped = Math.Clamp(x, 0f, width - 1);
            int left = (int)clamped;
            int right = Math.Min(left + 1, width - 1);
            return MathHelper.Lerp(_sunlight[left], _sunlight[right], clamped - left);
        }

        private static bool IsSolid(Tile tile)
            => tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];

        private static bool IsFullBlock(Tile tile)
            => IsSolid(tile) && tile.Slope == SlopeType.Solid && !tile.IsHalfBlock;

        private static byte WaterAmount(Tile tile)
            => tile.LiquidType == LiquidID.Water ? tile.LiquidAmount : (byte)0;

        public void DisposeMask()
        {
            _waterMask?.Dispose();
            _waterMask = null;
        }
    }
}
