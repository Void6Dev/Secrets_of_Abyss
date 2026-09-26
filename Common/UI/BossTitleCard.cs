using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;

namespace SoA.Common.UI
{
    // Имя босса посреди экрана при первой встрече: крупное название, под ним подзаголовок
    // с тонкими линиями по бокам. Появляется, стоит и тает. Чистый клиентский интерфейс:
    // вызывающий сам решает, когда показать (обычно — на рёве после появления)
    public class BossTitleCard : ModSystem
    {
        private const int FadeInTicks = 25;
        private const int FadeOutTicks = 45;
        private const float RiseDistance = 14f;        // название чуть всплывает на входе
        private const float ScreenHeightShare = 0.27f; // высота строки названия на экране
        private const float TitleScale = 1.05f;
        private const float SubtitleScale = 1.15f;
        private const float OrnamentLength = 90f;
        private const float OrnamentGap = 14f;

        private static readonly Color TitleColor = new(255, 208, 112);
        private static readonly Color SubtitleColor = new(170, 215, 255);

        private static LocalizedText _title;
        private static LocalizedText _subtitle;
        private static int _timer;
        private static int _duration;

        public static void Show(LocalizedText title, LocalizedText subtitle, int durationTicks)
        {
            if (Main.dedServ)
                return;
            _title = title;
            _subtitle = subtitle;
            _duration = Math.Max(durationTicks, FadeInTicks + FadeOutTicks);
            _timer = 0;
        }

        public override void OnWorldUnload() => _duration = 0;

        public override void PostUpdateEverything()
        {
            if (_duration > 0 && ++_timer >= _duration)
                _duration = 0;
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            if (_duration <= 0)
                return;

            // Под инвентарём и подсказками: надпись не должна перекрывать интерфейс игрока
            int index = layers.FindIndex(layer => layer.Name == "Vanilla: Inventory");
            layers.Insert(index >= 0 ? index : 0, new LegacyGameInterfaceLayer(
                "SoA: Boss Title Card", () => { Draw(Main.spriteBatch); return true; }, InterfaceScaleType.UI));
        }

        private static void Draw(SpriteBatch sb)
        {
            float fadeIn = MathHelper.Clamp(_timer / (float)FadeInTicks, 0f, 1f);
            float fadeOut = MathHelper.Clamp((_duration - _timer) / (float)FadeOutTicks, 0f, 1f);
            float alpha = Math.Min(fadeIn, fadeOut);
            float rise = (1f - (1f - fadeIn) * (1f - fadeIn)) * RiseDistance;

            float centerX = Main.screenWidth / 2f;
            float titleY = Main.screenHeight * ScreenHeightShare + RiseDistance - rise;

            DynamicSpriteFont titleFont = FontAssets.DeathText.Value;
            string title = _title?.Value ?? string.Empty;
            Vector2 titleSize = titleFont.MeasureString(title) * TitleScale;
            ChatManager.DrawColorCodedStringWithShadow(sb, titleFont, title,
                new Vector2(centerX, titleY), TitleColor * alpha, 0f, titleSize / TitleScale / 2f, new Vector2(TitleScale));

            DynamicSpriteFont subtitleFont = FontAssets.MouseText.Value;
            string subtitle = _subtitle?.Value ?? string.Empty;
            Vector2 subtitleSize = subtitleFont.MeasureString(subtitle) * SubtitleScale;
            float subtitleY = titleY + titleSize.Y * 0.55f + subtitleSize.Y * 0.5f;
            ChatManager.DrawColorCodedStringWithShadow(sb, subtitleFont, subtitle,
                new Vector2(centerX, subtitleY), SubtitleColor * alpha, 0f, subtitleSize / SubtitleScale / 2f,
                new Vector2(SubtitleScale));

            // Тонкие линии по бокам подзаголовка — разворачиваются вместе с появлением
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            float length = OrnamentLength * fadeIn;
            float lineY = subtitleY;
            float left = centerX - subtitleSize.X / 2f - OrnamentGap;
            float right = centerX + subtitleSize.X / 2f + OrnamentGap;
            Color lineColor = TitleColor * (alpha * 0.8f);
            sb.Draw(pixel, new Rectangle((int)(left - length), (int)lineY, (int)length, 2), new Rectangle(0, 0, 1, 1), lineColor);
            sb.Draw(pixel, new Rectangle((int)right, (int)lineY, (int)length, 2), new Rectangle(0, 0, 1, 1), lineColor);
        }
    }
}
