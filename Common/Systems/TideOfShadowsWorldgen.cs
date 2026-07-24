using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using System.Collections.Generic;
using Terraria.IO;
using SoA.Content.Tiles.Nature;
using SoA.Content.Worldgen;

namespace SoA.Common.Systems
{
    // Регистрирует оба генпасса Прилива Теней в строгом порядке:
    // сначала конверсия океана, затем затонувший галеон поверх готового биома
    public class TideOfShadowsWorldgen : ModSystem
    {
        public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
        {
            int cleanupIndex = tasks.FindIndex(genpass => genpass.Name.Equals("Final Cleanup"));
            if (cleanupIndex == -1)
                return;

            tasks.Insert(cleanupIndex + 1, new TideOceanConversionPass("SoA Tide of Shadows Ocean", 90f));
            tasks.Insert(cleanupIndex + 2, new SunkenShipPass("SoA Sunken Ship", 180f, Mod));
        }
    }

    // Прилив Теней: океан со стороны джунглей, превращённый в скальную котловину
    // с озером посередине. Стадии: конверсия песков в Tidesand, скальное основание
    // из Tidestone, утёс-стена у края мира, скальные останцы из воды,
    // теневые дюны на берегу и декор дна (кораллы, ракушки, наносы)
    public class TideOceanConversionPass : GenPass
    {
        // Дюны тянутся дальше границы пляжа — захватываем с запасом
        private const int InlandMargin = 50;
        private const int TopY = 40;

        // --- Скальная котловина ---
        private const int RimWidth = 26;        // ширина утёса у края мира
        private const int RimHeightAboveWater = 18;
        private const int RockBedTop = 4;       // глубина, с которой песок сменяется камнем
        private const int RockBedBottom = 22;
        private const int SteepSlope = 3;       // перепад дна, на котором проступает скала

        // Останцы стоят между утёсом и зоной корабля (корабль ищет место от ~131 тайла)
        private static readonly int[] SeaStackOffsets = { 36, 50, 64 };

        private int _startX, _endX, _maxY;
        private int _edgeX;   // x у края мира
        private int _seaDir;  // направление от края мира вглубь суши

        public TideOceanConversionPass(string name, float loadWeight) : base(name, loadWeight)
        {
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "Shadow tides claim the coast";

            int jungleDir = -GenVars.dungeonSide; // джунгли напротив данжа
            TideOfShadowsWorldData.OceanSide = jungleDir; // флаг мира для активации биома

            if (jungleDir == -1)
            {
                _startX = 40;
                _endX = GenVars.leftBeachEnd + InlandMargin;
                _edgeX = 40;
                _seaDir = 1;
            }
            else
            {
                _startX = GenVars.rightBeachStart - InlandMargin;
                _endX = Main.maxTilesX - 40;
                _edgeX = Main.maxTilesX - 40;
                _seaDir = -1;
            }
            _maxY = (int)Main.worldSurface + 120;

            ConvertSand();
            BuildRockBed();

            int waterTop = FindOceanWaterTop();
            if (waterTop != -1)
            {
                SculptRockRim(waterTop);
                RaiseSeaStacks(waterTop);
            }

            SculptShadowDunes();
            DecorateSeabed();

            WorldGen.RangeFrame(_startX, TopY, _endX, _maxY);
        }

        // Вся песчаная семья прибрежья становится Tidesand
        private void ConvertSand()
        {
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();

            for (int x = _startX; x <= _endX; x++)
            {
                for (int y = TopY; y <= _maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile)
                        continue;
                    if (tile.TileType == TileID.Sand || tile.TileType == TileID.HardenedSand
                        || tile.TileType == TileID.Sandstone)
                        tile.TileType = tidesandType;
                }
            }
        }

