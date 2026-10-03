using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Particles;

namespace SoA.Common.Players
{
    // Ботинки Скользящего по приливу. Под водой — «сухая» физика: вода не режет скорость.
    // Прыжок под водой — один невысокий гребок до следующего касания дна.
    // Зажатый прыжок копит давление: экран подрагивает, вокруг вскипают пузыри, и когда
    // давление набрано — игрока выстреливает вверх, сквозь толщу воды и в воздух.
    // Скорость правит только владелец персонажа: у чужих клиентов расчёт разошёлся бы
    // с их собственной симуляцией. Эффекты видят все — по синхронизированному controlJump.
    public class TideskimmerPlayer : ModPlayer
    {
        // Ванильная вода: gravity 0.2, maxFallSpeed 5, а WaterCollision сдвигает
        // игрока лишь на половину скорости. Ботинки возвращают «сухую» физику
        private const float DiveGravity = 0.2f;
        private const float DiveFallSpeed = 9f;
        private const float UnderwaterAcceleration = 1.6f;

        private const float SinkUrgeSpeed = 7f;         // тяга вниз при зажатом «вниз»
        private const float SinkUrgeResponse = 0.07f;

        private const float HopSpeed = 4.5f;            // невысокий гребок, один до касания дна

        // ---------- Выстрел ----------
        private const int ChargeDelayTicks = 10;        // короче — обычное нажатие, давление не копится
        private const int ChargeTicks = 45;             // столько держать, чтобы выстрелило
        private const float ChargeHoverDrag = 0.88f;    // пока копится, игрока почти держит на месте
        private const float JetStartSpeed = 9f;
        private const float JetMaxSpeed = 17f;
        private const float JetAcceleration = 0.8f;
        private const float JetSteer = 3f;              // сколько можно подруливать влево-вправо
        private const int JetMaxTicks = 150;            // бездонный водоём не держит в полёте вечно
        private const float BreachSpeed = 14f;          // скорость вылета из воды
        private const int LaunchGraceTicks = 120;       // столько после вылета не копим падение
        private const float MaxShake = 2.5f;            // px, тряска экрана на полном давлении

        private static readonly Color BubbleColor = new(190, 230, 255);
        private static readonly Color WaterStreak = new(150, 215, 255);
        private static readonly Color PressureGlow = new(90, 180, 255);

        public bool hasTideskimmerBoots;

        private bool _wasSubmerged;
        private bool _hopReady = true;
        private bool _wasHoldingJump;
        private bool _ignoreHeldJump;   // прыжок зажат ещё в воздухе — под водой не считается
        private int _holdTicks;
        private bool _jetting;
        private int _jetTicks;
        private float _lastY;
        private int _launchGrace;
        private float _velocityBeforeJump;

        // 0..1 — насколько набрано давление (для отрисовки)
        public float Pressure => MathHelper.Clamp((_holdTicks - ChargeDelayTicks) / (float)ChargeTicks, 0f, 1f);
        public bool Jetting => _jetting;

        private bool IsLocalPlayer => Player.whoAmI == Main.myPlayer;

        private bool Submerged => Player.wet && !Player.lavaWet && !Player.honeyWet && !Player.shimmerWet;

        public override void ResetEffects()
        {
            hasTideskimmerBoots = false;
        }

        public override void PostUpdateRunSpeeds()
        {
            if (!hasTideskimmerBoots || !Submerged)
                return;

            // ignoreWater уводит игрока на DryCollision — вода перестаёт резать скорость вдвое
            Player.ignoreWater = true;
            Player.gravity = DiveGravity;
            Player.maxFallSpeed = DiveFallSpeed;
            Player.runAcceleration *= UnderwaterAcceleration;
            _velocityBeforeJump = Player.velocity.Y;
        }

        public override void PreUpdateMovement()
        {
            if (!hasTideskimmerBoots || Player.dead)
            {
                ResetDive();
                _launchGrace = 0;
                _wasSubmerged = false;
                return;
            }

            bool submerged = Submerged;

            if (submerged && !_wasSubmerged)
                EnterWater();
            else if (!submerged && _wasSubmerged)
                LeaveWater();

            if (submerged)
            {
                UpdateUnderwater();
            }
            else if (_launchGrace > 0)
            {
                _launchGrace--;
                // Ботинки гасят приземление после собственного вылета
                Player.fallStart = (int)(Player.position.Y / 16f);
            }

            _wasSubmerged = submerged;
            _lastY = Player.position.Y;
        }

