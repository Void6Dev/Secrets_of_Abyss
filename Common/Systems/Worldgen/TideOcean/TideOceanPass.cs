using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using SoA.Common.Utils;
using SoA.Content.Tiles.Nature;
using SoA.Content.Worldgen;

namespace SoA.Common.Systems.TideOcean
{
    // Прилив Теней. Биом не правит ванильный океан, а заменяет его: сначала на след
    // биома штампуется сплошной массив Tidestone, затем из него вырезается всё
    // остальное. Только так получается герметичная чаша на сотни тайлов глубиной,
    // нависающие своды и залы под сушей — на «правках дна» это недостижимо.
    //
    // Вертикальная раскладка (доли бюджета глубины D от зеркала воды).
    // Пять зон — это пять глав одной истории, а не пять биомов: см. Docs/BiomeConcept.md
    //   0.00–0.18  I   Затопленный порт   открытая вода, шельф, уступы, котловина
    //   0.18–0.40  II  Чёрные леса        затопленный зал, заросший ламинарией
    //   0.40–0.60  III Погружённый город  вертикальный провал с ярусами
    //   0.60–0.80  IV  Разлом             узкая траншея с трещинами
    //   0.80–1.00  V   Бездна             пустота, каскад камер
    //   дно        Сердце Бездны          запечатанная камера
    //
    // Печать стоит РОВНО на границе зон, а не там, где удобно шахте: это гарантирует,
    // что печать N открывает зону N+1 и не может давить выше собственного места
    public partial class TideOceanPass : GenPass
    {
        // --- След биома ---
        private const int SurfaceHeadroom = 64;   // небо над зеркалом воды под скалы и утёс
        private const int ShellThickness = 10;    // герметичная оболочка по краю следа
        private const int InlandRoofMargin = 26;  // порода между ванильной поверхностью и сводом
        private const float SurfaceWidthScale = 1.55f;
        private const float DeepWidthScale = 2.30f;

        // --- Бюджет глубины ---
        private const float DepthScale = 2.3f;    // множитель к (rockLayer - waterTop)
        private const int MinDepthBudget = 200;
        private const int UnderworldMargin = 260; // столько тайлов оставляем над адом

        // --- Границы зон в долях бюджета ---
        // Открытой воде отдана пятая часть бюджета, а не треть: 295 тайлов глубины на
        // 263 столбца разбега делали море глубже, чем шире, и спуск в нём при любой
        // раскладке выходил стеной. Освободившееся отдано затопленным пещерам —
        // там на тайл глубины приходится куда больше содержания, чем в толще воды
        // Прежние зоны 1 и 2 (шельф и котловина) слиты в одну: это одна и та же
        // открытая вода, и делить её границей было нечем — печати там не стояло
        private const float ZonePortBottom = 0.18f;       // дно котловины, ниже начинаются пещеры
        private const float ZoneForestBottom = 0.40f;     // дно зала с ламинарией
        private const float ZoneCityBottom = 0.60f;       // дно провала с городом
        private const float ZoneRiftBottom = 0.80f;       // дно траншеи, ниже только Бездна

        // --- Открытое море: шельф, перегиб, лестница уступов, котловина ---
        private const float BandCliff = 0.16f;            // доля ширины под великий утёс
        private const int BeachWidth = 104;               // сухой берег от уреза воды вглубь суши
        // Высота задана в тайлах, а не долей бюджета: доля растёт вместе с глубиной
        // бездны, и на большом мире пляж поднимался на 59 тайлов — то есть плато
        private const int BeachCrestHeight = 22;          // дюны над зеркалом воды

        private const float ShelfRun = 0.44f;             // доля разбега моря под шельф
        private const float BasinRun = 0.18f;             // доля разбега моря под котловину
        private const int ShelfBreakDepth = 35;           // глубина у внешней кромки шельфа
        private const int ShelfBarHeight = 9;             // насколько песчаная гряда поднимает дно
        private const int ShelfMaxSlope = 2;              // предел падения дна на столбец в пределах шельфа
        // Четыре уступа, а не пять: на пять полка выходит в десяток тайлов шириной,
        // то есть карниз, а не площадка, на которой можно стоять и драться
        private const int SlopeBenches = 4;               // полок на склоне между кромкой и котловиной
        private const float BenchTreadShare = 0.62f;      // какая доля склона уходит под горизонтальные полки

        // Кекуры: высота над водой связана с шириной, а основание — с местной глубиной.
        // Прежние надводные скалы задавали гребень абсолютно (_gWater минус случайные
        // 14-46) и росли с любого дна, поэтому над ямой в двести тайлов вырастала игла
        private const int StackMaxDepth = 30;             // глубже кекур не ставим
        private const float StackHeightToWidth = 0.45f;   // предел надводной части к ширине

