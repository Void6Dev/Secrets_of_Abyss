using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Players;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles;

namespace SoA.Content.Items.Weapons
{
    // Инферно-сюрикен: зажал — сюрикен раскаляется в руке, отпустил — бросок. Зарядку,
    // замах и вид броска ведёт InfernoShurikenHeld, полёт — InfernoShurikenProjectile
    public class InfernoShuriken : ModItem
    {
        public override void SetDefaults()
        {
            // Идеальный бросок бьёт несколько раз и взрывается, поэтому база ниже, чем у оружия этапа
            Item.damage = 42;
            Item.DamageType = DamageClass.Ranged;
            Item.width = 30;
            Item.height = 30;
            Item.useTime = 12;
            Item.useAnimation = 12;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.noMelee = true;
            Item.noUseGraphic = true;  // сюрикен в руке рисует InfernoShurikenHeld
            Item.knockBack = 3;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange;
            // Без UseSound: он звучал в начале зарядки. Звук броска — у снаряда
            Item.autoReuse = false;
            Item.channel = true;
            Item.shoot = ModContent.ProjectileType<InfernoShurikenHeld>();
            Item.shootSpeed = 1f;
            Item.consumable = false;
            Item.maxStack = 1;
        }

        public override bool CanUseItem(Player player)
            => player.GetModPlayer<InfernoShurikenPlayer>().cooldown == 0
            && player.ownedProjectileCounts[Item.shoot] == 0;

        // Жар везёт сам сюрикен в руке: так угольки на орбите видят и другие игроки
        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source,
            Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Projectile.NewProjectile(source, player.MountedCenter, Vector2.Zero, type, damage, knockback, player.whoAmI,
                0f, player.direction, player.GetModPlayer<InfernoShurikenPlayer>().heat);
            return false;
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            foreach (TooltipLine line in tooltips)
            {
                // Строка про идеальный бросок переливается от красного к белому накалу
                if (line.Name == "Tooltip2")
                {
                    float pulse = 0.5f + 0.5f * MathF.Sin(Main.GameUpdateCount * 0.08f);
                    line.OverrideColor = Color.Lerp(new Color(255, 80, 0), new Color(255, 220, 60), pulse);
                }
            }
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<LavaShard>(), 10);
            recipe.AddIngredient(ItemID.HellstoneBar, 15);
            recipe.AddIngredient(ItemID.Bone, 80);
            recipe.AddTile(TileID.Hellforge);
            recipe.Register();
        }
    }
}
