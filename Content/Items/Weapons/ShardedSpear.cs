using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;
using SoA.Content.Items.Materials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;

namespace SoA.Content.Items.Weapons
{
    public class ShardedSpear : ModItem
    {
        public override void SetDefaults()
        {
            Item.damage = 90;
            Item.width = 30;
            Item.height = 30;
            Item.useTime = 16;
            Item.useAnimation = 16;
            Item.noMelee = true;
            Item.DamageType = DamageClass.Magic;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.UseSound = SoundID.Item13;
            Item.mana = 10;
            Item.autoReuse = true;
            Item.channel = true;
            Item.shoot = ModContent.ProjectileType<ShardedSpearProjectile>();
            Item.shootSpeed = 14f;

            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Red;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Projectile.NewProjectile(source, position + velocity * 4.5f, velocity, ModContent.ProjectileType<ShardedSpearBeam>(), damage, knockback, player.whoAmI);

            return false;
        }


        public override Vector2? HoldoutOffset() => new Vector2(-14, 0);
    }
}