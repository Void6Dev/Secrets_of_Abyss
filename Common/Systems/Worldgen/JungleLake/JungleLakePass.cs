using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.WorldBuilding;
using SoA.Common.Utils;

namespace SoA.Common.Systems.JungleLake
{
    // Озеро в джунглях: новое место рыбацкой деревни. Вода здесь обычная джунглевая,
    // к Приливу Теней озеро не относится — это отдельная локация со своей историей.
    //
    // Чаша длинная и мелкая (до 17 тайлов): деревня стоит на сваях, а сваи должны
    // доставать до дна, да и «глубина» тут работает не на опасность, а на вид.
    // Берег выравнивается под один уровень, иначе зеркало воды получается ступенчатым,
    // а за краями озера рельеф плавно сводится к этому уровню: без сведения озеро
    // стояло в прямоугольной траншее с отвесными стенками.
    //
    // Посередине дна бьёт родник — по лору это отросток Разлома, через который
    // озеро «дышит» (Docs/JungleVillageConcept.md)
    public class JungleLakePass : GenPass
    {
        private const int MinLength = 150;
        private const int MaxLength = 214;
        // Ниже этой длины озеро уже не читается длинным, лучше не ставить совсем
        private const int ShortestLength = 80;
        private const int LengthStepDown = 14;
        private const int MaxLakeDepth = 17;
        // Берега разные: один пологий, заболоченный, другой круче. Одинаковые
        // съезды с двух сторон делали озеро похожим на вырытую ванну
        private const int SteepShoreRamp = 10;
        private const int MarshShoreRamp = 22;
        private const int BedLining = 4;           // подстилка грязи под дном, чтобы чаша не текла
        // Сколько столбцов за краем озера уходит на сведение рельефа к уровню берега
        private const int BankBlendColumns = 30;
        private const int SpringDepth = 10;        // глубина жерла родника ниже дна
        private const int SpringLining = 2;
        private const float MinJungleShare = 0.75f;
        private const float CenterPullPerTile = 0.06f;
        private const int TreePadHalfWidth = 2;    // ванильному дереву нужна ровная площадка 5 тайлов
        private const int MinTreeSpacing = 6;
        private const int MaxTreeSpacing = 10;
        private const int SearchRadius = 700;
        private const int SearchStep = 6;
        // Разброс высот в 18 тайлов джунгли не выдерживают: на 150 столбцов холмов
        // его не бывает, и участок не находился вообще. Ровность теперь предпочтение,
        // а не пропуск: берег всё равно выравнивается под один уровень
        private const int MaxSurfaceSpread = 44;
        // Снимаем вверх с запасом: берег ровняется по медиане, и холм над ней
        // может подниматься на весь допустимый разброс высот
        private const int ClearAboveHeight = 64;
        private const int NoiseChannel = 77;

        // Мель под камыш и открытая вода под кувшинки
        private const int ReedMaxDepth = 5;
        private const int LilyMinDepth = 5;

        // Почему участок не подошёл. Нужно только для диагностики в логе
        private enum RejectReason
        {
            None,
            NoGround,
            OutOfBounds,
            Structure,
            Layer,
            NotJungle
        }

