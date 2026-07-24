using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Graphics
{
    // Кинематографический захват камеры: плавно уводит взгляд на точку в мире
    // (босс на рёве/смене фазы), держит и плавно возвращает игроку.
    // Чистая косметика, работает только на клиенте; вызывается из AI через Focus().
    public class CameraFocusFx : ModSystem
    {
        private static Vector2 _target;
        private static int _timer;
        private static int _duration;  // полная длительность, тиков
        private static int _ease;      // длительность въезда/выезда камеры
        private static int _trackNpc = -1; // whoAmI NPC — фокус едет за движущимся боссом
        private static int _trackNpcType;

        public static void Focus(Vector2 worldTarget, int durationTicks = 70, int easeTicks = 20)
        {
            if (Main.dedServ)
                return;
            _target = worldTarget;
            _duration = durationTicks;
            _ease = System.Math.Max(1, easeTicks);
            _timer = 0;
            _trackNpc = -1;
        }

        // Вариант с привязкой к NPC: камера следует за боссом, пока тот жив
        public static void Focus(NPC npc, int durationTicks = 70, int easeTicks = 20)
        {
            if (Main.dedServ)
                return;
            Focus(npc.Center, durationTicks, easeTicks);
            _trackNpc = npc.whoAmI;
            _trackNpcType = npc.type;
        }

        public override void ModifyScreenPosition()
        {
            if (_duration <= 0)
                return;

            if (_trackNpc >= 0)
            {
                NPC npc = Main.npc[_trackNpc];
                if (npc.active && npc.type == _trackNpcType)
                    _target = npc.Center;
                else
                    _trackNpc = -1; // босс исчез — доигрываем от последней точки
            }

            _timer++;
            if (_timer >= _duration)
            {
                _duration = 0;
                return;
            }

            // Вес фокуса: плавный въезд → удержание → плавный выезд
            float weight;
            if (_timer < _ease)
                weight = MathHelper.SmoothStep(0f, 1f, _timer / (float)_ease);
            else if (_timer > _duration - _ease)
                weight = MathHelper.SmoothStep(0f, 1f, (_duration - _timer) / (float)_ease);
            else
                weight = 1f;

            Vector2 desired = _target - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
            Vector2 pos = Vector2.Lerp(Main.screenPosition, desired, weight);

            // Не выезжаем за края мира (океан — у самой границы карты)
            pos.X = MathHelper.Clamp(pos.X, 16f, Main.maxTilesX * 16f - Main.screenWidth - 16f);
            pos.Y = MathHelper.Clamp(pos.Y, 16f, Main.maxTilesY * 16f - Main.screenHeight - 16f);
            Main.screenPosition = pos;
        }
    }
}
