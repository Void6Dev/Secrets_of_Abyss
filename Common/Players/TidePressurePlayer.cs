using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Content.Buffs;
using SoA.Content.Worldgen;

namespace SoA.Common.Players
{
    // Давление глубины. Отсчёт идёт от печатей, а не от номера зоны: печать сама
    // и есть граница давления. Выше самой верхней печати не давит вообще — там
    // и галеон с алтарём, и арена Короля-краба, и туда игрок обязан попадать
    // без всякого снаряжения. Прошёл печать N вниз — получил уровень N.
    //
    // Правило одной строкой: печать не может давить выше того места, где стоит.
    //
    // Это не запрет входа: проход держит сама печать, по убитому боссу. Давление —
    // требование к снаряжению уже за ней.
    public class TidePressurePlayer : ModPlayer
    {
        private const int DefenseLossPerLevel = 8;
        private const float MoveSpeedLossPerLevel = 0.18f;   // доля от базовой скорости
        private const int DamageIntervalTicks = 60;
        private const int DamagePerLevel = 12;

        // Ступень лучшей надетой печати. Ставится аксессуаром каждый тик
        public int SigilTier;

        public override void ResetEffects() => SigilTier = 0;

        private int _damageCooldown;

        public override void PostUpdateEquips()
        {
            int missing = MissingLevels();
            if (missing <= 0)
            {
                _damageCooldown = 0;
                return;
            }

            Player.AddBuff(ModContent.BuffType<CrushingPressureDebuff>(), 2);
            Player.statDefense -= DefenseLossPerLevel * missing;
            Player.moveSpeed -= MoveSpeedLossPerLevel * missing;
            Player.wingTimeMax = 0;

            // Урон наносит только владелец персонажа: Hurt сам разошлёт его по сети,
            // а посчитанный на чужом клиенте он бы задвоился
            if (Player.whoAmI != Main.myPlayer)
                return;

            if (--_damageCooldown > 0)
                return;

            _damageCooldown = DamageIntervalTicks;
            Player.Hurt(
                PlayerDeathReason.ByCustomReason(
                    NetworkText.FromKey("Mods.SoA.Misc.PressureDeath", Player.name)),
                DamagePerLevel * missing,
                0);
        }

        // Сколько уровней давления не погашено печатью
        private int MissingLevels() => PressureLevel() - SigilTier;

        // Уровень давления в точке игрока: старшая ступень среди печатей,
        // что остались НАД ним. Ни одной печати выше — давления нет
        private int PressureLevel()
        {
            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)(Player.Center.Y / 16f);

            // Вне биома не давит: та же глубина на другом конце мира — обычная пещера
            if (TideOfShadowsWorldData.ZoneAt(tileX, tileY) == 0)
                return 0;

            int level = 0;
            foreach (TideSealSite site in TideOfShadowsWorldData.SealSites)
            {
                // Границей считается низ печати, а не верх: стоя в самом проёме,
                // игрок ещё не спустился и давление получать не должен
                if (tileY > site.Y + site.Height && site.Step > level)
                    level = site.Step;
            }
            return level;
        }
    }
}
