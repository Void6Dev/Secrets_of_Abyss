using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Particles;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles;

namespace SoA.Content.Items.Weapons
{
    // Когти Лавовой Тени: быстрые когтистые рывки к курсору, попеременно правой и левой
    // рукой (LavaClawSlash); каждый четвёртый удар — тяжёлый. Попадания копят урон в лавовой
    // метке, и она взрывается, когда удары прекращаются. ПКМ — огненный рывок с неуязвимостью.
    public class ClawsOfLavaShadow : ModItem
    {
        private const int DashCooldownTicks = 120;
        private const float HeavyDamageMultiplier = 1.4f;
        private const float HeavyKnockbackMultiplier = 2f;

        private int _dashCooldown;
        private bool _dashReady = true;

        public override void SetDefaults()
        {
            // Урон ниже, чем у обычного оружия этапа: метка копит его и взрывается вторым разом
            Item.damage = 24;
            Item.DamageType = DamageClass.Melee;
            Item.width = 30;
            Item.height = 20;
            Item.useTime = 13;
            Item.useAnimation = 13;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 2;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange; // этап адского камня, а не финал игры
            Item.autoReuse = true;
            Item.noMelee = true;       // режет снаряд взмаха
            Item.noUseGraphic = true;  // коготь в руке рисует LavaClawSlash
            Item.shoot = ModContent.ProjectileType<LavaClawSlash>();
            Item.shootSpeed = 1f;      // нужна только сторона прицела
        }

        public override void AddRecipes()
        {
            // Раньше здесь дважды стояли осколки (опечатка) и крафт шёл у простой печи
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<LavaShard>(), 12)
                .AddIngredient(ItemID.HellstoneBar, 10)
                .AddTile(TileID.Hellforge)
                .Register();
        }

        public override bool AltFunctionUse(Player player) => true;

        public override bool CanUseItem(Player player) => player.altFunctionUse != 2 || _dashReady;

        public override bool? UseItem(Player player)
        {
            if (player.altFunctionUse == 2 && _dashReady)
            {
                _dashReady = false;
                _dashCooldown = DashCooldownTicks;
                // Направление берётся от курсора — оно есть только у самого игрока
                if (player.whoAmI == Main.myPlayer)
                    player.GetModPlayer<LavaClawsPlayer>().StartDash();
                return true;
            }
            return null;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position,
            Vector2 velocity, int type, int damage, float knockback)
        {
            if (player.altFunctionUse == 2)
                return false; // ПКМ — рывок, взмаха нет

            LavaClawsPlayer claws = player.GetModPlayer<LavaClawsPlayer>();
            claws.NextSlash(out float side, out bool heavy);
            if (heavy)
            {
                damage = (int)(damage * HeavyDamageMultiplier);
                knockback *= HeavyKnockbackMultiplier;
            }

            Projectile.NewProjectile(source, player.MountedCenter, Vector2.Zero, type, damage, knockback,
                player.whoAmI, side, heavy ? 1f : 0f, velocity.ToRotation());
            return false;
        }

        public override void UpdateInventory(Player player)
        {
            if (_dashCooldown > 0)
                _dashCooldown--;

            // Рывок перезаряжается только на земле: иначе цепочка рывков заменяет крылья
            if (!_dashReady && _dashCooldown == 0 && player.velocity.Y == 0f)
            {
                _dashReady = true;
                OnDashReady(player);
            }
        }

        public override void HoldItem(Player player)
        {
            // Вне взмаха смотрим туда, куда бежим; во взмахе направление ведёт LavaClawSlash
            if (player.heldProj < 0 || Main.projectile[player.heldProj].type != Item.shoot)
                player.ChangeDir(Math.Sign(player.velocity.X != 0f ? player.velocity.X : player.direction));
        }

