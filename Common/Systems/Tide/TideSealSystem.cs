using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using SoA.Common.Graphics.Atmosphere;
using SoA.Content.Tiles.Furniture;
using SoA.Content.Tiles.Lighthouse;
using SoA.Content.Tiles.Shrines;
using SoA.Content.Biomes;

namespace SoA.Common.Systems
{
    // Печати прилива: каждая ступень держит проход в следующую зону. Когда босс ступени
    // убит, на замке проступает трещина — печать готова: стоит игроку подойти к ней (или
    // ПКМ по замку), сервер запускает ритуал (сцену у всех клиентов ведёт TideSealCinematic)
    // и по его окончании убирает мембрану, а сам замок исчезает с треском.
    //
    // Мембрану ставит генератор, снимает эта система — и только она, поэтому вся работа
    // с миром идёт под проверкой netMode: клиент тайлы не трогает, ему прилетает
    // готовый квадрат от сервера
    public class TideSealSystem : ModSystem
    {
        private const int CheckIntervalTicks = 60;   // условия меняются раз в бой, чаще проверять незачем
        private const int MaxSteps = 4;
        private const float ActivationRangePx = 30f * 16f;
        private const float ApproachRangePx = 14f * 16f; // подошёл к готовой печати — она ломается сама
        private const int ApproachCheckTicks = 10;

        // Длина ритуала от активации до снятия мембраны. Сцена клиента живёт по тем же тикам
        public const int RitualTicks = 180;

        // Битовые маски: ступень N — бит (N-1)
        private static int _openedMask;
        private static int _crackAnnouncedMask;   // о трещине уже сказали в чат, только сервер
        private static int _shrineMask;           // под снятой печатью стоит святилище жемчужины
        private static int _tickCounter;
        private static int _ritualStep;
        private static int _ritualTimer;

        public static bool IsOpened(int step) => (_openedMask & (1 << (step - 1))) != 0;

        // Босс ступени убит, но печать ещё не снята — на замке трещина, его можно активировать
        public static bool IsReady(int step) => IsStepCleared(step) && !IsOpened(step);

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

        // Старшая ступень среди печатей, оставшихся выше тайла. 0 — над ним ни одной.
        // Низ печати, а не верх: стоя в самом проёме, игрок ещё не спустился
        public static int SealsAbove(int tileY)
        {
            int step = 0;
            foreach (TideSealSite site in TideOfShadowsWorldData.SealSites)
            {
                if (tileY > site.Y + site.Height && site.Step > step)
                    step = site.Step;
            }
            return step;
        }

        public static bool TryGetSite(int step, out TideSealSite site)
        {
            foreach (TideSealSite candidate in TideOfShadowsWorldData.SealSites)
            {
                if (candidate.Step == step)
                {
                    site = candidate;
                    return true;
                }
            }
            site = default;
            return false;
        }

        public override void ClearWorld()
        {
            _openedMask = 0;
            _crackAnnouncedMask = 0;
            _shrineMask = 0;
            _tickCounter = 0;
            _ritualStep = 0;
            _ritualTimer = 0;
        }

        public override void SaveWorldData(TagCompound tag)
        {
            tag["tideSealsOpened"] = _openedMask;
            tag["tideSealsCracked"] = _crackAnnouncedMask;
            tag["tidePearlShrines"] = _shrineMask;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            _openedMask = tag.GetInt("tideSealsOpened");
            // Миры до ручной активации: о снятых печатях объявлять уже нечего
            _crackAnnouncedMask = tag.ContainsKey("tideSealsCracked") ? tag.GetInt("tideSealsCracked") : _openedMask;
            _shrineMask = tag.GetInt("tidePearlShrines");
        }

        public override void NetSend(BinaryWriter writer) => writer.Write((byte)_openedMask);

        public override void NetReceive(BinaryReader reader) => _openedMask = reader.ReadByte();

        public override void PostUpdateWorld()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            if (_ritualStep != 0 && --_ritualTimer <= 0)
                FinishRitual();

            if (_ritualStep == 0 && _tickCounter % ApproachCheckTicks == 0)
                CheckApproach();

            if (++_tickCounter < CheckIntervalTicks)
                return;
            _tickCounter = 0;

            for (int step = 1; step <= MaxSteps; step++)
            {
                int bit = 1 << (step - 1);
                // Святилище ставится при снятии печати; здесь — догоняем миры,
                // где печать сняли до появления святилищ
                if (IsOpened(step) && (_shrineMask & bit) == 0)
                    RaiseShrine(step);

                if ((_crackAnnouncedMask & bit) != 0 || !IsReady(step))
                    continue;

                _crackAnnouncedMask |= bit;
                Broadcast("Mods.SoA.Misc.SealCracked");
            }
        }

        // Игрок подошёл к готовой печати — ритуал начинается без ПКМ
        private static void CheckApproach()
        {
            for (int step = 1; step <= MaxSteps; step++)
            {
                if (!IsReady(step) || !TryGetSite(step, out TideSealSite site))
                    continue;
                foreach (Player player in Main.ActivePlayers)
                {
                    if (player.dead || Vector2.DistanceSquared(player.Center, site.LockCenter) > ApproachRangePx * ApproachRangePx)
                        continue;
                    TryBeginRitual(step, player.whoAmI);
                    return;
                }
            }
        }

