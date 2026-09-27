using System;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Common.Players
{
    // Общая зарядка оружия «зажал — отпустил» (Инферно-сюрикен, Коса огненной бури).
    // Владелец считает тики зажатия и стреляет при отпускании; факт зарядки уходит по сети,
    // и остальные клиенты ведут свой счётчик. Раньше chargeTime жил только у владельца,
    // поэтому замах (руки, слой с оружием) видел лишь сам игрок.
    public abstract class ChargedWeaponPlayer : ModPlayer
    {
        public int chargeTime;
        public int cooldownTimer;   // только у владельца: пока идёт, зарядка не начинается

        private bool _charging;
        private bool _wasCharging;
        private int _previousChargeTime;
        private byte _sentExtraState;   // заполняется только у копии для SendClientChanges

        protected abstract int WeaponType { get; }
        protected abstract int MaxChargeTicks { get; }
        protected abstract SoAPacketType PacketType { get; }
        protected virtual int MinChargeTicks => 1;

        public bool HoldingWeapon => Player.HeldItem.type == WeaponType;

        // Лишний байт состояния, который тоже видят другие клиенты (например, стаки Жара)
        protected virtual byte ExtraSyncState { get => 0; set { } }

        // Зажата ли атака — спрашивается только у владельца
        protected abstract bool ChargeInputHeld();

        // Каждый тик зарядки на всех клиентах (кроме сервера): свет, частицы, звуки порогов
        protected virtual void OnChargeTick() { }

        // Владелец отпустил атаку с зарядом не меньше MinChargeTicks; chargeTime ещё не сброшен
        protected abstract void OnRelease();

        // Заряд только что дошёл до ticks (а не стоит на нём с прошлого тика)
        protected bool JustReached(int ticks) => chargeTime >= ticks && _previousChargeTime < ticks;

        public override void PostUpdate()
        {
            bool isOwner = Player.whoAmI == Main.myPlayer;
            if (isOwner)
            {
                if (cooldownTimer > 0)
                    cooldownTimer--;
                _charging = cooldownTimer == 0 && HoldingWeapon && !Player.CCed && !Player.noItems && ChargeInputHeld();
            }
            else if (!HoldingWeapon)
            {
                _charging = false;
            }

            _previousChargeTime = chargeTime;
            if (_charging)
            {
                chargeTime = Math.Min(chargeTime + 1, MaxChargeTicks);
                _wasCharging = true;
                if (!Main.dedServ)
                    OnChargeTick();
                return;
            }

            // Сменил оружие посреди зарядки — бросок отменяется: HeldItem уже чужой
            if (_wasCharging && isOwner && chargeTime >= MinChargeTicks && HoldingWeapon)
                OnRelease();
            chargeTime = 0;
            _wasCharging = false;
        }

        // Поворот руки задан для взгляда вправо, влево — зеркально
        public static float MirrorForDirection(float rotation, int direction) => direction == -1 ? -rotation : rotation;

        #region Сеть

        public override void CopyClientState(ModPlayer targetCopy)
        {
            var copy = (ChargedWeaponPlayer)targetCopy;
            copy._charging = _charging;
            copy._sentExtraState = ExtraSyncState;
        }

        public override void SendClientChanges(ModPlayer clientPlayer)
        {
            var sent = (ChargedWeaponPlayer)clientPlayer;
            if (sent._charging != _charging || sent._sentExtraState != ExtraSyncState)
                SendState(-1, Main.myPlayer);
        }

        public override void SyncPlayer(int toWho, int fromWho, bool newPlayer) => SendState(toWho, fromWho);

        private void SendState(int toWho, int fromWho)
        {
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte)PacketType);
            packet.Write((byte)Player.whoAmI);
            packet.Write(_charging);
            packet.Write(ExtraSyncState);
            packet.Send(toWho, fromWho);
        }

        public static void ReceiveState<T>(BinaryReader reader, int sender) where T : ChargedWeaponPlayer
        {
            int playerIndex = reader.ReadByte();
            bool charging = reader.ReadBoolean();
            byte extra = reader.ReadByte();

            // Сервер не верит клиенту на слово, чьё это состояние
            if (Main.netMode == NetmodeID.Server)
                playerIndex = sender;

            ChargedWeaponPlayer mp = Main.player[playerIndex].GetModPlayer<T>();
            mp._charging = charging;
            mp.ExtraSyncState = extra;

            if (Main.netMode == NetmodeID.Server)
                mp.SendState(-1, playerIndex);
        }

        #endregion
    }
}
