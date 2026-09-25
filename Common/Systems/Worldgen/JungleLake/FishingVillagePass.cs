using System;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using SoA.Common.Utils;
using SoA.Content.Items.Fishing;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Other;

namespace SoA.Common.Systems.JungleLake
{
    // Рыбацкая деревня на сваях. Стоит на озере в джунглях, а не в океане:
    // море деревню погубило, и жить у него больше некому.
    //
    // Разделение труда осознанное. Хижины — авторские постройки, они стампятся
    // из Assets/Structures готовыми. Сваи, мосты и разрушения генерируются кодом,
    // потому что глубина под каждой платформой своя и заранее её не нарисовать.
    // Пока хижин нет, ставится процедурная заглушка — деревня работает уже сейчас.
    public class FishingVillagePass : GenPass
    {
        // Дерево джунглевое: доски деревня брала из того леса, в котором стояла
        private const ushort DeckTile = TileID.RichMahogany;
        private const ushort PileTile = TileID.RichMahoganyBeam;
        private const ushort HutWallType = WallID.RichMaogany;

        private const int MinPlatforms = 4;
        private const int MaxPlatforms = 7;
        private const int MinDeckWidth = 12;
        private const int MaxDeckWidth = 23;
        private const int PileSpacing = 5;

        // Деревня пала: просевших платформ теперь треть, а не четверть
        private const int RuinedPlatformChanceDenominator = 3;
        private const int LakeEdgeMargin = 3;   // у самого берега сваи ставить некуда

        // Названия модулей хижин. Строишь их в игре, экспортируешь StructureWand,
        // кладёшь в Assets/Structures — генератор подхватит автоматически
        private static readonly string[] HutModules = { "village_hut_a", "village_hut_b", "village_hut_c" };

        private readonly Mod _mod;
        private int _seabedScanBottom;

        public FishingVillagePass(string name, float loadWeight, Mod mod) : base(name, loadWeight)
        {
            _mod = mod;
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "Stilts rise from the shallows";

            if (!JungleLakeWorldData.Exists)
            {
                SoALog.Info("Village", "озера в джунглях нет, деревню ставить некуда");
                return;
            }

            int anchorX = JungleLakeWorldData.CenterX;
            int waterTop = JungleLakeWorldData.WaterTopY;
            int leftLimit = JungleLakeWorldData.LeftX + LakeEdgeMargin;
            int rightLimit = JungleLakeWorldData.RightX - LakeEdgeMargin;

            // Дно озера близко, глубоко сканировать нечего
            _seabedScanBottom = Math.Min(Main.maxTilesY - 40, JungleLakeWorldData.BedY + 8);

            int platforms = WorldGen.genRand.Next(MinPlatforms, MaxPlatforms + 1);
            int startX = Math.Max(leftLimit, anchorX - EstimateSpan(platforms) / 2);
            int cursorX = startX;

            int widestDeckX = -1, widestDeckY = 0, widestWidth = 0;
            int previousEdgeX = -1, previousDeckY = 0;
            int chestsPlaced = 0;

            for (int index = 0; index < platforms; index++)
            {
                int width = WorldGen.genRand.Next(MinDeckWidth, MaxDeckWidth);

                // За берег деревня не вылезает: дальше сваям не во что упираться
                if (cursorX + width > rightLimit)
                    break;

                int deckY = waterTop - WorldGen.genRand.Next(4, 16);

                // Просевшая платформа стоит почти у воды и без хижины. Разрушение
                // должно быть выборочным, иначе деревня читается как ровный ряд
                bool ruined = WorldGen.genRand.NextBool(RuinedPlatformChanceDenominator);
                if (ruined)
                    deckY = waterTop - WorldGen.genRand.Next(-2, 3);

                if (!BuildPlatform(cursorX, deckY, width, ruined, ref chestsPlaced))
                {
                    cursorX += width + WorldGen.genRand.Next(9, 24);
                    continue;
                }

                if (!ruined && width > widestWidth)
                {
                    widestWidth = width;
                    widestDeckX = cursorX;
                    widestDeckY = deckY;
                }

                if (previousEdgeX != -1)
                    BuildBridge(previousEdgeX, previousDeckY, cursorX, deckY);

                previousEdgeX = cursorX + width - 1;
                previousDeckY = deckY;
                cursorX += width + WorldGen.genRand.Next(9, 24);
            }

            // Вышка вытягивает силуэт вверх: у деревни он низкий, и без неё
            // она теряется среди джунглевых деревьев
            if (widestDeckX != -1)
                BuildWatchtower(widestDeckX + widestWidth / 2, widestDeckY);

            WorldGen.RangeFrame(startX - 8, waterTop - 60, cursorX + 8, _seabedScanBottom);
            SoALog.Info("Village",
                $"деревня на озере: x={startX}..{cursorX}, зеркало={waterTop}, сундуков={chestsPlaced}");
        }

