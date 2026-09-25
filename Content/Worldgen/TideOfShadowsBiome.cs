using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Nature;
using SoA.Content.Water;

namespace SoA.Content.Worldgen
{
    // Место будущей печати: узкий проход между зонами, который перекроется,
    // пока не выполнено условие прогресса соответствующей ступени
    public struct TideSealSite
    {
        public int Step;     // 1 — Королевский Краб, 2 — Стена Плоти, 3 — Плантера, 4 — Мунлорд
        public int X, Y, Width, Height;
    }

    // Обмеры биома, снятые при генерации. Границы и глубины зон у каждого мира свои,
    // поэтому их нельзя вычислить на лету — только сохранить и раздать клиентам
    public class TideOfShadowsWorldData : ModSystem
    {
        public const int ZoneCount = 5;

        public static int OceanSide;   // -1 — левый океан, 1 — правый, 0 — биома нет
        public static int EdgeX;       // столбец у края мира, от него отсчитываются ширины
        public static int WaterTopY;

        // Нижняя граница и вылет вглубь суши для каждой зоны. Вылет растёт с глубиной:
        // чаша завалена под сушу, и прямоугольником биом описать нельзя
        public static readonly int[] ZoneBottomY = new int[ZoneCount];
        public static readonly int[] ZoneWidth = new int[ZoneCount];

        public static List<TideSealSite> SealSites = new();

        // Направление вглубь суши от края мира
        public static int InlandDir => OceanSide == -1 ? 1 : -1;

        public static bool HasBounds => OceanSide != 0 && ZoneBottomY[ZoneCount - 1] > WaterTopY;

        // В какой зоне точка: 0 — вне биома, 1..5 по возрастанию глубины
        public static int ZoneAt(int tileX, int tileY)
        {
            if (!HasBounds)
                return 0;

            int offset = (tileX - EdgeX) * InlandDir;
            if (offset < 0 || tileY > ZoneBottomY[ZoneCount - 1] + 24)
                return 0;

            for (int zone = 0; zone < ZoneCount; zone++)
            {
                if (tileY <= ZoneBottomY[zone])
                    return offset <= ZoneWidth[zone] ? zone + 1 : 0;
            }
            return 0;
        }

        public override void ClearWorld()
        {
            OceanSide = 0;
            EdgeX = 0;
            WaterTopY = 0;
            Array.Clear(ZoneBottomY);
            Array.Clear(ZoneWidth);
            SealSites = new List<TideSealSite>();
        }

        public override void SaveWorldData(TagCompound tag)
        {
            if (OceanSide == 0)
                return;

            tag["tideOceanSide"] = OceanSide;
            tag["tideEdgeX"] = EdgeX;
            tag["tideWaterTopY"] = WaterTopY;
            tag["tideZoneBottomY"] = new List<int>(ZoneBottomY);
            tag["tideZoneWidth"] = new List<int>(ZoneWidth);

            var seals = new List<int>();
            foreach (TideSealSite site in SealSites)
            {
                seals.Add(site.Step);
                seals.Add(site.X);
                seals.Add(site.Y);
                seals.Add(site.Width);
                seals.Add(site.Height);
            }
            tag["tideSeals"] = seals;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            OceanSide = tag.GetInt("tideOceanSide");
            EdgeX = tag.GetInt("tideEdgeX");
            WaterTopY = tag.GetInt("tideWaterTopY");

            CopyInto(tag.GetList<int>("tideZoneBottomY"), ZoneBottomY);
            CopyInto(tag.GetList<int>("tideZoneWidth"), ZoneWidth);

            SealSites = new List<TideSealSite>();
            var seals = tag.GetList<int>("tideSeals");
            if (seals == null)
                return;

            for (int i = 0; i + 4 < seals.Count; i += 5)
            {
                SealSites.Add(new TideSealSite
                {
                    Step = seals[i], X = seals[i + 1], Y = seals[i + 2],
                    Width = seals[i + 3], Height = seals[i + 4]
                });
            }
        }

        private static void CopyInto(IList<int> source, int[] target)
        {
            if (source == null)
                return;
            for (int i = 0; i < target.Length && i < source.Count; i++)
                target[i] = source[i];
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write((sbyte)OceanSide);
            writer.Write(EdgeX);
            writer.Write(WaterTopY);
            for (int i = 0; i < ZoneCount; i++)
            {
                writer.Write(ZoneBottomY[i]);
                writer.Write(ZoneWidth[i]);
            }

            writer.Write((byte)SealSites.Count);
            foreach (TideSealSite site in SealSites)
            {
                writer.Write((byte)site.Step);
                writer.Write(site.X);
                writer.Write(site.Y);
                writer.Write((byte)site.Width);
                writer.Write((byte)site.Height);
            }
        }

        public override void NetReceive(BinaryReader reader)
        {
            OceanSide = reader.ReadSByte();
            EdgeX = reader.ReadInt32();
            WaterTopY = reader.ReadInt32();
            for (int i = 0; i < ZoneCount; i++)
            {
                ZoneBottomY[i] = reader.ReadInt32();
                ZoneWidth[i] = reader.ReadInt32();
            }

            SealSites = new List<TideSealSite>();
            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                SealSites.Add(new TideSealSite
                {
                    Step = reader.ReadByte(),
                    X = reader.ReadInt32(),
                    Y = reader.ReadInt32(),
                    Width = reader.ReadByte(),
                    Height = reader.ReadByte()
                });
            }
        }
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

        // Два слоя: дальняя гряда в самой глубине кадра и море со скалами перед ней.
        // Порядок именно такой — гряда рисуется первой и идёт медленнее, поэтому
        // на ходу между слоями появляется параллакс, а горизонт моря перекрывает
        // её низ и оставляет над водой только вершины
        public override int ChooseFarTexture()
            => BackgroundTextureLoader.GetBackgroundSlot(Mod, "Common/Backgrounds/TideOfShadows_background2");

        public override int ChooseMiddleTexture()
            => BackgroundTextureLoader.GetBackgroundSlot(Mod, "Common/Backgrounds/TideOfShadows_background");

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
            int tileX = (int)(player.Center.X / 16f);
            int tileY = (int)(player.Center.Y / 16f);

            // Основной путь: точные границы, снятые при генерации. Проверка по слою
            // здесь недопустима — зоны 3-5 лежат ниже поверхности, и биом бы гас
            if (TideOfShadowsWorldData.ZoneAt(tileX, tileY) != 0)
                return true;

            // Запасной путь для построек руками: много Tidesand/Tidestone у любого океана
            bool nearLeftEdge = tileX < OceanEdgeTiles;
            bool nearRightEdge = tileX > Main.maxTilesX - OceanEdgeTiles;
            if (!nearLeftEdge && !nearRightEdge)
                return false;

            var counts = ModContent.GetInstance<TideOfShadowsTileCount>();
            return counts.TidesandCount + counts.TidestoneCount >= RequiredTidesand;
        }

        // SpecialVisuals нет намеренно: сумрак над Приливом Теней (TideOfShadowsSky) убран —
        // он затемнял экран, а биому это не нужно ни в бою, ни вне его
    }
}
