using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace SoA.Common.Systems
{
    // Кто сейчас движется около экрана: игроки, NPC и снаряды. Общий список для всего,
    // что реагирует на пловцов, — физики водорослей (TideKelpPhysics) и фоновых частиц
    // (TideAmbience), чтобы каждый не обходил сущности сам.
    //
    // Собирается лениво, один раз за тик при первом обращении: так не важно, в каком
    // порядке движок обновляет системы. Только клиент: всё выводится из позиций сущностей,
    // которые и так синхронны
    public readonly struct WaterDisturber
    {
        public readonly Rectangle Hitbox;   // у снаряда — вместе с прошлым положением
        public readonly Vector2 Velocity;

        public WaterDisturber(Rectangle hitbox, Vector2 velocity)
        {
            Hitbox = hitbox;
            Velocity = velocity;
        }

        public Vector2 Center => Hitbox.Center.ToVector2();
    }

    public static class WaterDisturbers
    {
        private const int MaxDisturbers = 96;
        private const int ScreenPaddingPx = 16 * 16;

        private static readonly List<WaterDisturber> _list = new(MaxDisturbers);
        private static uint _collectedTick = uint.MaxValue;

        public static IReadOnlyList<WaterDisturber> Get()
        {
            if (_collectedTick != Main.GameUpdateCount)
            {
                _collectedTick = Main.GameUpdateCount;
                Collect();
            }
            return _list;
        }

        private static void Collect()
        {
            _list.Clear();
            if (Main.dedServ)
                return;

            Rectangle area = new(
                (int)Main.screenPosition.X - ScreenPaddingPx, (int)Main.screenPosition.Y - ScreenPaddingPx,
                Main.screenWidth + ScreenPaddingPx * 2, Main.screenHeight + ScreenPaddingPx * 2);

            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player player = Main.player[i];
                if (player.active && !player.dead)
                    TryAdd(area, player.Hitbox, player.velocity);
            }

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active)
                    TryAdd(area, npc.Hitbox, npc.velocity);
            }

            // Быстрый снаряд за тик перескакивает несколько тайлов, и сквозь заросли
            // он проходил бы пунктиром — хитбокс растягивается до прошлого положения
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile projectile = Main.projectile[i];
                if (!projectile.active)
                    continue;

                Rectangle current = projectile.Hitbox;
                Rectangle previous = new((int)projectile.oldPosition.X, (int)projectile.oldPosition.Y,
                    current.Width, current.Height);
                TryAdd(area, Rectangle.Union(current, previous), projectile.velocity);
            }
        }

        private static void TryAdd(Rectangle area, Rectangle hitbox, Vector2 velocity)
        {
            if (_list.Count >= MaxDisturbers || velocity.HasNaNs() || !hitbox.Intersects(area))
                return;
            _list.Add(new WaterDisturber(hitbox, velocity));
        }
    }
}