        private static int EstimateSpan(int platforms)
            => platforms * ((MinDeckWidth + MaxDeckWidth) / 2 + 16);

        // Настил, сваи под ним и хижина сверху
        private bool BuildPlatform(int leftX, int deckY, int width, bool ruined, ref int chestsPlaced)
        {
            if (leftX < 40 || leftX + width >= Main.maxTilesX - 40)
                return false;

            // Настил под хижиной обязан быть ровным: пол вело волной, хижина строилась
            // по уровню первого столбца, и под сундуком оказывалась пустота — поэтому
            // сундуки не ставились вообще. Волну оставляем только просевшим платформам,
            // и по смыслу так честнее: целый помост ровный, рухнувший перекошен
            int[] deckLevel = new int[width];
            int tilt = ruined ? WorldGen.genRand.Next(-1, 2) : 0;
            for (int i = 0; i < width; i++)
            {
                deckLevel[i] = deckY + (int)(tilt * i / (float)width * 3f);
                if (ruined && WorldGen.genRand.NextBool(6))
                    deckLevel[i]++;
            }

            bool anyDeck = false;
            for (int i = 0; i < width; i++)
            {
                // У просевшей платформы настил рваный
                if (ruined && WorldGen.genRand.NextBool(3))
                    continue;

                int x = leftX + i;
                PlaceSolid(x, deckLevel[i], DeckTile);
                anyDeck = true;

                if (i % PileSpacing == WorldGen.genRand.Next(PileSpacing))
                    BuildPile(x, deckLevel[i] + 1, ruined);
            }

            if (!anyDeck)
                return false;

            // Крайние сваи ставим всегда — без них настил висит в воздухе
            BuildPile(leftX, deckLevel[0] + 1, ruined);
            BuildPile(leftX + width - 1, deckLevel[width - 1] + 1, ruined);

            if (!ruined)
                RaiseHut(leftX, deckLevel[0], width, ref chestsPlaced);

            return true;
        }

        // Свая от настила до дна. Дно ищем по месту: под каждой платформой оно своё
        private void BuildPile(int x, int fromY, bool ruined)
        {
            int floorY = FindSeabed(x, fromY);
            if (floorY == -1)
                return;

            // Часть свай подломлена и не достаёт дна
            int stopY = ruined || WorldGen.genRand.NextBool(4)
                ? floorY - WorldGen.genRand.Next(3, 12)
                : floorY;

            int lean = WorldGen.genRand.NextBool(3) ? WorldGen.genRand.Next(-1, 2) : 0;
            for (int y = fromY; y <= stopY; y++)
            {
                int drift = lean == 0 ? 0 : (y - fromY) / 9 * lean;
                PlaceSolid(x + drift, y, PileTile);
            }

            // Подпорка-раскос у дна
            if (!ruined && WorldGen.genRand.NextBool(3) && stopY - 6 > fromY)
            {
                int braceDir = WorldGen.genRand.NextBool() ? 1 : -1;
                for (int step = 1; step <= WorldGen.genRand.Next(3, 7); step++)
                    PlaceSolid(x + braceDir * step, stopY - step, PileTile);
            }
        }

        // Хижина: стамп авторского модуля, а пока его нет — процедурная заглушка
        private void RaiseHut(int leftX, int deckY, int width, ref int chestsPlaced)
        {
            string module = HutModules[WorldGen.genRand.Next(HutModules.Length)];
            if (StructureIO.TryGetSize(_mod, module, out int w, out int h) && w <= width + 4)
            {
                int topLeftX = leftX + (width - w) / 2;
                int topLeftY = deckY - h;
                if (StructureIO.Place(_mod, module, topLeftX, topLeftY, WorldGen.genRand.NextBool()))
                {
                    PopulateStructureChests(topLeftX, topLeftY, w, h, ref chestsPlaced);
                    return;
                }
            }

            BuildFallbackHut(leftX, deckY, width, ref chestsPlaced);
        }

        private void BuildFallbackHut(int leftX, int deckY, int width, ref int chestsPlaced)
        {
            int hutWidth = Math.Min(width - 2, WorldGen.genRand.Next(8, 14));
            if (hutWidth < 5)
                return;

            int hutLeft = leftX + (width - hutWidth) / 2;
            int height = WorldGen.genRand.Next(5, 8);
            int roofY = deckY - height;

            for (int x = hutLeft; x < hutLeft + hutWidth; x++)
            {
                for (int y = roofY; y < deckY; y++)
                {
                    bool isEdge = x == hutLeft || x == hutLeft + hutWidth - 1 || y == roofY;
                    bool doorway = y >= deckY - 3 && x == hutLeft + hutWidth / 2;

                    if (isEdge && !doorway)
                        PlaceSolid(x, y, DeckTile);
                    else
                        ClearAndWall(x, y);
                }
            }

            // Свет виден издалека и делает деревню заметной ночью
            if (WorldGen.genRand.NextBool(2))
                WorldGen.PlaceTile(hutLeft + 1, deckY - 2, ModContent.TileType<Ttorch_tile>(), true);

            // Первый сундук ставим обязательно: он несёт снасти, и без него
            // деревня не даёт ничего, кроме досок
            if (chestsPlaced == 0 || (chestsPlaced < 3 && WorldGen.genRand.NextBool(2)))
            {
                int chestX = hutLeft + hutWidth / 2 - 1;
                if (PlaceVillageChest(chestX, deckY - 1, chestsPlaced))
                    chestsPlaced++;
            }
        }

