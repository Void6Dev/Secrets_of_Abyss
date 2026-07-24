using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace SoA.Common.Graphics
{
    // Общие визуальные примитивы поверх готовых шейдеров мода (SoA:BeamGlow / SoA:ImpactRing)
    // и переиспользуемых текстур. Инкапсулируют свап спрайтбатча в Immediate+Additive и обратно,
    // чтобы вызывающие (босс, снаряды) не дублировали Begin/End-бойлерплейт.
    public static class SoAVfx
    {
        private static Asset<Texture2D> _blob;
        private static Asset<Texture2D> _noise;

        // Мягкий круглый блоб — база для свечения/колец/трейлов
        public static Texture2D Blob =>
            (_blob ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/BeamDistortion", AssetRequestMode.ImmediateLoad)).Value;

        public static Texture2D Noise =>
            (_noise ??= ModContent.Request<Texture2D>("SoA/Assets/Textures/WaveNoise", AssetRequestMode.ImmediateLoad)).Value;

        // Свап в аддитивный Immediate-режим (для шейдеров/свечения) и обратно в обычную отрисовку.
        // Пара строго симметрична: на каждый BeginAdditive — свой EndAdditive.
        public static void BeginAdditive(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);
        }

        public static void EndAdditive(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);
        }

        // Свап в Immediate + обычный AlphaBlend — для шейдеров непрозрачной пыли/дыма,
        // где аддитив дал бы «свечение». Закрывать тем же EndAdditive (он восстанавливает Deferred).
        public static void BeginAlphaImmediate(SpriteBatch sb)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);
        }

        // Свирл-свечение через SoA:BeamGlow (голубое «водяное» ядро). sizePx — диаметр в мире.
        // Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawGlow(SpriteBatch sb, Vector2 worldPos, float sizePx, float opacity)
        {
            MiscShaderData shader = GameShaders.Misc["SoA:BeamGlow"];
            shader.UseOpacity(opacity);
            shader.Apply();
            Texture2D tex = Blob;
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, Color.White, 0f,
                tex.Size() / 2f, new Vector2(sizePx / tex.Width, sizePx / tex.Height), SpriteEffects.None, 0);
        }

        // Плоский аддитивный блоб произвольного цвета (без шейдера) — для цветных аур
        // (напр. красный пульс «треснувшего панциря»). Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawTintedGlow(SpriteBatch sb, Vector2 worldPos, Vector2 sizePx, Color color)
        {
            Texture2D tex = Blob;
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, color, 0f,
                tex.Size() / 2f, new Vector2(sizePx.X / tex.Width, sizePx.Y / tex.Height), SpriteEffects.None, 0);
        }

        // Расширяющееся кольцо удара через SoA:ImpactRing. progress 0..1 = радиус кольца.
        // blend: 0 — голубое, 1 — фиолетовое. Вызывать внутри BeginAdditive/EndAdditive.
        public static void DrawRing(SpriteBatch sb, Vector2 worldPos, float sizePx, float progress, float opacity, float blend = 0f)
        {
            MiscShaderData shader = GameShaders.Misc["SoA:ImpactRing"];
            shader.UseOpacity(opacity);
            shader.Shader.Parameters["uProgress"]?.SetValue(progress);
            shader.Shader.Parameters["uBlend"]?.SetValue(blend);
            shader.Apply();
            Texture2D tex = Blob;
            Main.EntitySpriteDraw(tex, worldPos - Main.screenPosition, null, Color.White, 0f,
                tex.Size() / 2f, new Vector2(sizePx / tex.Width, sizePx / tex.Height), SpriteEffects.None, 0);
        }
    }
}
