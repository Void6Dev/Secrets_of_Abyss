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
            Item.damage = 20;
            Item.DamageType = DamageClass.Melee;
            Item.width = 30;
            Item.height = 20;
            Item.useTime = 8;
            Item.useAnimation = 8;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 2;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange; 
            Item.autoReuse = true;
            Item.noMelee = true;       
            Item.noUseGraphic = true;  
            Item.shoot = ModContent.ProjectileType<LavaClawSlash>();
            Item.shootSpeed = 1f;    
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

        // ---------- РЫВОК ----------
        // Почти мгновенный: вся дистанция за несколько тиков, в конце — почти полная остановка.
        // Путь прощупывается заранее: на такой скорости ванильные столкновения пропускают
        // тонкие стены, и игрок проскакивал бы сквозь них
        private const float DashDistance = 260f;    // ~16 блоков
        private const int DashTicks = 5;
        private const float DashSweepStep = 4f;
        private const float DashExitSpeed = 3f;     // остаток скорости после рывка — почти без инерции
        private const int DashGraceTicks = 10;      // неуязвимость держится чуть дольше самого рывка
        private const float RibbonStep = 6f;        // шаг огненной ленты вдоль пути

        private static readonly Color DashFire = new(255, 130, 35);
        private static readonly Color DashCore = new(255, 230, 170);
        private static readonly Color DashEmber = new(200, 50, 20);
        private static readonly Color DashSmoke = new(60, 42, 36);

        private float _slashSide = -1f;
        private int _slashCount;
        private ulong _lastSlashTick;

        public bool isDashing;
        private Vector2 _dashDir;
        private float _dashRemaining;
        private float _dashStep;

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
            _dashDir = (Main.MouseWorld - Player.Center).SafeNormalize(Vector2.UnitX * Player.direction);
            _dashRemaining = FreeDistance(_dashDir, DashDistance);
            _dashStep = _dashRemaining / DashTicks;
            isDashing = _dashRemaining > 1f;

            SoundEngine.PlaySound(SoundID.Item74 with { Pitch = 0.2f }, Player.Center);
            if (!Main.dedServ)
                SpawnIgnition(Player.Center);
        }

        // Сколько можно пролететь по направлению, не задев сплошной блок
        private float FreeDistance(Vector2 dir, float max)
        {
            Vector2 position = Player.position;
            float traveled = 0f;
            while (traveled + DashSweepStep <= max)
            {
                Vector2 next = position + dir * DashSweepStep;
                if (Collision.SolidCollision(next, Player.width, Player.height))
                    break;
                position = next;
                traveled += DashSweepStep;
            }
            return traveled;
        }

        // Скорость ставим прямо перед сдвигом игрока: гравитация и ограничения скорости
        // этого тика уже отработали и рывок не срежут
        public override void PreUpdateMovement()
        {
            if (!isDashing)
                return;

            float step = Math.Min(_dashStep, _dashRemaining);
            Vector2 from = Player.Center;
            Player.velocity = _dashDir * step;
            _dashRemaining -= step;

            Player.fallStart = (int)(Player.position.Y / 16f); // рывок вниз — не падение
            Player.immune = true;
            Player.immuneTime = Math.Max(Player.immuneTime, DashGraceTicks);

            if (!Main.dedServ)
                SpawnFireRibbon(from, from + Player.velocity);

            if (_dashRemaining <= 0.5f)
                EndDash();
        }

        private void EndDash()
        {
            isDashing = false;
            Player.velocity = _dashDir * DashExitSpeed;
            if (!Main.dedServ)
                SpawnDashEnd(Player.Center + Player.velocity);
        }

        // Воспламенение на старте: кольцо раскалённых брызг и вспышка
        private void SpawnIgnition(Vector2 at)
        {
            for (int i = 0; i < 18; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                SoAParticles.SpawnStreak(at + dir * 8f, dir * Main.rand.NextFloat(3f, 8f),
                    Color.Lerp(DashFire, DashCore, Main.rand.NextFloat(0.5f)), Main.rand.NextFloat(2f, 3.2f),
                    gravity: 0.05f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.2f);
            }
            SoAParticles.SpawnGlow(at, Vector2.Zero, DashCore, 30f, 130f, 10);
            SoAParticles.AddLight(at, DashFire, 1.8f, 12);
        }

        // Огненная лента вдоль пути: раскалённое ядро, языки пламени вверх и дым. Живёт около
        // секунды и тает с хвоста — позади остаётся горящий след, а не облачко у ног
        private void SpawnFireRibbon(Vector2 from, Vector2 to)
        {
            Vector2 segment = to - from;
            float length = segment.Length();
            int points = Math.Max(1, (int)(length / RibbonStep));
            for (int i = 0; i < points; i++)
            {
                Vector2 at = from + segment * (i / (float)points) + Main.rand.NextVector2Circular(4f, 4f);

                // Ядро ленты: раскалённое и широкое, медленно разгорается и остывает
                SoAParticles.SpawnGlow(at, Vector2.Zero, Color.Lerp(DashFire, DashCore, Main.rand.NextFloat(0.3f)) * 0.55f,
                    34f, 16f, Main.rand.Next(38, 60));

                if (i % 2 == 0)
                {
                    SoAParticles.SpawnStreak(at, new Vector2(Main.rand.NextFloatDirection() * 0.6f, -Main.rand.NextFloat(1f, 2.6f)),
                        Color.Lerp(DashFire, DashEmber, Main.rand.NextFloat()), Main.rand.NextFloat(2f, 3.4f),
                        gravity: -0.04f, life: Main.rand.Next(30, 55), lengthPerSpeed: 2.4f);
                }
                if (i % 4 == 0)
                {
                    SoAParticles.SpawnSmoke(at, new Vector2(0f, -0.5f), DashSmoke, 24f, 70f, 0.3f, Main.rand.Next(50, 75));
                }
            }
            SoAParticles.AddLight(Vector2.Lerp(from, to, 0.5f), DashFire, 1.6f, 40);
        }

        private void SpawnDashEnd(Vector2 at)
        {
            for (int i = 0; i < 14; i++)
            {
                Vector2 dir = _dashDir.RotatedByRandom(1.1f);
                SoAParticles.SpawnStreak(at, dir * Main.rand.NextFloat(3f, 8f), DashFire,
                    Main.rand.NextFloat(2f, 3f), gravity: 0.12f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.2f);
            }
            SoAParticles.SpawnGlow(at, Vector2.Zero, DashFire * 0.6f, 30f, 80f, 10);
        }
    }
}
