using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace SoA.Content.Items.Accessories.UseAccessories
{
    public class HaloOfBreathingDrawLayer : PlayerDrawLayer
    {
        public const int FrameCount = 4;

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.Head);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            return drawInfo.drawPlayer.GetModPlayer<HOBusage>().showHaloVisual;
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;

            var texture = ModContent.Request<Texture2D>("SoA/Content/Items/Accessories/HaloOfBreathing_Head").Value;

            int frameHeight = texture.Height / FrameCount;
            int currentFrame = player.GetModPlayer<HOBusage>().haloFrame;
            Rectangle sourceRect = new(0, currentFrame * frameHeight, texture.Width, frameHeight);

            // Standard tML head-center position (accounts for body frame, headPosition animation, gfxOffset)
            Vector2 headCenter = new Vector2(
                (int)(drawInfo.Position.X - Main.screenPosition.X - player.bodyFrame.Width / 2 + player.width / 2),
                (int)(drawInfo.Position.Y - Main.screenPosition.Y + player.height - player.bodyFrame.Height + 4f)
            ) + player.headPosition + drawInfo.headVect;

            // Float just above the head (use frameHeight, not full texture height)
            headCenter.Y -= frameHeight / 2f + 2f;

            Vector2 origin = new(texture.Width / 2f, frameHeight / 2f);
            SpriteEffects effects = player.direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            drawInfo.DrawDataCache.Add(new DrawData(
                texture,
                headCenter,
                sourceRect,
                Color.White,
                0f,
                origin,
                1f,
                effects,
                0
            ));
        }
    }
}