        private const int CliffHeight = 52;          // высота великого утёса над водой
        private const float CliffTalusReach = 2.6f;  // на сколько ширин полосы растянут осыпной склон
        private const float CliffTerraces = 4f;      // уступов на склоне: не гладкая наклонная
        private const int DeepRoofThickness = 24;    // порода между дном моря и сводом глубин
        private const int MinDeepCavernHeight = 44;  // высота горла: уже неё зал не пережимается

        // Пережимы зала глубин. Длина волны берётся такой, чтобы на всю ширину
        // легло 2-3 горла: зал во всю ширину читался одной коробкой
        private const float CavernPinchFrequency = 0.0026f;
        private const float CavernPinchThreshold = 0.82f;  // выше порога гребня начинается горло
        private const float CavernPinchDepth = 0.82f;      // какую долю высоты забирает горло
        private const int TrenchStartOffset = 74;    // устье траншеи: дальше от края мира, чем оболочка
        private const int TempleInnerMargin = 6;     // тайлы Храма, которые монолит не трогает
        private const int TempleOuterMargin = 34;    // толщина скалы вокруг Храма, куда не заходит резьба

        // Каналы шума. Разные числа гарантируют, что формы не коррелируют между собой
        private const int ChFloor = 101;
        private const int ChCliff = 211;
        private const int ChRoof = 307;
        private const int ChColumn = 419;
        private const int ChCanyon = 523;
        private const int ChTrench = 631;
        private const int ChCave = 739;
        private const int ChDetail = 853;

        private TideGrid _grid;
        private int _edgeX, _dir;
        private int _topY, _waterTopY, _abyssBottomY;
        private int _depthBudget;
        private int _shoreGx, _surfaceWidth, _deepWidth;
        private int _gWater;
        private int _gPortBottom, _gForestBottom, _gCityBottom, _gRiftBottom, _gAbyssBottom;
        private int[] _vanillaSurfaceGy;
        private int[] _seaFloorGy;
        private int[] _rockTopGy;   // верх породы с учётом осыпи утёса: на неё садятся скалы
        private int[] _inlandEdge;
        private bool _hasTemple;
        private Rectangle _templeInner, _templeOuter;
        private readonly List<int> _crestGx = new();     // кромки: перегиб шельфа и верх каждого сброса
        private readonly List<int> _terraceGx = new();   // ровные площадки: шельф, полки склона, котловина
        private int _deepestFlatGx;                      // середина котловины — туда садится арена
        private readonly List<TideSealSite> _sealSites = new();

