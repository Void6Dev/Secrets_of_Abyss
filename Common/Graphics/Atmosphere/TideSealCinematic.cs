using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Particles;
using SoA.Common.Systems;
using SoA.Common.Utils;
using SoA.Content.Biomes;

namespace SoA.Common.Graphics.Atmosphere
{
    // Сцена снятия печати. Сервер запускает ритуал (TideSealSystem) и через RitualTicks
    // убирает мембрану; клиент за то же время играет:
    //   • нарастание — трещина на замке разгорается, символ ступени гаснет, всё, что
    //     плавает вокруг, втягивает к низу прохода, дно гудит и дрожит;
    //   • слом — вспышка, осколки, удар по экрану;
    //   • отлив — поток воды с пузырями ещё какое-то время уходит вниз сквозь проход.
    // Только клиент: всё выводится из номера ступени, по сети приходит лишь старт
    public class TideSealCinematic : ModSystem
    {
        private const int AftermathTicks = 120;
        private const int RumbleIntervalTicks = 8;
        private const int GroanTick = 100;
        private const float PullRadiusPx = 70f * 16f;
        private const float InflowMinDistancePx = 160f;
        private const float InflowMaxDistancePx = 640f;
        private const float ShakeFalloffPx = 2400f;
        private const int BurstStreaks = 50;
        private const int BurstChips = 26;

        public static readonly Color CrackColor = new(120, 230, 255);
        private static readonly Color SigilColor = new(175, 115, 255);
        private const int SigilShards = 28;
        private static readonly Color InflowColor = new(90, 190, 240);
        private static readonly Color ChipColor = new(70, 64, 92);

        private static int _step;
        private static int _age;

        public static bool IsPlaying => _step != 0;

        public static void Start(int step)
        {
            if (Main.dedServ)
                return;
            _step = step;
            _age = 0;

            if (TideSealSystem.TryGetSite(step, out TideSealSite site))
                SoundEngine.PlaySound(SoundID.DD2_EtherianPortalOpen with { Pitch = -0.5f, Volume = 1.2f }, site.LockCenter);
        }

        // 0..1 — ход ритуала этой ступени, -1 — ритуала нет
        public static float RitualProgress(int step)
            => _step == step ? MathHelper.Clamp(_age / (float)TideSealSystem.RitualTicks, 0f, 1f) : -1f;

        // Множитель свечения трещины: в ритуале разгорается, после слома гаснет
        public static float CrackBoost(int step)
        {
            if (_step != step)
                return 1f;
            if (_age <= TideSealSystem.RitualTicks)
                return 1f + 2.5f * SoAEasing.QuadIn(RitualProgress(step));
            return 1f - MathHelper.Clamp((_age - TideSealSystem.RitualTicks) / 20f, 0f, 1f);
        }

        // Символ ступени на замке гаснет за время ритуала
        public static float SymbolFade(int step)
        {
            float progress = RitualProgress(step);
            return progress < 0f ? 1f : 1f - SoAEasing.QuadOut(progress);
        }

        public override void OnWorldUnload() => _step = 0;

        public override void PostUpdateEverything()
        {
            if (_step == 0 || Main.dedServ)
                return;

            if (!TideSealSystem.TryGetSite(_step, out TideSealSite site))
            {
                _step = 0;
                return;
            }

            _age++;
            int ritual = TideSealSystem.RitualTicks;

            if (_age < ritual)
                UpdateBuildUp(site, _age / (float)ritual);
            else if (_age == ritual)
                Break(site);
            else if (_age < ritual + AftermathTicks)
                UpdateAftermath(site, (_age - ritual) / (float)AftermathTicks);
            else
                _step = 0;
        }

