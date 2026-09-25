using System;
using Terraria;
using Terraria.WorldBuilding;
using SoA.Common.Utils;

namespace SoA.Common.Systems.TideOcean
{
    // Резьба зон по монолиту: открытое море, надводные скалы, зал глубин, бездна.
    public partial class TideOceanPass
    {
        private int _trenchTopGy;
        private int _heartGx, _heartGy, _heartWidth, _heartHeight;
        private int _cavernMinHeight, _cavernMaxHeight;
        private bool _cityCarved;

        // Зоны 1-2: открытая вода от зеркала до дна, великий утёс у края мира,
        // песчаная шапка на пологих участках и голая скала на уступах
        private void CarveOpenSea()
        {
            BuildSeaProfile();

            float maxSeaDepth = Math.Max(1f, _gPortBottom - _gWater);
            _rockTopGy = new int[_grid.Width];
            int previousFloor = -1;

            for (int gx = 0; gx <= _shoreGx && gx < _grid.Width; gx++)
            {
                int floorGy = _seaFloorGy[gx];
                float cliffBlend = CliffBlendAt(gx);
                int rockTopGy = CliffRockTopGyAt(gx, floorGy, cliffBlend);
                _rockTopGy[gx] = rockTopGy;

                // Небо и вода над породой. Над зеркалом резать можно без оглядки
                // на оболочку: утечь наверх воде некуда
                for (int gy = 0; gy < rockTopGy; gy++)
                {
                    if (gy < _gWater)
                        _grid.Set(gx, gy, TideGrid.Air);
                    else
                        CarveIfAllowed(gx, gy, TideGrid.Water);
                }

                // Песок привязан к настоящей глубине, а не к доле ширины: в мелких
                // бухтах его много, в глубоких почти нет, на уступах и утёсе — голая скала
                // На осыпном склоне утёса песка нет — там голая скала
                bool steep = previousFloor != -1 && Math.Abs(rockTopGy - previousFloor) >= 3;
                if (!steep && cliffBlend < 0.25f)
                {
                    float reach = TideNoise.Clamp01((rockTopGy - _gWater) / maxSeaDepth);
                    int sandDepth = (int)(TideNoise.Lerp(11f, 1f, reach)
                                          + TideNoise.Fbm(gx * 0.08f, ChDetail, 2) * 3f);
                    for (int d = 0; d < sandDepth; d++)
                        _grid.Set(gx, rockTopGy + d, TideGrid.Sand);
                }

                previousFloor = rockTopGy;
            }

            CarveCrestOverhangs();
            RoughenSlopeFaces();
            RaiseSeaStacks();
        }

        // Фактура склона: ниши и карманы в стенках сбросов, осыпь на полках.
        // Без неё сброс остаётся гладкой наклонной плоскостью, а по такой плоскости
        // сразу видно, что её провели формулой. Это фактура открытой стенки, обращённой
        // в воду, а не запечатанные полости внутри породы — те убраны намеренно
        private void RoughenSlopeFaces()
        {
            int fromGx = (int)(_shoreGx * BandCliff) + 8;
            int toGx = _shoreGx - BeachWidth - 8;

            for (int gx = fromGx; gx <= toGx; gx += WorldGen.genRand.Next(3, 10))
            {
                int here = FloorAt(gx);
                int seaward = FloorAt(gx - 5);
                int drop = seaward - here;

                if (drop >= 8)
                    CarveWallNiches(gx, here, seaward);
                else if (here - _gWater > 12)
                    DropTalus(gx, here);
            }
        }