        public JungleLakePass(string name, float loadWeight) : base(name, loadWeight)
        {
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "A still lake settles in the jungle";

            // Длину подбираем сверху вниз. Ровного участка на 200 тайлов в джунглях
            // может не быть вовсе, и вкапывать озеро в склон с разбросом высот под
            // сотню тайлов — это карьер, а не озеро. Короткое озеро лучше карьера
            int length = 0;
            int leftX = -1, bankY = 0;
            for (int attempt = WorldGen.genRand.Next(MinLength, MaxLength + 1);
                attempt >= ShortestLength;
                attempt -= LengthStepDown)
            {
                if (!TryChooseSite(attempt, allowAnySpread: false, out leftX, out bankY))
                    continue;

                length = attempt;
                break;
            }

            // Последняя попытка: самое короткое озеро на самом ровном из найденного,
            // каким бы неровным оно ни было. Лучше кривое озеро, чем никакого
            if (length == 0 && TryChooseSite(ShortestLength, allowAnySpread: true, out leftX, out bankY))
                length = ShortestLength;

            if (length == 0)
            {
                SoALog.Info("JungleLake",
                    $"участок не найден ни на одной длине: центр джунглей x={GenVars.jungleOriginX}");
                return;
            }

            SoALog.Info("JungleLake", $"озеро: x={leftX}..{leftX + length - 1}, берег y={bankY}, длина={length}");

            // Какой берег заболоченный — решает мир, а не код
            bool marshOnLeft = WorldGen.genRand.NextBool();
            int leftRamp = marshOnLeft ? MarshShoreRamp : SteepShoreRamp;
            int rightRamp = marshOnLeft ? SteepShoreRamp : MarshShoreRamp;

            int deepestY = CarveBasin(leftX, length, bankY, leftRamp, rightRamp);
            CarveSpring(leftX, length, bankY, leftRamp, rightRamp, out int springX, out int springBottomY);

            BlendBank(leftX, -1, bankY);
            BlendBank(leftX + length - 1, 1, bankY);
            DressBanks(leftX, length, bankY);
            PlantShoreTrees(leftX, -1);
            PlantShoreTrees(leftX + length - 1, 1);

            JungleLakeWorldData.LeftX = leftX;
            JungleLakeWorldData.RightX = leftX + length - 1;
            JungleLakeWorldData.WaterTopY = bankY + 1;
            JungleLakeWorldData.BedY = deepestY;
            JungleLakeWorldData.SpringX = springX;
            JungleLakeWorldData.SpringY = springBottomY;

            int margin = BankBlendColumns + 6;
            WorldGen.RangeFrame(leftX - margin, bankY - ClearAboveHeight - 4,
                leftX + length + margin, Math.Max(deepestY, springBottomY) + BedLining + 6);
        }

        // Ищем ровный участок джунглей: чем меньше разброс высот, тем меньше
        // придётся ломать под ровное зеркало воды
        private static bool TryChooseSite(int length, bool allowAnySpread, out int leftX, out int bankY)
        {
            leftX = -1;
            bankY = 0;

            int origin = GenVars.jungleOriginX;

            float bestScore = float.MaxValue;
            // Запас на случай, когда джунгли нигде не ровнее порога: озеро всё равно
            // должно появиться, поэтому берём самый ровный участок из возможных
            float flattestScore = float.MaxValue;
            int flattestLeftX = -1, flattestBankY = 0, flattestSpread = 0;

            // Счётчики отказов: без них причина неудачи не видна, а каждый прогон
            // стоит генерации мира
            var rejects = new Dictionary<RejectReason, int>();
            int windows = 0;

            for (int left = origin - SearchRadius; left <= origin + SearchRadius - length; left += SearchStep)
            {
                if (left < 120 || left + length >= Main.maxTilesX - 120)
                    continue;

                windows++;
                if (!MeasureWindow(left, length, out int windowBankY, out int spread, out RejectReason reject))
                {
                    rejects[reject] = rejects.GetValueOrDefault(reject) + 1;
                    continue;
                }

                // К центру джунглей тянемся заметно: на слабой тяге выигрывал
                // ровный край джунглей, и полозера оказывалось на обычной земле
                float score = spread + Math.Abs(left + length / 2 - origin) * CenterPullPerTile;

                if (score < flattestScore)
                {
                    flattestScore = score;
                    flattestLeftX = left;
                    flattestBankY = windowBankY;
                    flattestSpread = spread;
                }

                if (spread > MaxSurfaceSpread || score >= bestScore)
                    continue;

                bestScore = score;
                leftX = left;
                bankY = windowBankY;
            }

            if (leftX != -1)
                return true;

            // Порог ровности обходим только когда об этом попросили: иначе первая же
            // длина хватала склон с разбросом под сотню тайлов, и лестница длин
            // не успевала сработать
            if (!allowAnySpread)
                return false;

            if (flattestLeftX == -1)
            {
                string reasons = string.Join(", ", rejects.Select(pair => $"{pair.Key}={pair.Value}"));
                SoALog.Info("JungleLake",
                    $"ни одно окно не подошло: окон={windows}, отказы: {reasons}, " +
                    $"worldSurface={(int)Main.worldSurface}, rockLayer={(int)Main.rockLayer}");
                return false;
            }

            SoALog.Info("JungleLake",
                $"ровного участка нет, берём самый ровный: разброс={flattestSpread}, порог={MaxSurfaceSpread}");
            leftX = flattestLeftX;
            bankY = flattestBankY;
            return true;
        }