        private static void UpdateBuildUp(TideSealSite site, float progress)
        {
            float eased = SoAEasing.QuadIn(progress);
            Vector2 center = site.LockCenter;
            Vector2 sink = site.PassageBottom;

            TideAmbience.SetPull(sink, 0.05f + 0.35f * eased, PullRadiusPx);

            if (_age % RumbleIntervalTicks == 0)
                ScreenShake.Punch(center, Main.rand.NextVector2Unit(), 1f + 5f * eased, 14f, 12, ShakeFalloffPx, "SoASealRumble");

            if (_age == GroanTick)
                SoundEngine.PlaySound(SoundID.Roar with { Pitch = -1f, Volume = 0.7f }, center);

            // Со всех сторон вода стягивается к низу прохода и закручивается
            int inflow = 1 + (int)(4f * progress);
            for (int i = 0; i < inflow; i++)
            {
                Vector2 from = sink + Main.rand.NextVector2Unit() * Main.rand.NextFloat(InflowMinDistancePx, InflowMaxDistancePx);
                if (!SoACombat.IsInWater(from))
                    continue;

                Vector2 velocity = (sink - from).SafeNormalize(Vector2.UnitY).RotatedBy(0.45f) * (2f + 9f * eased);
                SoAParticles.SpawnStreak(from, velocity, InflowColor * (0.4f + 0.6f * progress), 1.5f, 0f, 30, 4f);
            }

            // Трещина сочится пузырями, их тоже тянет вниз
            if (_age % 3 == 0)
            {
                Vector2 bubbleVelocity = (sink - center).SafeNormalize(Vector2.UnitY) * (1f + 2f * eased);
                SoAParticles.SpawnBubble(center + Main.rand.NextVector2Circular(10f, 18f), bubbleVelocity,
                    Main.rand.NextFloat(3f, 7f), CrackColor, 80);
            }

            SoAParticles.AddLight(center, CrackColor, 0.8f + 2f * progress, 2);
        }

        private static void Break(TideSealSite site)
        {
            Vector2 center = site.LockCenter;

            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.8f, Volume = 1.3f }, center);
            SoundEngine.PlaySound(SoundID.Splash with { Pitch = -0.7f, Volume = 1.2f }, center);
            ScreenShake.Punch(center, Vector2.UnitY, 12f, 8f, 40, ShakeFalloffPx * 1.25f, "SoASealBreak");

            SoAParticles.SpawnGlow(center, Vector2.Zero, CrackColor, 20f, 260f, 30);
            SoAParticles.SpawnGlow(center, Vector2.Zero, Color.White, 10f, 120f, 14);
            SoAParticles.AddLight(center, CrackColor, 4f, 40);

            for (int i = 0; i < BurstStreaks; i++)
            {
                Vector2 velocity = Main.rand.NextVector2Unit() * Main.rand.NextFloat(6f, 14f);
                SoAParticles.SpawnStreak(center, velocity, CrackColor, 2f, 0f, Main.rand.Next(20, 40));
            }

            // Треск: рисунок печати разлетается кольцом фиолетовых осколков, сам замок исчезает
            // (его тайлы снимает TideSealSystem в тот же тик)
            SoundEngine.PlaySound(SoundID.Shatter with { Pitch = -0.3f, Volume = 1.1f }, center);
            SoundEngine.PlaySound(SoundID.Item27 with { Pitch = -0.6f }, center);
            for (int i = 0; i < SigilShards; i++)
            {
                Vector2 dir = (MathHelper.TwoPi * i / SigilShards + Main.rand.NextFloat(-0.08f, 0.08f)).ToRotationVector2();
                SoAParticles.SpawnStreak(center + dir * 20f, dir * Main.rand.NextFloat(4f, 9f), SigilColor, 1.8f, 0f,
                    Main.rand.Next(18, 32));
            }

            for (int i = 0; i < BurstChips; i++)
            {
                Vector2 velocity = Main.rand.NextVector2Circular(5f, 5f) + new Vector2(0f, 1.5f);
                SoAParticles.SpawnDebris(center + Main.rand.NextVector2Circular(16f, 16f), velocity, ChipColor,
                    Main.rand.NextFloat(3f, 6f), 90);
            }
        }

        // Отлив: проход открыт, вода ещё какое-то время уходит в него потоком
        private static void UpdateAftermath(TideSealSite site, float progress)
        {
            float fade = 1f - progress;
            Vector2 center = site.LockCenter;
            Vector2 sink = site.PassageBottom;
            float halfWidth = site.Width * 8f * 0.6f;

            TideAmbience.SetPull(sink, 0.4f * fade, PullRadiusPx);

            int torrent = (int)(4f * fade + Main.rand.NextFloat());
            for (int i = 0; i < torrent; i++)
            {
                var from = new Vector2(center.X + Main.rand.NextFloat(-halfWidth, halfWidth),
                    Main.rand.NextFloat(site.Y * 16f, sink.Y));
                var velocity = new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), Main.rand.NextFloat(6f, 12f) * fade);
                SoAParticles.SpawnStreak(from, velocity, InflowColor * fade, 1.6f, 0f, 26, 4f);

                if (Main.rand.NextBool(3))
                    SoAParticles.SpawnBubble(from, velocity * 0.4f, Main.rand.NextFloat(3f, 8f), CrackColor, 70);
            }

            if (_age % (RumbleIntervalTicks * 2) == 0)
                ScreenShake.Punch(center, Main.rand.NextVector2Unit(), 2.5f * fade, 10f, 14, ShakeFalloffPx, "SoASealRumble");
        }
    }
}
