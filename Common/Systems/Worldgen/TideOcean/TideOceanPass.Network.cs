using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using SoA.Common.Utils;
using SoA.Content.Tiles.Nature;
using SoA.Content.Tiles.Other;
using SoA.Content.Walls;
using SoA.Content.Worldgen;

namespace SoA.Common.Systems.TideOcean
{
    // Связность биома: шахты между зонами, сеть тоннелей в породе,
    // воздушные карманы, секретные комнаты и декор дна.
    public partial class TideOceanPass
    {
        // Задник глубины: за породой Tidestone_wall, за наносами Tidesand_wall.
        // Своего спрайта у стен пока нет, подмена ванильным задана в самих классах
        private static ushort DeepWallType => (ushort)ModContent.WallType<Tidestone_wall>();
        private static ushort DeepSandWallType => (ushort)ModContent.WallType<Tidesand_wall>();

        private const int ShaftRadius = 5;
        private const int SealRectWidth = 20;
        private const int SealRectHeight = 10;

        // Площадка затонувшего галеона: пасс корабля берёт её вместо своего сканирования
        public static int ShipAnchorX { get; private set; }
        public static int ShipAnchorFloorY { get; private set; }
        // Площадка ориентира зоны 1. Раньше на ней стояла рыбацкая деревня, теперь
        // деревня уехала на озеро в джунглях, а здесь встаёт коралловая арка
        public static int LandmarkAnchorX { get; private set; }
        public static int LandmarkWaterTopY { get; private set; }

        private static void ResetShipAnchor()
        {
            ShipAnchorX = 0;
            ShipAnchorFloorY = 0;
            LandmarkAnchorX = 0;
            LandmarkWaterTopY = 0;
        }

        // Ориентир встаёт на ровное мелководье шельфа: там читается силуэт и хватает
        // толщи воды на арку. Целимся в глубину около 26 тайлов — ниже арка тонет,
        // выше пробивает зеркало воды и превращается в мост
        private void ChooseLandmarkSite()
        {
            // Целимся глубже, чем при деревне на сваях: арке нужна толща воды над пятой,
            // а на 17 тайлах шельфа она выходит приплюснутой и не читается
            const int PreferredDepth = 38;
            const int MinDepth = 14;
            const int KeepClearOfLandmarks = 50;

            int best = -1, bestScore = int.MaxValue;
            int fallback = -1, fallbackScore = int.MaxValue;

            // Верхнего предела глубины нет намеренно: на большом мире открытая вода
            // уходит на две-три сотни тайлов, и любой потолок отсекал все площадки
            // сразу. Глубокая арка хуже мелкой, но лучше отсутствующей
            var depths = new List<string>();

            foreach (int terraceGx in _terraceGx)
            {
                if (terraceGx < 8 || terraceGx >= _seaFloorGy.Length)
                    continue;

                int depth = _seaFloorGy[terraceGx] - _gWater;
                depths.Add($"gx={terraceGx}:{depth}");

                // Мелкие площадки не отбрасываются, а получают штраф: настоящую глубину
                // всё равно промеряет пасс арки по готовому миру, а здешние _seaFloorGy —
                // это профиль до вырезания, и доверять ему как последнему слову нельзя
                int score = Math.Abs(depth - PreferredDepth) + (depth < MinDepth ? 1000 : 0);

                // Площадка, занятая ареной или террасой галеона, идёт в запас:
                // лучше поставить ориентир рядом с ними, чем не поставить вовсе
                bool crowded = Math.Abs(terraceGx - _arenaShaftGx) < KeepClearOfLandmarks
                    || Math.Abs(terraceGx - _shipTerraceGx) < KeepClearOfLandmarks;

                if (crowded)
                {
                    if (score < fallbackScore)
                    {
                        fallbackScore = score;
                        fallback = terraceGx;
                    }
                    continue;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = terraceGx;
                }
            }

            if (best == -1)
                best = fallback;

            if (best == -1)
            {
                SoALog.Info("TideOcean",
                    $"площадка ориентира не найдена, глубины площадок: {string.Join(", ", depths)}");
                return;
            }

            LandmarkAnchorX = _grid.ToWorldX(best);
            LandmarkWaterTopY = _waterTopY;
            SoALog.Info("TideOcean",
                $"площадка ориентира: x={LandmarkAnchorX}, глубина={_seaFloorGy[best] - _gWater}");
        }

