using System;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using SoA.Common.Utils;
using SoA.Content.Tiles.Nature;
using SoA.Content.Walls;

namespace SoA.Common.Systems.TideOcean
{
    // Коралловая арка — ориентир зоны 1 на месте, откуда уехала рыбацкая деревня.
    // Сделана рельефом, а не постройкой: своих спрайтов не требует, а силуэт читается
    // издалека, потому что стоит поперёк открытой воды и подсвечен ракушками.
    //
    // Пролёт вырезается насквозь: сквозь арку надо проплывать, иначе это просто скала
    // с горбом. Поэтому пасс идёт после лепки биома, по готовому рельефу
    public class CoralArchPass : GenPass
    {
        private const int MinSpan = 30;
        private const int MaxSpan = 74;
        private const int MinThickness = 4;
        private const int MaxThickness = 8;
        private const float HeightToSpan = 0.62f;
        private const int MinArchHeight = 10;
        private const int WaterSurfaceMargin = 4;   // верхушка не пробивает зеркало воды
        private const int SeabedScanDepth = 240;

        // Поиск места по готовому миру
        private const int SiteSearchRadius = 520;
        private const int SiteSearchStep = 4;
        private const int PreferredSiteDepth = 40;
        private const int MinSiteDepth = 20;
        private const int FlatSampleSpan = 24;   // на сколько тайлов в стороны проверяем ровность дна
        private const int FootFlareHeight = 9;      // на сколько тайлов расширяются пяты
        private const int NoiseChannel = 91;

        // Арка набрана растрескавшимся приливным камнем: она рукотворная и старая,
        // и должна отличаться от свежей породы вокруг. Своей коралловой стены нет —
        // если появится, меняется эта одна строка
        private static ushort ArchWallType => (ushort)ModContent.WallType<Tidestone_wall_cracked>();

        public CoralArchPass(string name, float loadWeight) : base(name, loadWeight)
        {
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "Coral bends into an arch";

            int centerX = TideOceanPass.LandmarkAnchorX;
            int waterTop = TideOceanPass.LandmarkWaterTopY;
            if (centerX <= 0 || waterTop <= 0)
            {
                SoALog.Info("CoralArch", "площадки ориентира нет, арка не ставится");
                return;
            }

            int scanBottom = Math.Min(Main.maxTilesY - 40, waterTop + SeabedScanDepth);

            // Площадка от биома — только подсказка. Настоящее место ищем сами по готовому
            // рельефу: профиль, из которого биом выбирает площадки, не знает ни песчаной
            // гряды шельфа, ни последующего сглаживания, и отдаёт глубину в один тайл
            if (!TryFindSite(centerX, waterTop, scanBottom, out int siteX, out int floorY))
            {
                SoALog.Info("CoralArch", $"глубокой ровной воды рядом с x={centerX} не нашлось");
                return;
            }

            centerX = siteX;

            // Высота ограничена сверху толщей воды: арка над водой читалась бы мостом
            int headroom = floorY - waterTop - WaterSurfaceMargin;
            if (headroom < MinArchHeight)
            {
                SoALog.Info("CoralArch",
                    $"мало толщи воды: x={centerX}, дно={floorY}, зеркало={waterTop}, запас={headroom}");
                return;
            }

            int span = WorldGen.genRand.Next(MinSpan, MaxSpan + 1);
            int height = (int)(span * HeightToSpan);

            // Не режем только высоту: приплюснутая арка читается горбом, а не аркой.
            // Если толщи воды мало, вместе с высотой сужается и пролёт
            if (height > headroom)
            {
                height = headroom;
                span = Math.Clamp((int)(height / HeightToSpan), MinSpan, span);
            }

            int halfSpan = span / 2;
            SoALog.Info("CoralArch", $"арка: x={centerX}, дно={floorY}, пролёт={span}, высота={height}");

            BuildArch(centerX, floorY, halfSpan, height, waterTop);
            Decorate(centerX, floorY, halfSpan, height);

            WorldGen.RangeFrame(centerX - halfSpan - MaxThickness - 4, floorY - height - MaxThickness - 4,
                centerX + halfSpan + MaxThickness + 4, floorY + 6);
        }

