using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // КЛЕШНЯ ИЗ ПЕСКА. Пока король под землёй, он бьёт клешнёй снизу туда, где стоит игрок
    // (KingCrabSandClaw). Каждый удар заранее подсвечен трещиной и столбом, бьёт ровно в
    // подсвеченную полосу и целится в точку на момент начала предупреждения — стой на месте
    // и получишь, отойди — промах. С фазами ударов в залпе больше и они чаще, но
    // предупреждение никогда не короче ~0.6 с.
    //   • подкоп: дойдя до игрока, перед выходом наружу — залп (стадия BurrowSubClaws);
    //   • «королевская гвардия» и бой свиты: залпы периодически, пока король ждёт внизу.
    // Снаряды спавнит только сервер.
    public partial class King_crab
    {
        private const float BurrowSubClaws = 1.25f;  // между ходом и бугром выхода: король ещё скрыт

        private const int SandClawDamage = 30;
        private const float SandClawGroundProbe = 600f; // как глубоко под игроком искать грунт для удара
        private const int KnightCourtClawPeriod = 150;  // пауза между залпами под «гвардией»
        private const int CourtDuelClawPeriod = 270;    // под боем свиты реже: игроку и так есть с кем драться
        private const int CourtDuelClawMax = 2;
        private const int UndergroundClawFirstDelay = 90;

        // Залп по фазам: 1 → 2 → 3 удара, предупреждение 54 → 46 → 38 тиков
        private int SandClawCount => Desperate ? 3 : Phase2 ? 2 : 1;
        private int SandClawWarnTicks => Desperate ? 38 : Phase2 ? 46 : 54;
        private int SandClawGapTicks => Desperate ? 30 : 40;

        private int _clawVolleyLeft;
        private int _clawVolleyTimer;
        private int _clawCooldown;

        // Сколько длится залп от первого предупреждения до ухода последней клешни
        private int ClawVolleyTicks(int count)
            => (count - 1) * SandClawGapTicks + SandClawWarnTicks + KingCrabSandClaw.StrikeTicks;

        private void StartClawVolley(int count)
        {
            _clawVolleyLeft = count;
            _clawVolleyTimer = 0;
        }

        private void TickClawVolley(Player target)
        {
            if (_clawVolleyLeft <= 0 || Main.netMode == NetmodeID.MultiplayerClient)
                return;
            if (_clawVolleyTimer-- > 0)
                return;

            SpawnSandClaw(target);
            _clawVolleyLeft--;
            _clawVolleyTimer = SandClawGapTicks;
        }

        // Ждём под землёй: время от времени — новый залп
        private void TickUndergroundClaws(Player target, int period, int maxCount)
        {
            TickClawVolley(target);
            if (_clawVolleyLeft > 0 || --_clawCooldown > 0)
                return;
            StartClawVolley(Math.Min(SandClawCount, maxCount));
            _clawCooldown = period;
        }

        private void ResetUndergroundClaws()
        {
            _clawVolleyLeft = 0;
            _clawCooldown = UndergroundClawFirstDelay;
        }

        // Удар в грунт под игроком. Нет грунта (игрок над пропастью) — удара нет
        private void SpawnSandClaw(Player target)
        {
            float x = target.Center.X;
            float ground = FindGroundY(x, target.Bottom.Y - 8f, SandClawGroundProbe, true);
            if (float.IsNaN(ground))
                return;

            Projectile.NewProjectile(NPC.GetSource_FromAI(),
                new Vector2(x, ground - KingCrabSandClaw.HitboxHeight / 2f), Vector2.Zero,
                ModContent.ProjectileType<KingCrabSandClaw>(), ProjDamage(SandClawDamage), 3f,
                Main.myPlayer, SandClawWarnTicks);
        }

        // Подкоп, 3b. Дошёл до игрока — держится под ним и бьёт клешнёй, потом выходит наружу
        private void BurrowClaws(Player target)
        {
            NPC.noTileCollide = true;
            float goalY = SurfaceAbove(target.Center.X, target.Bottom.Y) + BurrowDepth;
            NPC.velocity = new Vector2(
                MathHelper.Clamp((target.Center.X - NPC.Center.X) * 0.05f, -9f, 9f),
                MathHelper.Clamp((goalY - NPC.Center.Y) * 0.05f, -8f, 8f));

            if (EveryTicks(BurrowRumbleInterval))
                ScreenRumble(BurrowRumbleMin * 1.5f);

            TickClawVolley(target);

            if (Timer > 0f)
                return;

            StateData = NPC.Center.X; // точка выхода зафиксирована — дальше король её держит
            EnterSubState(BurrowSubWarn, BurrowWarnTicks);
        }
    }
}
