using System;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using SoA.Common.Systems;
using SoA.Content.Buffs;
using SoA.Content.Biomes;

namespace SoA.Common.Players
{
    // Давление глубины. Отсчёт идёт от печатей, а не от номера зоны: печать сама
    // и есть граница давления. Выше самой верхней печати не давит вообще — там
    // и галеон с алтарём, и арена Короля-краба. Ниже печати N давление растёт плавно:
    // N у самого прохода и до N+1 у дна зоны под ним.
    //
    // Как в Бездне Каламити, давление не снимается полностью: жемчужины глубины
    // гасят его большую часть, но четверть остаётся всегда. Главное, что оно делает, —
    // воздух уходит быстрее. Своего урона в воде нет: давление ускоряет ванильный
    // отсчёт дыхания, а им же идёт и ванильное утопление, когда воздух кончился.
    //
    // Это не запрет входа: проход держит сама печать, по убитому боссу.
    //
    // Спрятаться от давления в воздухе нельзя. Выкопанный карман или туннель в стене
    // выключали главное — воздух не тратился, и урона не было. Теперь вне воды глубина
    // давит напрямую: по 1 урону, и каждую секунду чаще — до 1 урона за тик.
    public class TidePressurePlayer : ModPlayer
    {
        public const int MaxPearls = 4;
        private const float PearlRelief = 0.75f;        // сколько уровня снимает одна жемчужина
        private const float MinPressureShare = 0.25f;   // эта доля давления остаётся всегда

        private const float DefenseLossPerPressure = 4f;
        private const float MoveSpeedLossPerPressure = 0.08f;
        private const float WingsOffPressure = 1f;      // глубже, чем позволяют жемчужины, — без крыльев

        // Ступень удушья вне воды растёт раз в секунду и в воде спадает с той же
        // скоростью: нырок туда-обратно счёт не обнуляет. Жемчужины её не гасят.
        // Урон всегда по 1, ступень задаёт только частоту: на ступени N удар раз
        // в 60 / 2^(N-1) тиков — 60, 30, 15, 8, 4, 2, 1
        private const int DryLevelStepTicks = 60;
        private const int MaxDryLevel = 7;
        private const int SlowestDryHitTicks = 60;
        private const float PressurePerDryLevel = 0.5f;   // чтобы в подсказке было видно, как давит

        // Единица lifeRegen — полжизни в секунду: 120 = 1 жизнь за каждый тик
        private const int LifeRegenPerHitPerTick = 120;

        // Съеденные жемчужины глубины. Сохраняются и синхронизируются
        public int Pearls;

        // Святилища, из которых игрок уже взял жемчужину: ступень N — бит (N-1).
        // Нужно только самому игроку, по сети не ходит
        private int _shrinesTaken;
        private float _breathDebt;
        private int _dryLevel;
        private int _dryLevelTimer;
        private bool _headInWater;

        // Давление в точке игрока после жемчужин. 0 — не давит
        public float Pressure { get; private set; }

        public bool HasTakenShrine(int step) => (_shrinesTaken & (1 << (step - 1))) != 0;

        public void MarkShrineTaken(int step) => _shrinesTaken |= 1 << (step - 1);

        public override void PostUpdateEquips()
        {
            float depthPressure = EffectivePressure(RawPressure());
            if (depthPressure <= 0f)
            {
                Pressure = 0f;
                _breathDebt = 0f;
                _dryLevel = 0;
                _dryLevelTimer = 0;
                return;
            }

            UpdateDryLevel();
            Pressure = depthPressure + _dryLevel * PressurePerDryLevel;

            Player.AddBuff(ModContent.BuffType<CrushingPressureDebuff>(), 2);
            Player.statDefense -= (int)Math.Round(DefenseLossPerPressure * Pressure);
            Player.moveSpeed -= MoveSpeedLossPerPressure * Pressure;
            if (Pressure >= WingsOffPressure)
                Player.wingTimeMax = 0;
        }

        // Воздух — забота владельца персонажа: на чужом клиенте отсчёт задвоился бы
        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer || Pressure <= 0f)
                return;

