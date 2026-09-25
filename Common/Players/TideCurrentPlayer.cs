using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Utils;
using SoA.Content.Worldgen;

namespace SoA.Common.Players
{
    // Течения открытой воды Прилива Теней. Несут в сторону моря и вниз по каскаду:
    // спуск даётся легко, подъём требует усилий. Заодно наполняют толщу воды, которая
    // до сих пор была пустой — поток видно по сносимой взвеси.
    public class TideCurrentPlayer : ModPlayer
    {
        private const float MaxDriftSpeed = 1.9f;   // предельная скорость сноса, пикселей за тик
        private const float Responsiveness = 0.022f;
        // Течения живут в открытой воде, а это теперь одна зона — Затопленный порт.
        // Ниже начинается зал под дном, там несёт уже не течение, а давление
        private const int PortZone = 1;

        public override void PostUpdate()
        {
            // Скорость правит только владелец персонажа: на сервере и у чужих
            // клиентов это разошлось бы с их собственной симуляцией
            if (Player.whoAmI != Main.myPlayer)
                return;
            if (!Player.wet || Player.lavaWet || Player.honeyWet)
                return;

            int tileX = (int)(Player.Center.X / 16f);
            int tileY = (int)(Player.Center.Y / 16f);
            int zone = TideOfShadowsWorldData.ZoneAt(tileX, tileY);
            if (zone != PortZone)
                return;

            float strength = CurrentStrengthAt(tileY);
            if (strength <= 0.01f)
                return;

            int seaward = -TideOfShadowsWorldData.InlandDir;

            // Вертикальная составляющая колеблется по шуму от места и времени:
            // ровный поток вниз ощущался бы как лифт, а не как течение
            float swirl = TideNoise.Signed(tileX * 0.02f + Main.GameUpdateCount * 0.004f, 4201, 3);

            Vector2 target = new(seaward * MaxDriftSpeed * strength,
                                MaxDriftSpeed * strength * (0.35f + 0.45f * swirl));

            Player.velocity.X = MathHelper.Lerp(Player.velocity.X, target.X, Responsiveness);
            Player.velocity.Y = MathHelper.Lerp(Player.velocity.Y, target.Y, Responsiveness * 0.6f);

            if (!Main.dedServ)
                EmitDrift(target);
        }

        // Ближе к поверхности почти штиль, к нижней границе зоны 2 — полный поток
        private static float CurrentStrengthAt(int tileY)
        {
            int waterTop = TideOfShadowsWorldData.WaterTopY;
            int bottom = TideOfShadowsWorldData.ZoneBottomY[PortZone - 1];
            if (bottom <= waterTop)
                return 0f;

            float depth = TideNoise.Clamp01((tileY - waterTop) / (float)(bottom - waterTop));
            return TideNoise.SmoothStep(0.06f, 0.55f, depth);
        }

        private void EmitDrift(Vector2 flow)
        {
            if (!Main.rand.NextBool(4))
                return;

            Dust drift = Dust.NewDustDirect(Player.position - new Vector2(48f, 48f),
                Player.width + 96, Player.height + 96, DustID.Water);
            drift.noGravity = true;
            drift.velocity = flow * Main.rand.NextFloat(1.4f, 2.6f);
            drift.scale = Main.rand.NextFloat(0.5f, 0.95f);
            drift.alpha = 140;
        }
    }
}
