using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using SoA.Common.Graphics.Atmosphere;
using SoA.Common.Graphics.Particles;
using SoA.Common.Systems;
using SoA.Common.Utils;
using SoA.Content.Worldgen;

namespace SoA.Common.Players
{
    // Ощущение воды Прилива для игрока.
    //   Графика (всегда, у всех клиентов, по синхронным позиции и скорости):
    //     всплеск при входе — по силе удара, капли при выходе и ещё пару секунд после,
    //     пузыри дыхания изо рта — реже в покое, чаще в рывке и когда кончается воздух.
    //   Физика (экспериментальная, SoAClientConfig.ExperimentalWaterPhysics, только у владельца):
    //     инерция — разгон и торможение мягче, тело скользит; плавучесть — у поверхности
    //     тянет вверх, в глубине почти нейтрально; нырок с высоты уводит глубже;
    //     течения (TideCurrents) сносят. С ботинками Прилива не работает — у них свой режим
    public class TideWaterFeelPlayer : ModPlayer
    {
        // ---------- Графика ----------
        private const float SplashMinSpeed = 2f;
        private const float SplashFullSpeed = 14f;
        private const int DripTicks = 120;
        private const int BreathIntervalCalm = 100;
        private const int BreathIntervalActive = 30;
        private const int BreathIntervalGasping = 14;
        private const float ActiveSwimSpeed = 3.5f;
        private static readonly Color BubbleColor = new(170, 215, 255);

        // ---------- Физика ----------
        private const float GlideAcceleration = 0.55f;    // доля ванильного разгона
        private const float GlideSlowdown = 0.3f;         // доля ванильного торможения: тело скользит
        private const float BuoyancyDepthTiles = 6f;      // глубже — почти нейтральная плавучесть
        private const float SurfaceBuoyancy = 1.15f;      // доля гравитации, что тянет вверх у поверхности
        private const float DeepBuoyancy = 0.85f;
        private const float VerticalDrag = 0.97f;
        private const float DiveCarry = 0.7f;             // доля скорости падения, что уходит в нырок
        private const float DiveMinSpeed = 6f;
        private const int DiveTicks = 30;
        private const float CurrentPush = 0.06f;          // как быстро течение подхватывает тело
        private const float WaterGravity = 0.2f;          // ванильная гравитация в воде

        private bool _wasInWater;
        private Vector2 _lastVelocity;
        private int _dripTicks;
        private int _breathTimer = BreathIntervalCalm;
        private int _diveTicks;
        private float _diveSpeed;

        private bool InTideWater => Player.wet && !Player.lavaWet && !Player.honeyWet && !Player.shimmerWet
            && TideOfShadowsWorldData.Contains(Player.Center);

        private bool PhysicsActive => Player.whoAmI == Main.myPlayer && TideCurrents.Enabled && InTideWater
            && !Player.GetModPlayer<TideskimmerPlayer>().hasTideskimmerBoots && Player.mount?.Active != true;

        #region Физика

        public override void PostUpdateRunSpeeds()
        {
            if (!PhysicsActive)
                return;

            Player.runAcceleration *= GlideAcceleration;
            Player.runSlowdown *= GlideSlowdown;
            if (_diveTicks > 0)
                Player.maxFallSpeed = Math.Max(Player.maxFallSpeed, _diveSpeed);
        }

        public override void PreUpdateMovement()
        {
            if (!PhysicsActive)
            {
                _diveTicks = 0;
                return;
            }

            // Нырок: часть скорости падения переживает удар о воду и тает за DiveTicks
            if (_diveTicks > 0)
            {
                float carry = _diveSpeed * (_diveTicks / (float)DiveTicks);
                Player.velocity.Y = Math.Max(Player.velocity.Y, carry);
                _diveTicks--;
            }
            else
            {
                ApplyBuoyancy();
            }

            Player.velocity += TideCurrents.At(Player.Center) * CurrentPush;
        }

