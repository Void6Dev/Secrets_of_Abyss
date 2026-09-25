using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using SoA.Content.Tiles.Other;
using SoA.Content.Worldgen;

namespace SoA.Common.Systems
{
    // Печати прилива: каждая ступень держит проход в следующую зону, пока не убит
    // её босс. Мембрану ставит генератор, снимает эта система — и только она,
    // поэтому вся работа с миром здесь идёт под проверкой netMode: клиент тайлы
    // не трогает, ему прилетает готовый квадрат от сервера.
    public class TideSealSystem : ModSystem
    {
        private const int CheckIntervalTicks = 60;   // условия меняются раз в бой, чаще проверять незачем
        private const int MaxSteps = 4;

        // Битовая маска снятых печатей: ступень N — бит (N-1)
        private static int _openedMask;
        private static int _tickCounter;

        public static bool IsOpened(int step) => (_openedMask & (1 << (step - 1))) != 0;

        // Условие ступени. Порядок жёсткий: краб -> Стена Плоти -> Плантера -> Мунлорд
        public static bool IsStepCleared(int step) => step switch
        {
            1 => DownedBossSystem.downedKingCrab,
            2 => Main.hardMode,
            3 => NPC.downedPlantBoss,
            4 => NPC.downedMoonlord,
            _ => true
        };

        // Ступень печати, накрывающей тайл. 0 — тайл не в печати
        public static int StepAt(int tileX, int tileY)
        {
            foreach (TideSealSite site in TideOfShadowsWorldData.SealSites)
            {
                if (tileX >= site.X && tileX < site.X + site.Width &&
                    tileY >= site.Y && tileY < site.Y + site.Height)
                    return site.Step;
            }
            return 0;
        }

        public override void ClearWorld()
        {
            _openedMask = 0;
            _tickCounter = 0;
        }

        public override void SaveWorldData(TagCompound tag) => tag["tideSealsOpened"] = _openedMask;

        public override void LoadWorldData(TagCompound tag) => _openedMask = tag.GetInt("tideSealsOpened");

        public override void NetSend(BinaryWriter writer) => writer.Write((byte)_openedMask);

        public override void NetReceive(BinaryReader reader) => _openedMask = reader.ReadByte();

        public override void PostUpdateWorld()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            if (++_tickCounter < CheckIntervalTicks)
                return;

            _tickCounter = 0;

            for (int step = 1; step <= MaxSteps; step++)
            {
                if (IsOpened(step) || !IsStepCleared(step))
                    continue;

                OpenSeal(step);
                _openedMask |= 1 << (step - 1);
            }
        }

        // Снятие печати: мембрана исчезает, проход заливается водой обратно,
        // замок переводится во второе состояние
        private static void OpenSeal(int step)
        {
            ushort barrierType = (ushort)ModContent.TileType<TideSealBarrier_tile>();
            ushort sealType = (ushort)ModContent.TileType<TideSeal_tile>();
            bool announced = false;

            foreach (TideSealSite site in TideOfShadowsWorldData.SealSites)
            {
                if (site.Step != step)
                    continue;

                for (int x = site.X; x < site.X + site.Width; x++)
                {
                    for (int y = site.Y; y < site.Y + site.Height; y++)
                    {
                        if (!WorldGen.InWorld(x, y, 2))
                            continue;

                        Tile tile = Main.tile[x, y];
                        if (!tile.HasTile)
                            continue;

                        if (tile.TileType == barrierType)
                        {
                            tile.HasTile = false;
                            // Печати стоят глубоко под водой: пустой карман тут же
                            // засосало бы течением, проще залить сразу
                            tile.LiquidType = LiquidID.Water;
                            tile.LiquidAmount = 255;
                        }
                        else if (tile.TileType == sealType && tile.TileFrameX < TideSeal_tile.StyleWidth)
                        {
                            tile.TileFrameX += TideSeal_tile.StyleWidth;
                        }
                    }
                }

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendTileSquare(-1, site.X, site.Y, site.Width, site.Height);

                if (!announced)
                {
                    Announce(step);
                    announced = true;
                }
            }
        }

        private static void Announce(int step)
        {
            LocalizedText text = Language.GetText("Mods.SoA.Misc.SealBroken" + step);

            if (Main.netMode == NetmodeID.Server)
                Terraria.Chat.ChatHelper.BroadcastChatMessage(text.ToNetworkText(), new Microsoft.Xna.Framework.Color(150, 200, 255));
            else
                Main.NewText(text.Value, 150, 200, 255);
        }
    }
}
