using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using SoA.Content.Projectiles;
using SoA.Common.CustomClasses;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace SoA.Content.Items.Weapons
{
    public class ShardedSpear : ModItem
    {
        private readonly SimpleItemAnimation _anim = new(8, 6);

        public override void SetStaticDefaults()
        {
            Main.RegisterItemAnimation(Type, new DrawAnimationVertical(6, 8));
        }

        public override void SetDefaults()
        {
            Item.damage = 90;
            Item.width = 30;
            Item.height = 30;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.DamageType = DamageClass.Magic;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.UseSound = SoundID.Item13;
            Item.mana = 15;
            Item.autoReuse = false;
            Item.channel = true;
            Item.shoot = ModContent.ProjectileType<ShardedSpearHoldout>();
            Item.shootSpeed = 1f;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Red;
        }

        public override bool CanUseItem(Player player)
        {
            return player.ownedProjectileCounts[Item.shoot] == 0;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source,
            Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Projectile.NewProjectile(source, player.MountedCenter, Vector2.Zero,
                type, damage, knockback, player.whoAmI);
            return false;
        }

        public override void UpdateInventory(Player player)
        {
            _anim.Update();
        }

        public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
            Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            Texture2D tex = TextureAssets.Item[Type].Value;
            Rectangle src = _anim.GetFrame(tex);
            Vector2 drawOrigin = _anim.GetOrigin(tex);

            spriteBatch.Draw(tex, position, src, drawColor, 0f, drawOrigin, scale * 1.3f, SpriteEffects.None, 0f);
            return false;
        }
    }
}
