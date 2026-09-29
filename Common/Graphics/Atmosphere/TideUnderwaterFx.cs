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

        private Texture2D _waterMask;
        private Color[] _maskPixels = Array.Empty<Color>();

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

            GraphicsDevice device = Main.graphics.GraphicsDevice;
            device.Textures[MaskSampler] = _waterMask;
            device.SamplerStates[MaskSampler] = SamplerState.PointClamp;
        }

        // R — доля тайла, залитая водой (у твёрдого блока 0: вода за ним не видна).
        // G — твёрдый блок, над которым вода: верх дна, на нём играют каустики
        private void FillWaterMask(Point origin, int width, int height)
        {
            if (_waterMask == null || _waterMask.IsDisposed || _waterMask.Width != width || _waterMask.Height != height)
            {
                _waterMask?.Dispose();
                _waterMask = new Texture2D(Main.graphics.GraphicsDevice, width, height);
                _maskPixels = new Color[width * height];
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int tileX = origin.X + x;
                    int tileY = origin.Y + y;
                    Tile tile = Framing.GetTileSafely(tileX, tileY);
                    byte water = 0;
                    byte seabed = 0;

                    if (IsSolid(tile))
                    {
                        Tile above = Framing.GetTileSafely(tileX, tileY - 1);
                        if (!IsSolid(above) && WaterAmount(above) > 0)
                            seabed = 255;
                    }
                    else
                    {
                        water = WaterAmount(tile);
                    }

                    _maskPixels[y * width + x] = new Color(water, seabed, (byte)0, (byte)255);
                }
            }

            _waterMask.SetData(_maskPixels);
        }

        private static bool IsSolid(Tile tile)
            => tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];

        private static byte WaterAmount(Tile tile)
            => tile.LiquidType == LiquidID.Water ? tile.LiquidAmount : (byte)0;

        public void DisposeMask()
        {
            _waterMask?.Dispose();
            _waterMask = null;
        }
    }
}
