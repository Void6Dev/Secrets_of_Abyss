using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Placebles;
using SoA.Content.Tiles.Nature;

namespace SoA.Content.Projectiles
{
    // Падающий блок Tidesand: ванильный ИИ падающего тайла,
    // при приземлении ставит обратно Tidesand_tile (маппинг в TileID.Sets.FallingBlockProjectile)
    public class TidesandBallFalling : ModProjectile
    {
        public override string Texture => "SoA/Content/Items/Placebles/Tidesand";

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.FallingBlockDoesNotFallThroughPlatforms[Type] = true;
            ProjectileID.Sets.ForcePlateDetection[Type] = true;
            // Без этого маппинга ванильный ИИ при приземлении ставит землю (тайл 0)
            ProjectileID.Sets.FallingBlockTileItem[Type] = new ProjectileID.Sets.FallingBlockTileItemInfo(
                ModContent.TileType<Tidesand_tile>(), ModContent.ItemType<Tidesand>());
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.knockBack = 6f;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.aiStyle = ProjAIStyleID.FallingTile;
        }
    }
}
