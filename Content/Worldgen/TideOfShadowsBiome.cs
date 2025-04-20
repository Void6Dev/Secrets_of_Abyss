// using Terraria;
// using Terraria.ModLoader;
// using Terraria.Graphics.Capture;
// using Microsoft.Xna.Framework;
// using Microsoft.Xna.Framework.Graphics;
// using SoA.Content.Items;
// using SoA.Common;

// namespace SoA.Content.Worldgen
// {
//     public class TideOfShadowsBiome : ModBiome
//     {
//         public override ModWaterStyle WaterStyle => ModContent.GetInstance<YourWaterStyle>();
//         public override ModSurfaceBackgroundStyle SurfaceBackgroundStyle => ModContent.GetInstance<TideOfShadows_background>();
//         public override CaptureBiome.TileColorStyle TileColorStyle => CaptureBiome.TileColorStyle.Crimson;

//         public override int Music => MusicLoader.GetMusicSlot(Mod, "Assets/Music/TideOfShadowsTheme");

//         public override int BiomeTorchItemType => ModContent.ItemType<Items.Placebles.Ttorch>();
//         public override int BiomeCampfireItemType => ModContent.ItemType<Items.Placebles.Tcampfire>();

//         public override string BestiaryIcon => base.BestiaryIcon;
//         public override string BackgroundPath => base.BackgroundPath;
//         public override Color? BackgroundColor => Color.DarkBlue;
//         public override string MapBackground => BackgroundPath;

//         public override bool IsBiomeActive(Player player)
//         {
//             bool b1 = ModContent.GetInstance<YourBiomeTileCount>().exampleBlockCount >= 40;
//             bool b2 = Math.Abs(player.position.ToTileCoordinates().X - Main.maxTilesX / 2) < Main.maxTilesX / 6;
//             bool b3 = player.ZoneSkyHeight || player.ZoneOverworldHeight;
//             return b1 && b2 && b3;
//         }

//         public override SceneEffectPriority Priority => SceneEffectPriority.BiomeLow;
//     }
// }