        public TideOceanPass(string name, float loadWeight) : base(name, loadWeight)
        {
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "Shadow tides claim the coast";
            ResetShipAnchor();   // статик переживает смену мира, а якорь у каждого свой
            TideNoise.Reseed(WorldGen.genRand.Next(1, int.MaxValue));

            Log($"пасс запущен: мир {Main.maxTilesX}x{Main.maxTilesY}, worldSurface={(int)Main.worldSurface}, "
                + $"rockLayer={(int)Main.rockLayer}, dungeonSide={GenVars.dungeonSide}, "
                + $"leftBeachEnd={GenVars.leftBeachEnd}, rightBeachStart={GenVars.rightBeachStart}");

            if (!ComputeBounds())
            {
                Log("ОБМЕРЫ НЕ СНЯТЫ — биом не построен");
                return;
            }

            Log($"обмеры: edgeX={_edgeX} dir={_dir} waterTop={_waterTopY} depthBudget={_depthBudget} "
                + $"surfaceWidth={_surfaceWidth} deepWidth={_deepWidth} grid={_grid.Width}x{_grid.Height}; "
                + $"масштаб мира {WorldScale:0.00}, доля карты {_deepWidth * 100 / Main.maxTilesX}% по ширине "
                + $"и {_depthBudget * 100 / Main.maxTilesY}% по высоте");
            Log(_hasTemple
                ? $"Храм Джунглей обойдён коробкой {_templeOuter.Width}x{_templeOuter.Height} на сдвиге {_templeOuter.X}"
                : "Храм Джунглей в след биома не попал");

            StampMonolith();
            progress.Value = 0.12f;

            CarveOpenSea();
            HangCoastalGrowths();
            progress.Value = 0.30f;

            CarveDeepCavern();
            CarveSunkenCity();
            progress.Value = 0.46f;

            CarveAbyss();
            CarveHeartChamber();
            progress.Value = 0.60f;

            ConnectZones();
            EnforceZoneBarriers();
            progress.Value = 0.72f;

            _grid.Smooth(2, ShellThickness - 2, 2);
            _grid.Erode(ShellThickness - 2, 2);
            progress.Value = 0.82f;

            _grid.SealRim(InlandEdgeAt, ShellThickness, _gWater);
            ExportPreview();
            progress.Value = 0.90f;

            _grid.Blit(
                (ushort)ModContent.TileType<Tidestone_tile>(),
                (ushort)ModContent.TileType<Tidesand_tile>(),
                DeepWallType,
                DeepSandWallType,
                _gPortBottom);

            BlendInlandSand();
            BlendInlandRock();
            DecorateSeabed();
            PlantKelpForests();
            PlantBlackForest();
            RaiseSeals();
            PublishWorldData();

            Log($"биом построен: зоны по Y = {string.Join(", ", TideOfShadowsWorldData.ZoneBottomY)}, "
                + $"вылеты = {string.Join(", ", TideOfShadowsWorldData.ZoneWidth)}, печатей = {_sealSites.Count}");
            Log($"чёрные леса: высота зала {_cavernMinHeight}..{_cavernMaxHeight}; "
                + $"город {(_cityCarved ? "вырезан" : "НЕ ВЛЕЗ")} {_topY + _gForestBottom}..{_topY + _gCityBottom}; "
                + $"траншея {_trenchTopGy + _topY}..{_topY + _gAbyssBottom}, "
                + $"Сердце {_grid.ToWorldX(_heartGx)},{_topY + _heartGy} ({_heartWidth}x{_heartHeight})");

            int minX = Math.Min(_edgeX, _edgeX + _deepWidth * _dir);
            int maxX = Math.Max(_edgeX, _edgeX + _deepWidth * _dir);
            WorldGen.RangeFrame(Math.Max(12, minX), Math.Max(20, _topY),
                Math.Min(Main.maxTilesX - 12, maxX), Math.Min(Main.maxTilesY - 20, _abyssBottomY + ShellThickness));
            progress.Value = 1f;
        }

        // Ванильный океан имеет примерно одну и ту же абсолютную ширину на любом
        // размере мира, а расстояние от воды до слоя камня на маленьком мире сжато
        // непропорционально. Без этого множителя биом занимал 55% высоты и 18%
        // ширины маленького мира против 40% и 9% большого — то есть был вдвое
        // крупнее по доле карты именно там, где места меньше всего
        private static float WorldScale
            => Math.Clamp(0.34f + 0.66f * (Main.maxTilesX / 8400f), 0.6f, 1.15f);

        // Замеры мира и раскладка зон. Всё, что дальше, считается от этих чисел
        private bool ComputeBounds()
        {
            int jungleDir = -GenVars.dungeonSide;   // океан со стороны джунглей
            TideOfShadowsWorldData.OceanSide = jungleDir;

            int oceanWidth;
            if (jungleDir == -1)
            {
                _edgeX = 40;
                _dir = 1;
                oceanWidth = Math.Max(160, GenVars.leftBeachEnd - _edgeX);
            }
            else
            {
                _edgeX = Main.maxTilesX - 40;
                _dir = -1;
                oceanWidth = Math.Max(160, _edgeX - GenVars.rightBeachStart);
            }

            _waterTopY = FindOceanWaterTop();
            if (_waterTopY == -1)
            {
                Log($"зеркало воды не найдено у edgeX={_edgeX}, dir={_dir} — океана на этой стороне нет");
                return false;
            }

            float scale = WorldScale;

            // Масштаб мира применяется к глубине и подкопу под сушу, но НЕ к надводной
            // части: именно она даёт пляж и открытое море, и сжимать её нельзя —
            // на узком отрезке каскад бухт не успевает прочитаться
            _surfaceWidth = Math.Max(oceanWidth + BeachWidth, (int)(oceanWidth * SurfaceWidthScale));
            _deepWidth = Math.Max(_surfaceWidth + 60, (int)(oceanWidth * DeepWidthScale * scale));
            _shoreGx = _surfaceWidth;

            _depthBudget = Math.Max(MinDepthBudget, (int)((Main.rockLayer - _waterTopY) * DepthScale * scale));
            _abyssBottomY = _waterTopY + _depthBudget;

            int deepestAllowed = Main.maxTilesY - UnderworldMargin;
            if (_abyssBottomY > deepestAllowed)
            {
                _abyssBottomY = deepestAllowed;
                _depthBudget = _abyssBottomY - _waterTopY;
            }
            if (_depthBudget < MinDepthBudget)
            {
                Log($"бюджет глубины {_depthBudget} меньше минимума {MinDepthBudget} — строить нечего");
                return false;
            }

            _topY = Math.Max(30, _waterTopY - SurfaceHeadroom);
            _gWater = _waterTopY - _topY;
            _gPortBottom = _gWater + (int)(_depthBudget * ZonePortBottom);
            _gForestBottom = _gWater + (int)(_depthBudget * ZoneForestBottom);
            _gCityBottom = _gWater + (int)(_depthBudget * ZoneCityBottom);
            _gRiftBottom = _gWater + (int)(_depthBudget * ZoneRiftBottom);
            // Устье траншеи считается здесь, а не в CarveAbyss: ось колодца города
            // строится на него, а город режется раньше
            _trenchTopGy = _gCityBottom + 16;
            _gAbyssBottom = _gWater + _depthBudget;

            int width = _deepWidth + ShellThickness;
            int height = _gAbyssBottom + ShellThickness + 6;
            _grid = new TideGrid(width, height, _edgeX, _topY, _dir);

            CacheInlandEdge();
            CacheVanillaSurface();
            FindTempleBox();
            return true;
        }

