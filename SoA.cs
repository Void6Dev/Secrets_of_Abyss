using System.IO;
using Terraria.ModLoader;
using SoA.Common.Players;
using SoA.Common.Systems;
using SoA.Content.NPCs.Bosses.KingCrab;
using SoA.Content.Tiles.Furniture;
using SoA.Content.Tiles.Lighthouse;
using SoA.Content.Tiles.Shrines;

namespace SoA
{
    // Первый байт каждого пакета мода
    public enum SoAPacketType : byte
    {
        FlinxAura,
        SealActivate,   // клиент -> сервер: игрок активировал готовую печать
        SealRitual,     // сервер -> клиенты: начался ритуал, играть сцену
        DepthPearls,
        LighthouseState,   // клиент -> сервер: игрок повернул луч цепью или щёлкнул лампу
        AltarPearl,        // клиент -> сервер -> клиенты: жемчужина легла в королевский алтарь
        CrabAttackLanded,  // клиент -> сервер: атака короля достала игрока (для оглушения на промахе)
    }

    public class SoA : Mod
    {
        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            var type = (SoAPacketType)reader.ReadByte();
            switch (type)
            {
                case SoAPacketType.FlinxAura:
                    FlinxArmorSetBonusPlayer.ReceiveAura(reader, whoAmI);
                    break;
                case SoAPacketType.SealActivate:
                case SoAPacketType.SealRitual:
                    TideSealSystem.HandlePacket(type, reader, whoAmI);
                    break;
                case SoAPacketType.DepthPearls:
                    TidePressurePlayer.ReceivePearls(reader, whoAmI);
                    break;
                case SoAPacketType.LighthouseState:
                    LighthouseLampEntity.ReceiveState(reader, whoAmI);
                    break;
                case SoAPacketType.AltarPearl:
                    CrabRoyalAltar_tile.ReceivePearl(reader, whoAmI);
                    break;
                case SoAPacketType.CrabAttackLanded:
                    King_crab.ReportAttackLanded();
                    break;
                default:
                    Logger.Warn($"Unknown packet type {type}");
                    break;
            }
        }
    }
}
