using System;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Nature;
using SoA.Content.Water;

namespace SoA.Content.Worldgen
{
    // Какой океан стал Приливом Теней: -1 — левый, 1 — правый, 0 — биома в мире нет.
    // Ставится при генерации, сохраняется в мире и шлётся клиентам
    public class TideOfShadowsWorldData : ModSystem
    {
        public static int OceanSide;

        public override void ClearWorld() => OceanSide = 0;

        public override void SaveWorldData(TagCompound tag)
        {
            if (OceanSide != 0)
                tag["tideOceanSide"] = OceanSide;
        }

        public override void LoadWorldData(TagCompound tag) => OceanSide = tag.GetInt("tideOceanSide");

        public override void NetSend(BinaryWriter writer) => writer.Write((sbyte)OceanSide);

        public override void NetReceive(BinaryReader reader) => OceanSide = reader.ReadSByte();
    }

    // Счётчик тайлов биома: обновляется движком каждый тик сцены
    public class TideOfShadowsTileCount : ModSystem
    {
        public int TidesandCount { get; private set; }
        public int TidestoneCount { get; private set; }

        public override void TileCountsAvailable(ReadOnlySpan<int> tileCounts)
        {
            TidesandCount = tileCounts[ModContent.TileType<Tidesand_tile>()];
            TidestoneCount = tileCounts[ModContent.TileType<Tidestone_tile>()];
        }
    }

    // Кастомный задний фон биома: Common/Backgrounds автозагружается tML как фоновые слоты
    public class TideOfShadowsBackgroundStyle : ModSurfaceBackgroundStyle
    {
        public override void ModifyFarFades(float[] fades, float transitionSpeed)
        {
            for (int i = 0; i < fades.Length; i++)
            {
                if (i == Slot)
                {
                    fades[i] += transitionSpeed;
                    if (fades[i] > 1f)
                        fades[i] = 1f;
                }
                else
                {
                    fades[i] -= transitionSpeed;
                    if (fades[i] < 0f)
                        fades[i] = 0f;
                }
            }
        }

        public override int ChooseFarTexture()
            => BackgroundTextureLoader.GetBackgroundSlot(Mod, "Common/Backgrounds/TideOfShadows_background");

        public override int ChooseMiddleTexture() => -1;

        public override int ChooseCloseTexture(ref float scale, ref double parallax, ref float a, ref float b) => -1;
    }

    // Прилив Теней: океан со стороны джунглей, засыпанный Tidesand.
    public class TideOfShadowsBiome : ModBiome
    {
        private const int RequiredTidesand = 50;
        private const int OceanEdgeTiles = 380;

        public override ModSurfaceBackgroundStyle SurfaceBackgroundStyle
            => ModContent.GetInstance<TideOfShadowsBackgroundStyle>();

        public override ModWaterStyle WaterStyle
            => ModContent.GetInstance<TideWaterStyle>();

        public override int BiomeTorchItemType => ModContent.ItemType<Ttorch>();
        public override int BiomeCampfireItemType => ModContent.ItemType<Tcampfire>();
        // BiomeHigh: иначе ванильный океан перебивает наш задний фон у побережья
        public override SceneEffectPriority Priority => SceneEffectPriority.BiomeHigh;

        public override bool IsBiomeActive(Player player)
        {
            bool surfaceLevel = player.ZoneOverworldHeight || player.ZoneSkyHeight;
            if (!surfaceLevel)
                return false;

            int tileX = (int)(player.Center.X / 16f);
            bool nearLeftEdge = tileX < OceanEdgeTiles;
            bool nearRightEdge = tileX > Main.maxTilesX - OceanEdgeTiles;

            // Основной путь: игрок в океане, помеченном при генерации.
            // Дно с Tidesand может быть далеко за пределами сканирования сцены,
            // поэтому на счётчик тайлов тут полагаться нельзя
            int side = TideOfShadowsWorldData.OceanSide;
            if ((side == -1 && nearLeftEdge) || (side == 1 && nearRightEdge))
                return true;

            // Запасной путь для построек руками: много Tidesand/Tidestone рядом у любого океана
            var counts = ModContent.GetInstance<TideOfShadowsTileCount>();
            bool enoughTiles = counts.TidesandCount + counts.TidestoneCount >= RequiredTidesand;
            return enoughTiles && (nearLeftEdge || nearRightEdge);
        }

        public override void SpecialVisuals(Player player, bool isActive)
        {
            player.ManageSpecialBiomeVisuals(TideOfShadowsSky.Key, isActive, player.Center);
        }
    }
}
