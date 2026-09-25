using Microsoft.Xna.Framework;
using Terraria;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Тяжёлые эффекты ударов поверх ванильной пыли: обломки грунта, клубы пыли, брызги, искры,
    // свет на мир и волна-преломление экрана. Ванильная пыль — 8 px и живёт миг, поэтому
    // удар туши в 4200 HP выглядел как хлопок в ладоши. Чистая косметика: эмиттеры сами
    // молчат на выделенном сервере, звать их можно прямо из AI.
    public partial class King_crab
    {
        private static readonly Color WaterSprayColor = new(150, 210, 255);
        private static readonly Color SparkColor = new(255, 205, 140);
        private static readonly Color ImpactLightColor = new(255, 225, 180);
        private static readonly Color TideLightColor = new(140, 205, 255);
        private static readonly Color RoyalLightColor = new(255, 200, 110);
        private static readonly Color RageLightColor = new(255, 70, 40);

        private const float ScreenWaveMaxDistance = 1600f; // дальше волна-преломление не включается

        // Обломки грунта. directionX = 0 — конус вверх, ±1 — разлёт в эту сторону (удар о стену)
        private void SpawnImpactDebris(Vector2 at, int count, float power, float directionX = 0f)
        {
            if (Main.dedServ)
                return;

            Color ground = GroundColor(at);
            Color stone = new Color(112, 106, 100);
            for (int i = 0; i < count; i++)
            {
                float vx = directionX != 0f
                    ? directionX * Main.rand.NextFloat(1.5f, 6f)
                    : Main.rand.NextFloatDirection() * 4.5f;
                Vector2 velocity = new Vector2(vx, -Main.rand.NextFloat(3f, 9.5f)) * power;

                // Треть — камни, остальное — комья грунта чуть темнее пыли
                Color color = Main.rand.NextBool(3) ? stone : new Color(ground.ToVector3() * Main.rand.NextFloat(0.65f, 0.9f));
                float size = Main.rand.NextFloat(3f, 8f) * (0.85f + 0.25f * power);
                SoAParticles.SpawnDebris(at + new Vector2(Main.rand.NextFloat(-26f, 26f), -6f), velocity, color,
                    size, Main.rand.Next(55, 95));
            }
        }

        // Клуб пыли по грунту: пуфы расходятся в стороны от точки удара, вспухают и оседают
        private void SpawnDustCloud(Vector2 at, float width, int puffs, float power = 1f)
        {
            if (Main.dedServ)
                return;

            Color dust = Color.Lerp(GroundColor(at), Color.White, 0.15f);
            for (int i = 0; i < puffs; i++)
            {
                float side = Main.rand.NextFloatDirection();
                Vector2 position = at + new Vector2(side * width * 0.3f, -Main.rand.NextFloat(6f, 24f));
                Vector2 velocity = new Vector2(side * Main.rand.NextFloat(2f, 6f), -Main.rand.NextFloat(0.3f, 1.8f)) * power;
                float start = Main.rand.NextFloat(70f, 110f);
                float end = start * Main.rand.NextFloat(2.2f, 3f) * (0.8f + 0.2f * power);
                SoAParticles.SpawnSmoke(position, velocity, dust, start, end,
                    Main.rand.NextFloat(0.4f, 0.6f), Main.rand.Next(50, 90));
            }
        }

        // Брызги воды: светлые штрихи с гравитацией, веером вокруг direction
        private void SpawnWaterSpray(Vector2 at, int count, float speed, Vector2 direction, float spread)
        {
            if (Main.dedServ)
                return;

            direction = direction.SafeNormalize(-Vector2.UnitY);
            for (int i = 0; i < count; i++)
            {
                Vector2 velocity = direction.RotatedBy(Main.rand.NextFloatDirection() * spread)
                    * speed * Main.rand.NextFloat(0.45f, 1f);
                SoAParticles.SpawnStreak(at + Main.rand.NextVector2Circular(10f, 6f), velocity,
                    WaterSprayColor * Main.rand.NextFloat(0.5f, 0.9f), Main.rand.NextFloat(3f, 6f),
                    gravity: 0.3f, life: Main.rand.Next(24, 40));
            }
        }

        // Искры металла: тонкие, быстрые, почти без гравитации
        private void SpawnSparks(Vector2 at, int count, float speed)
        {
            if (Main.dedServ)
                return;

            for (int i = 0; i < count; i++)
            {
                Vector2 velocity = Main.rand.NextVector2Unit() * speed * Main.rand.NextFloat(0.4f, 1f);
                SoAParticles.SpawnStreak(at, velocity, SparkColor, Main.rand.NextFloat(2f, 3.2f),
                    gravity: 0.08f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.5f);
            }
        }

        // Свет на мир + мягкое светящееся пятно в точке удара
        private void ImpactLight(Vector2 at, Color color, float intensity, int life = 14)
        {
            if (Main.dedServ)
                return;
            SoAParticles.AddLight(at, color, intensity, life);
            SoAParticles.SpawnGlow(at, Vector2.Zero, color * 0.55f, 70f * intensity, 150f * intensity, life + 4);
        }

        // Волна-преломление экрана (RoarShockwaveFx): только для самых тяжёлых моментов
        // и только когда игрок рядом — фильтр полноэкранный. Яркость кадра не меняет
        private static void ScreenWave(Vector2 at, float strength, float duration, int waves = 1)
        {
            if (Main.dedServ || Main.LocalPlayer.Distance(at) > ScreenWaveMaxDistance)
                return;
            RoarShockwaveFx.Trigger(at, duration, strength, waves);
        }

        // Волна, едущая за боссом: рёв он издаёт на ходу
        private void ScreenWaveFollow(float strength, float duration, int waves = 1)
        {
            if (Main.dedServ || Main.LocalPlayer.Distance(NPC.Center) > ScreenWaveMaxDistance)
                return;
            RoarShockwaveFx.Trigger(NPC, duration, strength, waves);
        }

        private static Color GroundColor(Vector2 at)
        {
            Vector3 tint = GetGroundTint(at);
            return new Color(tint.X, tint.Y, tint.Z);
        }
    }
}