        // Верёвочный мост между платформами: провисает по параболе
        private void BuildBridge(int fromX, int fromY, int toX, int toY)
        {
            int span = toX - fromX;
            if (span < 3 || span > 40)
                return;

            int sag = Math.Max(1, span / 6 + WorldGen.genRand.Next(0, 3));
            for (int i = 1; i < span; i++)
            {
                float t = i / (float)span;
                float curve = 4f * t * (1f - t);   // 0 на концах, 1 в середине
                int y = (int)(TideNoise.Lerp(fromY, toY, t) + curve * sag);

                // Обрыв в середине пролёта: не все мосты пережили годы
                if (WorldGen.genRand.NextBool(24))
                    break;

                WorldGen.PlaceTile(fromX + i, y, TileID.Platforms, true, false, -1, 0);
            }
        }

        private void BuildWatchtower(int x, int deckY)
        {
            int height = WorldGen.genRand.Next(15, 25);
            int topY = deckY - height;

            for (int y = deckY - 1; y >= topY; y--)
            {
                PlaceSolid(x - 1, y, PileTile);
                PlaceSolid(x + 1, y, PileTile);
                // Поперечины через неравные промежутки
                if ((deckY - y) % WorldGen.genRand.Next(3, 6) == 0)
                    PlaceSolid(x, y, PileTile);
            }

            for (int dx = -2; dx <= 2; dx++)
                PlaceSolid(x + dx, topY - 1, DeckTile);

            WorldGen.PlaceTile(x, topY - 2, ModContent.TileType<Ttorch_tile>(), true);
        }

        private void PopulateStructureChests(int left, int top, int w, int h, ref int chestsPlaced)
        {
            for (int x = left; x < left + w; x++)
            {
                for (int y = top; y < top + h; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || !Main.tileContainer[tile.TileType])
                        continue;
                    if (tile.TileFrameX % 36 != 0 || tile.TileFrameY % 36 != 0)
                        continue;

                    int index = Chest.CreateChest(x, y);
                    if (index == -1)
                        index = Chest.FindChest(x, y);
                    if (index < 0)
                        continue;

                    FillVillageChest(Main.chest[index], chestsPlaced);
                    chestsPlaced++;
                }
            }
        }

        private static bool PlaceVillageChest(int x, int y, int alreadyPlaced)
        {
            int index = WorldGen.PlaceChest(x, y, TileID.Containers, false, 0);
            if (index < 0)
                return false;

            FillVillageChest(Main.chest[index], alreadyPlaced);
            return true;
        }

        // Лут рыбацкий, а не пиратский: снасти, наживка, припасы.
        // Первый сундук содержит вещь, ради которой стоит сюда доплыть
        private static void FillVillageChest(Chest chest, int alreadyPlaced)
        {
            var fill = new ChestFill(chest);

            if (alreadyPlaced == 0)
            {
                fill.Add(ItemID.ReinforcedFishingPole);
                fill.Add(ModContent.ItemType<Ichthyofang>(), WorldGen.genRand.Next(2, 6));
            }

            // Снасти деревни — единственный запас на удочку прилива до того,
            // как игрок начнёт вылавливать их сам
            fill.Add(ModContent.ItemType<SunkenTackle>(), WorldGen.genRand.Next(2, 6));
            fill.Add(ItemID.SilverCoin, WorldGen.genRand.Next(15, 70));
            fill.Add(ModContent.ItemType<Tidesand>(), WorldGen.genRand.Next(20, 50));
            fill.Add(ItemID.GillsPotion, WorldGen.genRand.Next(1, 3));

            if (WorldGen.genRand.NextBool(3))
                fill.Add(ModContent.ItemType<TideCrate>());

            if (WorldGen.genRand.NextBool())
                fill.Add(ItemID.MasterBait, WorldGen.genRand.Next(1, 3));
            else
                fill.Add(ItemID.ApprenticeBait, WorldGen.genRand.Next(2, 6));

            if (WorldGen.genRand.NextBool(3))
                fill.Add(ItemID.Sextant);
        }

        private int FindSeabed(int x, int fromY) => GenTiles.FindFloor(x, fromY, _seabedScanBottom);

        private static void PlaceSolid(int x, int y, ushort type) => GenTiles.PlaceSolid(x, y, type);

        private static void ClearAndWall(int x, int y) => GenTiles.Clear(x, y, HutWallType);
    }
}