        // Ванильный рельеф запоминаем до того, как монолит его затрёт:
        // по нему считается глубина свода под сушей
        private void CacheVanillaSurface()
        {
            _vanillaSurfaceGy = new int[_grid.Width];
            for (int gx = 0; gx < _grid.Width; gx++)
            {
                int x = _grid.ToWorldX(gx);
                _vanillaSurfaceGy[gx] = x < 12 || x >= Main.maxTilesX - 12 ? -1 : FindSurface(x) - _topY;
            }
        }

        // Сплошной массив Tidestone по всему следу биома.
        // Над сушей кровля уходит вниз, чтобы поверхность и джунгли остались нетронутыми
        private void StampMonolith()
        {
            for (int gx = 0; gx < _grid.Width; gx++)
            {
                int topGy = MonolithTopGy(gx);
                if (topGy < 0)
                    continue;

                int bottomGy = Math.Min(_grid.Height - 1, _gAbyssBottom + ShellThickness);
                if (topGy > bottomGy)
                    continue;

                // След биома — не прямоугольник: стенка чаши завалена вглубь суши,
                // поэтому колонка начинается там, где след впервые доходит до gx
                for (int gy = topGy; gy <= bottomGy; gy++)
                {
                    if (_inlandEdge[gy] < gx || InsideTempleInner(gx, gy))
                        continue;
                    _grid.Set(gx, gy, TideGrid.Stone);
                }
            }
        }

        // Кровля монолита: в море — от самого неба, под сушей — ниже ванильной земли
        private int MonolithTopGy(int gx)
        {
            if (gx <= _shoreGx)
                return 0;

            int surfaceGy = _vanillaSurfaceGy[gx];
            if (surfaceGy < 0)
                return -1;

            // Свод не поднимается выше уровня, на котором начинается глубокий океан
            int roof = Math.Max(surfaceGy + InlandRoofMargin, _gWater + 8);
            return Math.Min(roof, _gPortBottom);
        }

        // Внутренняя (со стороны суши) граница следа биома на данной глубине.
        // Стенка чаши наклонена: чем глубже, тем дальше зал уходит под сушу.
        // Считается один раз в таблицу — её дёргает каждая проверка CanCarve
        private void CacheInlandEdge()
        {
            _inlandEdge = new int[_grid.Height];
            for (int gy = 0; gy < _grid.Height; gy++)
            {
                if (gy <= _gWater)
                {
                    _inlandEdge[gy] = _shoreGx;
                    continue;
                }

                float t = TideNoise.Clamp01((gy - _gWater) / (float)_depthBudget);
                float lean = TideNoise.SmoothStep(0.15f, 0.78f, t);
                float baseWidth = TideNoise.Lerp(_shoreGx, _deepWidth, lean);
                float wobble = TideNoise.Signed(gy * 0.031f, ChRoof, 3) * 22f;

                // Крупная волна в 22 тайла размазана на две сотни строк, и с экрана
                // стенка всё равно читалась отвесом. Мелкие октавы ломают её на масштабе,
                // который видно вблизи: зубцы в 6-8 тайлов и дрожь в 2-3
                float jitter = TideNoise.Signed(gy * 0.17f, ChRoof + 7, 2) * 7f
                             + TideNoise.Signed(gy * 0.43f, ChRoof + 19, 2) * 3f;

                _inlandEdge[gy] = Math.Clamp((int)(baseWidth + wobble + jitter), _shoreGx, _grid.Width - 1);
            }
        }

