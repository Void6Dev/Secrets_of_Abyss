using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ReLogic.Content;

namespace SoA.Common.Systems
{
    public class SoASystem : ModSystem
    {
        public override void Load()
        {
            if (Main.dedServ)
                return;

            GameShaders.Misc["SoA:BeamGlow"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/BeamDistortion", AssetRequestMode.ImmediateLoad),
                "GlowPass"
            );

            GameShaders.Misc["SoA:ImpactRing"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/BeamDistortion", AssetRequestMode.ImmediateLoad),
                "RingPass"
            );

            GameShaders.Misc["SoA:ShardedBeam"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/ShardedBeam", AssetRequestMode.ImmediateLoad),
                "BeamPass"
            );

            GameShaders.Misc["SoA:CursedSmoke"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CursedTrail", AssetRequestMode.ImmediateLoad),
                "SmokePass"
            );

            GameShaders.Misc["SoA:CursedBlast"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CursedTrail", AssetRequestMode.ImmediateLoad),
                "BlastPass"
            );

             GameShaders.Misc["SoA:WaveNoise"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/WaveNoise", AssetRequestMode.ImmediateLoad),
                "WavePass"
            );

            GameShaders.Misc["SoA:FireTornado"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/FireTornado", AssetRequestMode.ImmediateLoad),
                "TornadoPass"
            );

            GameShaders.Misc["SoA:SteamGeyser"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/FireTornado", AssetRequestMode.ImmediateLoad),
                "SteamPass"
            );

            GameShaders.Misc["SoA:CrabBubble"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CrabBubble", AssetRequestMode.ImmediateLoad),
                "BubblePass"
            );

            GameShaders.Misc["SoA:BubblePop"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CrabBubble", AssetRequestMode.ImmediateLoad),
                "PopPass"
            );

            GameShaders.Misc["SoA:BurrowBurst"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/BurrowBurst", AssetRequestMode.ImmediateLoad),
                "BurstPass"
            );
        }
    }
}
