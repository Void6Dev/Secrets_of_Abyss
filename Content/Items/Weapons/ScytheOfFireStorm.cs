using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Weapons;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles;

namespace SoA.Content.Items.Weapons
{
    // Коса огненной бури: зажал — лезвие раскаляется по ступеням, отпустил — взмах,
    // с кончика которого срывается серп или торнадо. Весь цикл ведёт ScytheOfFireStormClub
    public class ScytheOfFireStorm : ClubItem
    {
        public const int MaxActiveTornadoes = 2;

        public override void SetDefaults()
        {
            DefaultToClub(ModContent.ProjectileType<ScytheOfFireStormClub>(), damage: 28, knockback: 2f);
            Item.DamageType = DamageClass.Magic;
            Item.mana = 6; // цена клика; ступени заряда доплачиваются при отпускании
            Item.width = 40;
            Item.height = 40;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<LavaShard>(), 15)
                .AddIngredient(ItemID.AshBlock, 10)
                .AddIngredient(ModContent.ItemType<HellStar>(), 1)
                .AddTile(TileID.Hellforge)
                .Register();
        }
    }
}