        private int InlandEdgeAt(int gy)
            => gy < 0 ? _shoreGx : (gy >= _inlandEdge.Length ? _inlandEdge[^1] : _inlandEdge[gy]);

        // Открытое море устроено как настоящая подводная окраина: широкий пологий
        // шельф, резкий перегиб на его кромке, лестница уступов вниз и ровная
        // котловина у подножия. Прежняя цепочка бухт и порогов не годилась в
        // принципе — она размазывала перепад по всей ширине моря, а перепад тут
        // сопоставим с шириной, и любая раскладка выходила либо стеной, либо
        // одинаковым съездом под полсотни градусов от самого берега
        private void BuildSeaProfile()
        {
            _seaFloorGy = new int[_grid.Width];
            _crestGx.Clear();
            _terraceGx.Clear();

            int seaStart = (int)(_shoreGx * BandCliff) + 6;   // подножие великого утёса
            int seaEnd = _shoreGx - BeachWidth;               // урез воды
            int run = Math.Max(60, seaEnd - seaStart);
            int shelfBreakGx = seaEnd - (int)(run * ShelfRun);
            int seaDepth = Math.Max(60, _gPortBottom - _gWater);

            var nodes = BuildShelfNodes(seaStart, seaEnd, run, seaDepth);

            for (int gx = 0; gx <= _shoreGx && gx < _grid.Width; gx++)
            {
                float depth = SampleProfile(nodes, gx);

                float warp = TideNoise.Signed(gx * 0.014f, ChFloor + 3, 3) * 8f;
                float relief = TideNoise.Signed((gx + warp) * 0.031f, ChFloor, 4) * 7f
                             + TideNoise.Signed(gx * 0.082f, ChFloor + 11, 3) * 3f
                             + TideNoise.Signed(gx * 0.21f, ChFloor + 23, 2) * 1.3f;

                // На мелководье рельеф приглушён: полный размах в одиннадцать тайлов
                // соизмерим с глубиной самого шельфа, и полка от него читается рваной
                relief *= TideNoise.Lerp(0.3f, 1f, TideNoise.Clamp01(depth / seaDepth));

                // Песчаные гряды поперёк шельфа. Где гряда высокая, а вода мелкая,
                // она выходит к зеркалу воды отмелью, а между грядами остаётся лагуна
                if (gx > shelfBreakGx && gx <= seaEnd)
                {
                    float bar = TideNoise.Ridged(gx * 0.021f, ChDetail + 41, 2);
                    relief -= ShelfBarHeight * TideNoise.Clamp01((bar - 0.55f) / 0.45f);
                }

                // Дюнные гряды на сухом берегу: две частоты, чтобы читались и крупные
                // валы, и мелкая рябь между ними
                if (gx > seaEnd)
                    relief -= TideNoise.Fbm(gx * 0.028f, ChDetail + 5, 3) * 9f
                            + TideNoise.Fbm(gx * 0.105f, ChDetail + 9, 2) * 3.5f;

                float floor = _gWater + depth + relief;
                _seaFloorGy[gx] = (int)TideNoise.SoftMin(floor, _gPortBottom - 8, 16f);
            }

            // Гладкость нужна только шельфу: лестница уступов ниже кромки обязана
            // оставаться лестницей, и общий предел уклона срезал бы ей сбросы
            LimitSeaFloorSlope(shelfBreakGx, seaEnd, ShelfMaxSlope);
        }

        // Жёсткий предел уклона дна на отрезке: два встречных прохода срезают всё,
        // что круче limit. Оба прохода только поднимают дно, поэтому узкие провалы
        // затягиваются, а общая форма остаётся. Без него отдельный выброс шума
        // давал на полке ступеньку в полтора десятка тайлов на один столбец
        private void LimitSeaFloorSlope(int fromGx, int toGx, int limit)
        {
            int first = Math.Max(0, fromGx);
            int last = Math.Min(toGx, _seaFloorGy.Length - 1);

            for (int gx = first + 1; gx <= last; gx++)
                _seaFloorGy[gx] = Math.Min(_seaFloorGy[gx], _seaFloorGy[gx - 1] + limit);

            for (int gx = last - 1; gx >= first; gx--)
                _seaFloorGy[gx] = Math.Min(_seaFloorGy[gx], _seaFloorGy[gx + 1] + limit);
        }

