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
    // Чаша длинная и мелкая (15 тайлов): деревня стоит на сваях, а сваи должны
    // доставать до дна, да и «глубина» тут работает не на опасность, а на вид.
    // Берег выравнивается под один уровень, иначе зеркало воды получается ступенчатым
    public class JungleLakePass : GenPass
    {
        private const int MinLength = 150;
        private const int MaxLength = 214;
        // Ниже этой длины озеро уже не читается длинным, лучше не ставить совсем
        private const int ShortestLength = 80;
        private const int LengthStepDown = 14;
        private const int LakeDepth = 15;
        private const int ShoreRampColumns = 9;    // сколько столбцов уходит на плавный вход в воду
        private const int BedLining = 4;           // подстилка грязи под дном, чтобы чаша не текла
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
        private const int ReedMaxDepth = 4;
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

            int deepestY = CarveBasin(leftX, length, bankY);
            DressBanks(leftX, length, bankY);

            JungleLakeWorldData.LeftX = leftX;
            JungleLakeWorldData.RightX = leftX + length - 1;
            JungleLakeWorldData.WaterTopY = bankY + 1;
            JungleLakeWorldData.BedY = deepestY;

            WorldGen.RangeFrame(leftX - 6, bankY - ClearAboveHeight - 4,
                leftX + length + 6, deepestY + BedLining + 6);
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

                // К центру джунглей тянемся, но ровность важнее
                float score = spread + Math.Abs(left + length / 2 - origin) * 0.02f;

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
                for (int y = groundY; y <= groundY + LakeDepth + BedLining; y++)
                {
                    if (!GenTiles.InBounds(x, y))
                    {
                        reject = RejectReason.OutOfBounds;
                        return false;
                    }

                    Tile probe = Main.tile[x, y];
                    if (probe.TileType == TileID.LihzahrdBrick || Main.tileContainer[probe.TileType])
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
            if (max + LakeDepth + BedLining >= (int)Main.rockLayer)
            {
                reject = RejectReason.Layer;
                return false;
            }

            // Джунглями считаем участок, если хотя бы половина столбцов на грязи:
            // на поверхности хватает камня, песка и проплешин
            if (jungleColumns < length * 0.5f)
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
        private static int CarveBasin(int leftX, int length, int bankY)
        {
            int waterTopY = bankY + 1;
            int deepestY = waterTopY;

            for (int i = 0; i < length; i++)
            {
                int x = leftX + i;
                int depth = DepthAt(x, i, length);

                ClearAbove(x, bankY);

                // Сплошная грязь от берега до подстилки: чаша обязана держать воду
                for (int y = bankY; y <= bankY + depth + BedLining; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || !Main.tileSolid[tile.TileType])
                        GenTiles.PlaceSolid(x, y, TileID.Mud);
                }

                for (int y = waterTopY; y <= bankY + depth; y++)
                    GenTiles.Flood(x, y);

                deepestY = Math.Max(deepestY, bankY + depth);
            }

            return deepestY;
        }

        // Плавные съезды с берегов и рваное дно посередине: ровная ванна читается
        // как вырытый котлован, а не как озеро
        private static int DepthAt(int x, int column, int length)
        {
            float ramp = TideNoise.Clamp01(Math.Min(column, length - 1 - column) / (float)ShoreRampColumns);
            float relief = 0.82f + 0.3f * TideNoise.Fbm(x * 0.03f, NoiseChannel, 3);
            int depth = (int)MathF.Round(LakeDepth * ramp * relief);
            return Math.Clamp(depth, 0, LakeDepth);
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
            for (int y = waterTopY; y < bankY + LakeDepth + 2; y++)
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
