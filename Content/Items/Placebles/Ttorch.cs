using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Other;

namespace SoA.Content.Items.Placebles
{
    public class Ttorch : ModItem
    {
        public override void SetStaticDefaults()
        {
            ItemID.Sets.Torches[Type] = true;      // участвует в торч-свапе и удаче факелов
            ItemID.Sets.WaterTorches[Type] = true; // умный курсор ставит его под водой
        }

        public override void SetDefaults()
        {
            Item.width = 10;
            Item.height = 12;
            Item.maxStack = 9999;
            Item.value = 50;
            Item.rare = ItemRarityID.White;

            Item.useStyle = ItemUseStyleID.Swing;
            Item.holdStyle = ItemHoldStyleID.HoldFront;
            Item.useTime = 10;
            Item.useAnimation = 15;
            Item.autoReuse = true;
            Item.useTurn = true;
            Item.flame = true; // noWet не ставим — факел горит и в воде

            Item.consumable = true;
            Item.createTile = ModContent.TileType<Ttorch_tile>();
        }

        public override void HoldItem(Player player)
        {
            // Свет и искры в руке (в том числе под водой)
            if (Main.rand.NextBool(player.itemAnimation > 0 ? 40 : 15))
            {
                Dust d = Dust.NewDustDirect(
                    new Vector2(player.itemLocation.X + (player.direction == -1 ? -12f : 6f), player.itemLocation.Y - 14f),
                    4, 4, player.wet ? DustID.Water : DustID.BlueTorch);
                d.noGravity = true;
                d.velocity *= 0.3f;
            }

            Vector2 flamePos = player.RotatedRelativePoint(
                new Vector2(player.itemLocation.X + 12f * player.direction, player.itemLocation.Y - 14f), true);
            Lighting.AddLight(flamePos, 0.15f, 0.65f, 0.95f);
        }

        public override void PostUpdate()
        {
            Lighting.AddLight(Item.Center, 0.1f, 0.45f, 0.65f);
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe(3);
            recipe.AddIngredient(ItemID.Torch, 3);
            recipe.AddIngredient(ItemID.Coral, 1);
            recipe.AddTile(TileID.WorkBenches);
            recipe.Register();

            // Альтернатива из люминофора медузы-тени: в самом биоме ванильный коралл не растёт
            Recipe lumenRecipe = CreateRecipe(6);
            lumenRecipe.AddIngredient(ItemID.Torch, 6);
            lumenRecipe.AddIngredient<Materials.DarkLumen>(1);
            lumenRecipe.AddTile(TileID.WorkBenches);
            lumenRecipe.Register();
        }
    }
}
