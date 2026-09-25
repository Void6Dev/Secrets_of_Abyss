using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace SoA.Common.Utils
{
    // Единая проверка «промок» для водяного оружия Прилива Теней: годится и физический
    // контакт с жидкостью, и метка, которую оставляет само оружие. Раньше каждый снаряд
    // проверял только target.wet, из-за чего на суше водяные бонусы не работали вообще.
    public static class SoACombat
    {
        public const int SoakDuration = 240;

        // Бонус водяного оружия по промокшим целям
        public const float SoakedDamageMultiplier = 1.25f;

        public static bool IsSoaked(NPC npc)
        {
            return npc.wet || npc.HasBuff(BuffID.Wet);
        }

        public static void Soak(NPC npc, int ticks = SoakDuration)
        {
            npc.AddBuff(BuffID.Wet, ticks);
        }

        // Для ModifyHitNPC водяных снарядов: одна строка вместо своей копии множителя
        public static void ApplySoakBonus(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (IsSoaked(target))
                modifiers.FinalDamage *= SoakedDamageMultiplier;
        }

        // Точка в воде (не в лаве и не в мёде) — для снарядов, которые меняют поведение под водой
        public static bool IsInWater(Vector2 worldPosition)
        {
            Point tilePos = worldPosition.ToTileCoordinates();
            Tile tile = Framing.GetTileSafely(tilePos.X, tilePos.Y);

            return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water;
        }

        // Ближайший враг, по которому можно стрелять; null если никого в радиусе.
        // requireLineOfSight отсеивает цели за стенами — иначе самонаведение тащит снаряд в грунт.
        public static NPC FindClosestNPC(Vector2 center, float maxDistance, bool requireLineOfSight = false)
        {
            NPC closest = null;
            float closestDistance = maxDistance;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];

                if (!npc.CanBeChasedBy())
                    continue;

                float distance = Vector2.Distance(center, npc.Center);

                if (distance >= closestDistance)
                    continue;

                if (requireLineOfSight && !Collision.CanHitLine(center, 1, 1, npc.position, npc.width, npc.height))
                    continue;

                closestDistance = distance;
                closest = npc;
            }

            return closest;
        }
    }
}