        // Место под арку: глубокая и ровная открытая вода недалеко от подсказки.
        // Ровность считается по двум пробам в стороны — пята арки не должна висеть
        // над обрывом уступа, иначе дуга садится вкось
        private static bool TryFindSite(int hintX, int waterTop, int scanBottom,
            out int centerX, out int floorY)
        {
            centerX = -1;
            floorY = -1;

            float bestScore = float.MaxValue;
            int deepestFound = 0;

            for (int x = hintX - SiteSearchRadius; x <= hintX + SiteSearchRadius; x += SiteSearchStep)
            {
                if (x < 40 || x >= Main.maxTilesX - 40)
                    continue;

                // Под зеркалом должна быть вода, а не берег и не суша
                Tile underSurface = Main.tile[x, waterTop + 2];
                if (underSurface.HasTile || underSurface.LiquidAmount < 200)
                    continue;

                int candidateFloor = GenTiles.FindFloor(x, waterTop, scanBottom);
                if (candidateFloor == -1)
                    continue;

                int depth = candidateFloor - waterTop;
                deepestFound = Math.Max(deepestFound, depth);
                if (depth < MinSiteDepth)
                    continue;

                int leftFloor = GenTiles.FindFloor(x - FlatSampleSpan, waterTop, scanBottom);
                int rightFloor = GenTiles.FindFloor(x + FlatSampleSpan, waterTop, scanBottom);
                if (leftFloor == -1 || rightFloor == -1)
                    continue;

                int unevenness = Math.Abs(leftFloor - candidateFloor) + Math.Abs(rightFloor - candidateFloor);
                float score = Math.Abs(depth - PreferredSiteDepth) + unevenness * 1.5f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                centerX = x;
                floorY = candidateFloor;
            }

            if (centerX == -1)
                SoALog.Info("CoralArch", $"самая большая найденная глубина={deepestFound}, нужно от {MinSiteDepth}");

            return centerX != -1;
        }

        // Тело арки — эллиптическая полоса из СТЕН, а не из блоков: арка читается
        // силуэтом на фоне, но не перегораживает воду, и сквозь неё плывут где угодно,
        // а не только через проём. Толщина гуляет по шуму, иначе дуга выходит чертёжной
        private static void BuildArch(int centerX, int floorY, int halfSpan, int height, int waterTop)
        {
            int reach = halfSpan + MaxThickness + 2;
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = centerX + dx;
                float noise = TideNoise.Fbm(dx * 0.055f, NoiseChannel, 3);
                int thickness = MinThickness + (int)MathF.Round((MaxThickness - MinThickness) * noise);

                for (int dy = 0; dy <= height + thickness + FootFlareHeight; dy++)
                {
                    int y = floorY - dy;
                    if (y <= waterTop)
                        break;

                    // Пяты расширяются к дну: без этого арка стоит на двух карандашах
                    float flare = dy < FootFlareHeight ? (FootFlareHeight - dy) / (float)FootFlareHeight * 3f : 0f;

                    float radius = EllipseRadius(dx, dy, halfSpan, height);
                    float outer = 1f + (thickness + flare) / halfSpan;

                    if (radius > outer)
                        continue;

                    // И проём, и тело арки — вода: разница только в фоне. Породу под
                    // аркой снимаем, иначе на месте проёма остаётся горб дна
                    GenTiles.Flood(x, y);

                    if (radius >= 1f)
                        GenTiles.SetWall(x, y, ArchWallType);
                }
            }
        }

        private static float EllipseRadius(int dx, int dy, int halfSpan, int height)
        {
            float rx = dx / (float)halfSpan;
            float ry = dy / (float)height;
            return MathF.Sqrt(rx * rx + ry * ry);
        }

        // Стены декор не держат — сажать кораллы и ракушки можно только на дно.
        // Поэтому светящаяся кайма идёт по грунту под пролётом и гуще к середине:
        // с верхних уступов виден светлый овал, а в нём тёмная дуга арки
        private static void Decorate(int centerX, int floorY, int halfSpan, int height)
        {
            int coralType = ModContent.TileType<ShadowCoral_tile>();
            int glowShellType = ModContent.TileType<GlowShell_tile>();
            ushort stoneType = (ushort)ModContent.TileType<Tidestone_tile>();
            ushort sandType = (ushort)ModContent.TileType<Tidesand_tile>();

            int reach = halfSpan + MaxThickness + 2;
            int scanBottom = Math.Min(Main.maxTilesY - 30, floorY + 30);

            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = centerX + dx;
                int groundY = GenTiles.FindFloor(x, floorY - height, scanBottom);
                if (groundY == -1)
                    continue;

                Tile ground = Main.tile[x, groundY + 1];
                if (!ground.HasTile || (ground.TileType != stoneType && ground.TileType != sandType))
                    continue;

                if (Main.tile[x, groundY].LiquidAmount < 200)
                    continue;

                // К середине пролёта свечения больше
                float centerShare = 1f - TideNoise.Clamp01(Math.Abs(dx) / (float)Math.Max(1, halfSpan));
                float shellChance = 0.08f + 0.42f * centerShare;
                float coralChance = 0.2f * (1f - centerShare);

                float roll = WorldGen.genRand.NextFloat();
                if (roll < shellChance)
                    WorldGen.PlaceObject(x, groundY, glowShellType, true, WorldGen.genRand.Next(3));
                else if (roll < shellChance + coralChance)
                    WorldGen.PlaceObject(x, groundY, coralType, true, WorldGen.genRand.Next(3));

                dx += 2;   // не облепляем каждый столбец подряд
            }
        }
    }
}