        // Ниша выгрызена в стенку сброса: центр ставим по самой стенке, диск
        // отжимает породу вглубь суши и оставляет карман, в который можно заплыть
        private void CarveWallNiches(int gx, int topGy, int bottomGy)
        {
            int count = WorldGen.genRand.Next(1, 4);
            for (int i = 0; i < count; i++)
            {
                int gy = WorldGen.genRand.Next(topGy + 3, Math.Max(topGy + 4, bottomGy - 2));
                float radius = WorldGen.genRand.NextFloat(2.5f, 6.5f);
                _grid.Disc(gx + radius * 0.4f, gy, radius, TideGrid.Water, ChDetail + 71 + gx, 0.45f);

                // Пора рядом с нишей: мелкая, но именно они сбивают ощущение плоскости
                if (WorldGen.genRand.NextBool())
                    _grid.Disc(gx + WorldGen.genRand.Next(-4, 5), gy + WorldGen.genRand.Next(-7, 8),
                        WorldGen.genRand.NextFloat(1.6f, 2.8f), TideGrid.Water, ChDetail + 83 + gx, 0.5f);
            }
        }

        // Осыпь на полке: обломки, сползшие с ближайшего сброса выше
        private void DropTalus(int gx, int floorGy)
        {
            if (!WorldGen.genRand.NextBool(3))
                return;

            int blocks = WorldGen.genRand.Next(1, 4);
            for (int i = 0; i < blocks; i++)
            {
                float radius = WorldGen.genRand.NextFloat(2f, 4.6f);
                _grid.Disc(gx + WorldGen.genRand.Next(-6, 7), floorGy - radius + 1f, radius,
                    TideGrid.Stone, ChDetail + 97 + gx, 0.5f, footprintOnly: true);
            }
        }

        private int FloorAt(int gx)
            => _seaFloorGy[Math.Clamp(gx, 0, _seaFloorGy.Length - 1)];

        // Кекуры на отмелях: приземистые останцы, торчащие из воды. Ставятся только
        // там, где до дна недалеко, а надводная часть связана с шириной — поэтому
        // игла в двести тайлов, какие давали прежние надводные скалы, невозможна
        private void RaiseSeaStacks()
        {
            int gx = _shoreGx - BeachWidth - WorldGen.genRand.Next(30, 90);
            int limit = (int)(_shoreGx * BandCliff) + 30;

            while (gx > limit)
            {
                TryRaiseStack(gx);
                gx -= WorldGen.genRand.Next(70, 190);
            }
        }

        private void TryRaiseStack(int gxCenter)
        {
            if (gxCenter < 12 || gxCenter >= _seaFloorGy.Length - 12)
                return;

            int floorGy = _seaFloorGy[gxCenter];
            int depth = floorGy - _gWater;
            if (depth > StackMaxDepth || depth < 5)
                return;

            int halfWidth = WorldGen.genRand.Next(6, 15);
            int crown = Math.Min((int)(halfWidth * 2 * StackHeightToWidth), WorldGen.genRand.Next(7, 17));
            int crestGy = _gWater - crown;
            int rootGy = floorGy + 3;

            // Диск рисуется с центром в своей строке, поэтому верхний диск уводит породу
            // вверх ещё на свой радиус. Начинаем ниже ровно на него, иначе кекур встаёт
            // в полтора раза выше объявленного предела, и правило про ширину — фикция
            int topRadius = (int)(halfWidth * 0.45f);
            for (int gy = crestGy + topRadius; gy <= rootGy; gy++)
            {
                float t = (gy - crestGy) / (float)Math.Max(1, rootGy - crestGy);
                // Книзу столб расширяется, а у самого уреза воды подточен прибоем
                float taper = 0.45f + 0.55f * t;
                float notch = 1f - 0.22f * MathF.Exp(-MathF.Abs(gy - _gWater) / 3f);
                float grain = 0.88f + 0.24f * TideNoise.Fbm(gy * 0.15f + gxCenter * 0.3f, ChDetail + 11, 2);

                _grid.Disc(gxCenter, gy, Math.Max(2f, halfWidth * taper * notch * grain),
                    TideGrid.Stone, ChDetail + 13);
            }
        }

