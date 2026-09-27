using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using SoA.Common.Graphics.Particles;

namespace SoA.Content.Items.Accessories
{
    // Нимб дыхания. Работает только в большом и глубоком водоёме: под водой воздух тратится
    // втрое медленнее, вокруг носителя мягкий золотистый свет. Плавать не учит.
    public class HOBusage : ModPlayer
    {
        private const int DeepCheckInterval = 10;      // водоём пересчитываем не каждый тик
        private const int ScanHalfWidth = 12;          // область 25 × 21 тайлов вокруг головы
        private const int ScanHalfHeight = 10;
        private const float RequiredWaterShare = 0.45f;
        private const int MinColumnDepth = 10;         // тайлов воды по вертикали через голову
        private const int MaxColumnScan = 40;
        private const int BreathSlowdown = 3;          // во сколько раз медленнее тратится воздух
        private const float FadeSpeed = 0.05f;
        private const int FrameTicks = 6;

        private static readonly Color HaloGold = new(255, 225, 140);
        private static readonly Color BubbleColor = new(210, 235, 255);

        public bool hasHaloOfBreathing;
        public bool showHaloVisual;

        public int haloFrame;
        public int haloFrameCounter;

        private int _checkTimer;
        private int _breathTick;

        public bool InDeepWater { get; private set; }

        // 0..1 — нимб плавно разгорается в глубокой воде и гаснет вне её
        public float Glow { get; private set; }

        // Воздуха меньше трети — нимб пульсирует чаще
        public bool LowBreath => InDeepWater && Player.breath < Player.breathMax / 3;

        public override void ResetEffects()
        {
            hasHaloOfBreathing = false;
            showHaloVisual = false;
        }

        public override void PostUpdate()
        {
            if (!hasHaloOfBreathing)
            {
                InDeepWater = false;
                Glow = 0f;
                return;
            }

            if (++_checkTimer >= DeepCheckInterval)
            {
                _checkTimer = 0;
                bool deep = IsInDeepWater();
                if (deep && !InDeepWater && !Main.dedServ)
                    PlayActivationEffect();
                InDeepWater = deep;
            }

            SlowBreathLoss();

            Glow = MathHelper.Clamp(Glow + (InDeepWater ? FadeSpeed : -FadeSpeed), 0f, 1f);
            if (Glow <= 0f)
                return;

            if (!Main.dedServ)
                AmbientEffects();

            if (++haloFrameCounter >= FrameTicks)
            {
                haloFrameCounter = 0;
                haloFrame = (haloFrame + 1) % HaloOfBreathingDrawLayer.FrameCount;
            }
        }

        // Ванильный отсчёт дыхания растёт на 1 за тик; откатываем его в двух тиках из трёх,
        // и воздух уходит втрое медленнее. Дыхание — забота владельца персонажа
        private void SlowBreathLoss()
        {
            if (Player.whoAmI != Main.myPlayer || !InDeepWater || Player.breathCD <= 0)
                return;

            _breathTick = (_breathTick + 1) % BreathSlowdown;
            if (_breathTick != 0)
                Player.breathCD--;
        }

        // Голова под водой, через неё проходит толща воды не меньше MinColumnDepth,
        // и вода занимает заметную часть области вокруг — лужи и узкие колодцы не считаются
        private bool IsInDeepWater()
        {
            Point head = (Player.Top + new Vector2(0f, 6f)).ToTileCoordinates();
            if (!IsWater(head.X, head.Y))
                return false;

            int column = 1;
            for (int y = head.Y - 1; column < MaxColumnScan && IsWater(head.X, y); y--)
                column++;
            for (int y = head.Y + 1; column < MaxColumnScan && IsWater(head.X, y); y++)
                column++;
            if (column < MinColumnDepth)
                return false;

            int water = 0;
            int total = (ScanHalfWidth * 2 + 1) * (ScanHalfHeight * 2 + 1);
            for (int x = -ScanHalfWidth; x <= ScanHalfWidth; x++)
            {
                for (int y = -ScanHalfHeight; y <= ScanHalfHeight; y++)
                {
                    if (IsWater(head.X + x, head.Y + y))
                        water++;
                }
            }
            return water >= total * RequiredWaterShare;
        }

        private static bool IsWater(int x, int y)
        {
            if (!WorldGen.InWorld(x, y))
                return false;
            Tile tile = Main.tile[x, y];
            return tile.LiquidAmount > 128 && tile.LiquidType == LiquidID.Water;
        }

        public Vector2 HaloWorldPosition => Player.Top + new Vector2(0f, -8f + Player.gfxOffY);

        private void AmbientEffects()
        {
            float pulse = LowBreath ? 0.8f + 0.2f * (float)System.Math.Sin(Main.GameUpdateCount * 0.3f) : 1f;
            Lighting.AddLight(Player.Center, HaloGold.ToVector3() * (0.65f * Glow * pulse));

            Vector2 halo = HaloWorldPosition;
            if (Main.rand.NextBool(12))
            {
                SoAParticles.SpawnGlow(halo + new Vector2(Main.rand.NextFloat(-10f, 10f), 0f),
                    new Vector2(Main.rand.NextFloatDirection() * 0.2f, -Main.rand.NextFloat(0.3f, 0.8f)),
                    HaloGold * (0.6f * Glow), 6f, 2f, Main.rand.Next(40, 70));
            }
            if (Main.rand.NextBool(LowBreath ? 5 : 14))
            {
                SoAParticles.SpawnBubble(Player.Center + Main.rand.NextVector2Circular(8f, 12f),
                    new Vector2(0f, -Main.rand.NextFloat(0.3f, 1f)), Main.rand.NextFloat(3f, 6f), BubbleColor, 80);
            }
        }

        // Вход в глубокую воду: кольцо золотых искр от нимба, вспышка и перезвон
        private void PlayActivationEffect()
        {
            Vector2 halo = HaloWorldPosition;
            SoundEngine.PlaySound(SoundID.Item29 with { Volume = 0.35f, Pitch = 0.7f }, halo);
            SoAParticles.SpawnGlow(halo, Vector2.Zero, HaloGold * 0.8f, 20f, 90f, 16);
            for (int i = 0; i < 16; i++)
            {
                Vector2 dir = (MathHelper.TwoPi * i / 16f).ToRotationVector2();
                SoAParticles.SpawnStreak(halo + dir * 6f, dir * 3f, HaloGold, 1.6f, gravity: 0f, life: 18, lengthPerSpeed: 2.5f);
            }
            for (int i = 0; i < 10; i++)
            {
                SoAParticles.SpawnBubble(Player.Center + Main.rand.NextVector2Circular(12f, 16f),
                    new Vector2(Main.rand.NextFloatDirection() * 0.8f, -Main.rand.NextFloat(0.5f, 2f)),
                    Main.rand.NextFloat(3f, 8f), BubbleColor, 70);
            }
        }
    }
}