        // Лёгкое дрожание камеры, пока копится давление, — только у самого игрока
        public override void ModifyScreenPosition()
        {
            if (!IsLocalPlayer || !hasTideskimmerBoots)
                return;

            float shake = _jetting ? MaxShake * 0.6f : MaxShake * Pressure * Pressure;
            if (shake > 0.05f)
                Main.screenPosition += Main.rand.NextVector2Circular(shake, shake);
        }

        private void EnterWater()
        {
            _hopReady = true;
            _holdTicks = 0;
            _ignoreHeldJump = Player.controlJump;
            _wasHoldingJump = Player.controlJump;
            // Всплеск входа общий для всех — TideWaterFeelPlayer
        }

        private void UpdateUnderwater()
        {
            Player.fallStart = (int)(Player.position.Y / 16f); // об дно на нырке не убиться

            // Ванильный прыжок под водой не нужен: он давал обычную высоту прыжка
            if (Player.jump > 0)
            {
                Player.jump = 0;
                if (IsLocalPlayer && !_jetting)
                    Player.velocity.Y = _velocityBeforeJump;
            }

            if (OnFloor())
                _hopReady = true;

            bool holding = Player.controlJump;
            bool pressed = holding && !_wasHoldingJump;
            _wasHoldingJump = holding;
            if (!holding)
                _ignoreHeldJump = false;

            if (_jetting)
            {
                UpdateJet();
                return;
            }

            if (pressed && !_ignoreHeldJump && _hopReady)
            {
                _hopReady = false;
                if (IsLocalPlayer)
                    Player.velocity.Y = -HopSpeed;
                if (!Main.dedServ)
                    HopPuff();
            }

            if (holding && !_ignoreHeldJump)
            {
                _holdTicks++;
                if (_holdTicks > ChargeDelayTicks)
                    UpdateCharge();
                if (_holdTicks >= ChargeDelayTicks + ChargeTicks)
                    StartJet();
            }
            else
            {
                _holdTicks = 0;
                if (IsLocalPlayer && Player.controlDown)
                    Player.velocity.Y = MathHelper.Lerp(Player.velocity.Y, SinkUrgeSpeed, SinkUrgeResponse);
            }

            if (!Main.dedServ && _holdTicks == 0 && Player.velocity.LengthSquared() > 9f && Main.rand.NextBool(4))
            {
                SoAParticles.SpawnBubble(Player.Bottom + Main.rand.NextVector2Circular(8f, 4f),
                    -Player.velocity * 0.1f, Main.rand.NextFloat(4f, 7f), BubbleColor, 60);
            }
        }

        private bool OnFloor()
            => Player.velocity.Y >= 0f && Collision.SolidCollision(Player.BottomLeft, Player.width, 4, true);

        // Давление: игрока придерживает, пузыри вскипают всё гуще, у ног разгорается свечение
        private void UpdateCharge()
        {
            float pressure = Pressure;
            if (IsLocalPlayer)
                Player.velocity *= ChargeHoverDrag;

            if (Main.dedServ)
                return;

            if (_holdTicks == ChargeDelayTicks + 1)
                SoundEngine.PlaySound(SoundID.Item85 with { Volume = 0.5f, Pitch = -0.4f }, Player.Center);

            if (Main.rand.NextFloat() < 0.35f + 0.65f * pressure)
            {
                Vector2 offset = new(Main.rand.NextFloat(-18f, 18f), Main.rand.NextFloat(-6f, 22f));
                SoAParticles.SpawnBubble(Player.Center + offset,
                    new Vector2(-offset.X * 0.03f, -Main.rand.NextFloat(0.5f, 1.5f)),
                    Main.rand.NextFloat(3f, 6f + 6f * pressure), BubbleColor, Main.rand.NextFloat() < 0.5f ? 50 : 80);
            }
            if (Main.GameUpdateCount % 6 == 0)
                SoAParticles.SpawnGlow(Player.Bottom, Vector2.Zero, PressureGlow * (0.25f + 0.45f * pressure), 20f, 50f + 30f * pressure, 12);
            Lighting.AddLight(Player.Bottom, PressureGlow.ToVector3() * (0.2f + 0.5f * pressure));
        }