        // Подмытые карнизы под кромкой перегиба и под верхом каждого сброса. Именно
        // нависающая порода отличает естественный обрыв от выкопанной ямы: под кромкой
        // можно проплыть, а сверху она читается как обрыв
        private void CarveCrestOverhangs()
        {
            foreach (int crestGx in _crestGx)
            {
                if (crestGx < 8 || crestGx >= _seaFloorGy.Length - 8)
                    continue;

                int lip = _seaFloorGy[crestGx];
                int notchGy = lip + WorldGen.genRand.Next(6, 18);
                int reach = WorldGen.genRand.Next(14, 34);
                float radius = WorldGen.genRand.NextFloat(2.6f, 5.2f);

                // В сторону моря — это меньшие gx
                CarveTunnel(crestGx + 5, notchGy, crestGx - reach, notchGy + WorldGen.genRand.Next(-4, 8),
                    radius * 0.65f, radius, TideGrid.Water, ChDetail + 61 + crestGx, 0.55f);
            }
        }

        // --- Висячий рельеф Прибрежья ---
        private const int GrowthClearance = 4;      // столько воды нужно под козырьком, чтобы там что-то вешать
        private const int GrowthFloorGap = 5;       // нарост не должен доставать до дна
        private const int HalfColumnFloorGap = 3;   // полуколонне до дна можно ближе
        private const int MinGrowthSpan = 14;       // ниже этого зазора вешать нечего

        // С козырьков порогов, сводов арок и подмытых шапок в толщу воды свисают
        // наросты и полуколонны. Без них весь рельеф открытого моря лежал на дне:
        // от зеркала воды до грунта плыть было мимо пустоты
        private void HangCoastalGrowths()
        {
            int gx = (int)(_shoreGx * BandCliff) + 4;
            int limit = _shoreGx - BeachWidth + 12;

            while (gx < limit)
            {
                TryHangGrowth(gx);
                gx += WorldGen.genRand.Next(5, 17);
            }

            // Кромки — главные козырьки: под ними наросты гуще, и сверху
            // по ним читается, где следующий сброс вниз
            foreach (int crestGx in _crestGx)
            {
                int count = WorldGen.genRand.Next(2, 6);
                for (int i = 0; i < count; i++)
                    TryHangGrowth(crestGx + WorldGen.genRand.Next(-26, 27));
            }
        }

        private void TryHangGrowth(int gx)
        {
            int ceilingGy = FindOpenWaterCeiling(gx);
            if (ceilingGy == -1)
                return;

            int floorGy = RockTopGyAt(gx);
            int span = floorGy - ceilingGy;
            if (span < MinGrowthSpan)
                return;

            // Полуколонна тянется почти до дна и работает вертикальным ориентиром,
            // остальные формы висят в толще и до грунта не доходят
            bool halfColumn = span > 34 && WorldGen.genRand.NextBool(5);
            int maxLength = span - (halfColumn ? HalfColumnFloorGap : GrowthFloorGap);
            int length = halfColumn
                ? (int)(maxLength * WorldGen.genRand.NextFloat(0.62f, 0.92f))
                : Math.Min(maxLength, WorldGen.genRand.Next(6, 27));
            if (length < 5)
                return;

            float baseRadius = halfColumn
                ? WorldGen.genRand.NextFloat(3.4f, 6.2f)
                : WorldGen.genRand.NextFloat(2.2f, 4.6f);
            float lean = TideNoise.Signed(gx * 0.09f, ChDetail + 37, 2) * (halfColumn ? 0.05f : 0.13f);

            for (int step = 0; step <= length; step++)
            {
                float t = step / (float)length;
                // Сосулька сходит на нет книзу, полуколонна почти не сужается
                // и снова раздаётся у пяты
                float shape = halfColumn
                    ? 0.82f + 0.36f * MathF.Abs(t - 0.45f)
                    : 1f - t;
                float grain = 0.75f + 0.5f * TideNoise.Fbm((ceilingGy + step) * 0.11f + gx * 0.27f, ChDetail + 41, 2);
                float radius = baseRadius * shape * grain;
                if (radius < 1.4f)
                    break;

                _grid.Disc(gx + lean * step, ceilingGy + step, radius, TideGrid.Stone, ChDetail + 43,
                    footprintOnly: true);
            }
        }

