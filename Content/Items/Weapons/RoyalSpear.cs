using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Players;
using SoA.Content.Projectiles;
using SoA.Content.Items.Materials;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Items.Weapons
{
    // Апгрейд ванильного трезубца клешнёй Короля-краба — выходит копьё.
    //
    // ЛКМ — всё решает RoyalSpearFlurry: клик даёт связку «укол, взмах, тяжёлый
    // выпад», зажатие копит шквал частых уколов (чем дольше держал, тем больше
    // ударов), отпускание его выпускает.
    // ПКМ — бросок с замахом: копьё копит силу, пока кнопка зажата, и на отпускании
    // уходит тем дальше и больнее, чем дольше держали. Втыкается и остаётся там: пока
    // за ним не придут, оружия у игрока нет. Зажатая ПКМ пересобирает воткнутое копьё
    // песком прямо в руке (RoyalSpearReforge).
    // Полная шкала прилива превращает ПКМ в приливный рывок: игрок проносится к точке
    // над курсором, копьё бьёт вниз и оглушает цель, игрок пикирует следом.
    //
    // Снаряды копья лежат в Content/Projectiles/RoyalSpear, состояние игрока — в
    // Common/Players/RoyalSpear.
    // LegacyName — чтобы предметы в старых сохранениях не стали unloaded после переимено-
    // вания класса из RoyalTrident.
    [LegacyName("RoyalTrident")]
    public class RoyalSpear : ModItem
    {
        // Анимация ЛКМ только запускает контроллер: дальше он сам держит предмет
        // занятым, пока не отработает последний удар
        private const int StrikeUseTime = 12;

        // Замах ПКМ длится ровно столько, сколько игрок держит кнопку, поэтому анимация
        // короткая: она только запускает канал, дальше всё считает RoyalSpearCharge
        private const int ThrowUseTime = 14;

        private const float ThrowDamageMultiplier = 1.1f;
        private const float WetUseSpeedMultiplier = 1.15f;

        public override void SetDefaults()
        {
            Item.damage = 36;
            Item.DamageType = DamageClass.Melee;
            Item.width = 32;
            Item.height = 32;
            Item.useTime = Item.useAnimation = StrikeUseTime;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 5.5f;

            // Копьё бьёт сериями мелких ударов, и защита врага вычитается из каждого:
            // без пробития шквал по бронированной цели терял почти половину урона
            Item.ArmorPenetration = 10;
            Item.value = Item.sellPrice(gold: 1, silver: 50);
            Item.rare = ItemRarityID.Green;
            Item.UseSound = null; // звук даёт каждый удар сам
            Item.channel = true;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.shoot = ModContent.ProjectileType<RoyalSpearFlurry>();
            Item.shootSpeed = 3.5f;
        }

        public override bool AltFunctionUse(Player player) => true;

        public override bool CanUseItem(Player player)
        {
            // Посреди приливного рывка руки заняты — копьё летит впереди игрока
            if (player.GetModPlayer<RoyalSpearPlayer>().Dashing)
                return false;

            if (player.altFunctionUse == 2)
            {
                Item.useStyle = ItemUseStyleID.Swing;
                Item.useTime = Item.useAnimation = ThrowUseTime;
                Item.UseSound = null; // звук замаха даёт сам заряд
                Item.channel = true;
                Item.autoReuse = false;

                // Замах, пересборка или серия ЛКМ уже идут — второе нажатие ничего не начинает
                if (player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearCharge>()] > 0
                    || player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearReforge>()] > 0
                    || player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearFlurry>()] > 0)
                    return false;

                // Копьё в мире: бросать нечего, но воткнутое можно собрать песком в руке.
                // Летящее — нельзя, оно ещё никуда не воткнулось
                if (player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearThrown>()] > 0)
                    return RoyalSpearThrown.FindOwned(player, stuckOnly: true) != null;

                return true;
            }

            Item.channel = true;
            Item.autoReuse = false; // одно нажатие — одна серия

            // Копьё воткнуто где-то в мире или лежит в замахе — колоть нечем
            if (player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearThrown>()] > 0
                || player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearCharge>()] > 0
                || player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearReforge>()] > 0)
                return false;

            Item.useStyle = ItemUseStyleID.Shoot;
            Item.useTime = Item.useAnimation = StrikeUseTime;
            Item.UseSound = null;

            return player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearFlurry>()] == 0;
        }

        // В воде копьё у себя дома
        public override float UseSpeedMultiplier(Player player)
        {
            return player.wet && !player.lavaWet && !player.honeyWet ? WetUseSpeedMultiplier : 1f;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position,
            Vector2 velocity, int type, int damage, float knockback)
        {
            Vector2 aim = Vector2.Normalize(velocity);

            if (player.altFunctionUse == 2)
            {
                // Воткнутое копьё не бросают заново — его собирают песком прямо в руке
                if (RoyalSpearThrown.FindOwned(player, stuckOnly: true) != null)
                {
                    Projectile.NewProjectile(source, player.MountedCenter, aim,
                        ModContent.ProjectileType<RoyalSpearReforge>(), 0, 0f, player.whoAmI);
                    SoundEngine.PlaySound(SoundID.Dig with { Pitch = 0.4f, Volume = 0.7f }, player.Center);
                    return false;
                }

                // Дальше копьё живёт в замахе: сила броска, слэм и сам вылет — там
                Projectile.NewProjectile(source, player.MountedCenter, aim,
                    ModContent.ProjectileType<RoyalSpearCharge>(),
                    (int)(damage * ThrowDamageMultiplier), knockback * 1.4f, player.whoAmI);
                SoundEngine.PlaySound(SoundID.Item7 with { Pitch = 0.2f, Volume = 0.6f }, player.Center);
                return false;
            }

            Projectile.NewProjectile(source, player.MountedCenter, aim, type, damage, knockback, player.whoAmI);
            return false;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.Trident)
                .AddIngredient(ModContent.ItemType<RoyalClaw>(), 3)
                .AddIngredient(ModContent.ItemType<Tidesand>(), 15)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}