        private static bool MeasureWindow(int left, int length, out int bankY, out int spread,
            out RejectReason reject)
        {
            bankY = 0;
            spread = 0;
            reject = RejectReason.None;

            int[] heights = new int[length];
            int min = int.MaxValue, max = int.MinValue, jungleColumns = 0;

            for (int i = 0; i < length; i++)
            {
                int x = left + i;
                int groundY = GenTiles.FindGroundTop(x);
                if (groundY == -1)
                {
                    reject = RejectReason.NoGround;
                    return false;
                }

                Tile ground = Main.tile[x, groundY];
                if (ground.TileType == TileID.Mud || ground.TileType == TileID.JungleGrass)
                    jungleColumns++;

                // Чужие постройки участок отменяют, а вода и стены нет: лужи и джунглевый
                // фон тут норма, чаша всё равно перекладывается грязью и заливается заново
                for (int y = groundY; y <= groundY + MaxLakeDepth + BedLining; y++)
                {
                    if (!GenTiles.InBounds(x, y))
                    {
                        reject = RejectReason.OutOfBounds;
                        return false;
                    }

                    Tile probe = Main.tile[x, y];
                    if (IsProtected(probe))
                    {
                        reject = RejectReason.Structure;
                        return false;
                    }
                }

                heights[i] = groundY;
                min = Math.Min(min, groundY);
                max = Math.Max(max, groundY);
            }

            // Проверка по worldSurface тут была ошибкой: линия слоя проходит ровно
            // по уровню земли, и условие «вся чаша выше неё» отбраковывало все окна
            // подряд. Поверхность гарантирует сам FindGroundTop, а от подземелья
            // достаточно того, что чаша не достаёт до каменного слоя
            if (max + MaxLakeDepth + SpringDepth + SpringLining >= (int)Main.rockLayer)
            {
                reject = RejectReason.Layer;
                return false;
            }

            // Джунглями считаем участок, где грязи большинство: на поверхности
            // хватает камня и проплешин, но половины мало — озеро вставало на границе
            if (jungleColumns < length * MinJungleShare)
            {
                reject = RejectReason.NotJungle;
                return false;
            }

            // Порог ровности проверяет вызывающий: ему нужен разброс и у тех участков,
            // которые в порог не влезли, иначе запасной вариант выбрать не из чего
            spread = max - min;

            // Медиана, а не среднее: пара глубоких провалов не должна утаскивать
            // берег вниз, иначе озеро вкапывается в склон
            Array.Sort(heights);
            bankY = heights[length / 2];
            return true;
        }

        // Возвращает Y самой глубокой точки чаши
        private static int CarveBasin(int leftX, int length, int bankY, int leftRamp, int rightRamp)
        {
            int waterTopY = bankY + 1;
            int deepestY = waterTopY;

            for (int i = 0; i < length; i++)
            {
                int x = leftX + i;
                int depth = DepthAt(x, i, length, leftRamp, rightRamp);

                ClearAbove(x, bankY);

                // Сплошная грязь от берега до подстилки: чаша обязана держать воду
                for (int y = bankY; y <= bankY + depth + BedLining; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || !Main.tileSolid[tile.TileType])
                        GenTiles.PlaceSolid(x, y, TileID.Mud);
                }

                // Ряд берега над водой обязан быть открыт. Раньше он оставался
                // грязью и зарастал травой: озеро лежало под сплошной крышкой,
                // камыш и кувшинки не ставились, а сваи упирались в крышку
                if (depth > 0)
                {
                    GenTiles.Clear(x, bankY);
                    Main.tile[x, bankY].WallType = WallID.None;
                }

                // Стены под водой снимаем: озеро, вырытое в склон, иначе стоит
                // на подземном фоне и читается пещерой, а не открытой водой
                for (int y = waterTopY; y <= bankY + depth; y++)
                {
                    GenTiles.Flood(x, y);
                    Main.tile[x, y].WallType = WallID.None;
                }

                deepestY = Math.Max(deepestY, bankY + depth);
            }

            return deepestY;
        }