        private readonly List<(int Gx, int Gy, int Width, int Height)> _sealRects = new();
        private int _arenaShaftGx, _shipTerraceGx;

        // Отладочный снимок биома целиком. Выключить перед релизом:
        // на генерацию мира он добавляет файл в папку сохранений
        private static readonly bool ExportPreviewImage = true;

        private void ExportPreview()
        {
            if (!ExportPreviewImage)
                return;

            try
            {
                string path = TideGridPreview.Save(_grid, _sealRects, _gWater,
                    Path.Combine(Main.SavePath, "TideOceanPreview"),
                    $"tide_{Main.maxTilesX}x{Main.maxTilesY}_{Main.ActiveWorldFileData?.Seed ?? 0}.png");
                Log("снимок биома: " + path);
            }
            catch (Exception error)
            {
                // Снимок — вспомогательный; его падение не должно ломать генерацию мира
                Log("снимок биома не сохранён: " + error.Message);
            }
        }

        // Единственные проходы между зонами. Каждый узкий и записан как место
        // будущей печати — обойти зону стороной нельзя
        private void ConnectZones()
        {
            ShapeArenaAndShaft();
            ShapeForestToCityShaft();
            ShapeCityToRiftShaft();
            RecordTrenchPinch();
            ShapeHeartShaft();
            ShapeShipTerrace();
            ChooseLandmarkSite();
        }

        // Арена Королевского Краба на террасе зоны 2 и шахта под ней.
        // Именно этот пол потом обрушится после его смерти
        private void ShapeArenaAndShaft()
        {
            // Спуск начинается с котловины у подножия склона. Игрок идёт от берега
            // по шельфу, сваливается по уступам и упирается ровно в неё — маршрут
            // читается рельефом, а не подсказкой
            int gxCenter = _deepestFlatGx > 0
                ? Math.Clamp(_deepestFlatGx, ShellThickness + 50, _shoreGx - 50)
                : (int)(_shoreGx * 0.52f);
            _arenaShaftGx = gxCenter;
            // Полуширина 46 выравнивала 92 столбца при склоне в 96: арена вместе
            // с террасой галеона стирала лестницу уступов целиком, и спуск
            // превращался в один прямой съезд между двумя выглаженными площадками
            int half = 34;
            int from = Math.Max(ShellThickness + 4, gxCenter - half);
            int to = Math.Min(_shoreGx - 6, gxCenter + half);

            int sum = 0, count = 0;
            for (int gx = from; gx <= to; gx++, count++)
                sum += _seaFloorGy[gx];
            int arenaFloor = count > 0 ? sum / count : _seaFloorGy[gxCenter];

            for (int gx = from; gx <= to; gx++)
            {
                int target = arenaFloor + (int)(TideNoise.Signed(gx * 0.09f, ChDetail + 41, 2) * 2f);
                for (int gy = _gWater; gy < target; gy++)
                    CarveIfAllowed(gx, gy, TideGrid.Water);
                for (int gy = target; gy <= target + 16; gy++)
                    _grid.Set(gx, gy, gy < target + 5 ? TideGrid.Sand : TideGrid.Stone);
                _seaFloorGy[gx] = target;
            }

            int roofGy = DeepRoofGyAt(gxCenter);
            CarveTunnel(gxCenter, arenaFloor + 2, gxCenter, roofGy + 10,
                ShaftRadius - 1, ShaftRadius + 1, TideGrid.Water, ChCave + 3, 0.14f);

            // Печать садится ровно на границу зон, а не в середину шахты:
            // иначе она давит выше того места, где стоит
            AddSeal(1, gxCenter, _gPortBottom);
        }

        // Из леса в город. Провал города вверху широкий, но шахту всё равно режем:
        // она задаёт, ГДЕ игрок войдёт, а значит и где встанет печать
        private void ShapeForestToCityShaft()
        {
            int gxCenter = CityTopGx;
            int floorGy = DeepFloorGyAt(gxCenter);

            CarveTunnel(gxCenter, floorGy - 2, gxCenter, _gForestBottom + 12,
                ShaftRadius - 1, ShaftRadius + 1, TideGrid.Water, ChCave + 7, 0.14f);

            AddSeal(2, gxCenter, _gForestBottom);
        }

