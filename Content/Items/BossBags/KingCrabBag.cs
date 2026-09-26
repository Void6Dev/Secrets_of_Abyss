using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Content.Items.BossBags
{
    // Мешок Короля-краба. Как у ванильных боссов: падает в Expert и Master, каждому игроку свой;
    // в Classic клешни падают напрямую (см. King_crab.ModifyNPCLoot). Королевское копьё в мешок
    // не кладём: в сцене смерти оно собирается из песка ровно там, где выпадает, во всех режимах
    public class KingCrabBag : ModItem
    {
        private static readonly Vector3 GroundGlow = new(0.25f, 0.45f, 0.6f); // морской отсвет, как у ткани мешка

        public override void SetStaticDefaults()
        {
            ItemID.Sets.BossBag[Type] = true;
            ItemID.Sets.PreHardmode[Type] = true;
            Item.ResearchUnlockCount = 3;
        }

        public override void SetDefaults()
        {
            Item.maxStack = Item.CommonMaxStack;
            Item.consumable = true;
            Item.width = 32;
            Item.height = 32;
            Item.rare = ItemRarityID.Purple;
            Item.expert = true;
        }

        public override bool CanRightClick() => true;

        public override void ModifyItemLoot(ItemLoot itemLoot)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<RoyalClaw>(), 1,
                King_crab.RoyalClawDropMin, King_crab.RoyalClawDropMax));
            itemLoot.Add(ItemDropRule.CoinsBasedOnNPCValue(ModContent.NPCType<King_crab>()));
        }

        // Лежащий на земле мешок светится, как ванильные: его видно в тёмной воде
        public override void PostUpdate() => Lighting.AddLight(Item.Center, GroundGlow);
    }
}
