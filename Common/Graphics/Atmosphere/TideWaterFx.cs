using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    // Поверхность воды Прилива: всплески при входе и выходе, капли и рябь — кольца,
    // которые расходятся по поверхности от всплеска и от всего, что плывёт прямо под ней.
    // Только клиент: всё выводится из позиций сущностей, по сети ничего не ходит
    public class TideWaterFx : ModSystem
    {
        private const int MaxRipples = 64;
        private const int MaxSurfaceScanTiles = 24;
        private const int SwimRippleIntervalTicks = 9;
        private const float SwimRippleDepthPx = 20f;     // плывёт так близко к поверхности — от него рябь
        private const float SwimRippleMinSpeed = 1.5f;
        private const float MinRippleBrightness = 0.25f;

        public static readonly Color SprayColor = new(150, 205, 235);
        private static readonly Color FoamColor = new(200, 235, 255);
        private static readonly Color BubbleColor = new(170, 215, 255);

        private struct Ripple
        {
            public Vector2 Position;   // точка на поверхности
            public float MaxRadius;
            public float Strength;
            public int Age;
            public int Life;
        }

        private static readonly List<Ripple> _ripples = new(MaxRipples);

        public override void Load()
        {
            if (!Main.dedServ)
                On_Main.DrawDust += DrawAfterDust;
        }

        public override void Unload() => _ripples.Clear();

        public override void OnWorldUnload() => _ripples.Clear();

        #region Поверхность

        // Точка поверхности над водной точкой: идём вверх, пока в тайле есть вода.
        // Вода в тайле стоит снизу, поэтому верх — это доля незалитой части верхнего тайла
        public static bool TryFindSurface(Vector2 underwater, out Vector2 surface)
        {
            Point tile = underwater.ToTileCoordinates();
            surface = default;
            if (!IsWaterTile(tile.X, tile.Y))
                return false;

            for (int i = 0; i < MaxSurfaceScanTiles; i++)
            {
                Tile above = Framing.GetTileSafely(tile.X, tile.Y - 1);
                if (IsSolid(above))
                    return false;   // под потолком: поверхности, по которой бежит рябь, здесь нет
                if (!IsWaterTile(tile.X, tile.Y - 1))
                {
                    Tile top = Framing.GetTileSafely(tile.X, tile.Y);
                    float filled = top.LiquidAmount / 255f;
                    surface = new Vector2(underwater.X, (tile.Y + 1f - filled) * 16f);
                    return true;
                }
                tile.Y--;
            }
            return false;
        }

        private static bool IsWaterTile(int x, int y)
        {
            Tile tile = Framing.GetTileSafely(x, y);
            return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water && !IsSolid(tile);
        }

        private static bool IsSolid(Tile tile)
            => tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];

        #endregion

        #region Всплески

        // Вход в воду. strength 0..1 — от тихого шага до прыжка с высоты
        public static void EntrySplash(Vector2 surface, float strength, Vector2 entryVelocity)
        {
            if (Main.dedServ)
                return;
            strength = MathHelper.Clamp(strength, 0f, 1f);

            // Корона брызг: вверх и в стороны, в сторону движения чуть больше
            int drops = 6 + (int)(26 * strength);
            for (int i = 0; i < drops; i++)
            {
                float spread = Main.rand.NextFloat(-1f, 1f);
                Vector2 velocity = new(spread * (2f + 4f * strength) + entryVelocity.X * 0.25f,
                    -Main.rand.NextFloat(2f, 4f + 7f * strength) * (1f - 0.45f * Math.Abs(spread)));
                SoAParticles.SpawnStreak(surface + new Vector2(spread * 10f, -2f), velocity, SprayColor,
                    Main.rand.NextFloat(1.4f, 2.4f + strength), gravity: 0.3f, life: Main.rand.Next(20, 36), lengthPerSpeed: 2.2f);
            }

            // Пена на поверхности и облако пузырей, увлечённое вниз
            SoAParticles.SpawnGlow(surface, Vector2.Zero, FoamColor * (0.35f + 0.4f * strength), 10f, 40f + 70f * strength, 14);
            int bubbles = 4 + (int)(18 * strength);
            for (int i = 0; i < bubbles; i++)
            {
                Vector2 at = surface + new Vector2(Main.rand.NextFloat(-12f, 12f), Main.rand.NextFloat(4f, 10f + 30f * strength));
                Vector2 velocity = new(Main.rand.NextFloatDirection() * 1.2f, Main.rand.NextFloat(0.5f, 1.5f + 3f * strength));
                SoAParticles.SpawnBubble(at, velocity, Main.rand.NextFloat(2.5f, 5f + 4f * strength), BubbleColor, 70);
            }

            AddRipple(surface, 0.4f + 0.6f * strength);
            if (strength > 0.6f)
                SoundEngine.PlaySound(SoundID.Splash with { Pitch = -0.3f, Volume = 0.5f + 0.4f * strength }, surface);
        }

        // Выход из воды: невысокий веер капель, тело дальше капает само (DripFrom)
        public static void ExitSplash(Vector2 surface, float strength)
        {
            if (Main.dedServ)
                return;
            strength = MathHelper.Clamp(strength, 0f, 1f);

            int drops = 4 + (int)(12 * strength);
            for (int i = 0; i < drops; i++)
            {
                Vector2 velocity = new(Main.rand.NextFloatDirection() * 2.5f, -Main.rand.NextFloat(1.5f, 3f + 4f * strength));
                SoAParticles.SpawnStreak(surface + new Vector2(Main.rand.NextFloat(-8f, 8f), 0f), velocity, SprayColor,
                    Main.rand.NextFloat(1.2f, 2f), gravity: 0.3f, life: Main.rand.Next(18, 30), lengthPerSpeed: 2f);
            }
            AddRipple(surface, 0.3f + 0.5f * strength);
        }

        // Капля, сорвавшаяся с мокрого тела
        public static void DripFrom(Rectangle body)
        {
            var at = new Vector2(Main.rand.NextFloat(body.Left + 2, body.Right - 2), Main.rand.NextFloat(body.Top + 6, body.Bottom - 4));
            SoAParticles.SpawnStreak(at, new Vector2(Main.rand.NextFloatDirection() * 0.2f, Main.rand.NextFloat(0.5f, 1.5f)),
                SprayColor * 0.8f, Main.rand.NextFloat(1f, 1.6f), gravity: 0.25f, life: 26, lengthPerSpeed: 2.5f);
        }

        #endregion

        #region Рябь

        public static void AddRipple(Vector2 surface, float strength)
        {
            if (Main.dedServ)
                return;
            if (_ripples.Count >= MaxRipples)
                _ripples.RemoveAt(0);

            strength = MathHelper.Clamp(strength, 0.1f, 1f);
            _ripples.Add(new Ripple
            {
                Position = surface,
                MaxRadius = 20f + 70f * strength,
                Strength = strength,
                Life = 36 + (int)(40 * strength),
            });
        }

        public override void PostUpdateDusts()
        {
            if (Main.dedServ)
                return;

            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                Ripple ripple = _ripples[i];
                ripple.Age++;
                if (ripple.Age >= ripple.Life)
                    _ripples.RemoveAt(i);
                else
                    _ripples[i] = ripple;
            }

            if (TideAtmosphere.IsActive && Main.GameUpdateCount % SwimRippleIntervalTicks == 0)
                SpawnSwimRipples();
        }

        // Кто плывёт у самой поверхности, гонит по ней мелкую рябь
        private static void SpawnSwimRipples()
        {
            foreach (WaterDisturber swimmer in WaterDisturbers.Get())
            {
                if (Math.Abs(swimmer.Velocity.X) < SwimRippleMinSpeed || !TideOfShadowsWorldData.Contains(swimmer.Center))
                    continue;

                // Ищем от низа тела: у того, кто плывёт, торча из воды, верх в воздухе
                var bottom = new Vector2(swimmer.Center.X, swimmer.Hitbox.Bottom - 2f);
                if (!TryFindSurface(bottom, out Vector2 surface) || swimmer.Hitbox.Top - surface.Y > SwimRippleDepthPx)
                    continue;

                float speed = Math.Min(Math.Abs(swimmer.Velocity.X) / 6f, 1f);
                AddRipple(surface, 0.15f + 0.3f * speed);
            }
        }

        private static void DrawAfterDust(On_Main.orig_DrawDust orig, Main self)
        {
            orig(self);
            if (_ripples.Count == 0)
                return;

            SpriteBatch sb = Main.spriteBatch;
            Texture2D streak = SoAVfx.SoftStreak;
            Vector2 origin = streak.Size() / 2f;

            sb.Begin(SpriteSortMode.Deferred, SoAVfx.GlowBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                Main.GameViewMatrix.TransformationMatrix);

            foreach (Ripple ripple in _ripples)
            {
                float progress = ripple.Age / (float)ripple.Life;
                float radius = ripple.MaxRadius * SoAEasing.CircOut(progress);
                float fade = (1f - progress) * (1f - progress) * ripple.Strength;

                // Рябь видна там, куда падает свет: ночью и в тени — чуть-чуть
                Vector3 light = Lighting.GetColor(ripple.Position.ToTileCoordinates()).ToVector3();
                float brightness = Math.Max(Math.Max(light.X, Math.Max(light.Y, light.Z)), MinRippleBrightness);
                Color color = FoamColor * (fade * brightness);

                // Сбоку поверхность видна ребром: кольцо — это два гребня, бегущих в стороны,
                // и второй, слабее, вдогонку
                for (int wave = 0; wave < 2; wave++)
                {
                    float waveRadius = radius * (1f - 0.4f * wave);
                    float waveAlpha = wave == 0 ? 1f : 0.5f;
                    float length = 6f + waveRadius * 0.35f;
                    var scale = new Vector2(length / streak.Width, 1.6f / streak.Height);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 at = ripple.Position + new Vector2(side * waveRadius, -1f) - Main.screenPosition;
                        sb.Draw(streak, at, null, color * waveAlpha, 0f, origin, scale, SpriteEffects.None, 0f);
                    }
                }
            }

            sb.End();
        }

        #endregion
    }
}