        // Уровень зеркала воды: минимальный по нескольким колонкам котловины
        private int FindOceanWaterTop()
        {
            int best = -1;
            for (int off = 60; off <= 240; off += 20)
            {
                int x = _edgeX + off * _seaDir;
                if (x <= _startX || x >= _endX)
                    continue;
                for (int y = TopY; y < _maxY; y++)
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

        // Под слоем Tidesand — скальное основание; на крутых перепадах дна скала выходит наружу
        private void BuildRockBed()
        {
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();
            int prevFloor = -1;

            for (int x = _startX + 1; x <= _endX - 1; x++)
            {
                int floorY = FindUnderwaterFloor(x);
                if (floorY == -1)
                {
                    prevFloor = -1;
                    continue;
                }

                for (int y = floorY + RockBedTop; y <= floorY + RockBedBottom && y < _maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile && Main.tileSolid[tile.TileType])
                        tile.TileType = tidestoneType;
                }

                // Крутой уступ — скала проступает сквозь песок до самой поверхности дна
                if (prevFloor != -1 && Math.Abs(floorY - prevFloor) >= SteepSlope)
                {
                    for (int y = floorY; y < floorY + RockBedTop; y++)
                    {
                        Tile tile = Main.tile[x, y];
                        if (tile.HasTile && Main.tileSolid[tile.TileType])
                            tile.TileType = tidestoneType;
                    }
                }
                prevFloor = floorY;
            }
        }

        // Утёс-стена у края мира: замыкает котловину, превращая океан в горное озеро
        private void SculptRockRim(int waterTop)
        {
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();

            for (int off = 0; off < RimWidth; off++)
            {
                int x = _edgeX + off * _seaDir;
                if (x <= 20 || x >= Main.maxTilesX - 20)
                    continue;

                // Гребень выше всего у края мира и спадает под воду к центру озера
                float t = off / (float)RimWidth;
                double noise = 3.0 * Math.Sin(off * 0.7) + 2.0 * Math.Sin(off * 0.23 + 1.7);
                int crestY = waterTop - (int)((1f - t) * RimHeightAboveWater) - 2 + (int)noise;
                if (crestY < TopY + 10)
                    crestY = TopY + 10;

                int floorY = FindSurface(x);
                if (floorY == -1)
                    continue;

                for (int y = Math.Min(crestY, floorY); y <= floorY + 8 && y < _maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    tile.ResetToType(tidestoneType);
                    tile.LiquidAmount = 0;
                }
            }
        }

        // Скальные останцы, торчащие из озера у подножия утёса
        private void RaiseSeaStacks(int waterTop)
        {
            ushort tidestoneType = (ushort)ModContent.TileType<Tidestone_tile>();

            foreach (int offset in SeaStackOffsets)
            {
                int width = WorldGen.genRand.Next(2, 5);
                int baseTop = waterTop + WorldGen.genRand.Next(-4, 3); // чуть выше или ниже зеркала
                for (int dx = 0; dx < width; dx++)
                {
                    int x = _edgeX + (offset + dx) * _seaDir;
                    if (x <= _startX || x >= _endX)
                        continue;
                    int floorY = FindUnderwaterFloor(x);
                    if (floorY == -1)
                        continue;

                    // Рваный верх: края столба ниже середины
                    int topY = baseTop + Math.Abs(dx - width / 2) + WorldGen.genRand.Next(2);
                    for (int y = topY; y <= floorY && y < _maxY; y++)
                    {
                        Tile tile = Main.tile[x, y];
                        tile.ResetToType(tidestoneType);
                        tile.LiquidAmount = 0;
                    }
                }
            }
        }

        // Сухой берег: волнистые гребни дюн, как будто прилив застыл тенью
        private void SculptShadowDunes()
        {
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();

            for (int x = _startX + 5; x <= _endX - 5; x++)
            {
                int surfaceY = FindSurface(x);
                if (surfaceY == -1)
                    continue;

                Tile surface = Main.tile[x, surfaceY];
                if (surface.TileType != tidesandType)
                    continue;
                // Только сухой берег — под водой дюны не насыпаем
                if (Main.tile[x, surfaceY - 1].LiquidAmount > 0)
                    continue;

                double wave = 1.6 + 1.7 * Math.Sin(x * 0.13) + 1.2 * Math.Sin(x * 0.043 + 2.0);
                int crest = Math.Clamp((int)wave, 0, 4);
                for (int h = 1; h <= crest; h++)
                {
                    Tile above = Main.tile[x, surfaceY - h];
                    if (above.HasTile || above.LiquidAmount > 0)
                        break;
                    above.ResetToType(tidesandType);
                }
            }
        }

        // Морское дно: тёмные кораллы, светящиеся ракушки и песчаные наносы
        private void DecorateSeabed()
        {
            ushort tidesandType = (ushort)ModContent.TileType<Tidesand_tile>();
            int coralType = ModContent.TileType<ShadowCoral_tile>();
            int glowShellType = ModContent.TileType<GlowShell_tile>();

            for (int x = _startX + 3; x <= _endX - 3; x++)
            {
                int floorY = FindUnderwaterFloor(x);
                if (floorY == -1 || Main.tile[x, floorY].TileType != tidesandType)
                    continue;

                float roll = WorldGen.genRand.NextFloat();
                if (roll < 0.10f)
                {
                    WorldGen.PlaceObject(x, floorY - 1, coralType, true, WorldGen.genRand.Next(3));
                }
                else if (roll < 0.17f)
                {
                    WorldGen.PlaceObject(x, floorY - 1, glowShellType, true, WorldGen.genRand.Next(3));
                }
                else if (roll < 0.28f)
                {
                    // Низкий нанос: дно перестаёт быть плоской линией
                    int mound = WorldGen.genRand.Next(1, 3);
                    for (int h = 1; h <= mound; h++)
                    {
                        Tile above = Main.tile[x, floorY - h];
                        if (above.HasTile)
                            break;
                        above.ResetToType(tidesandType);
                        above.LiquidAmount = 0;
                    }
                }
            }
        }

        private int FindSurface(int x)
        {
            for (int y = TopY; y < _maxY; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    return y;
            }
            return -1;
        }

        private static int FindUnderwaterFloor(int x)
        {
            bool sawWater = false;
            for (int y = TopY; y < (int)Main.worldSurface + 100; y++)
            {
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile && tile.LiquidAmount > 100 && tile.LiquidType == LiquidID.Water)
                    sawWater = true;
                if (tile.HasTile && Main.tileSolid[tile.TileType])
                    return sawWater ? y : -1;
            }
            return -1;
        }
    }
}
