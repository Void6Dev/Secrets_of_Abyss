using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;

namespace SoA.Common.Systems.Loot
{
    // Материалы лавового оружия с ванильных врагов Преисподней. Раньше Лавовому осколку и
    // Звезде преисподней неоткуда было взяться, и Когти, Сюрикен и Коса не крафтились вовсе.
    //   • Лавовый осколок — частый дроп: на Когти (12) уходит порядка двух десятков убийств;
    //   • Звезда преисподней — редкость с демонов (ещё она лежит в Теневых сундуках, см. ShadowChestLoot)
    public class UnderworldMaterialDrops : GlobalNPC
    {
        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
        {
            int shard = ModContent.ItemType<LavaShard>();
            int star = ModContent.ItemType<HellStar>();

            switch (npc.type)
            {
                case NPCID.FireImp:
                    npcLoot.Add(ItemDropRule.Common(shard, 3, 1, 2));
                    npcLoot.Add(ItemDropRule.Common(star, 40));
                    break;

                case NPCID.LavaSlime:
                    npcLoot.Add(ItemDropRule.Common(shard, 3, 1, 2));
                    break;

                case NPCID.Hellbat:
                case NPCID.Lavabat:
                    npcLoot.Add(ItemDropRule.Common(shard, 4));
                    break;

                case NPCID.Demon:
                    npcLoot.Add(ItemDropRule.Common(shard, 2, 2, 3));
                    npcLoot.Add(ItemDropRule.Common(star, 20));
                    break;

                case NPCID.VoodooDemon:
                    npcLoot.Add(ItemDropRule.Common(shard, 2, 2, 3));
                    npcLoot.Add(ItemDropRule.Common(star, 15));
                    break;

                case NPCID.BoneSerpentHead: // лут змея висит на голове
                    npcLoot.Add(ItemDropRule.Common(shard, 2, 2, 4));
                    break;

                case NPCID.RedDevil:
                    npcLoot.Add(ItemDropRule.Common(shard, 2, 2, 4));
                    npcLoot.Add(ItemDropRule.Common(star, 12));
                    break;
            }
        }
    }
}
