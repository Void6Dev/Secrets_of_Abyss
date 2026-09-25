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
    // Апгрейд ванильного трезубца клешнёй Короля-краба.
    // Спрайт — ванильный Trident (placeholder до собственного арта).
    //
    // ЛКМ — быстрая связка: три укола веером внутри одной анимации, по одному
    // каждые StabUseTime тиков (схема ванильного Piercing Starlight: useAnimation
    // кратен useTime, и ванилла сама стреляет несколько раз за одно нажатие).
    // ПКМ — бросок с замахом: копьё копит силу, пока кнопка зажата, и на отпускании
    // уходит тем дальше и больнее, чем дольше держали. Втыкается, поднимает гейзер и
    // остаётся там: пока за ним не придут ногами, оружия у игрока нет.
    // Если идти лень — зажатая ПКМ пересобирает воткнутое копьё песком прямо в руке,
    // но медленно и без заряда (RoyalSpearReforge).
    //
    // Снаряды копья лежат в Content/Projectiles/RoyalSpear, состояние игрока — в
    // Common/Players/RoyalSpear.
    // LegacyName — чтобы предметы в старых сохранениях не стали unloaded после переимено-
    // вания класса из RoyalTrident.
    [LegacyName("RoyalTrident")]
    public class RoyalSpear : ModItem
    {
        // Тиков между уколами связки. Вся анимация — StabUseTime * ComboLength,
        // именно из этой кратности ванилла и делает три удара за одно нажатие
        private const int StabUseTime = 8;

        // Веер: каждый укол уходит под своим углом, радиан. Последний бьёт прямо
        private static readonly float[] StepAim = { 0.12f, -0.12f, 0f };

        // Разброс прицела на один удар: связка не должна выглядеть штамповкой
        private const float AimJitter = 0.045f;

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
            Item.useTime = StabUseTime;
            Item.useAnimation = StabUseTime * RoyalSpearPlayer.ComboLength;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 5.5f;
            Item.value = Item.sellPrice(gold: 1, silver: 50);
            Item.rare = ItemRarityID.Green;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.shoot = ModContent.ProjectileType<RoyalSpearProjectile>();
            Item.shootSpeed = 3.5f;
        }

        public override bool AltFunctionUse(Player player) => true;

        public override bool CanUseItem(Player player)
        {
            if (player.altFunctionUse == 2)
            {
                Item.useStyle = ItemUseStyleID.Swing;
                Item.useTime = Item.useAnimation = ThrowUseTime;
                Item.UseSound = null; // звук замаха даёт сам заряд
                Item.channel = true;
                Item.autoReuse = false;

                // Замах или пересборка уже идут — второе нажатие ничего не начинает
                if (player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearCharge>()] > 0
                    || player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearReforge>()] > 0)
                    return false;

                // Копьё в мире: бросать нечего, но воткнутое можно собрать песком в руке.
                // Летящее — нельзя, оно ещё никуда не воткнулось
                if (player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearThrown>()] > 0)
                    return RoyalSpearThrown.FindOwned(player, stuckOnly: true) != null;

                return true;
            }

            Item.channel = false;
            Item.autoReuse = false; // одно нажатие — одна связка, как у Starlight

            // Трезубец воткнут где-то в мире или лежит в замахе — колоть нечем
            if (player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearThrown>()] > 0
                || player.ownedProjectileCounts[ModContent.ProjectileType<RoyalSpearCharge>()] > 0)
                return false;

            Item.useStyle = ItemUseStyleID.Shoot;
            Item.useTime = StabUseTime;
            Item.useAnimation = StabUseTime * RoyalSpearPlayer.ComboLength;
            Item.UseSound = null; // ванилла звучала бы раз на связку, звук даёт каждый укол

            // CanUseItem зовётся только на старте анимации (ванилла проверяет
            // itemAnimation == 0), так что связка отсчитывается ровно от нуля
            player.GetModPlayer<RoyalSpearPlayer>().ResetCombo();

            // Больше связки уколов одновременно не бывает
            return player.ownedProjectileCounts[Item.shoot] < RoyalSpearPlayer.ComboLength;
        }

        // В воде трезубец у себя дома
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

            int step = player.GetModPlayer<RoyalSpearPlayer>().AdvanceCombo();

            // Рандом бросаем один раз здесь: Shoot идёт на клиенте бьющего, и значение
            // уезжает в ai вместе с пакетом создания. Кинь его в снаряде — у каждого
            // клиента копьё махало бы по-своему, мимо собственного хитбокса
            float variance = Main.rand.NextFloat(-1f, 1f);
            Vector2 stabAim = velocity.RotatedBy(StepAim[step] + variance * AimJitter);

            SoundEngine.PlaySound(
                SoundID.Item1 with { Volume = 0.75f, Pitch = 0.12f * step }, player.Center);

            Projectile.NewProjectile(source, position, stabAim, type, damage, knockback,
                player.whoAmI, step, variance);
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