        // Из города в устье траншеи. Шахта садится ровно на ось траншеи —
        // иначе при неудачном шуме она упирается в породу и Разлом недостижим
        private void ShapeCityToRiftShaft()
        {
            int gxCenter = (int)TrenchAxisAt(_trenchTopGy + 8);

            CarveTunnel(gxCenter, _gCityBottom - 14, gxCenter, _trenchTopGy + 8,
                ShaftRadius - 1, ShaftRadius + 1, TideGrid.Water, ChCave + 17, 0.14f);

            AddSeal(3, gxCenter, _gCityBottom);
        }

        // Печать Разлома встаёт в пережим траншеи: там она уже узкая по построению,
        // и это ровно граница Разлома с Бездной
        private void RecordTrenchPinch()
            => AddSeal(4, (int)TrenchAxisAt(_gRiftBottom), _gRiftBottom);

        // Ход в Сердце Бездны. Печати здесь больше нет: пятая печать по концепции
        // не гейт, а развилка, и делается отдельно
        private void ShapeHeartShaft()
        {
            int heartTop = _heartGy - _heartHeight / 2;
            float axis = TrenchAxisAt(heartTop - 26);

            CarveTunnel(axis, heartTop - 26, _heartGx, heartTop + 2,
                ShaftRadius - 2, ShaftRadius, TideGrid.Water, ChCave + 11, 0.2f);
        }

        // Ровная терраса под галеон — на ней он и сядет
        // Галеон затонувший — он обязан лежать на дне под водой. Раньше место
        // считалось как доля ширины берега, и после появления пляжа эта точка
        // оказалась на суше: корабль вставал над водой
        private void ShapeShipTerrace()
        {
            int gxCenter = ChooseShipBasin();
            if (gxCenter < 0)
                return;   // подходящей глубины нет — пусть пасс корабля ищет сам

            _shipTerraceGx = gxCenter;
            // Галеон ложится на уступ склона и слегка его расширяет, а не срезает
            // весь склон под себя: ширина террасы соизмерима с полкой, а не со склоном
            int half = 20;
            int from = Math.Max(ShellThickness + 4, gxCenter - half);
            int to = Math.Min(_shoreGx - 6, gxCenter + half);

            int floorGy = _seaFloorGy[Math.Clamp(gxCenter, 0, _seaFloorGy.Length - 1)];
            for (int gx = from; gx <= to; gx++)
            {
                for (int gy = _gWater; gy < floorGy; gy++)
                    CarveIfAllowed(gx, gy, TideGrid.Water);
                for (int gy = floorGy; gy <= floorGy + 14; gy++)
                    _grid.Set(gx, gy, gy < floorGy + 5 ? TideGrid.Sand : TideGrid.Stone);
                _seaFloorGy[gx] = floorGy;
            }

            ShipAnchorX = _grid.ToWorldX(gxCenter);
            ShipAnchorFloorY = _grid.ToWorldY(floorGy);
        }

        // Самая глубокая ровная площадка, кроме арены Краба: корпусу нужно уйти
        // под воду целиком, а лечь он может только на полку, но не на сброс
        private int ChooseShipBasin()
        {
            const int MinHullDepth = 34;
            int best = -1, bestDepth = 0;

            foreach (int terraceGx in _terraceGx)
            {
                if (terraceGx < 40 || terraceGx >= _seaFloorGy.Length - 40)
                    continue;
                if (Math.Abs(terraceGx - _arenaShaftGx) < 80)
                    continue;

                int depth = _seaFloorGy[terraceGx] - _gWater;
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = terraceGx;
                }
            }

            return bestDepth >= MinHullDepth ? best : -1;
        }

        // Мембрана и замок ставятся последним проходом по готовому миру: до этого
        // в шахте ещё резался рельеф и сыпался декор, и печать бы затёрло
        private void RaiseSeals()
        {
            ushort barrierType = (ushort)ModContent.TileType<TideSealBarrier_tile>();
            ushort sealType = (ushort)ModContent.TileType<TideSeal_tile>();

            foreach (TideSealSite site in _sealSites)
            {
                for (int x = site.X; x < site.X + site.Width; x++)
                {
                    for (int y = site.Y; y < site.Y + site.Height; y++)
                    {
                        if (!WorldGen.InWorld(x, y, 12))
                            continue;

                        Tile tile = Main.tile[x, y];
                        // Порода остаётся собой: затыкаем только просвет прохода
                        if (tile.HasTile && Main.tileSolid[tile.TileType])
                            continue;

                        tile.ResetToType(barrierType);
                        tile.LiquidAmount = 0;
                    }
                }

                PlaceSealLock(site, sealType);
            }
        }

