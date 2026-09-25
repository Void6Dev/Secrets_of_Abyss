using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.WorldBuilding;
using Terraria.ModLoader;
using SoA.Common.Utils;
using SoA.Content.Items.Accessories;
using SoA.Content.Items.BossSummons;
using SoA.Content.Items.Materials;
using SoA.Content.Tiles.Nature;

namespace SoA.Common.Systems
{
    // Затонувший галеон Прилива Теней. Раньше корабль строился процедурно;
    // теперь стамп готовой постройки из Assets/Structures/sunken_ship.str.
    // По умолчанию нос смотрит вправо; если океан слева от центра мира —
    // постройка отражается по горизонтали. Сундуки структура не хранит,
    // поэтому после установки регистрируем их заново и наполняем кодом.
    public class SunkenShipPass : GenPass
    {
        private const string StructureName = "sunken_ship";
        private const int BuryDepth = 5;        // нижние ряды корпуса утоплены в дно
        private const int MinWaterDepth = 20;   // столько воды нужно, чтобы корабль ушёл под воду
        private const int ScanStart = 90;       // от края мира вглубь океана
        private const int ScanEnd = 360;
        private const int FallbackOffset = 220;

        private readonly Mod _mod;

        public SunkenShipPass(string name, float loadWeight, Mod mod) : base(name, loadWeight)
        {
            _mod = mod;
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
        {
            progress.Message = "Sinking a forgotten galleon";

            if (!StructureIO.TryGetSize(_mod, StructureName, out int w, out int h))
                return;

            int jungleDir = -GenVars.dungeonSide;    // океан со стороны джунглей
            bool mirror = jungleDir == -1;           // океан слева → отражаем корабль
            int half = w / 2;

            int bestCx = -1, bestFloor = 0, bestFit = int.MaxValue;

            // Генератор биома специально ровняет террасу зоны 2 под корпус.
            // Свой скан оставляем на случай, если биом не сгенерировался
            if (TideOcean.TideOceanPass.ShipAnchorX != 0)
            {
                bestCx = TideOcean.TideOceanPass.ShipAnchorX;
                bestFloor = TideOcean.TideOceanPass.ShipAnchorFloorY;
            }

            // Скан океана: ищем ровную площадку шириной с корпус
            for (int offset = ScanStart + half; bestCx == -1 && offset <= ScanEnd; offset += 4)
            {
                int cx = jungleDir == -1 ? offset : Main.maxTilesX - offset;
                if (!EvaluateSite(cx, half, out int floorY, out int fit))
                    continue;
                if (fit < bestFit)
                {
                    bestFit = fit;
                    bestCx = cx;
                    bestFloor = floorY;
                }
                if (bestFit <= 6)
                    break;
            }

            // Запасная позиция, если ровной площадки не нашлось
            if (bestCx == -1)
            {
                bestCx = jungleDir == -1 ? FallbackOffset : Main.maxTilesX - FallbackOffset;
                if (!ColumnWaterFloor(bestCx, out _, out bestFloor))
                    return; // воды нет вовсе — корабль тонуть негде
            }

            int topLeftX = bestCx - half;
            int topLeftY = bestFloor + BuryDepth - (h - 1);

            FillSeabedUnder(topLeftX, w, topLeftY + h);
            if (!StructureIO.Place(_mod, StructureName, topLeftX, topLeftY, mirror))
                return;
            PopulateChests(new Rectangle(topLeftX, topLeftY, w, h));

            WorldGen.RangeFrame(topLeftX - 2, topLeftY - 2, topLeftX + w + 2, topLeftY + h + 2);
        }

        // Ровность дна под всей шириной корпуса + достаточная глубина воды
        private static bool EvaluateSite(int cx, int half, out int floorY, out int fit)
        {
            floorY = 0;
            fit = int.MaxValue;
            int minFloor = int.MaxValue, maxFloor = int.MinValue;
            long sum = 0;
            int count = 0;

            for (int lx = -half + 6; lx <= half - 6; lx += 10)
            {
                int x = cx + lx;
                if (x <= 30 || x >= Main.maxTilesX - 30)
                    return false;
                if (!ColumnWaterFloor(x, out int waterTop, out int floor))
                    return false;
                if (floor - waterTop < MinWaterDepth)
                    return false;

                minFloor = Math.Min(minFloor, floor);
                maxFloor = Math.Max(maxFloor, floor);
                sum += floor;
                count++;
            }

            if (count == 0)
                return false;

            fit = maxFloor - minFloor;
            floorY = (int)(sum / count);
            return true;
        }

        // Верх воды и первый твёрдый пол под ней в колонке
        private static bool ColumnWaterFloor(int x, out int waterTop, out int floor)
        {
            waterTop = -1;
            floor = -1;
            for (int y = 40; y < (int)Main.worldSurface + 120; y++)
            {
                Tile tile = Main.tile[x, y];
                if (waterTop == -1 && tile.LiquidAmount > 100 && tile.LiquidType == LiquidID.Water)
                    waterTop = y;
                if (waterTop != -1 && tile.HasTile && Main.tileSolid[tile.TileType])
                {
                    floor = y;
                    break;
                }
            }
            return waterTop != -1 && floor != -1;
        }

        // Засыпаем пустоты под корпусом до грунта, чтобы корабль не висел над полостью
        private static void FillSeabedUnder(int leftX, int width, int bottomRowY)
        {
            ushort sand = (ushort)ModContent.TileType<Tidesand_tile>();
            ushort stone = (ushort)ModContent.TileType<Tidestone_tile>();
            int maxY = (int)Main.worldSurface + 140;

            for (int x = leftX; x < leftX + width; x++)
            {
                for (int y = bottomRowY; y < maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile && Main.tileSolid[tile.TileType])
                        break;
                    tile.ResetToType(y < bottomRowY + 5 ? sand : stone);
                    tile.LiquidAmount = 0;
                }
            }
        }

