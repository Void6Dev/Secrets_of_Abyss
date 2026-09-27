using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.Items.Accessories
{
    // Нимб над головой: мягко покачивается, светится ореолом, время от времени по нему
    // пробегает блик. Разгорается и гаснет плавно (HOBusage.Glow), при нехватке воздуха пульсирует
    public class HaloOfBreathingDrawLayer : PlayerDrawLayer
    {
        public const int FrameCount = 4;

        private const float BobAmplitude = 1.5f;
        private const float HaloGlowSize = 46f;
        private const float AuraSize = 110f;
        private const int GlintPeriod = 200;
        private const int GlintTicks = 14;

        private static readonly Color HaloGold = new(255, 225, 140);
        private static readonly Color GlintColor = new(255, 250, 225);

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.Head);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            var halo = drawInfo.drawPlayer.GetModPlayer<HOBusage>();
            return halo.showHaloVisual && halo.Glow > 0f;
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            var halo = player.GetModPlayer<HOBusage>();
            float glow = halo.Glow;

            var texture = ModContent.Request<Texture2D>("SoA/Content/Items/Accessories/HaloOfBreathing/HaloOfBreathing_Head").Value;
            int frameHeight = texture.Height / FrameCount;
            Rectangle sourceRect = new(0, halo.haloFrame * frameHeight, texture.Width, frameHeight);

            // Стандартная позиция центра головы tML (кадр тела, анимация головы, gfxOffY)
            Vector2 headCenter = new Vector2(
                (int)(drawInfo.Position.X - Main.screenPosition.X - player.bodyFrame.Width / 2 + player.width / 2),
                (int)(drawInfo.Position.Y - Main.screenPosition.Y + player.height - player.bodyFrame.Height + 4f)
            ) + player.headPosition + drawInfo.headVect;

            float time = Main.GameUpdateCount;
            Vector2 haloPos = headCenter - new Vector2(0f, frameHeight / 2f + 2f + BobAmplitude * (1f + (float)Math.Sin(time * 0.05f)));
            float pulse = halo.LowBreath
                ? 0.7f + 0.3f * (float)Math.Sin(time * 0.3f)
                : 0.85f + 0.15f * (float)Math.Sin(time * 0.06f);

            Texture2D soft = SoAVfx.SoftGlow;
            Vector2 softOrigin = soft.Size() / 2f;

            // Мягкий свет вокруг головы и плеч
            SoAVfx.AddPlayerDraw(ref drawInfo, soft, headCenter + new Vector2(0f, 6f), SoAVfx.Additive(HaloGold * (0.12f * glow)), 0f, softOrigin,
                new Vector2(AuraSize / soft.Width));
            // Ореол нимба
            SoAVfx.AddPlayerDraw(ref drawInfo, soft, haloPos, SoAVfx.Additive(HaloGold * (0.45f * glow * pulse)), 0f, softOrigin,
                new Vector2(HaloGlowSize / soft.Width, HaloGlowSize * 0.55f / soft.Height));

            Vector2 origin = new(texture.Width / 2f, frameHeight / 2f);
            SpriteEffects effects = player.direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            SoAVfx.AddPlayerDraw(ref drawInfo, texture, haloPos, Color.White * glow, 0f, origin, Vector2.One, effects, sourceRect);
            // Сияние поверх спрайта
            SoAVfx.AddPlayerDraw(ref drawInfo, texture, haloPos, SoAVfx.Additive(HaloGold * (0.35f * glow * pulse)), 0f, origin,
                new Vector2(1.08f), effects, sourceRect);

            DrawGlint(ref drawInfo, haloPos, texture.Width, glow, player.whoAmI);
        }

        // Блик пробегает по нимбу слева направо раз в несколько секунд
        private static void DrawGlint(ref PlayerDrawSet drawInfo, Vector2 haloPos, int haloWidth, float glow, int seed)
        {
            int phase = (int)((Main.GameUpdateCount + seed * 37) % GlintPeriod);
            if (phase >= GlintTicks)
                return;

            float t = phase / (float)GlintTicks;
            float strength = (float)Math.Sin(t * MathHelper.Pi) * glow;
            Vector2 at = haloPos + new Vector2(MathHelper.Lerp(-haloWidth * 0.4f, haloWidth * 0.4f, t), 0f);
            Texture2D streak = SoAVfx.SoftStreak;
            Vector2 origin = streak.Size() / 2f;
            Color color = SoAVfx.Additive(GlintColor * strength);
            SoAVfx.AddPlayerDraw(ref drawInfo, streak, at, color, 0f, origin, new Vector2(18f / streak.Width, 3f / streak.Height));
            SoAVfx.AddPlayerDraw(ref drawInfo, streak, at, color, MathHelper.PiOver2, origin, new Vector2(12f / streak.Width, 2.5f / streak.Height));
        }
    }
}