        private static void PlaceSealLock(TideSealSite site, ushort sealType)
        {
            int originX = site.X + site.Width / 2 - 1;
            int originY = site.Y + site.Height / 2 - 1;

            for (int dx = 0; dx < TideSeal_tile.SizeInTiles; dx++)
            {
                for (int dy = 0; dy < TideSeal_tile.SizeInTiles; dy++)
                {
                    int x = originX + dx;
                    int y = originY + dy;
                    if (!WorldGen.InWorld(x, y, 12))
                        continue;

                    Tile tile = Main.tile[x, y];
                    tile.ResetToType(sealType);
                    // Кадр многотайла проставляем руками: WorldGen.PlaceObject здесь
                    // не годится, печать висит в воде без опоры
                    tile.TileFrameX = (short)(dx * TideSeal_tile.FrameStep);
                    tile.TileFrameY = (short)(dy * TideSeal_tile.FrameStep);
                    tile.LiquidAmount = 0;
                }
            }
        }

        private void AddSeal(int step, int gxCenter, int gy)
        {
            int gx = gxCenter - SealRectWidth / 2;
            int gyTop = gy - SealRectHeight / 2;
            _sealRects.Add((gx, gyTop, SealRectWidth, SealRectHeight));
            RecordSeal(step, gx, gyTop, SealRectWidth, SealRectHeight);
        }

        // Гарантия, что зоны разделены породой везде, кроме записанных шахт.
        // Без неё резьба соседних зон сходится напрямую и печать обходится стороной
        private void EnforceZoneBarriers()
        {
            // Каждая граница зон запечатывается породой, и единственная дырка в ней —
            // прямоугольник печати. Иначе зону можно обойти стороной, и печать
            // превращается в декорацию
            int[] boundaries =
            {
                _gPortBottom,
                _gForestBottom,
                _gCityBottom,
                _gRiftBottom,
                _heartGy - _heartHeight / 2 - 3
            };

            foreach (int boundary in boundaries)
            {
                for (int gy = boundary - 2; gy <= boundary + 2; gy++)
                {
                    if (gy < 0 || gy >= _grid.Height)
                        continue;

                    for (int gx = 0; gx < _grid.Width; gx++)
                    {
                        if (_grid.Get(gx, gy) == TideGrid.Untouched || InsideSealRect(gx, gy))
                            continue;
                        _grid.Set(gx, gy, TideGrid.Stone);
                    }
                }
            }

            SealHeartShell();
        }

        private bool InsideSealRect(int gx, int gy)
        {
            foreach ((int rx, int ry, int rw, int rh) in _sealRects)
            {
                if (gx >= rx && gx < rx + rw && gy >= ry - 3 && gy < ry + rh + 3)
                    return true;
            }
            return false;
        }

