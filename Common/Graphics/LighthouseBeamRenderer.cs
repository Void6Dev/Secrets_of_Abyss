using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using SoA.Content.Tiles.Furniture;
using SoA.Content.Tiles.Lighthouse;
using SoA.Content.Tiles.Shrines;

namespace SoA.Common.Graphics
{
    // Лучи маяков. Рисуются из системы, а не из PreDraw лампы: отрисовка тайлов
    // кэшируется на несколько кадров, и вращающийся луч в ней дёргался бы.
    // Луч упирается в первый блок, который держит свет: стекло фонарной комнаты
    // он проходит, скалу — нет, блок под активатором — проходит. Вдоль луча ставится настоящий свет — он освещает
    // воду и берег, а не только рисуется поверх
    public class LighthouseBeamRenderer : ModSystem
    {
        // Маяк виден через полкарты: луч должен доставать далеко в море
        private const float MaxBeamLengthPx = 240f * 16f;
        private const float RayStepPx = 8f;
        private const float LampClearancePx = 24f;    // свои тайлы лампы луч не проверяет
        // Конус задан углом раскрытия, а не шириной на конце: короткий луч в стену
        // тогда такой же широкий у стены, как длинный на той же дистанции.
        // 0.42 px на px длины — около 23° полного раствора
        private const float StartWidthPx = 34f;       // у лампы — примерно ширина самой лампы
        private const float WidthPerLengthPx = 0.42f;
        private const float DisplayTurnLerp = 0.2f;

        private const float DayOpacity = 0.3f;
        private const float NightOpacity = 1f;
        private const float GlowSizePx = 70f;

        private const float LightStepPx = 48f;
        private const float LightStrength = 0.9f;
        private const float LightSideShare = 0.3f;     // боковые огни — на трети ширины от оси
        private const float LightSideStrength = 0.6f;
        private const float ScreenMarginPx = MaxBeamLengthPx;

        private static readonly Color BeamColor = new(255, 228, 165);
        private static readonly Vector3 BeamLight = new(1f, 0.88f, 0.6f);

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            foreach (TileEntity entity in TileEntity.ByID.Values)
            {
                if (entity is not LighthouseLampEntity lamp || !lamp.Lit || !NearScreen(lamp))
                    continue;

                lamp.DisplayAngle = float.IsNaN(lamp.DisplayAngle)
                    ? lamp.BeamAngle
                    : Terraria.Utils.AngleLerp(lamp.DisplayAngle, lamp.BeamAngle, DisplayTurnLerp);

                LightAlongBeam(lamp);
            }
        }

        public override void PostDrawTiles()
        {
            bool began = false;
            var shader = GameShaders.Misc["SoA:LighthouseBeam"];
            Texture2D quad = SoAVfx.Quad;
            float opacity = TimeOfDayOpacity();

            foreach (TileEntity entity in TileEntity.ByID.Values)
            {
                if (entity is not LighthouseLampEntity lamp || !lamp.Lit || !NearScreen(lamp) || float.IsNaN(lamp.DisplayAngle))
                    continue;

                if (!began)
                {
                    Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearClamp,
                        DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                    began = true;
                }

                float length = BeamLength(lamp.Center, lamp.DisplayAngle);

                float endWidth = StartWidthPx + length * WidthPerLengthPx;
                shader.UseColor(BeamColor).UseOpacity(opacity);
                shader.Shader.Parameters["uLength"]?.SetValue(length);
                shader.Shader.Parameters["uMaxLength"]?.SetValue(MaxBeamLengthPx);
                shader.Shader.Parameters["uStartWidth"]?.SetValue(StartWidthPx / endWidth);
                shader.Apply();

                Main.graphics.GraphicsDevice.Textures[1] = SoAVfx.Noise;
                Main.graphics.GraphicsDevice.SamplerStates[1] = SamplerState.LinearWrap;

                Main.spriteBatch.Draw(quad, lamp.Center - Main.screenPosition, null, Color.White,
                    lamp.DisplayAngle - MathHelper.PiOver2, new Vector2(quad.Width / 2f, 0f),
                    new Vector2(endWidth / quad.Width, length / quad.Height), SpriteEffects.None, 0f);
            }

            if (!began)
                return;

            Main.spriteBatch.End();

            // Ореол самой лампы — отдельным батчем, без шейдера луча
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            Texture2D glow = SoAVfx.SoftGlow;
            float pulse = 0.9f + 0.1f * MathF.Sin(Main.GlobalTimeWrappedHourly * 2.5f);
            foreach (TileEntity entity in TileEntity.ByID.Values)
            {
                if (entity is not LighthouseLampEntity lamp || !lamp.Lit || !NearScreen(lamp))
                    continue;

                Main.spriteBatch.Draw(glow, lamp.Center - Main.screenPosition, null, BeamColor * (0.7f * pulse * opacity),
                    0f, glow.Size() / 2f, GlowSizePx / glow.Width, SpriteEffects.None, 0f);
            }
            Main.spriteBatch.End();
        }

