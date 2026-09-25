using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Systems
{
    // Сетка возмущений растительности: кто и в какую сторону только что задел тайл.
    //
    // Ваниль такую сетку держит сама — Main.instance.TilesRenderer.Wind, читается
    // через GetWindGridPush. Своя нужна по трём причинам:
    //
    //  1. Ванильная сканирует только игроков (WindGrid.ScanPlayers), а записать в неё
    //     чужой толчок нельзя: SetWindTime закрыт. NPC и снаряды сквозь заросли
    //     проходили бы как сквозь пустоту.
    //  2. Ванильная форма толчка — треугольник: наклон нарастает от нуля и падает
    //     обратно. Пока игрок стоит в растении, тайл помечается каждый кадр, время
    //     не набегает, и наклон равен нулю — растение стоит прямо сквозь игрока.
    //     Здесь толчок начинается с максимума и гаснет затухающими колебаниями:
    //     стебель отжат, пока сквозь него плывут, и после отпускает с отдачей.
    //  3. Сила ванильного толчка не зависит от того, кто задел. Здесь она берётся
    //     от скорости и размера — кит гнёт лес сильнее рыбёшки.
    //
    // Всё чисто клиентское и синхронизации не требует: считается из позиций
    // сущностей, которые и так разосланы по сети.
    public sealed class TideFloraWind : ModSystem
    {
        // Толчок гаснет за это время. Дольше — и лес «дышит» после каждого проплыва
        public const int PushCycleTicks = 34;

        // Столько раз стебель качнётся туда-обратно, прежде чем встанет
        private const float SwingCount = 1.25f;

        private const float MinPushSpeed = 0.6f;    // медленнее — это не движение
        private const float SpeedForFullPush = 8f;  // на этой скорости толчок в полную силу

        private const float PlayerPushScale = 1f;
        private const float ProjectilePushScale = 0.5f;
        private const float SmallNpcPushScale = 0.7f;
        private const float LargeNpcPushScale = 1.4f;
        private const int LargeNpcWidth = 64;

        // Запас вокруг экрана: то, что за краем, попадёт в кадр через миг
        private const int ScreenPadding = 16 * 8;

        private struct Touch
        {
            public int Time;          // 0 — тайла никто не касался
            public sbyte DirectionX;
            public float Strength;
        }

        private static TideFloraWind _instance;

        // Сетка накрывает экран и заворачивается по модулю — так же устроена
        // ванильная. Дальние тайлы наложатся друг на друга, но их не видно
        private Touch[,] _grid = new Touch[1, 1];
        private int _width = 1;
        private int _height = 1;
        private int _time;
        private Rectangle _screenArea;

        public override void OnModLoad() => _instance = this;

        public override void Unload() => _instance = null;

        // Наклон в долях от -1 до 1 для тайла (i, j). Ноль — никто не задевал
        public static float PushAt(int i, int j)
        {
            TideFloraWind wind = _instance;
            if (wind == null || i < 0 || j < 0)
                return 0f;

            // Настройку выключили посреди взмаха — время в сетке замерло бы,
            // и половина леса осталась стоять отжатой
            if (!Main.SettingsEnabled_TilesSwayInWind)
                return 0f;

            Touch touch = wind._grid[i % wind._width, j % wind._height];
            if (touch.Time == 0)
                return 0f;

            int elapsed = wind._time - touch.Time;
            if (elapsed < 0 || elapsed >= PushCycleTicks)
                return 0f;

            // Начинаем с максимума и гасим квадратичной огибающей: пока сквозь
            // растение плывут, тайл метится каждый кадр и наклон держится в пике
            float phase = elapsed / (float)PushCycleTicks;
            float envelope = (1f - phase) * (1f - phase);
            return MathF.Cos(phase * MathF.PI * 2f * SwingCount)
                * envelope * touch.Strength * touch.DirectionX;
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ || !Main.SettingsEnabled_TilesSwayInWind)
                return;

            _time++;
            EnsureSize();
            _screenArea = new Rectangle(
                (int)Main.screenPosition.X - ScreenPadding,
                (int)Main.screenPosition.Y - ScreenPadding,
                Main.screenWidth + ScreenPadding * 2,
                Main.screenHeight + ScreenPadding * 2);

            ScanPlayers();
            ScanNpcs();
            ScanProjectiles();
        }

        private void EnsureSize()
        {
            int width = Main.screenWidth / 16 + Main.offScreenRange / 8 + 8;
            int height = Main.screenHeight / 16 + Main.offScreenRange / 8 + 8;
            if (width <= _width && height <= _height)
                return;

            _width = Math.Max(_width, width);
            _height = Math.Max(_height, height);
            _grid = new Touch[_width, _height];
        }

        // В одиночной игре шевелить заросли может только свой игрок, на клиенте —
        // все: чужой пловец обязан оставлять след, иначе выдаёт себя неподвижным лесом
        private void ScanPlayers()
        {
            if (Main.netMode == Terraria.ID.NetmodeID.SinglePlayer)
            {
                Player self = Main.player[Main.myPlayer];
                if (!self.dead)
                    Mark(self.Hitbox, self.velocity, PlayerPushScale);
                return;
            }

            for (int k = 0; k < Main.maxPlayers; k++)
            {
                Player player = Main.player[k];
                if (player.active && !player.dead)
                    Mark(player.Hitbox, player.velocity, PlayerPushScale);
            }
        }

        private void ScanNpcs()
        {
            for (int k = 0; k < Main.maxNPCs; k++)
            {
                NPC npc = Main.npc[k];
                if (!npc.active)
                    continue;

                float scale = npc.width >= LargeNpcWidth || npc.height >= LargeNpcWidth
                    ? LargeNpcPushScale
                    : SmallNpcPushScale;
                Mark(npc.Hitbox, npc.velocity, scale);
            }
        }

        private void ScanProjectiles()
        {
            for (int k = 0; k < Main.maxProjectiles; k++)
            {
                Projectile projectile = Main.projectile[k];
                if (!projectile.active)
                    continue;

                Rectangle hitbox = projectile.Hitbox;
                Mark(hitbox, projectile.velocity, ProjectilePushScale);

                // Быстрый снаряд за кадр перескакивает несколько тайлов, и след
                // получался бы пунктиром — метим заодно прошлое положение
                hitbox.X = (int)projectile.oldPosition.X;
                hitbox.Y = (int)projectile.oldPosition.Y;
                Mark(hitbox, projectile.velocity, ProjectilePushScale);
            }
        }

        private void Mark(Rectangle hitbox, Vector2 velocity, float scale)
        {
            if (velocity.HasNaNs())
                return;

            float speed = Math.Abs(velocity.X);
            if (speed < MinPushSpeed || !hitbox.Intersects(_screenArea))
                return;

            float strength = Math.Min(speed / SpeedForFullPush, 1f) * scale;
            int direction = Math.Sign(velocity.X);

            int left = Math.Max(hitbox.Left / 16, 0);
            int right = Math.Min(hitbox.Right / 16, Main.maxTilesX - 1);
            int top = Math.Max(hitbox.Top / 16, 0);
            int bottom = Math.Min(hitbox.Bottom / 16, Main.maxTilesY - 1);

            for (int x = left; x <= right; x++)
            {
                for (int y = top; y <= bottom; y++)
                    Write(x, y, direction, strength);
            }
        }

        private void Write(int tileX, int tileY, int direction, float strength)
        {
            ref Touch touch = ref _grid[tileX % _width, tileY % _height];

            // За кадр по тайлу могут пройти несколько сущностей: побеждает сильнейшая,
            // иначе снаряд гасил бы толчок от проплывающего мимо кита
            if (touch.Time == _time && touch.Strength >= strength)
                return;

            touch.Time = _time;
            touch.DirectionX = (sbyte)direction;
            touch.Strength = strength;
        }
    }
}
