using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using SoA.Common.Graphics.Particles;

namespace SoA.Common.Players
{
    // Сет флинксов: снежная аура вокруг игрока. Снежинки на орбите рисует FlinxSnowAuraLayer,
    // здесь — всё, что рождается частицами: снегопад вокруг, иней на мехе, морозное дыхание,
    // снег из-под ног. Двойное нажатие «вниз» (бонус сета) включает и выключает ауру;
    // выбор сохраняется в персонаже и уходит по сети — чужую ауру видно, только если она включена
    public class FlinxArmorSetBonusPlayer : ModPlayer
    {
        private const int FlakeChance = 4;              // снежинка вокруг — раз в столько тиков в среднем
        private const int SparkleChance = 9;
        private const int MinBreathTicks = 110;
        private const int MaxBreathTicks = 170;
        private const float RunSnowSpeed = 2.5f;
        private const int RunSnowInterval = 5;
        private const float HardLandingSpeed = 5f;

        public static readonly Color SnowColor = new(225, 240, 255);
        public static readonly Color FrostGlow = new(190, 235, 255, 0);

        public bool FlinxSet;              // сет надет; сбрасывается каждый тик
        public bool AuraEnabled = true;    // выбор игрока: сохраняется и синхронизируется

        private int _breathTimer = MinBreathTicks;
        private float _fallSpeed;

        public bool AuraActive => FlinxSet && AuraEnabled && !Player.dead;

        public override void ResetEffects() => FlinxSet = false;

        // Двойное нажатие «вниз» — только у самого игрока; остальным изменение уйдёт через SendClientChanges
        public override void ArmorSetBonusActivated()
        {
            if (!FlinxSet)
                return;

            AuraEnabled = !AuraEnabled;
            if (Player.whoAmI == Main.myPlayer)
                Main.NewText(Language.GetTextValue(AuraEnabled ? "Mods.SoA.Items.FlinxHood.AuraOn" : "Mods.SoA.Items.FlinxHood.AuraOff"),
                    SnowColor);
        }

        #region Сохранение и сеть

        public override void SaveData(TagCompound tag) => tag["FlinxAuraEnabled"] = AuraEnabled;

        public override void LoadData(TagCompound tag)
            => AuraEnabled = !tag.ContainsKey("FlinxAuraEnabled") || tag.GetBool("FlinxAuraEnabled");

        public override void CopyClientState(ModPlayer targetCopy)
            => ((FlinxArmorSetBonusPlayer)targetCopy).AuraEnabled = AuraEnabled;

        public override void SendClientChanges(ModPlayer clientPlayer)
        {
            if (((FlinxArmorSetBonusPlayer)clientPlayer).AuraEnabled != AuraEnabled)
                SendAura(-1, Main.myPlayer);
        }

        public override void SyncPlayer(int toWho, int fromWho, bool newPlayer) => SendAura(toWho, fromWho);

