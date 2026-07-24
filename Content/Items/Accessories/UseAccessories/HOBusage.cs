using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace SoA.Content.Items.Accessories.UseAccessories
{
    public class HOBusage : ModPlayer
    {
        public bool hasHaloOfBreathing = false;
        public bool showHaloVisual = false;

        public int haloFrame = 0;
        public int haloFrameCounter = 0;

        private bool _wasActiveLastTick = false;

        public override void ResetEffects()
        {
            hasHaloOfBreathing = false;
            showHaloVisual = false;
        }

        public override void PostUpdate()
        {
            if (hasHaloOfBreathing)
            {
                Lighting.AddLight(Player.Center, new Vector3(0.8f, 0.7f, 0.3f));

                if (!_wasActiveLastTick && Main.netMode != NetmodeID.Server)
                    PlayActivationEffect();

                if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(9))
                {
                    Dust bubble = Dust.NewDustDirect(Player.position, Player.width, Player.height,
                        DustID.Water, 0f, -Main.rand.NextFloat(0.5f, 1.5f));
                    bubble.noGravity = true;
                    bubble.scale = Main.rand.NextFloat(0.5f, 0.9f);
                    bubble.alpha = 100;
                }

                haloFrameCounter++;
                if (haloFrameCounter >= 6)
                {
                    haloFrameCounter = 0;
                    haloFrame = (haloFrame + 1) % HaloOfBreathingDrawLayer.FrameCount;
                }
            }

            _wasActiveLastTick = hasHaloOfBreathing;
        }

        private void PlayActivationEffect()
        {
            SoundEngine.PlaySound(SoundID.Item13 with { Volume = 0.5f, Pitch = 0.6f }, Player.Center);
            for (int i = 0; i < 20; i++)
            {
                Dust bubble = Dust.NewDustDirect(Player.position, Player.width, Player.height,
                    DustID.Water, Main.rand.NextFloat(-1.5f, 1.5f), -Main.rand.NextFloat(1f, 3.5f));
                bubble.noGravity = true;
                bubble.scale = Main.rand.NextFloat(0.8f, 1.4f);
                bubble.alpha = 80;
            }
        }
    }
}