        // Без ввода по вертикали тело само ищет равновесие: у поверхности всплывает,
        // в глубине почти висит. Гравитацию воды не отменяем — только уравновешиваем
        private void ApplyBuoyancy()
        {
            if (Player.controlUp || Player.controlDown || Player.controlJump)
                return;

            float depthTiles = BuoyancyDepthTiles;
            if (TideWaterFx.TryFindSurface(Player.Center, out Vector2 surface))
                depthTiles = Math.Min((Player.Center.Y - surface.Y) / 16f, BuoyancyDepthTiles);

            float share = MathHelper.Lerp(SurfaceBuoyancy, DeepBuoyancy, depthTiles / BuoyancyDepthTiles);
            Player.velocity.Y -= WaterGravity * share;
            Player.velocity.Y *= VerticalDrag;
        }

        #endregion

        #region Графика

        public override void PostUpdate()
        {
            bool inWater = InTideWater;

            if (inWater && !_wasInWater)
                OnEnterWater();
            else if (!inWater && _wasInWater)
                OnLeaveWater();

            if (!Main.dedServ)
            {
                if (_dripTicks > 0)
                    Drip(inWater);
                if (inWater)
                    Breathe();
            }

            _wasInWater = inWater;
            _lastVelocity = Player.velocity;
        }

        private void OnEnterWater()
        {
            // Скорость прошлого тика: в тик входа игра уже срезала её водой
            float speed = _lastVelocity.Length();
            if (speed >= SplashMinSpeed && !Main.dedServ && TideWaterFx.TryFindSurface(Player.Bottom - new Vector2(0f, 2f), out Vector2 surface))
                TideWaterFx.EntrySplash(surface, (speed - SplashMinSpeed) / (SplashFullSpeed - SplashMinSpeed), _lastVelocity);

            if (PhysicsActive && _lastVelocity.Y > DiveMinSpeed)
            {
                _diveSpeed = _lastVelocity.Y * DiveCarry;
                _diveTicks = DiveTicks;
            }
            _dripTicks = 0;
        }

        private void OnLeaveWater()
        {
            _dripTicks = DripTicks;
            if (Main.dedServ)
                return;

            float speed = Math.Max(-Player.velocity.Y, 0f);
            var surface = new Vector2(Player.Center.X, Player.Bottom.Y);
            TideWaterFx.ExitSplash(surface, speed / SplashFullSpeed + 0.2f);
        }

        // Мокрое тело капает: сначала часто, потом всё реже
        private void Drip(bool inWater)
        {
            if (inWater)
            {
                _dripTicks = 0;
                return;
            }

            float wetness = _dripTicks / (float)DripTicks;
            if (Main.rand.NextFloat() < 0.35f * wetness * wetness)
                TideWaterFx.DripFrom(Player.Hitbox);
            _dripTicks--;
        }

        // Пузыри изо рта, пока голова под водой. Жабры и прочие «дышащие» эффекты не мешают:
        // пузыри — это видимость, а не расход воздуха
        private void Breathe()
        {
            Vector2 mouth = Player.Center + new Vector2(Player.direction * 6f, -10f * Player.gravDir);
            if (!SoACombat.IsInWater(mouth) || --_breathTimer > 0)
                return;

            bool gasping = Player.breath < Player.breathMax / 3;
            bool active = Player.velocity.Length() > ActiveSwimSpeed;
            _breathTimer = gasping ? BreathIntervalGasping : active ? BreathIntervalActive : BreathIntervalCalm;
            _breathTimer += Main.rand.Next(-_breathTimer / 4, _breathTimer / 4 + 1);

            int count = gasping ? Main.rand.Next(2, 5) : Main.rand.Next(1, 3);
            for (int i = 0; i < count; i++)
            {
                Vector2 velocity = new(Player.direction * Main.rand.NextFloat(0.2f, 0.7f), -Main.rand.NextFloat(0.3f, 0.9f));
                float size = gasping ? Main.rand.NextFloat(4f, 7f) : Main.rand.NextFloat(2.5f, 4.5f);
                SoAParticles.SpawnBubble(mouth + Main.rand.NextVector2Circular(2f, 2f), velocity, size, BubbleColor, 120);
            }
        }

        #endregion
    }
}
