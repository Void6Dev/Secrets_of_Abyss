using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;
using SoA.Content.Projectiles;

namespace SoA.Content.Items.Weapons
{
    // Scythe of Fire Storm: зажал — обе руки оттягивают косу за спину,
    // на полном заряде вокруг лезвия загорается огненный ободок,
    // отпустил — коса выпускает огненное торнадо по траектории прицела.
    public class ScytheOfFireStorm : ModItem
    {
        public const int MaxActiveTornadoes = 2;

        public override void SetDefaults()
        {
            Item.damage = 38;
            Item.DamageType = DamageClass.Magic;
            Item.mana = 14;
            Item.width = 40;
            Item.height = 40;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.noMelee = true;
            Item.noUseGraphic = true; // косу рисует ScytheHeldDrawLayer во время замаха
            Item.knockBack = 3f;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange;
            Item.UseSound = SoundID.Item20;
            Item.autoReuse = false;
            Item.channel = true; // зарядку ведёт ScytheChargePlayer
            Item.shoot = ModContent.ProjectileType<FireTornadoProjectile>();
            Item.shootSpeed = 1f;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source,
            Vector2 position, Vector2 velocity, int type, int damage, float knockback) => false;

        public override void UseItemFrame(Player player) => ScytheChargePlayer.ApplyPullBack(player);

