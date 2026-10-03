using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Players;

namespace SoA.Content.Items.Armor
{
    // Снежинки на орбите вокруг игрока в сете флинксов. Орбита наклонная: та половина,
    // что уходит за спину, рисуется слоем за персонажем и тусклее, ближняя — поверх него.
    // Положение считается от времени, поэтому у всех клиентов снежинки кружат одинаково
    public abstract class FlinxSnowAuraLayer : PlayerDrawLayer
    {
        private const int Flakes = 5;
        private const float OrbitSpeed = 0.9f;
        private const float OrbitRadiusX = 26f;
        private const float OrbitRadiusY = 7f;
        private const float OrbitTilt = 5f;          // ближняя сторона орбиты ниже дальней
        private const float BobHeight = 4f;
        private const float ArmLength = 9f;
        private const float ArmThickness = 1.6f;
        private const float BackAlpha = 0.45f;

        protected abstract bool FrontHalf { get; }

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
            => drawInfo.shadow == 0f && drawInfo.drawPlayer.GetModPlayer<FlinxArmorSetBonusPlayer>().AuraActive;

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            Vector2 center = drawInfo.Position + new Vector2(player.width / 2f, player.height / 2f - 4f) - Main.screenPosition;
            float time = Main.GlobalTimeWrappedHourly;
            Texture2D streak = SoAVfx.SoftStreak;
            Texture2D glow = SoAVfx.SoftGlow;

            for (int i = 0; i < Flakes; i++)
            {
                float angle = time * OrbitSpeed + MathHelper.TwoPi * i / Flakes;
                float depth = (float)Math.Sin(angle);   // > 0 — перед игроком
                if (depth > 0f != FrontHalf)
                    continue;

                var offset = new Vector2((float)Math.Cos(angle) * OrbitRadiusX,
                    depth * OrbitRadiusY + (float)Math.Cos(angle) * OrbitTilt + (float)Math.Sin(time * 1.7f + i) * BobHeight);
                Vector2 at = center + offset;

                // Ближние крупнее и ярче — орбита читается объёмной
                float near = 0.5f + 0.5f * depth;
                float alpha = FrontHalf ? 0.65f + 0.35f * near : BackAlpha * (0.6f + 0.4f * near);
                float size = (0.75f + 0.35f * near) * (0.85f + 0.3f * (i % 3) / 2f);
                Color color = FlinxArmorSetBonusPlayer.FrostGlow * alpha;

                // Шесть лучей: три штриха через центр, каждая снежинка крутится в свою сторону
                float spin = time * 0.6f * (i % 2 == 0 ? 1f : -1f) + i;
                var armScale = new Vector2(ArmLength * size / streak.Width, ArmThickness / streak.Height);
                for (int arm = 0; arm < 3; arm++)
                    SoAVfx.AddPlayerDraw(ref drawInfo, streak, at, color, spin + MathHelper.Pi / 3f * arm,
                        streak.Size() / 2f, armScale);
                SoAVfx.AddPlayerDraw(ref drawInfo, glow, at, color * 0.8f, 0f, glow.Size() / 2f,
                    new Vector2(7f * size / glow.Width));
            }
        }
    }

    public class FlinxSnowAuraBackLayer : FlinxSnowAuraLayer
    {
        protected override bool FrontHalf => false;
        public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.Wings);
    }

    public class FlinxSnowAuraFrontLayer : FlinxSnowAuraLayer
    {
        protected override bool FrontHalf => true;
        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.FrontAccFront);
    }
}