        // Случайный козырёк в столбце: порода, под которой есть открытая вода.
        // Выбор идёт одним проходом без списка кандидатов
        private int FindOpenWaterCeiling(int gx)
        {
            if (gx < ShellThickness || _seaFloorGy == null || gx >= _seaFloorGy.Length)
                return -1;

            int bottom = RockTopGyAt(gx) - GrowthFloorGap - GrowthClearance;
            int found = -1;
            int seen = 0;

            for (int gy = _gWater + 2; gy < bottom; gy++)
            {
                if (!TideGrid.IsSolid(_grid.Get(gx, gy)) || !IsOpenWaterBelow(gx, gy, GrowthClearance))
                    continue;

                seen++;
                if (WorldGen.genRand.Next(seen) == 0)
                    found = gy;
            }

            return found;
        }

        private bool IsOpenWaterBelow(int gx, int gy, int depth)
        {
            for (int d = 1; d <= depth; d++)
            {
                if (_grid.Get(gx, gy + d) != TideGrid.Water)
                    return false;
            }
            return true;
        }

        // Верх породы: на осыпи утёса он выше дна, и наросты должны мерить зазор по нему
        private int RockTopGyAt(int gx)
            => _rockTopGy != null && gx >= 0 && gx < _rockTopGy.Length ? _rockTopGy[gx] : _seaFloorGy[gx];

        // Надводные скалы: пять архетипов вместо одного, расставленных
        // с переменным шагом, чтобы силуэт не читался как ряд столбов
        // Зона II — Чёрные леса: затопленный зал под морским дном. Свод провисает,
        // пол уходит к краю мира, зал зарастает ламинарией во всю высоту.
        //
        // Каменных колонн здесь больше нет намеренно. Вертикаль зала теперь держат
        // сами водоросли: лес обязан быть проходимым насквозь и шевелиться, а частокол
        // из породы делал ровно обратное — резал зал на глухие нефы. Руины остаются
        // рельефом пола и свода (AddCavernRelief), их природа и поглощает
        private void CarveDeepCavern()
        {
            int inlandLimit = InlandEdgeAt(_gForestBottom) - ShellThickness;
            _cavernMinHeight = int.MaxValue;
            _cavernMaxHeight = 0;

            for (int gx = ShellThickness; gx < inlandLimit && gx < _grid.Width; gx++)
            {
                int roofGy = DeepRoofGyAt(gx);
                int floorGy = DeepFloorGyAt(gx);
                for (int gy = roofGy; gy <= floorGy; gy++)
                    CarveIfAllowed(gx, gy, TideGrid.Water);

                int height = floorGy - roofGy;
                _cavernMinHeight = Math.Min(_cavernMinHeight, height);
                _cavernMaxHeight = Math.Max(_cavernMaxHeight, height);
            }

            AddCavernRelief(inlandLimit);
            CarveCanyons(inlandLimit);
        }

        // Колодец города. Полуширина держится в этих пределах: 16 тайлов — уже
        // проход, 34 — площадь. Шире делать нельзя, обе стены должны быть видны разом
        private const int CityHalfWidthMin = 16;
        private const int CityHalfWidthMax = 34;

        // Вход в город сверху: сюда же садится шахта из леса
        private int CityTopGx => Math.Clamp((int)(InlandEdgeAt(_gForestBottom) * 0.45f),
            ShellThickness + 40, _grid.Width - 40);

