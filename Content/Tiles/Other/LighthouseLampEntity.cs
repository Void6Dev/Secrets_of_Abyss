using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace SoA.Content.Tiles.Other
{
    // Состояние лампы маяка: куда смотрит луч и горит ли она. Живёт в мире,
    // сохраняется и ходит по сети. Менять его может любой игрок цепью или кликом
    // по лампе: клиент правит у себя сразу, шлёт серверу, сервер раздаёт остальным
    public class LighthouseLampEntity : ModTileEntity
    {
        public const int SizeInTiles = 2;

        // Куда смотрит луч, радианы. NaN — ещё не трогали: тогда луч смотрит
        // в сторону ближнего края мира, то есть в море
        public float Angle = float.NaN;
        public bool Lit = true;

        // Угол, который рисуется. Догоняет Angle плавно, чтобы чужой луч
        // не прыгал между сетевыми пакетами. Не сохраняется
        public float DisplayAngle = float.NaN;

        public float BeamAngle => float.IsNaN(Angle) ? SeawardAngle : Angle;

        private float SeawardAngle => Position.X < Main.maxTilesX / 2 ? MathHelper.Pi : 0f;

        public Vector2 Center => new((Position.X + SizeInTiles / 2f) * 16f, (Position.Y + SizeInTiles / 2f) * 16f);

        public override bool IsTileValidForEntity(int x, int y)
        {
            Tile tile = Main.tile[x, y];
            return tile.HasTile && tile.TileType == ModContent.TileType<LighthouseLamp_tile>()
                && tile.TileFrameX == 0 && tile.TileFrameY == 0;
        }

        // Лампа по любому из её тайлов
        public static bool TryGetAt(int i, int j, out LighthouseLampEntity lamp)
        {
            lamp = null;
            Tile tile = Main.tile[i, j];
            if (!tile.HasTile || tile.TileType != ModContent.TileType<LighthouseLamp_tile>())
                return false;

            var topLeft = new Point16(i - tile.TileFrameX / 18 % SizeInTiles, j - tile.TileFrameY / 18 % SizeInTiles);
            if (!TileEntity.ByPosition.TryGetValue(topLeft, out TileEntity entity))
                return false;

            lamp = entity as LighthouseLampEntity;
            return lamp != null;
        }

        public override void SaveData(TagCompound tag)
        {
            if (!float.IsNaN(Angle))
                tag["angle"] = Angle;
            tag["lit"] = Lit;
        }

        public override void LoadData(TagCompound tag)
        {
            Angle = tag.ContainsKey("angle") ? tag.GetFloat("angle") : float.NaN;
            Lit = !tag.ContainsKey("lit") || tag.GetBool("lit");
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(Angle);
            writer.Write(Lit);
        }

        public override void NetReceive(BinaryReader reader)
        {
            Angle = reader.ReadSingle();
            Lit = reader.ReadBoolean();
        }

        #region Сеть

        // Клиент поменял лампу у себя — отдаёт серверу. В одиночной игре слать некому
        public void SendState()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = Mod.GetPacket();
            packet.Write((byte)SoAPacketType.LighthouseState);
            packet.Write(ID);
            packet.Write(Angle);
            packet.Write(Lit);
            packet.Send();
        }

        // Сервер принимает состояние и раздаёт остальным. Отправителю не возвращаем:
        // он держит цепь, и эхо с отставанием дёргало бы его луч назад
        public static void ReceiveState(BinaryReader reader, int sender)
        {
            int id = reader.ReadInt32();
            float angle = reader.ReadSingle();
            bool lit = reader.ReadBoolean();

            if (Main.netMode != NetmodeID.Server)
                return;
            if (!TileEntity.ByID.TryGetValue(id, out TileEntity entity) || entity is not LighthouseLampEntity lamp)
                return;

            lamp.Angle = float.IsNaN(angle) ? float.NaN : MathHelper.WrapAngle(angle);
            lamp.Lit = lit;
            NetMessage.SendData(MessageID.TileEntitySharing, -1, sender, null, lamp.ID, lamp.Position.X, lamp.Position.Y);
        }

        #endregion
    }
}
