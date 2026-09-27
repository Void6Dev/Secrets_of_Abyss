using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Players;

namespace SoA.Content.Items.Accessories
{
    public class TideskimmerBoots : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 24;
            Item.accessory = true;
            Item.value = Item.sellPrice(gold: 4, silver: 20);
            Item.rare = ItemRarityID.Purple;
        }

        // Вся физика живёт в TideskimmerPlayer: там она видит хуки движения
        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.GetModPlayer<TideskimmerPlayer>().hasTideskimmerBoots = true;
        }
    }

    // Водоворот у ног, пока копится давление, и струя из-под ботинок во время выстрела
    public class TideskimmerDrawLayer : PlayerDrawLayer
    {
        private const int SwirlSegments = 14;
        private const float SwirlRadiusX = 20f;
        private const float SwirlRadiusY = 7f;
        private const int JetStreaks = 5;

        private static readonly Color SwirlColor = new(120, 200, 255);
        private static readonly Color JetColor = new(190, 235, 255);

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.Shoes);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            var boots = drawInfo.drawPlayer.GetModPlayer<TideskimmerPlayer>();
            return boots.hasTideskimmerBoots && (boots.Pressure > 0f || boots.Jetting);
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            var boots = player.GetModPlayer<TideskimmerPlayer>();
            Vector2 feet = player.Bottom - Main.screenPosition + new Vector2(0f, player.gfxOffY - 2f);
            Texture2D streak = SoAVfx.SoftStreak;
            Vector2 origin = streak.Size() / 2f;

            if (boots.Jetting)
            {
                // Струя: несколько вытянутых штрихов вниз, дрожат по длине
                for (int i = 0; i < JetStreaks; i++)
                {
                    float x = (i - (JetStreaks - 1) / 2f) * 5f;
                    float length = 30f + Main.rand.NextFloat(20f);
                    Vector2 at = feet + new Vector2(x, length * 0.5f);
                    SoAVfx.AddPlayerDraw(ref drawInfo, streak, at, SoAVfx.Additive(JetColor * (0.5f - Math.Abs(x) * 0.03f)),
                        MathHelper.PiOver2, origin, new Vector2(length / streak.Width, 5f / streak.Height));
                }
                return;
            }

            // Водоворот: два витка по эллипсу вокруг ног, крутится быстрее с давлением
            float pressure = boots.Pressure;
            float spin = Main.GameUpdateCount * (0.08f + 0.25f * pressure);
            for (int arm = 0; arm < 2; arm++)
            {
                for (int i = 0; i < SwirlSegments; i++)
                {
                    float along = i / (float)SwirlSegments;
                    float angle = spin + arm * MathHelper.Pi + along * MathHelper.Pi;
                    float next = angle + MathHelper.Pi / SwirlSegments;
                    float shrink = 1f - 0.35f * along;
                    Vector2 a = feet + new Vector2((float)Math.Cos(angle) * SwirlRadiusX, (float)Math.Sin(angle) * SwirlRadiusY) * shrink;
                    Vector2 b = feet + new Vector2((float)Math.Cos(next) * SwirlRadiusX, (float)Math.Sin(next) * SwirlRadiusY) * shrink;
                    Vector2 segment = b - a;
                    Color color = SoAVfx.Additive(SwirlColor * (pressure * (1f - along) * 0.8f));
                    SoAVfx.AddPlayerDraw(ref drawInfo, streak, (a + b) * 0.5f, color, segment.ToRotation(), origin,
                        new Vector2((segment.Length() + 2f) / streak.Width, (2f + 2f * pressure) / streak.Height));
                }
            }
        }
    }
}
