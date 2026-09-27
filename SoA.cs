using System.IO;
using Terraria.ModLoader;
using SoA.Common.Players;
using SoA.Content.Items.Weapons;

namespace SoA
{
    // Первый байт каждого пакета мода
    public enum SoAPacketType : byte
    {
        ShurikenCharge,
        ScytheCharge,
    }

    // Please read https://github.com/tModLoader/tModLoader/wiki/Basic-tModLoader-Modding-Guide#mod-skeleton-contents for more information about the various files in a mod.
    public class SoA : Mod
    {
        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            var type = (SoAPacketType)reader.ReadByte();
            switch (type)
            {
                case SoAPacketType.ShurikenCharge:
                    ChargedWeaponPlayer.ReceiveState<ShurikenChargePlayer>(reader, whoAmI);
                    break;
                case SoAPacketType.ScytheCharge:
                    ChargedWeaponPlayer.ReceiveState<ScytheChargePlayer>(reader, whoAmI);
                    break;
                default:
                    Logger.Warn($"Unknown packet type {type}");
                    break;
            }
        }
    }
}