        private void StartJet()
        {
            _jetting = true;
            _jetTicks = 0;
            _holdTicks = 0;
            _hopReady = false;

            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item85 with { Volume = 0.9f, Pitch = 0.2f }, Player.Center);
            SoundEngine.PlaySound(SoundID.Splash with { Volume = 0.8f, Pitch = -0.3f }, Player.Center);
            SoAParticles.SpawnGlow(Player.Bottom, Vector2.Zero, PressureGlow, 30f, 110f, 14);
            for (int i = 0; i < 24; i++)
            {
                Vector2 dir = new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(0.2f, 1f)).SafeNormalize(Vector2.UnitY);
                SoAParticles.SpawnBubble(Player.Bottom, dir * Main.rand.NextFloat(2f, 6f), Main.rand.NextFloat(4f, 10f), BubbleColor, 70);
            }
        }

        // Струя: вверх с разгоном, влево-вправо можно подруливать; упёрся в потолок — выдохлась
        private void UpdateJet()
        {
            _jetTicks++;
            bool stalled = _jetTicks > 3 && Math.Abs(Player.position.Y - _lastY) < 1f;
            if (_jetTicks > JetMaxTicks || stalled)
            {
                _jetting = false;
                return;
            }

            if (IsLocalPlayer)
            {
                float speed = Math.Min(JetStartSpeed + _jetTicks * JetAcceleration, JetMaxSpeed);
                Player.velocity.Y = -speed;
                float steer = (Player.controlRight ? 1f : 0f) - (Player.controlLeft ? 1f : 0f);
                Player.velocity.X = MathHelper.Lerp(Player.velocity.X, steer * JetSteer, 0.15f);
            }

            if (Main.dedServ)
                return;

            for (int i = 0; i < 3; i++)
            {
                SoAParticles.SpawnBubble(Player.Bottom + new Vector2(Main.rand.NextFloat(-10f, 10f), Main.rand.NextFloat(0f, 12f)),
                    new Vector2(Main.rand.NextFloatDirection() * 1.2f, Main.rand.NextFloat(0.5f, 2.5f)),
                    Main.rand.NextFloat(3f, 9f), BubbleColor, Main.rand.Next(40, 70));
            }
            SoAParticles.SpawnStreak(Player.Bottom + new Vector2(Main.rand.NextFloat(-8f, 8f), 0f),
                new Vector2(0f, Main.rand.NextFloat(3f, 6f)), WaterStreak * 0.7f, 2f, gravity: 0f, life: 12, lengthPerSpeed: 3f);
            Lighting.AddLight(Player.Center, PressureGlow.ToVector3() * 0.5f);
        }

        private void LeaveWater()
        {
            if (_jetting)
            {
                if (IsLocalPlayer)
                    Player.velocity.Y = -BreachSpeed;
                _launchGrace = LaunchGraceTicks;

                if (!Main.dedServ)
                    BreachSplash();
            }
            ResetDive();
        }

        private void ResetDive()
        {
            _jetting = false;
            _jetTicks = 0;
            _holdTicks = 0;
            _hopReady = true;
            _wasHoldingJump = false;
            _ignoreHeldJump = false;
        }

        #region Эффекты

        private void HopPuff()
        {
            for (int i = 0; i < 6; i++)
            {
                SoAParticles.SpawnBubble(Player.Bottom + new Vector2(Main.rand.NextFloat(-10f, 10f), 0f),
                    new Vector2(Main.rand.NextFloatDirection() * 1.5f, Main.rand.NextFloat(0.2f, 1.2f)),
                    Main.rand.NextFloat(3f, 6f), BubbleColor, 50);
            }
            SoundEngine.PlaySound(SoundID.SplashWeak with { Volume = 0.4f, Pitch = 0.4f }, Player.Bottom);
        }

        // Вылет из воды: столб брызг вверх, веер капель в стороны, вспышка
        private void BreachSplash()
        {
            Vector2 at = Player.Bottom;
            SoundEngine.PlaySound(SoundID.Splash with { Pitch = 0.4f }, at);
            for (int i = 0; i < 26; i++)
            {
                float spread = Main.rand.NextFloat(-1f, 1f);
                Vector2 velocity = new(spread * 4f, -Main.rand.NextFloat(4f, 11f) * (1f - 0.5f * Math.Abs(spread)));
                SoAParticles.SpawnStreak(at + new Vector2(spread * 14f, 0f), velocity, WaterStreak,
                    Main.rand.NextFloat(1.8f, 3f), gravity: 0.3f, life: Main.rand.Next(24, 40), lengthPerSpeed: 2.4f);
            }
            SoAParticles.SpawnGlow(at, Vector2.Zero, WaterStreak * 0.6f, 24f, 90f, 12);
            for (int i = 0; i < 14; i++)
            {
                Dust drop = Dust.NewDustDirect(at - new Vector2(16f, 6f), 32, 12, DustID.Water);
                drop.velocity = new Vector2(Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat(2f, 6f));
                drop.scale = Main.rand.NextFloat(0.9f, 1.5f);
            }
        }

        #endregion
    }
}
