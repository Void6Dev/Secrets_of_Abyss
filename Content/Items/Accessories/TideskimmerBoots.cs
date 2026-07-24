using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Tiles.Nature;

namespace SoA.Content.Items.Accessories
{
    // Ботинки прилива: хождение по воде + ускорение на песке.
    // Спрайт — ванильные Water Walking Boots (placeholder до собственного арта).
    public class TideskimmerBoots : ModItem
    {
        private const float SandMoveSpeedBonus = 0.25f;

        public override string Texture => "Terraria/Images/Item_" + ItemID.WaterWalkingBoots;

        public override void SetDefaults()
        {
            Item.width = 28;
            Item.height = 24;
            Item.accessory = true;
            Item.value = Item.sellPrice(gold: 4, silver: 20);
            Item.rare = ItemRarityID.Purple;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.waterWalk = true;

            if (!IsStandingOnSand(player))
                return;

            player.moveSpeed += SandMoveSpeedBonus;

            if (Main.netMode != NetmodeID.Server
                && Math.Abs(player.velocity.X) > 4f && player.velocity.Y == 0f && Main.rand.NextBool(3))
            {
                Dust d = Dust.NewDustDirect(player.BottomLeft - new Vector2(0f, 4f), player.width, 4, DustID.Sand);
                d.velocity = new Vector2(-player.velocity.X * 0.2f, -Main.rand.NextFloat(0.5f, 1.5f));
                d.scale = Main.rand.NextFloat(0.8f, 1.2f);
            }
        }

        private static bool IsStandingOnSand(Player player)
        {
            if (player.velocity.Y != 0f)
                return false;

            Point below = (player.Bottom + new Vector2(0f, 8f)).ToTileCoordinates();
            Tile tile = Framing.GetTileSafely(below.X, below.Y);
            if (!tile.HasTile)
                return false;

            return TileID.Sets.Conversion.Sand[tile.TileType]
                || tile.TileType == ModContent.TileType<Tidesand_tile>();
        }
    }
}
