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
        // Ширина под содержимое моря (см. LayOutSea): пляж, шельф, склоны, полка галеона,
        // котловина с ареной и риф. На 1.15 это не влезало, и склоны сжимались в обрывы
        private const float SurfaceWidthScale = 1.6f;
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
        // Доля бюджета на большом мире давала от 130 до 200+ тайлов моря — в зависимости
        // от того, где лёг слой камня. Раскладка моря (LayOutSea) рассчитана на ~140;
        // всё, что глубже, отдаётся Чёрным лесам
        private const int MaxSeaDepth = 140;
        private const float ZoneForestBottom = 0.40f;     // дно зала с ламинарией
        private const float ZoneCityBottom = 0.60f;       // дно провала с городом
        private const float ZoneRiftBottom = 0.80f;       // дно траншеи, ниже только Бездна

        // --- Открытое море: шельф, перегиб, лестница уступов, котловина ---
        private const float BandCliff = 0.07f;            // доля ширины под риф волнолома у края мира
        private const int BeachWidth = 72;                // сухой берег от уреза воды вглубь суши
        // Высота задана в тайлах, а не долей бюджета: доля растёт вместе с глубиной
        // бездны, и на большом мире пляж поднимался на 59 тайлов — то есть плато
        private const int BeachCrestHeight = 22;          // дюны над зеркалом воды
        private const int DuneFadeInWidth = 24;           // за столько столбцов от уреза дюны набирают полную высоту

        // Подъём пляжа к настоящей высоте суши на линии берега
        private const int LandSampleSpan = 8;             // столбцов суши, по которым меряется её высота
        private const int MinShoreFreeboard = 3;          // суша не ниже этого над зеркалом воды
        private const float LandRampRunPerTile = 2.5f;    // столбцов подъёма на тайл разницы высот
        private const int MinLandRampWidth = 24;
        private const int MaxLandRampWidth = 90;

        // Раскладка моря в тайлах (LayOutSea). Числа подобраны под большой мир:
        // глубина моря ~130, галеон 128x83 — мачты уходят под воду с полки на 92
        private const int ShelfWidth = 50;                // шельф от уреза до кромки
        private const int ShelfBreakDepth = 35;           // глубина у внешней кромки шельфа
        private const float SlopeGradient = 0.8f;         // средний уклон склонов: ~40°, в середине S-кривой ~50°
        private const int ShipLedgeWidth = 80;            // полка галеона: днище скруглено, 80 из 128 хватает
        private const int ShipLedgeDepth = 92;
        private const int ShipLedgeBasinGap = 25;         // полка не ниже котловины минус столько
        private const int BasinMinWidth = 80;             // арена Краба занимает 68
        private const float MinSlopeStretch = 0.5f;       // склоны не сжимаются круче двойного уклона
        private const float MaxSlopeStretch = 1.6f;
        private const int ShelfBreakLandmarkInset = 12;   // площадка ориентира чуть мельче кромки
        private const int ShelfBarHeight = 9;             // насколько песчаная гряда поднимает дно
        private const int ShelfMaxSlope = 2;              // предел падения дна на столбец по всему морю

        // Кекуры: высота над водой связана с шириной, а основание — с местной глубиной.
        // Прежние надводные скалы задавали гребень абсолютно (_gWater минус случайные
        // 14-46) и росли с любого дна, поэтому над ямой в двести тайлов вырастала игла
        private const int StackMaxDepth = 30;             // глубже кекур не ставим
        private const float StackHeightToWidth = 0.45f;   // предел надводной части к ширине

        // У края мира вместо горы — низкий риф, остов волнолома затопленного порта:
        // гребень у самой воды, местами захлёстывается. На нём стоят руины (Breakwater)
        private const int CliffHeight = 9;           // высота гребня рифа над водой
        private const float CliffTalusReach = 2.6f;  // на сколько ширин полосы растянут склон рифа от дна котловины
        private const float CliffTerraces = 2f;      // уступов на склоне: не гладкая наклонная
        private const float CliffCrestWander = 3f;   // крупная волна гребня, тайлов
        private const float CliffCrestJitter = 2f;   // мелкие зубцы гребня, тайлов
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
        private readonly List<int> _terraceGx = new();   // ровные площадки: шельф, кромка шельфа, котловина
        // Раскладка моря (LayOutSea): кромка шельфа, полка галеона, верх котловины
        private int _shelfBreakGx, _ledgeStartGx, _ledgeEndGx, _basinTopGx;
        private int _ledgeDepth, _shipLedgeGx;
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
            RoundOffNeighbourCaves();
            HardenInlandShell();
            PlaceBreakwaterRuin();
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

            // Рамка шире следа на полосу стыка: там скруглялись ванильные пещеры
            int minX = Math.Min(_edgeX, _grid.ToWorldX(_grid.Width - 1)) - CaveRoundingReach;
            int maxX = Math.Max(_edgeX, _grid.ToWorldX(_grid.Width - 1)) + CaveRoundingReach;
            WorldGen.RangeFrame(Math.Max(12, minX), Math.Max(20, _topY),
                Math.Min(Main.maxTilesX - 12, maxX),
                Math.Min(Main.maxTilesY - 20, _grid.ToWorldY(_grid.Height - 1) + CaveRoundingReach));
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
            _gPortBottom = _gWater + Math.Min((int)(_depthBudget * ZonePortBottom), MaxSeaDepth);
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
        // по нему считается глубина свода под сушей.
        // Поиск идёт от верха сетки, а не от неба: пасс работает после Final Cleanup,
        // и скан с y=30 находил летающий остров. Высота выходила выше сетки,
        // столбец целиком пропускался, и через весь биом шла полоса ванильной породы,
        // по которой резался зал без оболочки
        private void CacheVanillaSurface()
        {
            _vanillaSurfaceGy = new int[_grid.Width];
            for (int gx = 0; gx < _grid.Width; gx++)
            {
                int x = _grid.ToWorldX(gx);
                int surfaceY = x < 12 || x >= Main.maxTilesX - 12 ? -1 : FindSurface(x, _topY);
                _vanillaSurfaceGy[gx] = surfaceY == -1 ? -1 : surfaceY - _topY;
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

            int seaStart = (int)(_shoreGx * BandCliff) + 6;   // подножие рифа
            int seaEnd = _shoreGx - BeachWidth;               // урез воды
            int seaDepth = Math.Max(60, _gPortBottom - _gWater);

            LayOutSea(seaEnd, seaDepth);
            int shelfBreakGx = _shelfBreakGx;
            var nodes = BuildShelfNodes(seaStart, seaEnd, seaDepth);

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
                // валы, и мелкая рябь между ними. От уреза дюны нарастают постепенно:
                // включённые сразу, они ставили у кромки воды ступеньку до 12 тайлов
                if (gx > seaEnd)
                {
                    float duneFade = TideNoise.SmoothStep(seaEnd, seaEnd + DuneFadeInWidth, gx);
                    relief -= (TideNoise.Fbm(gx * 0.028f, ChDetail + 5, 3) * 9f
                             + TideNoise.Fbm(gx * 0.105f, ChDetail + 9, 2) * 3.5f) * duneFade;
                }

                float floor = _gWater + depth + relief;
                _seaFloorGy[gx] = (int)TideNoise.SoftMin(floor, _gPortBottom - 8, 16f);
            }

            BlendBeachIntoLand(seaEnd);

            // Предел уклона на всё море, от подножия рифа до уреза: отдельный выброс
            // шума на склоне иначе даёт стенку, которую не спишешь на рельеф
            LimitSeaFloorSlope(seaStart, seaEnd, ShelfMaxSlope);
        }

        // Гребень дюн стоит на фиксированной высоте, а суша за ним — на какой угодно,
        // и на линии берега выходила ступенька или стенка. Хвост пляжа подводится
        // к настоящей высоте суши; чем больше разница, тем длиннее подъём
        private void BlendBeachIntoLand(int seaEnd)
        {
            int landGy = SampleLandHeightGy();
            if (landGy < 0 || _shoreGx >= _seaFloorGy.Length)
                return;

            // Ниже бортика над водой сушу не опускаем: иначе море перельётся на берег
            landGy = Math.Min(landGy, _gWater - MinShoreFreeboard);

            int gap = Math.Abs(landGy - _seaFloorGy[_shoreGx]);
            int maxRamp = Math.Max(MinLandRampWidth, _shoreGx - seaEnd - 10);
            int rampWidth = Math.Clamp((int)(gap * LandRampRunPerTile), MinLandRampWidth,
                Math.Min(MaxLandRampWidth, maxRamp));
            int fromGx = _shoreGx - rampWidth;

            for (int gx = Math.Max(0, fromGx); gx <= _shoreGx; gx++)
            {
                float t = TideNoise.SmoothStep(fromGx, _shoreGx, gx);
                _seaFloorGy[gx] = (int)MathF.Round(TideNoise.Lerp(_seaFloorGy[gx], landGy, t));
            }
        }

        // Высота суши сразу за линией берега. Берётся медиана: одна яма или
        // бугор у самого шва не должны задавать высоту всему подъёму
        private int SampleLandHeightGy()
        {
            var samples = new List<int>(LandSampleSpan);
            for (int gx = _shoreGx + 1; gx <= _shoreGx + LandSampleSpan && gx < _vanillaSurfaceGy.Length; gx++)
            {
                if (_vanillaSurfaceGy[gx] >= 0)
                    samples.Add(_vanillaSurfaceGy[gx]);
            }

            if (samples.Count == 0)
                return -1;

            samples.Sort();
            return samples[samples.Count / 2];
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

        // Раскладка моря в тайлах, от уреза к краю мира: шельф, верхний склон, полка
        // галеона, нижний склон, котловина с ареной, склон рифа. Ширины считаются от
        // содержимого, а не долями моря: доли не знали, что полке нужно 80 столбцов,
        // арене 68, и всё, что не влезало, сжималось в обрывы. Если моря не хватает,
        // жмутся только склоны — и это пишется в лог
        private void LayOutSea(int seaEnd, int seaDepth)
        {
            int ledgeDepth = Math.Min(ShipLedgeDepth, seaDepth - ShipLedgeBasinGap);
            float upperRun = (ledgeDepth - ShelfBreakDepth) / SlopeGradient;
            float lowerRun = (seaDepth - ledgeDepth) / SlopeGradient;

            int reefFootGx = ReefFootGx;
            int fixedWidth = ShelfWidth + ShipLedgeWidth + BasinMinWidth;
            float slopeRoom = seaEnd - reefFootGx - fixedWidth;
            float stretch = MathHelper.Clamp(slopeRoom / (upperRun + lowerRun), MinSlopeStretch, MaxSlopeStretch);
            if (stretch < 0.95f)
                Log($"море узко для раскладки: склоны сжаты до {stretch:0.00} (не хватает {(int)(upperRun + lowerRun - slopeRoom)} столбцов)");

            _shelfBreakGx = seaEnd - ShelfWidth;
            _ledgeStartGx = _shelfBreakGx - (int)(upperRun * stretch);
            _ledgeEndGx = _ledgeStartGx - ShipLedgeWidth;
            _basinTopGx = _ledgeEndGx - (int)(lowerRun * stretch);
            _ledgeDepth = ledgeDepth;
            _shipLedgeGx = (_ledgeStartGx + _ledgeEndGx) / 2;
            _deepestFlatGx = (_basinTopGx + reefFootGx) / 2;

            Log($"раскладка моря: урез {seaEnd}, кромка шельфа {_shelfBreakGx}, полка {_ledgeStartGx}..{_ledgeEndGx} "
                + $"на глубине {ledgeDepth}, котловина {_basinTopGx}..{reefFootGx} на {seaDepth}, склоны x{stretch:0.00}");
        }

        // Узлы профиля от берега к краю мира по готовой раскладке (LayOutSea)
        private List<ProfileNode> BuildShelfNodes(int seaStart, int seaEnd, int seaDepth)
        {
            // Пляж — не полка на одной высоте, а уклон от дюн к урезу воды.
            // Показатель 1.5 делает его вогнутым: у воды почти плоско, вглубь суши круче.
            // Шельф вогнут слабее: при сильной вогнутости он оставался по колено
            // до самой кромки, и песчаные гряды выходили из воды сплошной сушей
            var nodes = new List<ProfileNode>
            {
                new() { Gx = _shoreGx, Depth = -BeachCrestHeight, Exponent = 1f },
                new() { Gx = seaEnd, Depth = -1f, Exponent = 1.5f },
                new() { Gx = _shelfBreakGx, Depth = ShelfBreakDepth, Exponent = 1.2f },
                // Верхний склон, полка галеона, нижний склон — каждый переход S-кривой
                new() { Gx = _ledgeStartGx, Depth = _ledgeDepth, Exponent = 1f },
                new() { Gx = _ledgeEndGx, Depth = _ledgeDepth + 2, Exponent = 1f },
                new() { Gx = _basinTopGx, Depth = seaDepth, Exponent = 1f }
            };

            _terraceGx.Add((_shelfBreakGx + seaEnd) / 2);   // середина шельфа — ровное мелководье
            _terraceGx.Add(_shelfBreakGx + ShelfBreakLandmarkInset); // край шельфа у кромки — там глубже всего на шельфе
            _terraceGx.Add(_deepestFlatGx);
            _crestGx.Add(_shelfBreakGx);   // кромки: под ними нависает карниз
            _crestGx.Add(_ledgeEndGx);

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

        // Где кончается склон рифа и начинается ровное дно котловины
        private int ReefFootGx => Math.Max(ShellThickness, (int)(_shoreGx * BandCliff * CliffTalusReach));

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

        // Гребень рифа у самого края мира: вырезы и контрфорсы даёт гребневой шум
        private int CliffCrestGyAt(int gx)
        {
            float crest = _gWater
                        - CliffHeight * (0.5f + 0.5f * TideNoise.Ridged(gx * 0.055f, ChCliff, 3))
                        - TideNoise.Signed(gx * 0.017f, ChCliff + 5, 4) * CliffCrestWander
                        - TideNoise.Signed(gx * 0.085f, ChCliff + 9, 2) * CliffCrestJitter;
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
            TideOfShadowsWorldData.InlandEdgeTopY = _topY;
            TideOfShadowsWorldData.InlandEdge = (int[])_inlandEdge.Clone();
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

        private static int FindSurface(int x, int startY)
        {
            for (int y = Math.Max(30, startY); y < Main.maxTilesY - 200; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    return y;
            }
            return -1;
        }
    }
}