        // Узлы профиля от берега к краю мира: пляж, шельф, лестница уступов, котловина
        private List<ProfileNode> BuildShelfNodes(int seaStart, int seaEnd, int run, int seaDepth)
        {
            int shelfBreakGx = seaEnd - (int)(run * ShelfRun);
            int basinTopGx = seaStart + (int)(run * BasinRun);

            // Пляж — не полка на одной высоте, а уклон от дюн к урезу воды.
            // Показатель 1.5 делает его вогнутым: у воды почти плоско, вглубь суши круче.
            // Шельф вогнут так же: у берега почти горизонтально, к кромке чуть круче
            var nodes = new List<ProfileNode>
            {
                new() { Gx = _shoreGx, Depth = -BeachCrestHeight, Exponent = 1f },
                new() { Gx = seaEnd, Depth = -1f, Exponent = 1.5f },
                new() { Gx = shelfBreakGx, Depth = ShelfBreakDepth, Exponent = 1.8f }
            };

            _terraceGx.Add((shelfBreakGx + seaEnd) / 2);   // середина шельфа — ровное мелководье

            // Лестница уступов. Полка почти горизонтальна, сброс между полками короток:
            // отвесный сброс на три десятка тайлов между двумя полками читается обрывом,
            // а такой же отвес во весь склон читается ошибкой генератора
            int slopeRun = Math.Max(SlopeBenches * 16, shelfBreakGx - basinTopGx);
            int treadTotal = (int)(slopeRun * BenchTreadShare);
            int riserTotal = slopeRun - treadTotal;

            var weights = new float[SlopeBenches];
            float weightSum = 0f;
            for (int i = 0; i < SlopeBenches; i++)
            {
                weights[i] = WorldGen.genRand.NextFloat(0.7f, 1.35f);
                weightSum += weights[i];
            }

            int cursorGx = shelfBreakGx;
            float cursorDepth = ShelfBreakDepth;
            float dropLeft = seaDepth - ShelfBreakDepth;

            for (int i = 0; i < SlopeBenches; i++)
            {
                float part = weights[i] / weightSum;
                _crestGx.Add(cursorGx);   // кромка уступа: под ней нависает карниз

                int riser = Math.Max(5, (int)(riserTotal * part));
                cursorGx = Math.Max(basinTopGx, cursorGx - riser);
                cursorDepth += dropLeft * part;
                nodes.Add(new ProfileNode { Gx = cursorGx, Depth = cursorDepth, Exponent = 1f });

                int tread = Math.Max(10, (int)(treadTotal * part));
                cursorGx = Math.Max(basinTopGx, cursorGx - tread);
                _terraceGx.Add(cursorGx + tread / 2);
                nodes.Add(new ProfileNode
                {
                    Gx = cursorGx,
                    Depth = cursorDepth + WorldGen.genRand.Next(-2, 3),
                    Exponent = 1f
                });
            }

            // Котловина: ровное дно, туда садится арена Краба
            nodes.Add(new ProfileNode { Gx = basinTopGx, Depth = seaDepth, Exponent = 1.2f });
            _deepestFlatGx = (basinTopGx + seaStart) / 2;
            _terraceGx.Add(_deepestFlatGx);

            // Профиль обязан дойти до самой оболочки. За последним узлом SampleProfile
            // возвращает константу, и дно раскатывается в плоскую плиту на всю ширину
            // утёса — ту самую прямоугольную яму у края мира
            nodes.Add(new ProfileNode { Gx = seaStart, Depth = seaDepth + 5, Exponent = 1f });
            nodes.Add(new ProfileNode { Gx = ShellThickness, Depth = seaDepth + 16, Exponent = 1f });

            return nodes;
        }

        // Кусочная кривая по узлам: Gx строго убывает, между узлами сглаженный переход
        private static float SampleProfile(List<ProfileNode> nodes, int gx)
        {
            if (gx >= nodes[0].Gx)
                return nodes[0].Depth;

            for (int i = 0; i < nodes.Count - 1; i++)
            {
                ProfileNode from = nodes[i];
                ProfileNode to = nodes[i + 1];
                if (gx > to.Gx)
                {
                    float t = (from.Gx - gx) / (float)Math.Max(1, from.Gx - to.Gx);
                    float shaped = TideNoise.SmoothStep(0f, 1f, MathF.Pow(TideNoise.Clamp01(t), to.Exponent));
                    return TideNoise.Lerp(from.Depth, to.Depth, shaped);
                }
            }
            return nodes[^1].Depth;
        }

        private struct ProfileNode
        {
            public int Gx;
            public float Depth;      // тайлов ниже зеркала воды, отрицательное — выше
            public float Exponent;   // кривизна подхода к узлу
        }

