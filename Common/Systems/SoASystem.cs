using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ReLogic.Content;
using SoA.Common.Graphics;

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

            // Собственные эффекты боя с Королём-крабом — заменили универсальные
            // BeamGlow/ImpactRing из BeamDistortion.fx
            GameShaders.Misc["SoA:CrabAura"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CrabRegalia", AssetRequestMode.ImmediateLoad),
                "AuraPass"
            );

            GameShaders.Misc["SoA:CrabRing"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CrabRegalia", AssetRequestMode.ImmediateLoad),
                "RingPass"
            );

            // Гейзер Королевского трезубца: струя и пенный венец у точки удара
            GameShaders.Misc["SoA:TideGeyser"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/TideGeyser", AssetRequestMode.ImmediateLoad),
                "GeyserPass"
            );

            // Гейзер: струю рисует спрайт с наложенным ColumnPass, всё остальное —
            // отдельные процедурные слои вокруг неё (порядок см. TideGeyserFx.Draw)
            Asset<Effect> geyser = Mod.Assets.Request<Effect>("Assets/Effects/TideGeyser",
                AssetRequestMode.ImmediateLoad);
            GameShaders.Misc["SoA:TideColumn"] = new MiscShaderData(geyser, "ColumnPass");
            GameShaders.Misc["SoA:TideSplash"] = new MiscShaderData(geyser, "SplashPass");
            GameShaders.Misc["SoA:TideDroplets"] = new MiscShaderData(geyser, "DropletPass");
            GameShaders.Misc["SoA:TidePuddle"] = new MiscShaderData(geyser, "PuddlePass");
            GameShaders.Misc["SoA:TideMist"] = new MiscShaderData(geyser, "MistPass");

            // Шлейф разогнанного снаряда: включается за порогом скорости
            GameShaders.Misc["SoA:SpeedRush"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/SpeedRush", AssetRequestMode.ImmediateLoad),
                "RushPass"
            );

            // «Ярость океана»: красная энергия по силуэту каждой части короля
            GameShaders.Misc["SoA:CrabRage"] = new MiscShaderData(
                Mod.Assets.Request<Effect>("Assets/Effects/CrabRegalia", AssetRequestMode.ImmediateLoad),
                "RagePass"
            );
        }
    }
}