        // Съезды с берегов разной длины и волнистое дно: две-три ямы и отмели
        // между ними. Раньше дно упиралось в жёсткий предел и лежало ровной плитой
        private static int DepthAt(int x, int column, int length, int leftRamp, int rightRamp)
        {
            float fromLeft = column / (float)leftRamp;
            float fromRight = (length - 1 - column) / (float)rightRamp;
            float ramp = TideNoise.SmoothStep(0f, 1f, Math.Min(fromLeft, fromRight));

            // Fbm жмётся к 0.5, поэтому разброс растягиваем: иначе ямы и отмели
            // отличаются на пару тайлов и дно всё равно читается плитой
            float noise = TideNoise.Clamp01((TideNoise.Fbm(x * 0.022f, NoiseChannel, 3) - 0.5f) * 2.4f + 0.5f);
            float relief = 0.4f + 0.7f * noise;
            float depth = TideNoise.SoftMin(MaxLakeDepth * relief, MaxLakeDepth, 3f) * ramp;
            return Math.Clamp((int)MathF.Round(depth), 0, MaxLakeDepth);
        }

        // Родник — узкое жерло в самой глубокой части средней трети дна. Обложено
        // грязью со всех сторон: под озером могут быть пещеры, и вода не должна уйти
        private static void CarveSpring(int leftX, int length, int bankY, int leftRamp, int rightRamp,
            out int springX, out int springBottomY)
        {
            int searchFrom = length / 3, searchTo = length * 2 / 3;
            int bestColumn = length / 2, bestDepth = -1;
            for (int i = searchFrom; i <= searchTo; i++)
            {
                int depth = DepthAt(leftX + i, i, length, leftRamp, rightRamp);
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    bestColumn = i;
                }
            }

            springX = leftX + bestColumn;
            int bedY = bankY + bestDepth;
            springBottomY = bedY + SpringDepth;

