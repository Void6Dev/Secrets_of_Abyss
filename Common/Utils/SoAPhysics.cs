using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace SoA.Common.Utils
{
    // Общая наземная физика для своих ИИ и снарядов, которые не пользуются ванильными aiStyle
    public static class SoAPhysics
    {
        private const float StepProbe = 2f; // касание пола и стены за упор не считаем

        // Уступ по ходу движения: высота, на которую надо подняться, чтобы пройти дальше,
        // или 0 — впереди свободно либо стена выше maxStep. Раньше без этого и король, и свита,
        // и волны по земле упирались даже в ступеньку в один блок: рывок засчитывался как удар
        // о стену, таран паладина — как врезался и оглушён, а волна просто гасла
        public static float FindStepUp(Vector2 position, int width, int height, float moveX, float maxStep)
        {
            if (Math.Abs(moveX) < 0.01f)
                return 0f;

            // Щуп вперёд не меньше пары пикселей: на медленном ходу иначе уступ «не виден»
            float probeX = Math.Sign(moveX) * Math.Max(Math.Abs(moveX), StepProbe);
            Vector2 ahead = new Vector2(position.X + probeX, position.Y);
            if (!Collision.SolidCollision(ahead - new Vector2(0f, StepProbe), width, height))
                return 0f;

            for (float step = StepProbe * 2f; step <= maxStep; step += StepProbe)
            {
                Vector2 lift = new Vector2(0f, step);
                if (!Collision.SolidCollision(ahead - lift, width, height)
                    && !Collision.SolidCollision(position - lift, width, height))
                    return step;
            }
            return 0f;
        }

        // Шаг вверх для наземного NPC со своей физикой. Звать в конце AI: ваниль сдвинет NPC
        // по скорости и посчитает столкновения уже после, и он встанет на уступ.
        // Возвращает высоту шага — владелец может сгладить им картинку, чтобы тело не дёргалось
        public static float TryStepUp(NPC npc, float maxStep)
        {
            bool grounded = npc.collideY || npc.velocity.Y == 0f;
            if (npc.noTileCollide || !grounded || npc.velocity.Y < 0f)
                return 0f;

            float step = FindStepUp(npc.position, npc.width, npc.height, npc.velocity.X, maxStep);
            if (step > 0f)
                npc.position.Y -= step;
            return step;
        }

        // Шаг вверх для снаряда, катящегося по земле: зовётся из OnTileCollide, когда его
        // остановила стена. true — уступ преодолён, скорость по X восстановлена
        public static bool TryStepUp(Projectile projectile, Vector2 oldVelocity, float maxStep)
        {
            float step = FindStepUp(projectile.position, projectile.width, projectile.height, oldVelocity.X, maxStep);
            if (step <= 0f)
                return false;

            projectile.position.Y -= step;
            projectile.velocity.X = oldVelocity.X;
            return true;
        }
    }
}