            DrainBreath();
        }

        // Ванильный отсчёт дыхания растёт на 1 за тик, пока голова под водой;
        // добавляем ещё Pressure за тик — воздух уходит в (1 + Pressure) раз быстрее.
        // Когда воздух кончился, тем же отсчётом идёт ванильное утопление, и оно
        // ускоряется так же — отдельный урон давления в воде не нужен
        private void DrainBreath()
        {
            if (Player.breathCD <= 0)
                return;

            _breathDebt += Pressure;
            while (_breathDebt >= 1f)
            {
                Player.breathCD++;
                _breathDebt -= 1f;
            }
        }

        // Голова над водой на глубине бывает только в выкопанной пустоте: генератор
        // ниже печатей воздуха не оставляет
        private void UpdateDryLevel()
        {
            _headInWater = Collision.DrownCollision(Player.position, Player.width, Player.height, Player.gravDir);

            // В мирах до таблицы стенки чаши след известен только прямоугольником,
            // и в него попадают сухие пещеры джунглей — там удушье убивало бы зря
            if (!TideOfShadowsWorldData.HasExactFootprint)
                _headInWater = true;

            if (++_dryLevelTimer < DryLevelStepTicks)
                return;
            _dryLevelTimer = 0;

            _dryLevel = _headInWater
                ? Math.Max(0, _dryLevel - 1)
                : Math.Min(MaxDryLevel, _dryLevel + 1);
        }

        private bool HidingInAir => !_headInWater && _dryLevel > 0;

        // Урон вне воды идёт через lifeRegen, как у горения: по одной жизни, без кадров
        // неуязвимости и вспышки на каждый удар, и по сети его разносит сама игра
        public override void UpdateBadLifeRegen()
        {
            if (Pressure <= 0f || !HidingInAir)
                return;

            int hitIntervalTicks = Math.Max(1, (int)Math.Round(SlowestDryHitTicks / Math.Pow(2, _dryLevel - 1)));

            if (Player.lifeRegen > 0)
                Player.lifeRegen = 0;
            Player.lifeRegenTime = 0;
            Player.lifeRegen -= LifeRegenPerHitPerTick / hitIntervalTicks;
        }

        // Смерть от удушья в кармане подписываем давлением, а не безликим «истёк жизнью»
        public override bool PreKill(double damage, int hitDirection, bool pvp, ref bool playSound,
            ref bool genDust, ref PlayerDeathReason damageSource)
        {
            if (Pressure > 0f && HidingInAir)
                damageSource = PlayerDeathReason.ByCustomReason(
                    NetworkText.FromKey("Mods.SoA.Misc.PressureDeath", Player.name));
            return true;
        }

        private float EffectivePressure(float raw)
        {
            if (raw <= 0f)
                return 0f;
            return Math.Max(raw - PearlRelief * Pearls, raw * MinPressureShare);
        }

        // Число печатей над игроком плюс то, насколько глубоко он в зоне под последней из них
        private float RawPressure()
        {
            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)(Player.Center.Y / 16f);

            // Вне биома не давит: та же глубина на другом конце мира — обычная пещера.
            // Проверка по следу, а не по прямоугольнику зоны: в прямоугольник
            // попадают пещеры джунглей за стенкой чаши, и давило бы в них
            if (!TideOfShadowsWorldData.InsideFootprint(tileX, tileY))
                return 0f;

            int sealsAbove = TideSealSystem.SealsAbove(tileY);
            if (sealsAbove == 0)
                return 0f;

            // Под печатью N лежит зона N+1, её глубина в DepthLevelAt — от N до N+1
            float depth = TideOfShadowsWorldData.DepthLevelAt(tileX, tileY);
            return sealsAbove + Math.Clamp(depth - sealsAbove, 0f, 1f);
        }

        #region Сохранение и сеть

        public override void SaveData(TagCompound tag)
        {
            tag["depthPearls"] = Pearls;
            tag["pearlShrinesTaken"] = _shrinesTaken;
        }

        public override void LoadData(TagCompound tag)
        {
            Pearls = Math.Clamp(tag.GetInt("depthPearls"), 0, MaxPearls);
            _shrinesTaken = tag.GetInt("pearlShrinesTaken");
        }

        public override void CopyClientState(ModPlayer targetCopy)
            => ((TidePressurePlayer)targetCopy).Pearls = Pearls;

        public override void SendClientChanges(ModPlayer clientPlayer)
        {
            if (((TidePressurePlayer)clientPlayer).Pearls != Pearls)
                SendPearls(-1, Main.myPlayer);
        }

        public override void SyncPlayer(int toWho, int fromWho, bool newPlayer) => SendPearls(toWho, fromWho);

        private void SendPearls(int toWho, int fromWho)
        {
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte)SoAPacketType.DepthPearls);
            packet.Write((byte)Player.whoAmI);
            packet.Write((byte)Pearls);
            packet.Send(toWho, fromWho);
        }

        public static void ReceivePearls(BinaryReader reader, int sender)
        {
            int playerIndex = reader.ReadByte();
            int pearls = reader.ReadByte();

            // Сервер не верит клиенту на слово, чьё это состояние
            if (Main.netMode == NetmodeID.Server)
                playerIndex = sender;

            var pressure = Main.player[playerIndex].GetModPlayer<TidePressurePlayer>();
            pressure.Pearls = Math.Clamp(pearls, 0, MaxPearls);
            if (Main.netMode == NetmodeID.Server)
                pressure.SendPearls(-1, playerIndex);
        }

        #endregion
    }
}
