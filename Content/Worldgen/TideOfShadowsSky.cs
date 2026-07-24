using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace SoA.Content.Worldgen
{
    // Сумрак над Приливом Теней: небо темнеет и уходит в фиолет,
    // облака полупрозрачные. Включается из TideOfShadowsBiome.SpecialVisuals
    public class TideOfShadowsSky : CustomSky
    {
        public const string Key = "SoA:TideSky";

        private const float FadeSpeed = 0.012f;
        private const float MaxOverlayAlpha = 0.32f;
        private static readonly Color OverlayColor = new(24, 14, 52);
        private static readonly Vector4 DuskTint = new(0.66f, 0.58f, 0.92f, 1f);

        private bool _active;
        private float _intensity;

        public override void Update(GameTime gameTime)
        {
            _intensity = _active
                ? Math.Min(1f, _intensity + FadeSpeed)
                : Math.Max(0f, _intensity - FadeSpeed);
        }

        public override Color OnTileColor(Color inColor)
        {
            // Солнечный свет холодеет вместе с небом
            Vector4 baseColor = inColor.ToVector4();
            return new Color(Vector4.Lerp(baseColor, baseColor * DuskTint, _intensity));
        }

        public override void Draw(SpriteBatch spriteBatch, float minDepth, float maxDepth)
        {
            // Рисуем один раз на самом дальнем слое, поверх ванильного неба
            if (maxDepth < float.MaxValue || minDepth >= float.MaxValue)
                return;

            spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle(0, 0, Main.screenWidth, Main.screenHeight),
                OverlayColor * (_intensity * MaxOverlayAlpha));
        }

        public override float GetCloudAlpha() => 1f - _intensity * 0.6f;

        public override void Activate(Vector2 position, params object[] args) => _active = true;

        public override void Deactivate(params object[] args) => _active = false;

        public override void Reset() => _active = false;

        public override bool IsActive() => _active || _intensity > 0f;
    }

    public class TideOfShadowsSkySystem : ModSystem
    {
        public override void Load()
        {
            if (Main.dedServ)
                return;

            // ManageSpecialBiomeVisuals требует под одним ключом и Filter, и Sky —
            // без фильтра ваниль падает с NRE при входе в зону
            Filters.Scene[TideOfShadowsSky.Key] = new Filter(
                new ScreenShaderData("FilterMiniTower")
                    .UseColor(0.10f, 0.05f, 0.22f)
                    .UseOpacity(0.18f),
                EffectPriority.High);
            SkyManager.Instance[TideOfShadowsSky.Key] = new TideOfShadowsSky();
        }
    }
}
