using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Graphics.CameraModifiers;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace SoA.Common.Graphics
{
    // Полноэкранная «звуковая волна» рёва (RoarShockwave.fx): круговой фронт рефракции,
    // расходящийся от точки в мире до ~половины экрана. Чистая косметика, работает
    // только на клиенте; вызывается из AI босса через Trigger().
    public class RoarShockwaveFx : ModSystem
    {
        public const string FilterKey = "SoA:RoarShockwave";
        private const int MaxWaves = 3;              // столько же фронтов разворачивает шейдер
        private const float WaveSpacing = 0.18f;     // сдвиг прогресса между фронтами каскада
        private const int ShakeTicks = 20;           // длительность толчка камеры на волну

        private static Vector2 _worldCenter;
        private static float _timer;
        private static float _duration;
        private static float _strength;
        private static int _waves;
        private static float _shakePerWave;
        private static int _wavesEmitted; // сколько фронтов уже стартовало (для толчков камеры)
        private static int _trackNpc = -1; // whoAmI NPC, за которым едет центр волны (босс движется во время рёва)
        private static int _trackNpcType;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            Filters.Scene[FilterKey] = new Filter(
                new ScreenShaderData(
                    Mod.Assets.Request<Effect>("Assets/Effects/RoarShockwave", AssetRequestMode.ImmediateLoad),
                    "RoarPass"),
                EffectPriority.VeryHigh);
        }

        // durationTicks — время расхождения волны, strength — сила искажения (~1).
        // waves > 1 — каскад последовательных фронтов (рёв финальной фазы);
        // shakePerWave > 0 — толчок камеры на старте каждого фронта.
        public static void Trigger(Vector2 worldCenter, float durationTicks = 50f, float strength = 1f,
            int waves = 1, float shakePerWave = 0f)
        {
            if (Main.dedServ)
                return;

            _worldCenter = worldCenter;
            _duration = durationTicks;
            _timer = 0f;
            _strength = strength;
            _waves = Math.Clamp(waves, 1, MaxWaves);
            _shakePerWave = shakePerWave;
            _wavesEmitted = 0;
            _trackNpc = -1;
            if (!Filters.Scene[FilterKey].IsActive())
                Filters.Scene.Activate(FilterKey);
        }

        // Вариант с привязкой к NPC: центр волны следует за боссом, пока тот жив
        public static void Trigger(NPC npc, float durationTicks = 50f, float strength = 1f,
            int waves = 1, float shakePerWave = 0f)
        {
            if (Main.dedServ)
                return;
            Trigger(npc.Center, durationTicks, strength, waves, shakePerWave);
            _trackNpc = npc.whoAmI;
            _trackNpcType = npc.type;
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ || !Filters.Scene[FilterKey].IsActive())
                return;

            _timer++;
            if (_timer >= _duration)
            {
                Filters.Scene.Deactivate(FilterKey);
                return;
            }

            // Центр едет за живым боссом; слот умер/переиспользован — остаёмся в последней точке
            if (_trackNpc >= 0)
            {
                NPC npc = Main.npc[_trackNpc];
                if (npc.active && npc.type == _trackNpcType)
                    _worldCenter = npc.Center;
                else
                    _trackNpc = -1;
            }

            float progress = _timer / _duration;

            // Толчок камеры на старте каждого нового фронта каскада
            int wavesStarted = Math.Min(1 + (int)(progress / WaveSpacing), _waves);
            while (_wavesEmitted < wavesStarted)
            {
                _wavesEmitted++;
                if (_shakePerWave > 0f)
                    Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                        _worldCenter, Main.rand.NextVector2Unit(), _shakePerWave, 6f, ShakeTicks, 1200f, FilterKey));
            }

            // Центр волны в экранных пикселях с учётом зума камеры
            Vector2 screenCenter = Vector2.Transform(
                _worldCenter - Main.screenPosition, Main.GameViewMatrix.TransformationMatrix);

            ScreenShaderData shader = Filters.Scene[FilterKey].GetShader()
                .UseTargetPosition(screenCenter)
                .UseProgress(progress)
                .UseIntensity(_strength)
                .UseOpacity(1f);
            shader.Shader.Parameters["uWaveCount"]?.SetValue((float)_waves);
            shader.Shader.Parameters["uWaveSpacing"]?.SetValue(WaveSpacing);
        }
    }
}