        // Насколько сильно в этом столбце проступает утёс: 1 у края мира, 0 в открытом море.
        // Переход растянут на CliffTalusReach ширин полосы — раньше он был жёстким,
        // и весь перепад в сотни тайлов приходился на один столбец, давая отвесную стену
        private float CliffBlendAt(int gx)
        {
            float cliffWidth = _shoreGx * BandCliff;
            float raw = TideNoise.SmoothStep(cliffWidth * CliffTalusReach, cliffWidth * 0.3f, gx);
            if (raw <= 0f)
                return 0f;

            // Кромка склона гуляет, иначе осыпь читается ровной диагональю
            float wobble = TideNoise.Signed(gx * 0.03f, ChCliff + 13, 3) * 0.13f;
            return TideNoise.Clamp01(raw + wobble);
        }

        // Верх породы в столбце: дно моря вдали от утёса, гребень у края мира,
        // между ними — осыпной склон уступами
        private int CliffRockTopGyAt(int gx, int floorGy, float blend)
        {
            if (blend <= 0.001f)
                return floorGy;

            float scaled = blend * CliffTerraces;
            float index = MathF.Floor(scaled);
            float rise = TideNoise.SmoothStep(0.58f, 1f, scaled - index);
            float stepped = (index + rise) / CliffTerraces;
            float shape = TideNoise.Lerp(blend, stepped, 0.55f);

            return (int)TideNoise.Lerp(floorGy, CliffCrestGyAt(gx), shape);
        }

        // Гребень утёса у самого края мира: вырезы и контрфорсы даёт гребневой шум
        private int CliffCrestGyAt(int gx)
        {
            float crest = _gWater
                        - CliffHeight * (0.5f + 0.5f * TideNoise.Ridged(gx * 0.055f, ChCliff, 3))
                        - TideNoise.Signed(gx * 0.017f, ChCliff + 5, 4) * 13f
                        - TideNoise.Signed(gx * 0.085f, ChCliff + 9, 2) * 5f;
            return (int)crest;
        }

        private bool CanCarve(int gx, int gy)
        {
            if (gx < ShellThickness || gy < 0 || gy > _gAbyssBottom)
                return false;
            if (InsideTempleOuter(gx, gy))
                return false;
            return gx < InlandEdgeAt(gy) - ShellThickness;
        }

        private void CarveIfAllowed(int gx, int gy, byte value)
        {
            if (CanCarve(gx, gy))
                _grid.Set(gx, gy, value);
        }

        // Заливка воды с оглядкой на герметичность: за оболочку резать нельзя
        private void CarveBlob(float gx, float gy, float radius, byte value, int channel, float roughness = 0.3f)
        {
            int span = (int)MathF.Ceiling(radius * (1f + roughness)) + 1;
            for (int dy = -span; dy <= span; dy++)
            {
                for (int dx = -span; dx <= span; dx++)
                {
                    int cx = (int)gx + dx;
                    int cy = (int)gy + dy;
                    if (!CanCarve(cx, cy))
                        continue;

                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > radius * (1f + roughness))
                        continue;

                    float wobble = TideNoise.Fbm(cx * 0.09f, cy * 0.09f, channel, 2) - 0.5f;
                    if (distance <= radius * (1f + wobble * 2f * roughness))
                        _grid.Set(cx, cy, value);
                }
            }
        }