        // Зона III — Погружённый город: вертикальный туннель в породе, дома врублены
        // в ОБЕ стены. Игрок спускается по колодцу, и город идёт мимо него слева
        // и справа — как улица, поставленная на попа.
        //
        // Не провал во всю ширину зала: тот читался пустой коробкой, в которой
        // дома жались к краям. Туннель узкий намеренно — обе стены должны попадать
        // в один экран, иначе половина города всегда за кадром
        private void CarveSunkenCity()
        {
            int top = _gForestBottom + 6;
            int bottom = _gCityBottom - 4;
            if (bottom - top < 40)
                return;

            for (int gy = top; gy <= bottom; gy++)
            {
                float axis = CityAxisAt(gy);
                float half = CityHalfWidthAt(gy);

                for (int gx = (int)(axis - half); gx <= (int)(axis + half); gx++)
                    CarveIfAllowed(gx, gy, TideGrid.Water);
            }

            CarveCityHouses(top, bottom);
            _cityCarved = true;
        }

        // Ось колодца: сверху садится на шахту из леса, снизу — на устье траншеи.
        // Так спуск получается сквозным по построению, а не по удаче шума
        private float CityAxisAt(int gy)
        {
            int top = _gForestBottom + 6;
            int bottom = _gCityBottom - 4;
            float t = TideNoise.Clamp01((gy - top) / (float)Math.Max(1, bottom - top));

            float fromGx = CityTopGx;
            float toGx = TrenchAxisAt(_trenchTopGy + 8);

            // Виляние гаснет к низу: у самого устья ось обязана совпасть с траншеей
            // тайл в тайл, иначе печать III окажется рядом с проходом, а не в нём
            float drift = TideNoise.Signed(gy * 0.014f, ChCanyon + 71, 3) * 16f * (1f - t);
            return TideNoise.Lerp(fromGx, toGx, TideNoise.SmoothStep(0f, 1f, t)) + drift;
        }

        // Полуширина колодца пульсирует: узкие горла чередуются с площадями
        private float CityHalfWidthAt(int gy)
        {
            float pulse = TideNoise.Fbm(gy * 0.017f, ChCanyon + 77, 3);
            return TideNoise.Lerp(CityHalfWidthMin, CityHalfWidthMax, pulse);
        }

        // Дома: прямоугольные комнаты, врубленные в обе стены колодца, и карниз
        // под каждой — иначе к дому не подойти, а спуск превращается в падение.
        // Ярусы идут парами: слева и справа на одной высоте — это одна улица
        private void CarveCityHouses(int top, int bottom)
        {
            const int FloorStep = 22;       // высота яруса
            const int LedgeThickness = 3;

            for (int gy = top + FloorStep; gy < bottom - FloorStep; gy += FloorStep)
            {
                float axis = CityAxisAt(gy);
                float half = CityHalfWidthAt(gy);

                for (int side = -1; side <= 1; side += 2)
                {
                    // Не каждый ярус застроен с обеих сторон: сплошная застройка
                    // на всю высоту читается как обои, а не как город
                    if (WorldGen.genRand.NextBool(6))
                        continue;

                    int roomWidth = WorldGen.genRand.Next(10, 19);
                    int roomHeight = WorldGen.genRand.Next(7, 13);
                    int wallGx = (int)(axis + side * half);
                    int from = side < 0 ? wallGx - roomWidth : wallGx;
                    int roomTop = gy - roomHeight;

                    for (int gx = from; gx < from + roomWidth; gx++)
                        for (int gy2 = roomTop; gy2 < gy; gy2++)
                            CarveIfAllowed(gx, gy2, TideGrid.Water);

                    // Карниз перед домом выступает в колодец: по нему и идёт улица
                    int ledgeFrom = side < 0 ? from : wallGx;
                    int ledgeTo = side < 0 ? wallGx + 4 : wallGx + roomWidth;
                    for (int gx = ledgeFrom; gx < ledgeTo; gx++)
                        for (int gy2 = gy; gy2 < gy + LedgeThickness; gy2++)
                            _grid.Set(gx, gy2, TideGrid.Stone);
                }
            }
        }