        public override void HoldItemFrame(Player player)
        {
            if (player.GetModPlayer<ScytheChargePlayer>().chargeTime > 0)
                ScytheChargePlayer.ApplyPullBack(player);
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<LavaShard>(), 15)
                .AddIngredient(ItemID.AshBlock, 10)
                .AddIngredient(ModContent.ItemType<HellStar>(), 1)
                .AddTile(TileID.Hellforge)
                .Register();
        }
    }

    // Рисует косу в оттянутых за спину руках; на полном заряде — огненный ободок вокруг лезвия
    public class ScytheHeldDrawLayer : PlayerDrawLayer
    {
        private const float DrawScale = 0.8f;
        // Точка хвата: смещена от центра спрайта вниз по древку (рука держит рукоять ниже).
        // Древко в спрайте идёт из верхнего правого угла (лезвие) в нижний левый (конец рукояти)
        private static readonly Vector2 GripOffsetFromCenter = new(-18f, 18f);

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            return player.HeldItem.type == ModContent.ItemType<ScytheOfFireStorm>()
                && player.GetModPlayer<ScytheChargePlayer>().chargeTime > 0;
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            var mp = player.GetModPlayer<ScytheChargePlayer>();
            float t = mp.ChargeRatio;
            bool fullCharge = mp.chargeTime >= ScytheChargePlayer.MaxCharge;

            float frontRot = ScytheChargePlayer.FrontArmRotation(t, player.direction);
            Vector2 handPos = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, frontRot);
            Vector2 drawPos = handPos - Main.screenPosition;
            float spriteRot = frontRot + MathHelper.PiOver4 * player.direction;

            Texture2D tex = ModContent.Request<Texture2D>("SoA/Content/Items/Weapons/ScytheOfFireStorm").Value;
            SpriteEffects fx = player.direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            // Пивот в точке хвата на древке; при развороте зеркалим по X
            Vector2 grip = tex.Size() / 2f + GripOffsetFromCenter;
            Vector2 origin = player.direction == -1 ? new Vector2(tex.Width - grip.X, grip.Y) : grip;

            // Огненный ободок: аддитивные копии спрайта по кругу (альфа 0 = светящийся бленд)
            if (fullCharge)
            {
                float pulse = 0.7f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.35f);
                Color rimColor = new Color(255, 130, 30, 0) * pulse;
                const int rimCopies = 8;
                for (int i = 0; i < rimCopies; i++)
                {
                    Vector2 offset = (MathHelper.TwoPi / rimCopies * i).ToRotationVector2()
                        * (3f + (float)Math.Sin(Main.GameUpdateCount * 0.2f));
                    drawInfo.DrawDataCache.Add(new DrawData(tex, drawPos + offset, null,
                        rimColor, spriteRot, origin, DrawScale, fx, 0));
                }
            }

            drawInfo.DrawDataCache.Add(new DrawData(tex, drawPos, null,
                Color.White, spriteRot, origin, DrawScale, fx, 0));
        }
    }

    // Зарядка косы: тот же подход, что у ShurikenChargePlayer (InfernoShuriken.cs)
    public class ScytheChargePlayer : ModPlayer
    {
        public const int MaxCharge = 60;
        public const int MinCharge = 15;

        private const float MinLaunchSpeed = 4.5f;
        private const float MaxLaunchSpeed = 12f;
        private const float MinDamageMult = 0.6f;
        private const float MaxDamageMult = 1.5f;

        public int chargeTime;
        private bool _wasCharging;
        private bool _fullPingSent;

        public float ChargeRatio => chargeTime / (float)MaxCharge;

        // Обе руки оттягивают косу за спину по мере зарядки
        public static float FrontArmRotation(float t, int direction)
        {
            float rotation = MathHelper.Lerp(-MathHelper.PiOver4, -MathHelper.Pi * 0.85f, t);
            return direction == -1 ? -rotation : rotation;
        }

        public static float BackArmRotation(float t, int direction)
        {
            float rotation = MathHelper.Lerp(-MathHelper.PiOver4 * 0.6f, -MathHelper.Pi * 0.72f, t);
            return direction == -1 ? -rotation : rotation;
        }

        public static void ApplyPullBack(Player player)
        {
            float t = player.GetModPlayer<ScytheChargePlayer>().ChargeRatio;
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full,
                FrontArmRotation(t, player.direction));
            player.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Full,
                BackArmRotation(t, player.direction));
        }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            bool heldScythe = Player.HeldItem.type == ModContent.ItemType<ScytheOfFireStorm>();
            bool charging = heldScythe && Player.channel && !Player.CCed;

            if (charging)
            {
                chargeTime = Math.Min(chargeTime + 1, MaxCharge);
                _wasCharging = true;
                ChargeVisuals();
            }
            else if (_wasCharging)
            {
                if (chargeTime >= MinCharge)
                    LaunchTornado();
                chargeTime = 0;
                _wasCharging = false;
                _fullPingSent = false;
            }
            else
            {
                chargeTime = 0;
                _fullPingSent = false;
            }
        }

        private void ChargeVisuals()
        {
            // Лезвие примерно в центре спрайта у передней руки
            Vector2 bladePos = Player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full,
                FrontArmRotation(ChargeRatio, Player.direction));

            bool fullCharge = chargeTime >= MaxCharge;
            if (fullCharge && !_fullPingSent)
            {
                _fullPingSent = true;
                SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.6f, Pitch = 0.4f }, Player.position);
            }

            Lighting.AddLight(bladePos, 0.9f * ChargeRatio, 0.4f * ChargeRatio, 0.05f);

            if (fullCharge)
            {
                // Огненный ободок: кольцо искр вращается вокруг лезвия
                if (Main.GameUpdateCount % 2 == 0)
                {
                    float angle = Main.GameUpdateCount * 0.3f;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 ringOffset = (angle + MathHelper.TwoPi / 3f * i).ToRotationVector2() * 24f;
                        Dust d = Dust.NewDustPerfect(bladePos + ringOffset, DustID.Torch);
                        d.noGravity = true;
                        d.velocity = ringOffset.RotatedBy(MathHelper.PiOver2) * 0.08f;
                        d.scale = 1.4f;
                    }
                }
            }
            else if (Main.rand.NextBool(2))
            {
                // Искры стягиваются к лезвию, пока идёт замах
                Vector2 spawnOffset = Main.rand.NextVector2CircularEdge(26f, 26f);
                Dust d = Dust.NewDustPerfect(bladePos + spawnOffset, DustID.Torch);
                d.noGravity = true;
                d.velocity = -spawnOffset * 0.12f;
                d.scale = 0.8f + ChargeRatio;
            }
        }

        private void LaunchTornado()
        {
            // Лимит без блокировки посоха: новый вихрь гасит самый старый
            int tornadoType = ModContent.ProjectileType<FireTornadoProjectile>();
            if (Player.ownedProjectileCounts[tornadoType] >= ScytheOfFireStorm.MaxActiveTornadoes)
            {
                Projectile oldest = null;
                foreach (Projectile proj in Main.projectile)
                {
                    if (proj.active && proj.owner == Player.whoAmI && proj.type == tornadoType
                        && (oldest == null || proj.timeLeft < oldest.timeLeft))
                    {
                        oldest = proj;
                    }
                }
                oldest?.Kill();
            }

            float ratio = ChargeRatio;
            float speed = MathHelper.Lerp(MinLaunchSpeed, MaxLaunchSpeed, ratio);
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
            int damage = (int)(Player.GetWeaponDamage(Player.HeldItem)
                * MathHelper.Lerp(MinDamageMult, MaxDamageMult, ratio));

            Projectile.NewProjectile(Player.GetSource_ItemUse(Player.HeldItem),
                Player.MountedCenter, dir * speed,
                ModContent.ProjectileType<FireTornadoProjectile>(), damage,
                Player.HeldItem.knockBack, Player.whoAmI, ratio);

            SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.9f, Pitch = -0.2f }, Player.Center);
            for (int i = 0; i < 14; i++)
            {
                Dust d = Dust.NewDustPerfect(Player.MountedCenter + dir * 20f, DustID.InfernoFork,
                    dir.RotatedByRandom(0.5f) * Main.rand.NextFloat(2f, 7f));
                d.noGravity = true;
                d.scale = 1f + ratio;
            }
        }
    }
}