        // Рывок снова готов: короткая вспышка на игроке и звон
        private static void OnDashReady(Player player)
        {
            if (!Main.dedServ && player.whoAmI == Main.myPlayer)
            {
                SoAParticles.SpawnGlow(player.Center, Vector2.Zero, new Color(255, 140, 40) * 0.7f, 20f, 90f, 14);
                for (int i = 0; i < 12; i++)
                {
                    Vector2 dir = Main.rand.NextVector2Unit();
                    SoAParticles.SpawnStreak(player.Center + dir * 12f, dir * Main.rand.NextFloat(2f, 5f),
                        new Color(255, 170, 60), 2f, gravity: 0f, life: 16, lengthPerSpeed: 2f);
                }
                SoundEngine.PlaySound(SoundID.Item29 with { Volume = 0.6f }, player.Center);
            }
        }
    }

    // Состояние Когтей на игроке: очередь взмахов (правая/левая, каждый 4-й тяжёлый) и рывок
    public class LavaClawsPlayer : ModPlayer
    {
        private const int ComboResetTicks = 40;   // пауза, после которой серия начинается заново
        private const int HeavyEvery = 4;
        private const int DashDuration = 25;
        private const float DashSpeed = 20f;

        private static readonly Color DashFire = new(255, 130, 35);
        private static readonly Color DashCore = new(255, 230, 170);
        private static readonly Color DashSmoke = new(60, 42, 36);

        private float _slashSide = -1f;
        private int _slashCount;
        private ulong _lastSlashTick;

        public bool isDashing;
        private int _dashTicks;
        private Vector2 _dashVelocity;

        public void NextSlash(out float side, out bool heavy)
        {
            if (Main.GameUpdateCount - _lastSlashTick > ComboResetTicks)
                _slashCount = 0;
            _lastSlashTick = Main.GameUpdateCount;

            _slashCount++;
            _slashSide = -_slashSide;
            side = _slashSide;
            heavy = _slashCount % HeavyEvery == 0;
        }

        public void StartDash()
        {
            isDashing = true;
            _dashTicks = 0;
            _dashVelocity = (Main.MouseWorld - Player.Center).SafeNormalize(Vector2.UnitX * Player.direction) * DashSpeed;
            Player.velocity = _dashVelocity;

            SoundEngine.PlaySound(SoundID.Item74, Player.Center);
            if (Main.dedServ)
                return;

            // Воспламенение: кольцо раскалённых брызг и вспышка
            for (int i = 0; i < 22; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                SoAParticles.SpawnStreak(Player.Center + dir * 8f, dir * Main.rand.NextFloat(4f, 10f),
                    Color.Lerp(DashFire, DashCore, Main.rand.NextFloat(0.5f)), Main.rand.NextFloat(2f, 3.5f),
                    gravity: 0.05f, life: Main.rand.Next(16, 28), lengthPerSpeed: 2.4f);
            }
            SoAParticles.SpawnGlow(Player.Center, Vector2.Zero, DashCore, 40f, 150f, 12);
            SoAParticles.AddLight(Player.Center, DashFire, 2f, 14);
        }

        public override void PostUpdate()
        {
            if (!isDashing)
                return;

            _dashTicks++;
            if (_dashTicks <= DashDuration)
            {
                Player.immune = true;
                Player.immuneTime = 15;
                if (!Main.dedServ)
                    SpawnDashTrail();
                return;
            }

            isDashing = false;
            _dashTicks = 0;
            Player.immune = false;
            if (!Main.dedServ)
                SpawnDashEnd();
        }

        // Огненный след: языки пламени срываются назад, за ними тянется дым
        private void SpawnDashTrail()
        {
            Vector2 back = -_dashVelocity.SafeNormalize(Vector2.Zero);
            for (int i = 0; i < 3; i++)
            {
                Vector2 at = Player.Center + Main.rand.NextVector2Circular(Player.width * 0.5f, Player.height * 0.5f);
                SoAParticles.SpawnStreak(at, back.RotatedByRandom(0.5f) * Main.rand.NextFloat(3f, 7f),
                    Color.Lerp(DashFire, DashCore, Main.rand.NextFloat(0.4f)), Main.rand.NextFloat(2.5f, 4f),
                    gravity: -0.04f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.2f);
            }
            if (_dashTicks % 3 == 0)
            {
                SoAParticles.SpawnSmoke(Player.Center, back * 1.5f + new Vector2(0f, -0.4f), DashSmoke,
                    30f, 90f, 0.4f, Main.rand.Next(40, 60));
            }
            SoAParticles.SpawnGlow(Player.Center, Vector2.Zero, DashFire * 0.35f, 50f, 70f, 6);
            Lighting.AddLight(Player.Center, DashFire.ToVector3());
        }

        private void SpawnDashEnd()
        {
            for (int i = 0; i < 18; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                SoAParticles.SpawnStreak(Player.Center, dir * Main.rand.NextFloat(3f, 8f), DashFire,
                    Main.rand.NextFloat(2f, 3f), gravity: 0.12f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.2f);
            }
            SoAParticles.SpawnSmoke(Player.Center, new Vector2(0f, -0.6f), DashSmoke, 40f, 120f, 0.45f, 60);
        }
    }
}