        private int DeepRoofGyAt(int gx)
        {
            PinchCavern(gx, out float roof, out _);
            return (int)roof;
        }

        private int DeepFloorGyAt(int gx)
        {
            PinchCavern(gx, out _, out float floor);
            return (int)floor;
        }

        // Свод и пол сводятся к середине зала там, где гребневой шум даёт горло.
        // Считаются вместе, потому что пережим обязан быть общим: разойдись они
        // по фазе — вместо горла получится просто наклонный пол
        private void PinchCavern(int gx, out float roof, out float floor)
        {
            roof = RawDeepRoofGy(gx);
            floor = RawDeepFloorGy(gx);

            float middle = (roof + floor) * 0.5f;
            float half = Math.Max(MinDeepCavernHeight * 0.5f,
                (floor - roof) * 0.5f * (1f - CavernPinchDepth * CavernPinchAt(gx)));

            roof = middle - half;
            floor = middle + half;
        }

        private float CavernPinchAt(int gx)
        {
            float ridge = TideNoise.Ridged(gx * CavernPinchFrequency, ChRoof + 41, 1);
            return TideNoise.Clamp01((ridge - CavernPinchThreshold) / (1f - CavernPinchThreshold));
        }

        private float RawDeepRoofGy(int gx)
        {
            float baseRoof = gx <= _shoreGx && gx < _seaFloorGy.Length
                ? _seaFloorGy[gx] + DeepRoofThickness
                : MonolithTopGy(gx) + 12;

            // Рельеф в обе стороны и мягкий предел снизу. Прежний односторонний Fbm
            // поверх Math.Max по _gPortBottom держал свод в полосе тридцати тайлов
            // на все семьсот столбцов — то есть ровным потолком
            float relief = TideNoise.Signed(gx * 0.011f, ChRoof, 4) * 34f
                         + TideNoise.Signed(gx * 0.047f, ChRoof + 3, 3) * 15f
                         + TideNoise.Signed(gx * 0.13f, ChRoof + 7, 2) * 5f;

            return TideNoise.SoftMax(baseRoof + relief, _gPortBottom + 6, 18f);
        }

        private float RawDeepFloorGy(int gx)
        {
            float p = TideNoise.Clamp01(gx / (float)Math.Max(1, _deepWidth));
            float baseFloor = TideNoise.Lerp(_gForestBottom - 8f, _gForestBottom - 30f, TideNoise.SmoothStep(0f, 0.85f, p));
            float bumps = TideNoise.Signed(gx * 0.019f, ChFloor + 21, 4) * 24f
                        + TideNoise.Signed(gx * 0.081f, ChFloor + 33, 3) * 6f;
            // Предел мягкий: жёсткий кламп раскатывал пол зала в прямую
            return TideNoise.SoftMin(baseFloor + bumps, _gForestBottom + 4, 14f);
        }


        // Сталактиты и наросты на полу и своде. Держат рельеф зала: без них
        // между рощами остаётся пустой коридор, и зал читается плоским
        private void AddCavernRelief(int inlandLimit)
        {
            int gx = ShellThickness + 6;
            while (gx < inlandLimit - 6)
            {
                int roofGy = DeepRoofGyAt(gx);
                int floorGy = DeepFloorGyAt(gx);
                int height = floorGy - roofGy;

                if (height >= 20)
                {
                    bool fromRoof = WorldGen.genRand.NextBool();
                    int length = WorldGen.genRand.Next(4, Math.Min(18, height / 2));
                    float baseRadius = WorldGen.genRand.NextFloat(2.2f, 4.5f);
                    int anchor = fromRoof ? roofGy : floorGy;
                    int tip = fromRoof ? roofGy + length : floorGy - length;

                    for (int gy = Math.Min(anchor, tip); gy <= Math.Max(anchor, tip); gy++)
                    {
                        float taper = Math.Abs(gy - tip) / (float)length;
                        _grid.Disc(gx, gy, Math.Max(1.6f, baseRadius * taper), TideGrid.Stone, ChColumn + 71,
                            footprintOnly: true);
                    }
                }

                gx += WorldGen.genRand.Next(11, 38);
            }
        }

