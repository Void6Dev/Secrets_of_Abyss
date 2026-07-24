using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;
using Terraria.DataStructures;
using SoA.Content.Projectiles;
using SoA.Content.Items.Materials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Items.Weapons
{
    public class InfernoShuriken : ModItem
    {
        public override void SetDefaults()
        {
            Item.damage = 75;
            Item.DamageType = DamageClass.Ranged;
            Item.width = 30;
            Item.height = 30;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.knockBack = 3;
            Item.value = Item.sellPrice(gold: 6);
            Item.rare = ItemRarityID.Green;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = false;
            Item.shoot = ModContent.ProjectileType<InfernoShurikenProjectile>();
            Item.shootSpeed = 16f;
            Item.consumable = false;
            Item.maxStack = 1;
            Item.channel = true;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source,
            Vector2 position, Vector2 velocity, int type, int damage, float knockback) => false;

        public override void UseItemFrame(Player player) => ApplyPullBack(player);

        public override void HoldItemFrame(Player player)
        {
            if (Main.mouseLeft && !player.mouseInterface) ApplyPullBack(player);
        }

        internal static void ApplyPullBack(Player player)
        {
            float t = ShurikenChargePlayer.ChargeT(player.GetModPlayer<ShurikenChargePlayer>().chargeTime);
            float rotation = MathHelper.Lerp(-MathHelper.PiOver4, -MathHelper.Pi * 0.82f, t);
            if (player.direction == -1)
                rotation *= -1f;
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, rotation);
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            foreach (TooltipLine line in tooltips)
            {
                if (line.Name == "Tooltip1")
                {
                    float pulse = 0.5f + 0.5f * MathF.Sin(Main.GameUpdateCount * 0.08f);
                    line.OverrideColor = Color.Lerp(new Color(255, 80, 0), new Color(255, 220, 60), pulse);
                }
            }
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<LavaShard>(), 10);
            recipe.AddIngredient(ItemID.HellstoneBar, 15);
            recipe.AddIngredient(ItemID.Bone, 80);
            recipe.AddTile(TileID.Hellforge);
            recipe.Register();
        }
    }

    public class ShurikenHeldDrawLayer : PlayerDrawLayer
    {
        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            return player.HeldItem.type == ModContent.ItemType<InfernoShuriken>()
                && player.GetModPlayer<ShurikenChargePlayer>().chargeTime > 0;
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            var mp = player.GetModPlayer<ShurikenChargePlayer>();
            float t = ShurikenChargePlayer.ChargeT(mp.chargeTime);
            bool inPerfect = ShurikenChargePlayer.IsPerfectWindow(mp.chargeTime);

            float rotation = MathHelper.Lerp(-MathHelper.PiOver4, -MathHelper.Pi * 0.82f, t);
            if (player.direction == -1)
                rotation *= -1f;

            Vector2 handPos = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, rotation);
            Vector2 drawPos = handPos - Main.screenPosition;
            float spriteRot = rotation + MathHelper.PiOver4 * player.direction;

            Texture2D texNormal = ModContent.Request<Texture2D>("SoA/Content/Items/Weapons/InfernoShuriken").Value;
            Texture2D texActive = ModContent.Request<Texture2D>("SoA/Content/Projectiles/InfernoShurikenProjectile_active").Value;
            Vector2 origin = texNormal.Size() / 2f;

            float perfectPulse = inPerfect ? (0.8f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.4f)) : 1f;
            Color activeColor = inPerfect
                ? Color.Lerp(Color.White, new Color(255, 160, 40), 0.5f) * perfectPulse
                : Color.White;

            if (t < 1f)
                drawInfo.DrawDataCache.Add(new DrawData(texNormal, drawPos, null,
                    Color.White * (1f - t), spriteRot, origin, 1f, SpriteEffects.None, 0));

            if (t > 0f)
                drawInfo.DrawDataCache.Add(new DrawData(texActive, drawPos, null,
                    activeColor * t, spriteRot, origin, 1f, SpriteEffects.None, 0));
        }
    }

    public class ShurikenChargePlayer : ModPlayer
    {
        public const int MaxCharge = 50;
        public const int PerfectWindowStart = 25;
        public const int PerfectWindowEnd = 40;
        private const int CooldownNormal = 20;
        private const int CooldownPerfect = 20;

        public int chargeTime = 0;
        public int cooldownTimer = 0;
        private bool _wasChan = false;
        private bool _perfectPingSent = false;

        // True only within the perfect timing window
        public static bool IsPerfectWindow(int ct) =>
            ct >= PerfectWindowStart && ct <= PerfectWindowEnd;

        // Sprite/arm fill: ramps to 1 at PerfectWindowStart, holds through window, fades back after
        public static float ChargeT(int ct)
        {
            if (ct <= PerfectWindowStart)
                return (float)ct / PerfectWindowStart;
            if (ct <= PerfectWindowEnd)
                return 1f;
            return MathHelper.Clamp(
                1f - (float)(ct - PerfectWindowEnd) / (MaxCharge - PerfectWindowEnd), 0f, 1f);
        }

        // Trajectory power: peaks at PerfectWindowEnd, degrades if held past it
        public static float TrajectoryRatio(int ct)
        {
            if (ct <= PerfectWindowEnd)
                return MathHelper.Clamp((float)ct / PerfectWindowEnd, 0.05f, 1f);
            return MathHelper.Lerp(1f, 0.4f,
                (float)(ct - PerfectWindowEnd) / (MaxCharge - PerfectWindowEnd));
        }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer) return;

            if (cooldownTimer > 0)
            {
                cooldownTimer--;
                chargeTime = 0;
                return;
            }

            bool heldShuriken = Player.HeldItem.type == ModContent.ItemType<InfernoShuriken>();
            bool channeling = heldShuriken && Main.mouseLeft && !Player.mouseInterface;

            if (channeling)
            {
                chargeTime = Math.Min(chargeTime + 1, MaxCharge);
                _wasChan = true;

                bool inPerfect = IsPerfectWindow(chargeTime);
                float glow = ChargeT(chargeTime);

                if (chargeTime == PerfectWindowStart && !_perfectPingSent)
                {
                    SoundEngine.PlaySound(SoundID.MaxMana, Player.position);
                    _perfectPingSent = true;
                }

                Lighting.AddLight(Player.MountedCenter,
                    inPerfect ? 1.5f : glow * 1.2f,
                    inPerfect ? 0.7f : glow * 0.5f,
                    0f);

                if (inPerfect && Main.GameUpdateCount % 4 == 0)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = MathHelper.TwoPi / 6 * i + Main.GameUpdateCount * 0.15f;
                        Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 14f;
                        Dust ring = Dust.NewDustDirect(Player.MountedCenter + offset - new Vector2(4), 8, 8,
                            DustID.InfernoFork, 0f, 0f);
                        ring.scale = 1.2f;
                        ring.noGravity = true;
                        ring.velocity = Vector2.Zero;
                    }
                }
                else if (!inPerfect && chargeTime > 5 && Main.rand.NextBool(3))
                {
                    Dust fire = Dust.NewDustDirect(Player.MountedCenter - new Vector2(8), 16, 16,
                        DustID.InfernoFork, Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f));
                    fire.scale = 0.5f + glow;
                    fire.noGravity = true;
                }
            }
            else if (_wasChan)
            {
                if (chargeTime >= 1)
                {
                    bool isPerfect = IsPerfectWindow(chargeTime);
                    FireShuriken(isPerfect);
                    cooldownTimer = isPerfect ? CooldownPerfect : CooldownNormal;
                }
                chargeTime = 0;
                _wasChan = false;
                _perfectPingSent = false;
            }
            else
            {
                chargeTime = 0;
                _perfectPingSent = false;
            }
        }

        private void FireShuriken(bool isPerfect)
        {
            float ratio = TrajectoryRatio(chargeTime);
            float speed = MathHelper.Lerp(8f, 22f, ratio);
            float gravity = MathHelper.Lerp(0.35f, 0.04f, ratio);
            Vector2 dir = Vector2.Normalize(Main.MouseWorld - Player.MountedCenter);
            var source = Player.GetSource_ItemUse(Player.HeldItem);
            int damage = Player.GetWeaponDamage(Player.HeldItem);
            if (isPerfect) damage = (int)(damage * 2f);

            // Negative ai1 = perfect shot flag; gravity = Math.Abs(ai1)
            float ai1 = isPerfect ? -gravity : gravity;
            Projectile.NewProjectile(source, Player.MountedCenter, dir * speed,
                ModContent.ProjectileType<InfernoShurikenProjectile>(), damage, Player.HeldItem.knockBack, Player.whoAmI,
                0f, ai1);

            int burstCount = isPerfect ? 20 : 10;
            for (int i = 0; i < burstCount; i++)
            {
                Dust d = Dust.NewDustDirect(Player.MountedCenter - new Vector2(8), 16, 16,
                    DustID.InfernoFork, dir.X * Main.rand.NextFloat(3f, 9f), dir.Y * Main.rand.NextFloat(3f, 9f));
                d.scale = isPerfect ? 1.8f : 1.2f;
                d.noGravity = true;
            }
            SoundEngine.PlaySound(isPerfect ? SoundID.Item73 : SoundID.Item1, Player.position);
        }
    }
}
