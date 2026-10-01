using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Common.Weapons
{
    // Предмет-дубина: сам не бьёт и не рисуется, только порождает ClubProjectile,
    // который ведёт замах, зарядку и удар, пока зажата атака
    public abstract class ClubItem : ModItem
    {
        protected void DefaultToClub(int clubProjectileType, int damage, float knockback)
        {
            Item.damage = damage;
            Item.knockBack = knockback;
            Item.DamageType = DamageClass.Melee;
            Item.useTime = 12;
            Item.useAnimation = 12;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.channel = true;
            Item.autoReuse = false;
            Item.shoot = clubProjectileType;
            Item.shootSpeed = 1f;
        }

        public override bool CanUseItem(Player player) => player.ownedProjectileCounts[Item.shoot] == 0;

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source,
            Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Projectile.NewProjectile(source, player.MountedCenter, Vector2.Zero, type, damage, knockback,
                player.whoAmI, 0f, 0f, player.direction);
            return false;
        }
    }
}