        // Каньоны в полу зала: извилистые, книзу сужаются до щели
        private void CarveCanyons(int inlandLimit)
        {
            int count = WorldGen.genRand.Next(2, 4);
            for (int i = 0; i < count; i++)
            {
                int gx = WorldGen.genRand.Next(ShellThickness + 40, Math.Max(ShellThickness + 60, inlandLimit - 40));
                int floorGy = DeepFloorGyAt(gx);
                int depth = WorldGen.genRand.Next(28, 52);
                int endGy = Math.Min(floorGy + depth, _gForestBottom + 30);
                float drift = TideNoise.Signed(gx * 0.09f, ChCanyon, 2) * 26f;

                CarveTunnel(gx, floorGy - 6, gx + drift, endGy, 2.2f, 7.5f, TideGrid.Water, ChCanyon + i * 17, 0.75f);
            }
        }

        // Зоны IV-V: Разлом и Бездна. Траншея вдоль края мира — после простора
        // города теснота. Ширина пульсирует гребневым шумом, поэтому щели
        // чередуются с карманами
        private void CarveAbyss()
        {
            for (int gy = _trenchTopGy; gy <= _gAbyssBottom; gy++)
            {
                float axis = TrenchAxisAt(gy);
                float halfWidth = TideNoise.Lerp(14f, 30f, TideNoise.Fbm(gy * 0.028f, ChTrench + 7, 3))
                                  * (0.55f + 0.45f * TideNoise.Ridged(gy * 0.019f, ChTrench + 11, 2))
                                  * PinchAt(gy);

                int from = (int)(axis - halfWidth);
                int to = (int)(axis + halfWidth);
                for (int gx = from; gx <= to; gx++)
                    CarveIfAllowed(gx, gy, TideGrid.Water);
            }

            CarveFissures();
            CarveAbyssChambers();
        }

        // Единственный источник правды об оси траншеи. Раньше её считали
        // в четырёх местах по-своему, и шахта из зала промахивалась мимо устья
        private float TrenchAxisAt(int gy)
        {
            int span = Math.Max(1, _gAbyssBottom - _trenchTopGy);
            float t = TideNoise.Clamp01((gy - _trenchTopGy) / (float)span);
            float wander = TideNoise.Signed(gy * 0.011f, ChTrench, 4) * 52f;
            float inlandDrift = TideNoise.SmoothStep(0f, 1f, t) * (_deepWidth * 0.22f);
            return TrenchStartOffset + wander + inlandDrift;
        }

        // Пережим траншеи там, где встанет печать: без него перекрывать нечего
        private float PinchAt(int gy)
        {
            float toPlantera = MathF.Abs(gy - _gRiftBottom);
            if (toPlantera > 14f)
                return 1f;
            return TideNoise.Lerp(0.24f, 1f, TideNoise.SmoothStep(0f, 14f, toPlantera));
        }

        // Разломы: тонкие трещины вбок от траншеи. Часть тупиковая — так и должно быть,
        // тупик делает карту живой, а не «каждый ход куда-то ведёт»
        private void CarveFissures()
        {
            int count = WorldGen.genRand.Next(10, 17);
            for (int i = 0; i < count; i++)
            {
                int gy = WorldGen.genRand.Next(_trenchTopGy + 10, _gAbyssBottom - 10);
                float axis = TrenchAxisAt(gy);

                int direction = WorldGen.genRand.NextBool(3) ? -1 : 1;   // внутрь суши чаще
                float length = WorldGen.genRand.Next(30, 85);
                // Наклон сопоставим с длиной: без этого все разломы выходили
                // почти горизонтальными и читались как повтор одной формы
                float slope = TideNoise.Signed(i * 3.7f, ChTrench + 29, 2);
                float endGx = axis + direction * length * (0.45f + 0.55f * (1f - MathF.Abs(slope)));
                float endGy = gy + slope * length * 0.85f;

                CarveTunnel(axis, gy, endGx, endGy, 1.6f, 3.4f, TideGrid.Water, ChTrench + 31 + i, 0.85f);
            }
        }