        // Шаг по лучу до первого блока, который держит свет
        private static float BeamLength(Vector2 origin, float angle)
        {
            Vector2 direction = angle.ToRotationVector2();
            for (float distance = LampClearancePx; distance < MaxBeamLengthPx; distance += RayStepPx)
            {
                Point tilePos = (origin + direction * distance).ToTileCoordinates();
                if (!WorldGen.InWorld(tilePos.X, tilePos.Y, 2))
                    return distance;

                // Блок, выключенный активатором, проходим и света не держит — луч идёт сквозь
                Tile tile = Main.tile[tilePos.X, tilePos.Y];
                if (tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]
                    && Main.tileBlockLight[tile.TileType])
                    return distance;
            }
            return MaxBeamLengthPx;
        }

        private static void LightAlongBeam(LighthouseLampEntity lamp)
        {
            float length = BeamLength(lamp.Center, lamp.DisplayAngle);
            Vector2 direction = lamp.DisplayAngle.ToRotationVector2();
            float opacity = TimeOfDayOpacity();

            Vector2 across = direction.RotatedBy(MathHelper.PiOver2);

            for (float distance = LightStepPx; distance < length; distance += LightStepPx)
            {
                float falloff = 1f - distance / MaxBeamLengthPx;
                Vector3 light = BeamLight * (LightStrength * falloff * opacity);
                Vector2 center = lamp.Center + direction * distance;
                Lighting.AddLight(center, light);

                // Луч широкий: одной цепочки огней по оси мало, по краям конуса
                // берег оставался тёмным
                float sideOffset = (StartWidthPx + distance * WidthPerLengthPx) * LightSideShare;
                if (sideOffset > LightStepPx)
                {
                    Lighting.AddLight(center + across * sideOffset, light * LightSideStrength);
                    Lighting.AddLight(center - across * sideOffset, light * LightSideStrength);
                }
            }
        }

        // Днём луч бледный, ночью в полную силу; на рассвете и закате переход плавный
        private static float TimeOfDayOpacity()
        {
            if (!Main.dayTime)
                return NightOpacity;

            float dayProgress = (float)(Main.time / Main.dayLength);
            float daylight = MathHelper.Clamp(MathF.Sin(dayProgress * MathHelper.Pi) * 3f, 0f, 1f);
            return MathHelper.Lerp(NightOpacity, DayOpacity, daylight);
        }

        private static bool NearScreen(LighthouseLampEntity lamp)
        {
            var area = new Rectangle((int)(Main.screenPosition.X - ScreenMarginPx), (int)(Main.screenPosition.Y - ScreenMarginPx),
                (int)(Main.screenWidth + ScreenMarginPx * 2), (int)(Main.screenHeight + ScreenMarginPx * 2));
            return area.Contains(lamp.Center.ToPoint());
        }
    }
}
