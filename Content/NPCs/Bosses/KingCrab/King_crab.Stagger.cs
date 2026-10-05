using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ОГЛУШЕНИЕ. Промахнулся тяжёлой атакой (слэм, рывок, захлоп, прыжок, хлопок, выход
    // из подкопа) — туша оседает, клешни падают на песок, над короной кружат звёзды.
    // Король стоит, не ранит контактом и ловит 1.5x урона (ShellCrack на всё оглушение).
    // Раньше промах давал только 30-тиковое «недовольство», во время которого король
    // продолжал идти на игрока: окна наказать его просто не было.
    public partial class King_crab
    {
        private const int StaggerRecoverPause = 24;   // выпрямился — короткий вдох перед следующей атакой
        private const int DizzyStarCount = 3;
        private const float DizzyStarRadius = 38f;

        private int StaggerDuration()
            => (int)MathHelper.Lerp(StaggerTicksFullHealth, StaggerTicksNearDeath, Fury);

        private void AIStagger()
        {
            ApplyGravity();
            Brake();

            if (Timer > 0f)
                return;

            EnterState(CrabState.Scuttle, StaggerRecoverPause);
        }

        // Из TickVisuals: звёзды над короной, пока король оглушён
        private void UpdateStaggerFx()
        {
            if (State != CrabState.Stagger || Main.dedServ || !EveryTicks(3))
                return;

            Vector2 crown = AnimatedFacingToWorld(new Vector2(CrownOffsetX, CrownOffsetY - 34f));
            float spin = Main.GameUpdateCount * 0.12f;
            for (int i = 0; i < DizzyStarCount; i++)
            {
                float angle = spin + MathHelper.TwoPi * i / DizzyStarCount;
                // Эллипс, а не круг: звёзды кружат «над головой», в перспективе
                Vector2 at = crown + new Vector2((float)Math.Cos(angle) * DizzyStarRadius,
                                                 (float)Math.Sin(angle) * DizzyStarRadius * 0.35f);
                Dust star = Dust.NewDustPerfect(at, DustID.YellowStarDust, Vector2.Zero);
                star.noGravity = true;
                star.scale = 1.1f;
                star.fadeIn = 0.6f;
            }
        }

        // Метка stagger_start: туша плюхается, клешни бьют в песок
        private void OnStaggerStart()
        {
            SoundEngine.PlaySound(SoundID.NPCHit4 with { Pitch = -0.6f, Volume = 0.9f }, NPC.Center);
            ScreenPunch(3f, 12, Vector2.UnitY);
            SpawnDustCloud(NPC.Bottom, 200f, 6, 0.8f);
        }
    }
}