            int outer = 1 + SpringLining;
            for (int y = bedY + 1; y <= springBottomY + SpringLining; y++)
            {
                // Жерло сужается книзу: сверху три тайла, у дна два
                int halfRight = y - bedY > SpringDepth / 2 ? 0 : 1;

                for (int dx = -outer; dx <= outer; dx++)
                {
                    int x = springX + dx;
                    bool inVent = y <= springBottomY && dx >= -1 && dx <= halfRight;

                    if (inVent)
                    {
                        GenTiles.Flood(x, y);
                        Main.tile[x, y].WallType = WallID.None;
                    }
                    else if (!GenTiles.IsSolid(x, y))
                    {
                        GenTiles.PlaceSolid(x, y, TileID.Mud);
                    }
                }
            }
        }

        // Сводит рельеф за краем озера к уровню берега: холм срезается в склон,
        // низина подсыпается. Нужен пологий спуск к воде, а не стенка траншеи
        private static void BlendBank(int edgeX, int direction, int bankY)
        {
            for (int step = 1; step <= BankBlendColumns; step++)
            {
                int x = edgeX + direction * step;
                int naturalY = GenTiles.FindGroundTop(x);
                if (naturalY == -1)
                    return;

                float t = TideNoise.SmoothStep(0f, 1f, step / (float)BankBlendColumns);
                int targetY = (int)MathF.Round(TideNoise.Lerp(bankY, naturalY, t));
                if (targetY == naturalY)
                    continue;

                // Дальше чужой постройки берег не трогаем: лучше короткий склон,
                // чем срезанный храм
                if (TouchesProtected(x, Math.Min(naturalY, targetY) - ClearAboveHeight, Math.Max(naturalY, targetY)))
                    return;

                ClearAbove(x, Math.Max(naturalY, targetY));

                // Подсыпаем низину до нового уровня. Срезанному холму тоже нужна
                // корка: срез мог вскрыть пещеру, и склон остался бы с дырой
                for (int y = targetY; y < Math.Max(naturalY, targetY + 3); y++)
                {
                    if (!GenTiles.IsSolid(x, y))
                        GenTiles.PlaceSolid(x, y, TileID.Mud);
                }

                if (Main.tile[x, targetY].TileType == TileID.Mud)
                    GenTiles.PlaceSolid(x, targetY, TileID.JungleGrass);
            }
        }

        // Склоны после сведения голые: ровная лысина вокруг озера выдаёт генератор
        // сильнее всего. Сажаем деревья через ванильный рост, чтобы порода дерева
        // взялась от джунглевой травы
        private static void PlantShoreTrees(int edgeX, int direction)
        {
            int step = WorldGen.genRand.Next(3, MinTreeSpacing + 1);
            while (step <= BankBlendColumns + MinTreeSpacing)
            {
                int x = edgeX + direction * step;
                int groundY = GenTiles.FindGroundTop(x);
                if (groundY != -1 && Main.tile[x, groundY].TileType == TileID.JungleGrass
                    && LevelTreePad(x, groundY))
                {
                    WorldGen.GrowTree(x, groundY);
                }

                step += WorldGen.genRand.Next(MinTreeSpacing, MaxTreeSpacing + 1);
            }
        }

        // На сведённом склоне ровных пяти тайлов почти не бывает, и дерево
        // не вырастало ни одно. Подрезаем под ствол маленькую ступеньку
        private static bool LevelTreePad(int centerX, int groundY)
        {
            for (int dx = -TreePadHalfWidth; dx <= TreePadHalfWidth; dx++)
            {
                if (TouchesProtected(centerX + dx, groundY - ClearAboveHeight, groundY + 1))
                    return false;
            }

            for (int dx = -TreePadHalfWidth; dx <= TreePadHalfWidth; dx++)
            {
                int x = centerX + dx;
                ClearAbove(x, groundY);
                GenTiles.PlaceSolid(x, groundY, TileID.JungleGrass);
                if (!GenTiles.IsSolid(x, groundY + 1))
                    GenTiles.PlaceSolid(x, groundY + 1, TileID.Mud);
            }
            return true;
        }

        private static bool IsProtected(Tile tile)
            => tile.HasTile && (tile.TileType == TileID.LihzahrdBrick || Main.tileContainer[tile.TileType]);

        private static bool TouchesProtected(int x, int fromY, int toY)
        {
            for (int y = fromY; y <= toY; y++)
            {
                if (GenTiles.InBounds(x, y) && IsProtected(Main.tile[x, y]))
                    return true;
            }
            return false;
        }

        // Деревья и лианы над будущим озером снимаем через KillTile: срезанные
        // напрямую, они оставили бы висеть кроны
        private static void ClearAbove(int x, int bankY)
        {
            for (int y = bankY - 1; y >= bankY - ClearAboveHeight; y--)
            {
                if (!GenTiles.InBounds(x, y))
                    continue;

                Tile tile = Main.tile[x, y];
                if (tile.HasTile)
                    WorldGen.KillTile(x, y, noItem: true);

                if (tile.LiquidAmount > 0)
                    tile.LiquidAmount = 0;

                // Подземные стены над срезанным грунтом закрывают небо, и берег
                // выглядит дном пещеры
                tile.WallType = WallID.None;
            }
        }

        // Трава по берегу, камыш на мели, кувшинки на открытой воде
        private static void DressBanks(int leftX, int length, int bankY)
        {
            int waterTopY = bankY + 1;

            for (int i = 0; i < length; i++)
            {
                int x = leftX + i;
                Tile bank = Main.tile[x, bankY];

                // Сухой берег: грязь наверху зарастает джунглевой травой
                if (bank.HasTile && bank.TileType == TileID.Mud && !Main.tile[x, bankY - 1].HasTile
                    && Main.tile[x, bankY - 1].LiquidAmount == 0)
                {
                    GenTiles.PlaceSolid(x, bankY, TileID.JungleGrass);
                    continue;
                }

                int depth = WaterDepthAt(x, waterTopY, bankY);
                if (depth <= 0)
                    continue;

                if (depth <= ReedMaxDepth)
                {
                    // Камыш растёт от дна к поверхности: точную привязку выбирает
                    // сам ванильный хелпер, поэтому пробуем всю мель по высоте
                    if (WorldGen.genRand.NextBool(2))
                        TryPlaceCatTail(x, waterTopY, waterTopY + depth);
                    continue;
                }

                if (depth >= LilyMinDepth && WorldGen.genRand.NextBool(7))
                    WorldGen.PlaceLilyPad(x, waterTopY);
            }
        }

        private static int WaterDepthAt(int x, int waterTopY, int bankY)
        {
            int depth = 0;
            for (int y = waterTopY; y < bankY + MaxLakeDepth + 2; y++)
            {
                if (!GenTiles.InBounds(x, y))
                    break;

                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    break;
                if (tile.LiquidAmount < 200)
                    break;

                depth++;
            }
            return depth;
        }

        private static void TryPlaceCatTail(int x, int fromY, int toY)
        {
            for (int y = fromY; y <= toY; y++)
            {
                if (WorldGen.PlaceCatTail(x, y).X > 0)
                    return;
            }
        }
    }
}
