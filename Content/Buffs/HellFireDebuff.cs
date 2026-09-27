using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace SoA.Content.Buffs
{
    // Адское пламя: горение втрое сильнее ванильного огня и замедление на 25%.
    // Урон и замедление ведёт HellFireGlobalNPC
    public class HellFireDebuff : ModBuff
    {
        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
        }
    }

    public class HellFireGlobalNPC : GlobalNPC
    {
        // Через lifeRegen, как ванильный огонь: урон идёт тиками игры на всех машинах одинаково
        // и не удваивается в сети. Раньше тут был StrikeNPC раз в 10 тиков (24 урона/с мимо защиты)
        private const int LifeRegenDrain = 24;   // полусекундные единицы: 12 урона/с
        private const int DamageNumber = 4;      // число над врагом при каждом тике горения
        private const float SlowFraction = 0.25f;

        private static bool IsBurning(NPC npc) => npc.HasBuff(ModContent.BuffType<HellFireDebuff>());

        public override void UpdateLifeRegen(NPC npc, ref int damage)
        {
            if (!IsBurning(npc))
                return;

            if (npc.lifeRegen > 0)
                npc.lifeRegen = 0;
            npc.lifeRegen -= LifeRegenDrain;
            if (damage < DamageNumber)
                damage = DamageNumber;
        }

        // Замедление без накопления: скорость не трогаем (иначе AI с разгоном тормозил почти
        // до нуля — было velocity *= 0.85 каждый кадр), а отнимаем четверть шага этого тика.
        // Боссов и сегменты составных врагов (черви) не замедляем
        public override void PostAI(NPC npc)
        {
            if (npc.friendly || npc.boss || npc.realLife >= 0 || NPCID.Sets.ShouldBeCountedAsBoss[npc.type] || !IsBurning(npc))
                return;

            // Сдвиг назад по ходу может задеть стену, от которой враг только что развернулся
            Vector2 slowed = npc.position - npc.velocity * SlowFraction;
            if (npc.noTileCollide || !Collision.SolidCollision(slowed, npc.width, npc.height))
                npc.position = slowed;
        }

        public override void DrawEffects(NPC npc, ref Color drawColor)
        {
            if (!IsBurning(npc))
                return;

            if (Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustDirect(npc.position, npc.width, npc.height,
                    DustID.InfernoFork, Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(0.5f, 2.5f));
                d.scale = Main.rand.NextFloat(0.6f, 1.3f);
                d.noGravity = false;
            }

            drawColor = Color.Lerp(drawColor, new Color(255, 60, 0), 0.35f);
        }
    }
}
