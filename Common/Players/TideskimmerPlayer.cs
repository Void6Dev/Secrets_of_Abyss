using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Common.Players
{
    // Ботинки Скользящего по приливу. Вместо хождения по воде — «дельфиний нырок»:
    // вода не гасит разгон, а глубина нырка копится в заряд. Под водой игрок
    // только невысоко подпрыгивает гребками, никакого подъёма самотёком — весь
    // заряд отдаётся разом, когда игрок пробивает поверхность и вылетает в воздух.
    // Скорость правит только владелец персонажа: у чужих клиентов расчёт
    // разошёлся бы с их собственной симуляцией.
    public class TideskimmerPlayer : ModPlayer
    {
        // Ванильная вода: gravity 0.2, maxFallSpeed 5, а WaterCollision сдвигает
        // игрока лишь на половину скорости. Ботинки возвращают «сухую» физику.
        private const float DiveGravity = 0.2f;
        private const float DiveFallSpeed = 9f;
        private const float UnderwaterAcceleration = 1.6f;

        private const float SinkUrgeSpeed = 7f;         // тяга вниз при зажатом «вниз»
        private const float SinkUrgeResponse = 0.07f;

        private const float SwimStrokeSpeed = 4f;       // низкий гребок по прыжку

        private const float NoChargeDepth = 4f;         // тайлов: мельче нырок не считается
        private const float FullChargeDepth = 26f;
        private const float LaunchSpeed = 5.5f;
        private const float LaunchSpeedPerCharge = 8f;
        private const float LaunchMinRiseSpeed = 1f;    // поверхность надо пробить, а не выползти
        private const float LaunchHorizontalPerCharge = 0.45f;
        private const int LaunchGraceTicks = 120;       // столько после вылета не копим падение

        public bool hasTideskimmerBoots;

        private bool _wasSubmerged;
        private float _entryY;
        private float _deepestY;
        private float _charge;
        private bool _fullChargeAnnounced;
        private bool _wasHoldingJump;
        private int _launchGrace;

        public float DiveCharge => _charge;

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
        }

        public override void PreUpdateMovement()
        {
            if (!hasTideskimmerBoots || Player.dead)
            {
                EndDive();
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
                UpdateDive();
            }
            else if (_launchGrace > 0)
            {
                _launchGrace--;
                // Ботинки гасят приземление после собственного вылета
                Player.fallStart = (int)(Player.position.Y / 16f);
            }

            _wasSubmerged = submerged;
        }

        private void EnterWater()
        {
            _entryY = Player.Center.Y;
            _deepestY = _entryY;
            _charge = 0f;
            _fullChargeAnnounced = false;
            // Зажатый ещё в воздухе прыжок не считается нажатием под водой
            _wasHoldingJump = Player.controlJump;

            if (Main.netMode != NetmodeID.Server && Player.velocity.Y > 6f)
                SplashBurst(12, 3.5f);
        }

        private void UpdateDive()
        {
            Player.fallStart = (int)(Player.position.Y / 16f); // об дно на нырке не убиться

            _deepestY = Math.Max(_deepestY, Player.Center.Y);
            float depthTiles = (_deepestY - _entryY) / 16f;
            _charge = MathHelper.Clamp((depthTiles - NoChargeDepth) / (FullChargeDepth - NoChargeDepth), 0f, 1f);

            // Один гребок на одно нажатие: зажатая клавиша не повторяется,
            // клавишу нужно отпустить и нажать снова
            bool pressedJump = Player.controlJump && !_wasHoldingJump;
            _wasHoldingJump = Player.controlJump;

            if (IsLocalPlayer)
            {
                // Ванильный подводный прыжок просит velocity.Y == 0, а с ignoreWater
                // игрок постоянно тонет и в это окно не попадает — гребок делаем сами
                if (pressedJump)
                    Player.velocity.Y = -SwimStrokeSpeed;
                else if (Player.controlDown)
                    Player.velocity.Y = MathHelper.Lerp(Player.velocity.Y, SinkUrgeSpeed, SinkUrgeResponse);
            }

            if (Main.netMode == NetmodeID.Server)
                return;

            if (_charge >= 1f && !_fullChargeAnnounced)
            {
                _fullChargeAnnounced = true;
                SoundEngine.PlaySound(SoundID.SplashWeak with { Volume = 0.6f, Pitch = 0.8f }, Player.Center);
                SplashBurst(14, 2f);
            }

            if (_charge > 0f && Player.velocity.LengthSquared() > 9f)
                TrailBubbles(0.25f + 0.5f * _charge);
        }

        private void LeaveWater()
        {
            if (_charge > 0f && Player.velocity.Y < -LaunchMinRiseSpeed)
            {
                if (IsLocalPlayer)
                {
                    Player.velocity.Y = -(LaunchSpeed + LaunchSpeedPerCharge * _charge);
                    Player.velocity.X *= 1f + LaunchHorizontalPerCharge * _charge;
                }

                _launchGrace = LaunchGraceTicks;

                if (Main.netMode != NetmodeID.Server)
                {
                    SoundEngine.PlaySound(SoundID.Splash with { Pitch = 0.3f + 0.3f * _charge }, Player.Center);
                    SplashBurst(18 + (int)(22f * _charge), 4f + 4f * _charge);
                }
            }

            EndDive();
        }

        private void EndDive()
        {
            _charge = 0f;
            _fullChargeAnnounced = false;
            _wasHoldingJump = false;
        }

        private void TrailBubbles(float chance)
        {
            if (Main.rand.NextFloat() >= chance)
                return;

            Dust bubble = Dust.NewDustDirect(Player.position, Player.width, Player.height, DustID.Water);
            bubble.velocity = -Player.velocity * 0.15f - new Vector2(0f, Main.rand.NextFloat(0.4f, 1.2f));
            bubble.noGravity = true;
            bubble.scale = 0.6f + 0.5f * _charge;
            bubble.alpha = 90;
        }

        private void SplashBurst(int dropCount, float upwardSpeed)
        {
            for (int i = 0; i < dropCount; i++)
            {
                Dust drop = Dust.NewDustDirect(Player.BottomLeft - new Vector2(6f, 8f),
                    Player.width + 12, 16, DustID.Water);
                drop.velocity = new Vector2(Main.rand.NextFloat(-2.5f, 2.5f),
                    -Main.rand.NextFloat(0.5f, 1f) * upwardSpeed);
                drop.scale = Main.rand.NextFloat(0.8f, 1.4f);
            }
        }
    }
}