        // Нижняя бездна: корневая система камер, отходящих от траншеи вглубь суши
        private void CarveAbyssChambers()
        {
            int count = WorldGen.genRand.Next(3, 6);
            for (int i = 0; i < count; i++)
            {
                int gy = WorldGen.genRand.Next(_gRiftBottom + 12, _gAbyssBottom - 14);
                float axis = TrenchAxisAt(gy);
                float offset = WorldGen.genRand.Next(45, 120);
                float chamberGx = axis + offset;
                float radius = WorldGen.genRand.NextFloat(16f, 30f);

                CarveBlob(chamberGx, gy, radius, TideGrid.Water, ChTrench + 61 + i, 0.34f);
                CarveTunnel(axis, gy, chamberGx, gy, 2.2f, 4f, TideGrid.Water, ChTrench + 71 + i, 0.6f);
            }
        }

        // Сердце Бездны: камера на самом дне, изначально отрезанная от всего.
        // Ход к ней прорезает ConnectZones и вешает туда печать Мунлорда
        private void CarveHeartChamber()
        {
            _heartWidth = WorldGen.genRand.Next(58, 82);
            _heartHeight = WorldGen.genRand.Next(30, 42);
            _heartGy = _gAbyssBottom - _heartHeight / 2 - 4;
            _heartGx = (int)(TrenchAxisAt(_heartGy) + WorldGen.genRand.Next(90, 150));

            int limit = InlandEdgeAt(_heartGy) - ShellThickness - _heartWidth / 2 - 4;
            if (_heartGx > limit)
                _heartGx = Math.Max(ShellThickness + _heartWidth / 2 + 4, limit);

            CarveHeartCavity();
        }

        private void CarveHeartCavity()
        {
            for (int dy = -_heartHeight / 2; dy <= _heartHeight / 2; dy++)
            {
                for (int dx = -_heartWidth / 2; dx <= _heartWidth / 2; dx++)
                {
                    float nx = dx / (_heartWidth * 0.5f);
                    float ny = dy / (_heartHeight * 0.5f);
                    float wobble = (TideNoise.Fbm((_heartGx + dx) * 0.07f, (_heartGy + dy) * 0.07f, ChTrench + 97, 3) - 0.5f) * 0.3f;
                    if (nx * nx + ny * ny <= 1f + wobble)
                        CarveIfAllowed(_heartGx + dx, _heartGy + dy, TideGrid.Water);
                }
            }
        }

        // Камеры и тоннели нижней бездны проходят вплотную к Сердцу и вскрывают его
        // сбоку — печать Мунлорда так обходится. Заливаем окрестность камнем заново
        // и прорезаем камеру ещё раз: оболочка гарантированно цела, а единственный
        // вход остаётся в записанном прямоугольнике печати
        private void SealHeartShell()
        {
            if (_heartWidth == 0)
                return;

            const int Shell = 9;
            int spanX = _heartWidth / 2 + Shell;
            int spanY = _heartHeight / 2 + Shell;

            for (int dy = -spanY; dy <= spanY; dy++)
            {
                for (int dx = -spanX; dx <= spanX; dx++)
                {
                    int gx = _heartGx + dx;
                    int gy = _heartGy + dy;
                    if (_grid.Get(gx, gy) == TideGrid.Untouched || InsideSealRect(gx, gy))
                        continue;
                    _grid.Set(gx, gy, TideGrid.Stone);
                }
            }

            CarveHeartCavity();
        }
    }
}