        // Мягкий стык с сушей: ванильный песок за новой линией берега
        // перекрашивается в Tidesand, высоты не трогаем
        private void BlendInlandSand()
        {
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();
            int fromGx = _shoreGx;
            int toGx = Math.Min(_grid.Width - 1, _shoreGx + 60);

            for (int gx = fromGx; gx <= toGx; gx++)
            {
                int x = _grid.ToWorldX(gx);
                if (x < 12 || x >= Main.maxTilesX - 12)
                    continue;

                // Ближе к суше подмена всё реже. Сплошная замена всех 60 столбцов
                // оставляла прямой вертикальный шов ровно на дальней границе полосы
                float inland = (gx - fromGx) / (float)Math.Max(1, toGx - fromGx);
                float share = 1f - TideNoise.SmoothStep(0.4f, 1f, inland);

                int bottom = Math.Min(Main.maxTilesY - 20, _topY + _gPortBottom);
                for (int y = _topY; y <= bottom; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || !IsSandFamily(tile.TileType))
                        continue;

                    if (TideNoise.Fbm(x * 0.07f, y * 0.07f, ChDetail + 53, 3) < share)
                        tile.TileType = tidesandType;
                }
            }
        }

        // --- Стык с соседним биомом ---
        private const int InlandBlendReach = 22;
        private const float InlandBlendMaxShare = 0.85f;   // у кромки немного нашей породы остаётся
        private const int NeighbourRowJitter = 7;          // разброс по высоте, откуда берётся порода
        private const int OutwardBlendReach = 16;           // насколько наша порода выходит в чужую землю
        private const float OutwardBlendMaxShare = 0.7f;

        // Монолит обрывается ровно по линии _inlandEdge, и на карте это читается
        // прямым швом между двумя породами. Здесь внешняя полоса Tidestone пятнами
        // замещается той породой, что лежит сразу за следом биома, поэтому два камня
        // входят друг в друга зубцами. Меняем твёрдое на твёрдое — герметичность
        // оболочки не страдает
        private void BlendInlandRock()
        {
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();

            // Тип соседней породы снимается по всем строкам заранее. Раньше он брался
            // один на строку и применялся ко всей полосе: полоса раскладывалась
            // горизонтальными брусками в один тайл высотой и в двадцать длиной,
            // с ровными торцами по кромке. Именно это и было видно в игре
            int rows = Math.Max(1, _gAbyssBottom - _gWater + 1);
            var neighbourByRow = new ushort[rows];
            for (int i = 0; i < rows; i++)
            {
                int gy = _gWater + i;
                neighbourByRow[i] = SampleNeighbourRock(InlandEdgeAt(gy), _topY + gy);
            }

            for (int gy = _gWater; gy <= _gAbyssBottom; gy++)
            {
                int y = _topY + gy;
                if (y < 20 || y >= Main.maxTilesY - 20)
                    continue;

                int edge = InlandEdgeAt(gy);

                // Ширина полосы гуляет: при постоянной все торцы выстраивались по линейке
                int reachSpan = Math.Clamp(
                    InlandBlendReach + (int)(TideNoise.Signed(gy * 0.06f, ChDetail + 61, 2) * InlandBlendReach * 0.6f),
                    6, InlandBlendReach * 2);

                for (int gx = edge - reachSpan; gx <= edge; gx++)
                {
                    int x = _grid.ToWorldX(gx);
                    if (x < 12 || x >= Main.maxTilesX - 12)
                        continue;

                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || (tile.TileType != tidestoneType && tile.TileType != tidesandType))
                        continue;

                    // У самой кромки замещается почти всё, вглубь биома — всё реже.
                    // Полностью не замещаем даже на кромке: сплошная замена внешнего
                    // ряда снова давала прямую линию чужой породы вдоль всего следа
                    float depthShare = (gx - (edge - reachSpan)) / (float)reachSpan;
                    float share = TideNoise.SmoothStep(0f, 1f, depthShare) * InlandBlendMaxShare;
                    if (TideNoise.Fbm(x * 0.085f, y * 0.085f, ChDetail + 137, 3) >= share)
                        continue;

                    // Порода берётся с соседних высот, а не строго со своей строки
                    int jitter = (int)(TideNoise.Signed(x * 0.13f, ChDetail + 77, 2) * NeighbourRowJitter);
                    int row = Math.Clamp(gy - _gWater + jitter, 0, rows - 1);
                    tile.TileType = neighbourByRow[row];
                }

                // Зубцы наружу: наша порода языками уходит в ванильную землю. Без этого
                // взаимное проникновение односторонее, и кромка всё равно читается —
                // по одну сторону только наше, по другую только чужое
                ReachIntoNeighbourRock(edge, y, tidestoneType, tidesandType);
            }
        }

        // Меняем твёрдое на твёрдое и только природные породы: воздух не появляется,
        // герметичность не страдает, ванильные постройки не задеваются
        private void ReachIntoNeighbourRock(int edgeGx, int y, ushort tidestoneType, ushort tidesandType)
        {
            for (int step = 1; step <= OutwardBlendReach; step++)
            {
                int x = _grid.ToWorldX(edgeGx + step);
                if (x < 12 || x >= Main.maxTilesX - 12)
                    return;

                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || !IsNaturalRock(tile.TileType))
                    continue;

                float fade = 1f - step / (float)OutwardBlendReach;
                float share = fade * fade * OutwardBlendMaxShare;
                if (TideNoise.Fbm(x * 0.06f, y * 0.06f, ChDetail + 211, 3) >= share)
                    continue;

                tile.TileType = IsSandFamily(tile.TileType) ? tidesandType : tidestoneType;
            }
        }

        private static bool IsNaturalRock(ushort type) => type switch
        {
            TileID.Dirt or TileID.Stone or TileID.Mud or TileID.ClayBlock or TileID.Silt
                or TileID.Slush or TileID.SnowBlock or TileID.IceBlock => true,
            _ => IsSandFamily(type)
        };

        private static bool IsSandFamily(ushort type)
            => type is TileID.Sand or TileID.HardenedSand or TileID.Sandstone;

        // Что лежит сразу за следом биома на этой высоте: у поверхности земля,
        // глубже камень, со стороны джунглей грязь
        private ushort SampleNeighbourRock(int edgeGx, int y)
        {
            for (int offset = 6; offset <= 34; offset += 4)
            {
                int x = _grid.ToWorldX(edgeGx + offset);
                if (x < 12 || x >= Main.maxTilesX - 12)
                    break;

                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    return StableRock(tile.TileType);
            }
            return TileID.Stone;
        }

        // Сыпучие породы в оболочку не пускаем: заменив ими стенку, мы бы уронили
        // её при первом касании и вскрыли биом. Но и подменять песок КАМНЕМ нельзя —
        // именно от этого в песчаном берегу появлялись серые плиты. Берём затвердевший
        // песок: он держится сам и выглядит как песок
        private static ushort StableRock(ushort type) => type switch
        {
            TileID.Sand => TileID.HardenedSand,
            TileID.Ebonsand => TileID.CorruptHardenedSand,
            TileID.Crimsand => TileID.CrimsonHardenedSand,
            TileID.Pearlsand => TileID.HallowHardenedSand,
            TileID.Silt or TileID.Slush => TileID.Stone,
            _ => type
        };

        // Декор дна. Ракушки работают навигацией: их заметно больше на маршрутах
        // спуска и в тёмных зонах, где ориентироваться больше не по чему
        private void DecorateSeabed()
        {
            int coralType = ModContent.TileType<ShadowCoral_tile>();
            int glowShellType = ModContent.TileType<GlowShell_tile>();
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();

            int bottom = Math.Min(Main.maxTilesY - 24, _abyssBottomY + 4);

            for (int gx = ShellThickness; gx < _grid.Width - ShellThickness; gx++)
            {
                int x = _grid.ToWorldX(gx);
                if (x < 14 || x >= Main.maxTilesX - 14)
                    continue;

                for (int y = _waterTopY; y < bottom; y++)
                {
                    Tile floor = Main.tile[x, y];
                    if (!floor.HasTile || (floor.TileType != tidesandType && floor.TileType != tidestoneType))
                        continue;

                    Tile above = Main.tile[x, y - 1];
                    if (above.HasTile || above.LiquidAmount < 200)
                        continue;

                    // Чем глубже, тем меньше кораллов и тем важнее светящиеся ракушки
                    float depth = TideNoise.Clamp01((y - _waterTopY) / (float)Math.Max(1, _depthBudget));
                    float coralChance = 0.11f * (1f - depth);
                    float shellChance = 0.04f + 0.10f * depth;

                    // Устье спуска подсвечено гроздьями ракушек: с верхних ступеней
                    // видно светящееся пятно внизу — цель, к которой хочется плыть
                    int toShaft = Math.Abs(gx - _arenaShaftGx);
                    if (toShaft < 34)
                        shellChance += 0.34f * (1f - toShaft / 34f);

                    float roll = WorldGen.genRand.NextFloat();
                    if (roll < coralChance)
                        WorldGen.PlaceObject(x, y - 1, coralType, true, WorldGen.genRand.Next(3));
                    else if (roll < coralChance + shellChance)
                        WorldGen.PlaceObject(x, y - 1, glowShellType, true, WorldGen.genRand.Next(3));

                    y += 6;   // не облепляем каждый выступ подряд
                }
            }
        }

        // --- Приливная флора ---
        private const int PortMaxHeight = 30;      // порт: мелководье, стебель не торчит из воды
        private const int ForestMaxHeight = 110;   // чёрные леса: стебель во всю высоту зала
        private const int KelpSurfaceMargin = 6;   // верхушка не торчит из воды
        private const int SeabedSearchMargin = 3;  // ближе этого к своду дно не ищем

        // Смесь видов по зонам. В порту мелко и светло — там правит папоротник,
        // в Чёрных лесах несущий вид ель; рыжий куст везде идёт мелким акцентом
        // у дна. Доля вида задана числом вхождений, а не отдельными весами:
        // так смесь читается с одного взгляда
        private static readonly int[] PortSpeciesMix =
        {
            Tidekelp_tile.SpeciesFern, Tidekelp_tile.SpeciesFern, Tidekelp_tile.SpeciesFern,
            Tidekelp_tile.SpeciesBush, Tidekelp_tile.SpeciesSpruce,
        };

        private static readonly int[] ForestSpeciesMix =
        {
            Tidekelp_tile.SpeciesSpruce, Tidekelp_tile.SpeciesSpruce, Tidekelp_tile.SpeciesSpruce,
            Tidekelp_tile.SpeciesSpruce, Tidekelp_tile.SpeciesFern, Tidekelp_tile.SpeciesBush,
        };

        // Роща держит свой вид, но каждый третий примерно стебель берётся из общей
        // смеси: между елями пробивается подлесок, и роща не выглядит посадкой
        private static int PickSpecies(int[] mix, int groveSpecies)
        {
            if (WorldGen.genRand.NextBool(3))
                return mix[WorldGen.genRand.Next(mix.Length)];
            return groveSpecies;
        }

        // Рощи в толще воды. Дно и так обжито кораллом и ракушками, а между дном
        // и зеркалом воды не было ничего: спуск читался как падение сквозь пустоту.
        // Ламинария даёт толще объём, укрытия и границу видимости
        private void PlantKelpForests()
        {
            ushort kelpType = (ushort)ModContent.TileType<Tidekelp_tile>();
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();

            int gx = (int)(_shoreGx * BandCliff) + 6;
            int limit = _shoreGx - BeachWidth + 8;
            int strands = 0;

            while (gx < limit)
            {
                int width = WorldGen.genRand.Next(14, 47);
                // Высота задаётся на рощу целиком: ровесники смотрятся зарослями,
                // а вразнобой — случайными палками. Вид со своим потолком высоты
                // подрежет её сам, поэтому куст останется кустом
                int groveHeight = WorldGen.genRand.Next(12, PortMaxHeight + 1);
                int groveSpecies = PortSpeciesMix[WorldGen.genRand.Next(PortSpeciesMix.Length)];

                for (int offset = 0; offset < width && gx + offset < limit; offset++)
                {
                    if (!WorldGen.genRand.NextBool(2))   // роща не сплошная стена
                        continue;

                    int height = groveHeight + WorldGen.genRand.Next(-4, 5);
                    int species = PickSpecies(PortSpeciesMix, groveSpecies);
                    if (PlantFloraStrand(gx + offset, height, species, PortMaxHeight,
                            _waterTopY + KelpSurfaceMargin, _topY + _gPortBottom,
                            kelpType, tidesandType, tidestoneType))
                        strands++;
                }

                gx += width + WorldGen.genRand.Next(26, 121);
            }

            Log($"ламинария порта: {strands} стеблей");
        }

        // Зона II — Чёрные леса. То же растение, но во всю высоту зала: стебли
        // тянутся от пола до свода, и лес читается как лес, а не как трава на дне.
        // Заросли гуще портовых и почти без разрывов: сквозь них надо продираться,
        // это и есть обещанный лабиринт растительности
        private void PlantBlackForest()
        {
            ushort kelpType = (ushort)ModContent.TileType<Tidekelp_tile>();
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();

            int limit = InlandEdgeAt(_gForestBottom) - ShellThickness;
            int gx = ShellThickness + 6;
            int strands = 0;

            while (gx < limit)
            {
                int width = WorldGen.genRand.Next(26, 71);
                int groveSpecies = ForestSpeciesMix[WorldGen.genRand.Next(ForestSpeciesMix.Length)];
                // Высота роще задаётся долей от местного просвета, а не в тайлах:
                // зал пережимается, и постоянная высота дала бы то лес, то щетину
                float groveFill = WorldGen.genRand.NextFloat(0.55f, 0.95f);

                for (int offset = 0; offset < width && gx + offset < limit; offset++)
                {
                    int column = gx + offset;
                    if (WorldGen.genRand.NextBool(5))   // редкие прогалины
                        continue;

                    int roofGy = DeepRoofGyAt(column);
                    int floorGy = DeepFloorGyAt(column);
                    int span = floorGy - roofGy;
                    if (span < Tidekelp_tile.MinHeightOf(Tidekelp_tile.SpeciesSpruce) + 6)
                        continue;

                    int height = (int)(span * groveFill) + WorldGen.genRand.Next(-6, 7);
                    int species = PickSpecies(ForestSpeciesMix, groveSpecies);
                    if (PlantFloraStrand(column, height, species, ForestMaxHeight,
                            _topY + roofGy + 2, _topY + floorGy + 6,
                            kelpType, tidesandType, tidestoneType))
                        strands++;
                }

                gx += width + WorldGen.genRand.Next(8, 40);
            }

            Log($"чёрный лес: {strands} стеблей");
        }

        // ceilingY — выше этой строки стебель не растёт (зеркало воды или свод зала),
        // searchBottomY — докуда искать дно в столбце
        private bool PlantFloraStrand(int gx, int targetHeight, int species, int zoneMaxHeight,
            int ceilingY, int searchBottomY, ushort kelpType,
            ushort tidesandType, ushort tidestoneType)
        {
            int x = _grid.ToWorldX(gx);
            if (x < 14 || x >= Main.maxTilesX - 14)
                return false;

            int floorY = FindSeabedY(x, ceilingY, searchBottomY, tidesandType, tidestoneType);
            if (floorY == -1)
                return false;

            // Потолок высоты берётся самый низкий из трёх: заказ рощи, предел зоны
            // и предел вида — рыжий куст не вытянется елью даже в высоком зале
            int minHeight = Tidekelp_tile.MinHeightOf(species);
            int maxHeight = Math.Min(zoneMaxHeight, Tidekelp_tile.MaxHeightOf(species));
            int available = floorY - ceilingY;
            int height = Math.Min(Math.Min(targetHeight, maxHeight), available);
            if (height < minHeight)
                return false;

            // Стебель обрезается на первом же занятом или обмелевшем тайле:
            // упереться в свод или в нарост он должен, а прорастать сквозь — нет
            for (int k = 0; k < height; k++)
            {
                Tile cell = Main.tile[x, floorY - 1 - k];
                if (cell.HasTile || cell.LiquidAmount < 200)
                {
                    height = k;
                    break;
                }
            }
            if (height < minHeight)
                return false;

            // Сторона первой лапы случайна, дальше её ведёт сам VariantAt: иначе
            // все стебли рощи начинали бы ветвиться в одну сторону
            bool branchedLeft = WorldGen.genRand.NextBool();
            for (int k = 0; k < height; k++)
            {
                int y = floorY - 1 - k;
                int variant = Tidekelp_tile.VariantAt(k, height, species,
                    WorldGen.genRand, ref branchedLeft);

                Tile tile = Main.tile[x, y];
                tile.HasTile = true;
                tile.TileType = kelpType;
                tile.TileFrameX = (short)(variant * Tidekelp_tile.FrameStepX);
                tile.TileFrameY = (short)(species * Tidekelp_tile.FrameStepY);
                tile.Slope = SlopeType.Solid;
                tile.IsHalfBlock = false;
            }

            return true;
        }

        // Первая сверху поверхность дна в столбце: тайл биома, над которым открытая вода
        private int FindSeabedY(int x, int fromY, int searchBottomY, ushort tidesandType, ushort tidestoneType)
        {
            int bottom = Math.Min(Main.maxTilesY - 24, searchBottomY);
            for (int y = fromY + SeabedSearchMargin; y < bottom; y++)
            {
                Tile floor = Main.tile[x, y];
                if (!floor.HasTile || (floor.TileType != tidesandType && floor.TileType != tidestoneType))
                    continue;

                Tile above = Main.tile[x, y - 1];
                return above.HasTile || above.LiquidAmount < 200 ? -1 : y;
            }
            return -1;
        }
    }
}