        private void CarveTunnel(float x0, float y0, float x1, float y1, float radiusMin, float radiusMax,
            byte value, int channel, float wander = 0.55f)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 2f)
                return;

            int steps = (int)length;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float px = TideNoise.Lerp(x0, x1, t) +
                           TideNoise.Signed(t * 6f + channel * 0.37f, channel, 3) * wander * length * 0.16f;
                float py = TideNoise.Lerp(y0, y1, t) +
                           TideNoise.Signed(t * 6f + 41.3f, channel + 7, 3) * wander * length * 0.10f;
                float radius = TideNoise.Lerp(radiusMin, radiusMax, TideNoise.Fbm(t * 9f, channel + 13, 3));
                CarveBlob(px, py, radius, value, channel);
            }
        }

        private void RecordSeal(int step, int gx, int gy, int width, int height)
        {
            _sealSites.Add(new TideSealSite
            {
                Step = step,
                X = _grid.ToWorldX(_dir > 0 ? gx : gx + width - 1),
                Y = _grid.ToWorldY(gy),
                Width = width,
                Height = height
            });
        }

        // Границы зон в мир: вылет каждой зоны берём из той же таблицы наклона,
        // по которой резался рельеф, иначе проверка биома разойдётся с геометрией
        private void PublishWorldData()
        {
            TideOfShadowsWorldData.EdgeX = _edgeX;
            TideOfShadowsWorldData.WaterTopY = _waterTopY;

            int[] bottoms = { _gPortBottom, _gForestBottom, _gCityBottom, _gRiftBottom, _gAbyssBottom };
            for (int zone = 0; zone < bottoms.Length; zone++)
            {
                TideOfShadowsWorldData.ZoneBottomY[zone] = _topY + bottoms[zone];
                TideOfShadowsWorldData.ZoneWidth[zone] = InlandEdgeAt(bottoms[zone]);
            }

            TideOfShadowsWorldData.SealSites = _sealSites;
        }

        // Уровень зеркала воды: минимальный по нескольким колонкам ванильного океана
        private int FindOceanWaterTop()
        {
            int best = -1;
            int maxY = (int)Main.worldSurface + 100;
            for (int off = 60; off <= 240; off += 20)
            {
                int x = _edgeX + off * _dir;
                if (x <= 20 || x >= Main.maxTilesX - 20)
                    continue;

                for (int y = 40; y < maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile && Main.tileSolid[tile.TileType])
                        break;
                    if (tile.LiquidAmount > 100 && tile.LiquidType == LiquidID.Water)
                    {
                        if (best == -1 || y < best)
                            best = y;
                        break;
                    }
                }
            }
            return best;
        }

        // Храм Джунглей ломает прогрессию, если его снести. Раньше из-за него
        // обрезалась вся ширина биома — весь подкоп под сушу пропадал ради одной
        // постройки. Теперь исключается только коробка вокруг Храма: монолит её
        // обходит, резьба туда не заходит, и Храм остаётся замурован в скале
        private void FindTempleBox()
        {
            const int ScanStep = 2;
            int minOff = int.MaxValue, maxOff = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            int scanTop = Math.Max(40, (int)Main.worldSurface - 60);
            int scanBottom = Math.Min(Main.maxTilesY - UnderworldMargin, (int)Main.rockLayer + 420);

            for (int off = 0; off <= _deepWidth + TempleOuterMargin; off += ScanStep)
            {
                int x = _edgeX + off * _dir;
                if (x <= 20 || x >= Main.maxTilesX - 20)
                    continue;

                for (int y = scanTop; y < scanBottom; y += ScanStep)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile)
                        continue;
                    if (tile.TileType != TileID.LihzahrdBrick && tile.TileType != TileID.LihzahrdAltar)
                        continue;

                    minOff = Math.Min(minOff, off);
                    maxOff = Math.Max(maxOff, off);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            _hasTemple = minOff != int.MaxValue;
            if (!_hasTemple)
                return;

            _templeInner = new Rectangle(minOff - TempleInnerMargin, minY - _topY - TempleInnerMargin,
                maxOff - minOff + TempleInnerMargin * 2, maxY - minY + TempleInnerMargin * 2);
            _templeOuter = new Rectangle(minOff - TempleOuterMargin, minY - _topY - TempleOuterMargin,
                maxOff - minOff + TempleOuterMargin * 2, maxY - minY + TempleOuterMargin * 2);
        }

        private bool InsideTempleInner(int gx, int gy) => _hasTemple && _templeInner.Contains(gx, gy);

        // Суперэллипс, а не прямоугольник: обход Храма не должен читаться как
        // вырезанная из биома прямоугольная дыра. Показатель 4 держит форму
        // близкой к коробке, но со скруглёнными и зашумлёнными краями
        private bool InsideTempleOuter(int gx, int gy)
        {
            if (!_hasTemple)
                return false;

            float nx = (gx - (_templeOuter.X + _templeOuter.Width * 0.5f)) / (_templeOuter.Width * 0.5f);
            float ny = (gy - (_templeOuter.Y + _templeOuter.Height * 0.5f)) / (_templeOuter.Height * 0.5f);
            float nx2 = nx * nx;
            float ny2 = ny * ny;
            float wobble = (TideNoise.Fbm(gx * 0.045f, gy * 0.045f, ChRoof + 41, 2) - 0.5f) * 0.3f;
            return nx2 * nx2 + ny2 * ny2 <= 1f + wobble;
        }

        // Диагностика уходит в client.log / server.log рядом с логами tModLoader
        private static void Log(string message)
            => ModContent.GetInstance<global::SoA.SoA>()?.Logger.Info("[TideOcean] " + message);

        private static int FindSurface(int x)
        {
            for (int y = 30; y < Main.maxTilesY - 200; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    return y;
            }
            return -1;
        }
    }
}