        private void SendAura(int toWho, int fromWho)
        {
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte)SoAPacketType.FlinxAura);
            packet.Write((byte)Player.whoAmI);
            packet.Write(AuraEnabled);
            packet.Send(toWho, fromWho);
        }

        public static void ReceiveAura(BinaryReader reader, int sender)
        {
            int playerIndex = reader.ReadByte();
            bool enabled = reader.ReadBoolean();

            // Сервер не верит клиенту на слово, чьё это состояние
            if (Main.netMode == NetmodeID.Server)
                playerIndex = sender;

            var flinx = Main.player[playerIndex].GetModPlayer<FlinxArmorSetBonusPlayer>();
            flinx.AuraEnabled = enabled;
            if (Main.netMode == NetmodeID.Server)
                flinx.SendAura(-1, playerIndex);
        }

        #endregion

        #region Аура

        public override void PostUpdate()
        {
            bool airborne = Player.velocity.Y != 0f;
            if (Main.dedServ || !AuraActive)
            {
                _fallSpeed = airborne ? Math.Max(_fallSpeed, Player.velocity.Y) : 0f;
                return;
            }

            Lighting.AddLight(Player.Center, 0.08f, 0.12f, 0.18f);
            SpawnSnowfall();
            SpawnFrostSparkle();
            SpawnBreath();
            SpawnFootSnow(airborne);
        }

        // Редкие хлопья падают вокруг игрока; на бегу их сносит назад
        private void SpawnSnowfall()
        {
            if (!Main.rand.NextBool(FlakeChance))
                return;

            Vector2 at = Player.Center + new Vector2(Main.rand.NextFloat(-44f, 44f), Main.rand.NextFloat(-52f, -18f));
            Vector2 velocity = new(-Player.velocity.X * 0.15f + Main.rand.NextFloat(-0.3f, 0.3f), Main.rand.NextFloat(0.1f, 0.4f));
            SoAParticles.SpawnFlake(at, velocity, Main.rand.NextFloat(2.5f, 4.5f), SnowColor, Main.rand.Next(90, 140));
        }

        // Иней на мехе: короткие морозные искорки по фигуре
        private void SpawnFrostSparkle()
        {
            if (!Main.rand.NextBool(SparkleChance))
                return;

            Vector2 at = Player.position + new Vector2(Main.rand.NextFloat(Player.width), Main.rand.NextFloat(Player.height));
            SoAParticles.SpawnGlow(at, new Vector2(0f, -0.3f), FrostGlow, 9f, 2f, 16);
        }

        // Облачко пара изо рта раз в пару секунд
        private void SpawnBreath()
        {
            if (--_breathTimer > 0)
                return;
            _breathTimer = Main.rand.Next(MinBreathTicks, MaxBreathTicks);

            Vector2 mouth = Player.Center + new Vector2(Player.direction * 7f, -12f * Player.gravDir);
            for (int i = 0; i < 2; i++)
            {
                Vector2 velocity = new(Player.direction * Main.rand.NextFloat(0.6f, 1.1f) + Player.velocity.X * 0.5f,
                    Main.rand.NextFloat(-0.25f, -0.05f));
                SoAParticles.SpawnSmoke(mouth, velocity, SnowColor, 4f, 18f, 0.35f, 40);
            }
        }

        // Снег из-под ног: на бегу — позёмка, при жёстком приземлении — облако в обе стороны
        private void SpawnFootSnow(bool airborne)
        {
            if (airborne)
            {
                _fallSpeed = Math.Max(_fallSpeed, Player.velocity.Y);
                return;
            }

            Vector2 feet = Player.Bottom - new Vector2(0f, 2f);
            if (_fallSpeed > HardLandingSpeed)
            {
                for (int i = 0; i < 8; i++)
                {
                    float side = i % 2 == 0 ? 1f : -1f;
                    SoAParticles.SpawnSmoke(feet, new Vector2(side * Main.rand.NextFloat(1f, 3f), -Main.rand.NextFloat(0.2f, 0.8f)),
                        SnowColor, 6f, 24f, 0.4f, 36);
                }
                for (int i = 0; i < 6; i++)
                    SoAParticles.SpawnFlake(feet, new Vector2(Main.rand.NextFloat(-2f, 2f), -Main.rand.NextFloat(1.5f, 3f)),
                        Main.rand.NextFloat(2.5f, 4f), SnowColor, 60);
            }
            _fallSpeed = 0f;

            if (Math.Abs(Player.velocity.X) > RunSnowSpeed && Main.GameUpdateCount % RunSnowInterval == 0)
            {
                Vector2 velocity = new(-Player.velocity.X * 0.15f, -Main.rand.NextFloat(0.3f, 0.8f));
                SoAParticles.SpawnSmoke(feet - new Vector2(Player.direction * 6f, 0f), velocity, SnowColor, 4f, 16f, 0.3f, 30);
            }
        }

        #endregion
    }

    // Удар кнута в снежной ауре: на месте попадания лопается облачко снежинок
    public class FlinxWhipFrost : GlobalProjectile
    {
        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation)
            => ProjectileID.Sets.IsAWhip[entity.type];

        public override void OnHitNPC(Projectile projectile, NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (Main.dedServ || !Main.player[projectile.owner].GetModPlayer<FlinxArmorSetBonusPlayer>().AuraActive)
                return;

            Vector2 at = target.Hitbox.ClosestPointInRect(Main.player[projectile.owner].Center);
            SoAParticles.SpawnGlow(at, Vector2.Zero, FlinxArmorSetBonusPlayer.FrostGlow, 14f, 50f, 10);
            for (int i = 0; i < 8; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                SoAParticles.SpawnStreak(at, dir * Main.rand.NextFloat(2f, 5f), FlinxArmorSetBonusPlayer.FrostGlow, 1.5f,
                    gravity: 0.05f, life: 14, lengthPerSpeed: 2f);
                SoAParticles.SpawnFlake(at, dir * Main.rand.NextFloat(0.5f, 2f) - Vector2.UnitY,
                    Main.rand.NextFloat(2.5f, 4f), FlinxArmorSetBonusPlayer.SnowColor, 70);
            }
        }
    }
}