        // Структура хранит только тайлы сундуков — регистрируем их заново и наполняем.
        // Первый найденный сундук становится капитанским, остальные — трюмными.
        private static void PopulateChests(Rectangle area)
        {
            bool baitPlaced = false;
            bool captainDone = false;

            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (!tile.HasTile || !Main.tileContainer[tile.TileType])
                        continue;
                    // Только левый-верхний угол сундука
                    if (tile.TileFrameX % 36 != 0 || tile.TileFrameY % 36 != 0)
                        continue;

                    int idx = Chest.CreateChest(x, y);
                    if (idx == -1)
                        idx = Chest.FindChest(x, y);
                    if (idx < 0)
                        continue;

                    if (!captainDone)
                    {
                        FillCaptainChest(Main.chest[idx], ref baitPlaced);
                        captainDone = true;
                    }
                    else
                    {
                        FillHoldChest(Main.chest[idx], ref baitPlaced);
                    }
                }
            }
        }

        private static void FillCaptainChest(Chest chest, ref bool baitPlaced)
        {
            int slot = 0;
            void Add(int type, int stack)
            {
                if (slot >= Chest.maxItems || type <= 0)
                    return;
                chest.item[slot].SetDefaults(type);
                chest.item[slot].stack = stack;
                slot++;
            }

            Add(ModContent.ItemType<TideskimmerBoots>(), 1);
            Add(ModContent.ItemType<CrabRoyalBait>(), 1);
            baitPlaced = true;
            Add(ItemID.GoldCoin, WorldGen.genRand.Next(5, 12));
            Add(ModContent.ItemType<Ichthyofang>(), WorldGen.genRand.Next(1, 6));
            Add(ItemID.GillsPotion, WorldGen.genRand.Next(2, 4));
            Add(ItemID.SwiftnessPotion, WorldGen.genRand.Next(2, 4));
            Add(ItemID.PirateHat, 1);
            Add(ItemID.Cannonball, WorldGen.genRand.Next(1));
        }

        private static void FillHoldChest(Chest chest, ref bool baitPlaced)
        {
            int slot = 0;
            void Add(int type, int stack)
            {
                if (slot >= Chest.maxItems || type <= 0)
                    return;
                chest.item[slot].SetDefaults(type);
                chest.item[slot].stack = stack;
                slot++;
            }

            // Гарантируем призывную приманку хотя бы в одном сундуке
            if (!baitPlaced)
            {
                Add(ModContent.ItemType<CrabRoyalBait>(), 1);
                baitPlaced = true;
            }
            Add(ItemID.SilverCoin, WorldGen.genRand.Next(20, 90));
            Add(ModContent.ItemType<Ichthyofang>(), WorldGen.genRand.Next(1, 4));
            Add(ItemID.Cannonball, WorldGen.genRand.Next(2, 6));
            if (WorldGen.genRand.NextBool())
                Add(ItemID.StickyGlowstick, WorldGen.genRand.Next(8, 20));
        }
    }
}