        // Одна попытка на печать: если места в шахте не нашлось, повторять каждую секунду незачем
        private static void RaiseShrine(int step)
        {
            _shrineMask |= 1 << (step - 1);
            if (TryGetSite(step, out TideSealSite site))
                DepthPearlShrine_tile.TryRaise(site);
        }

        #region Активация

        // Клиентская сторона ПКМ по замку: в одиночной игре ритуал стартует сразу,
        // в мультиплеере решает сервер
        public static void RequestActivation(int step)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                ModPacket packet = ModContent.GetInstance<SoA>().GetPacket();
                packet.Write((byte)SoAPacketType.SealActivate);
                packet.Write((byte)step);
                packet.Send();
                return;
            }

            TryBeginRitual(step, Main.myPlayer);
        }

        public static void HandlePacket(SoAPacketType type, BinaryReader reader, int whoAmI)
        {
            int step = reader.ReadByte();
            switch (type)
            {
                case SoAPacketType.SealActivate when Main.netMode == NetmodeID.Server:
                    TryBeginRitual(step, whoAmI);
                    break;
                case SoAPacketType.SealRitual when Main.netMode == NetmodeID.MultiplayerClient:
                    TideSealCinematic.Start(step);
                    break;
            }
        }

        private static void TryBeginRitual(int step, int playerIndex)
        {
            if (_ritualStep != 0 || !IsReady(step) || !TryGetSite(step, out TideSealSite site))
                return;

            // Сервер не верит клиенту на слово: активировать можно только стоя у замка
            Player player = Main.player[playerIndex];
            if (!player.active || player.dead ||
                Vector2.DistanceSquared(player.Center, site.LockCenter) > ActivationRangePx * ActivationRangePx)
                return;

            _ritualStep = step;
            _ritualTimer = RitualTicks;

            if (Main.netMode == NetmodeID.Server)
            {
                ModPacket packet = ModContent.GetInstance<SoA>().GetPacket();
                packet.Write((byte)SoAPacketType.SealRitual);
                packet.Write((byte)step);
                packet.Send();
            }
            else
            {
                TideSealCinematic.Start(step);
            }
        }

        private static void FinishRitual()
        {
            int step = _ritualStep;
            _ritualStep = 0;

            OpenSeal(step);
            _openedMask |= 1 << (step - 1);
            RaiseShrine(step);
            Broadcast("Mods.SoA.Misc.SealBroken" + step);

            // Маска печатей едет в данных мира — без этого клиенты увидят снятую мембрану,
            // но замок у них останется «готовым»
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.WorldData);
        }

        #endregion

        // Снятие печати: мембрана и сам замок исчезают, проход заливается водой обратно
        private static void OpenSeal(int step)
        {
            ushort barrierType = (ushort)ModContent.TileType<TideSealBarrier_tile>();
            ushort sealType = (ushort)ModContent.TileType<TideSeal_tile>();

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

                        if (tile.TileType == barrierType || tile.TileType == sealType)
                        {
                            tile.HasTile = false;
                            // Печати стоят глубоко под водой: пустой карман тут же
                            // засосало бы течением, проще залить сразу
                            tile.LiquidType = LiquidID.Water;
                            tile.LiquidAmount = 255;
                        }
                    }
                }

                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendTileSquare(-1, site.X, site.Y, site.Width, site.Height);
            }
        }

        // Только для проверки сцены (команда /soaseal): печать снова считается не снятой,
        // замок стоит на месте. Мембрану не возвращает — её умеет ставить только генератор
        public static void ResetForTesting(int step)
        {
            int bit = 1 << (step - 1);
            _openedMask &= ~bit;
            _crackAnnouncedMask &= ~bit;
            _ritualStep = 0;

            ushort sealType = (ushort)ModContent.TileType<TideSeal_tile>();
            foreach (TideSealSite site in TideOfShadowsWorldData.SealSites)
            {
                if (site.Step != step)
                    continue;

                // Снятый замок теперь исчезает — ставим его заново в закрытом виде
                Point origin = site.LockOrigin;
                for (int dx = 0; dx < TideSeal_tile.SizeInTiles; dx++)
                {
                    for (int dy = 0; dy < TideSeal_tile.SizeInTiles; dy++)
                    {
                        Tile tile = Main.tile[origin.X + dx, origin.Y + dy];
                        tile.ResetToType(sealType);
                        tile.TileFrameX = (short)(dx * TideSeal_tile.FrameStep);
                        tile.TileFrameY = (short)(dy * TideSeal_tile.FrameStep);
                    }
                }
            }
        }

        private static void Broadcast(string key)
        {
            LocalizedText text = Language.GetText(key);
            var color = new Color(150, 200, 255);

            if (Main.netMode == NetmodeID.Server)
                Terraria.Chat.ChatHelper.BroadcastChatMessage(text.ToNetworkText(), color);
            else
                Main.NewText(text.Value, color);
        }
    }
}
