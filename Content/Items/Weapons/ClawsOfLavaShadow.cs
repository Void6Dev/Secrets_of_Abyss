using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using SoA.Content.Items.Materials;
using SoA.Content.Buffs;
using Terraria.Audio;
using System;

namespace SoA.Content.Items.Weapons
{
    public class ClawsOfLavaShadow : ModItem
    {
        private int dashCooldown = 120;
        private int dashCooldownCounter = 0;
        private bool _dashReady = true;

        public override void SetDefaults() {
            // Урон ниже, чем у обычного оружия этапа: метка копит его и взрывается вторым разом
            Item.damage = 24;
            Item.DamageType = DamageClass.Melee;
            Item.width = 30;
            Item.scale = 2;
            Item.height = 20;
            Item.useTime = 12;
            Item.useAnimation = 12;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 2;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange; // этап адского камня, а не финал игры
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
        }

        public override void AddRecipes() {
            Recipe recipe = CreateRecipe();
            // Раньше здесь дважды стояли осколки (опечатка) и крафт шёл у простой печи
            recipe.AddIngredient(ModContent.ItemType<LavaShard>(), 12);
            recipe.AddIngredient(ItemID.HellstoneBar, 10);
            recipe.AddTile(TileID.Hellforge);
            recipe.Register();
        }

        public override void ModifyHitNPC(Player player, NPC target, ref NPC.HitModifiers modifiers) {
            target.AddBuff(ModContent.BuffType<LavaExplosionDebuff>(), 30);
            LavaExplosionGlobalNPC modNPC = target.GetGlobalNPC<LavaExplosionGlobalNPC>();
            modNPC.cumulativeDamage += Item.damage;
        }

        public override void MeleeEffects(Player player, Rectangle hitbox) {
            // Dense lava sparks along the swing arc
            for (int i = 0; i < 2; i++)
            {
                Dust spark = Dust.NewDustDirect(new Vector2(hitbox.X, hitbox.Y), hitbox.Width, hitbox.Height,
                    DustID.SolarFlare, Main.rand.NextFloat(-4f, 4f), Main.rand.NextFloat(-4f, 4f));
                spark.scale = Main.rand.NextFloat(1f, 2f);
                spark.noGravity = true;
            }
            if (Main.rand.NextBool(3))
            {
                Dust ember = Dust.NewDustDirect(new Vector2(hitbox.X, hitbox.Y), hitbox.Width, hitbox.Height,
                    DustID.InfernoFork, Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f));
                ember.scale = Main.rand.NextFloat(0.8f, 1.5f);
                ember.noGravity = false;
            }
            Lighting.AddLight(new Vector2(hitbox.Center.X, hitbox.Center.Y), 0.9f, 0.35f, 0f);
        }
        
        

        public override bool AltFunctionUse(Player player) {
            return true;
        }

        public override bool CanUseItem(Player player)
        {
            if (player.altFunctionUse == 2)
            {
                Item.noUseGraphic = true;
                Item.noMelee = true;
                return _dashReady;
            }

            Item.noUseGraphic = false;
            Item.noMelee = false;
            return true;
        }

        public override bool? UseItem(Player player)
        {
            if (player.altFunctionUse == 2 && _dashReady)
            {
                DashTowardsCursor(player);
                return true;
            }
            return base.UseItem(player);
        }

        private void DashTowardsCursor(Player player)
        {
            _dashReady = false;
            dashCooldownCounter = dashCooldown;
            LavaDashPlayer modPlayer = player.GetModPlayer<LavaDashPlayer>();
            modPlayer.StartDash();
            Vector2 dashDirection = Vector2.Normalize(Main.MouseWorld - player.Center);
            player.velocity = dashDirection * 15f;
        }

        public override void UpdateInventory(Player player)
        {
            if (dashCooldownCounter > 0)
                dashCooldownCounter--;

            if (!_dashReady && dashCooldownCounter == 0 && player.velocity.Y == 0)
            {
                _dashReady = true;
                OnDashReady(player);
            }

            if (player.altFunctionUse != 2)
            {
                Item.noUseGraphic = false;
                Item.noMelee = false;
            }
        }

        public override void HoldItem(Player player)
        {
            player.ChangeDir(MathF.Sign(player.velocity.X != 0 ? player.velocity.X : player.direction));
        }

    
        private void OnDashReady(Player player)
        {
            // ✨ вспышка
            for (int i = 0; i < 20; i++)
            {
                Dust dust = Dust.NewDustDirect(player.position, player.width, player.height,
                    DustID.InfernoFork,
                    Main.rand.NextFloat(-4f, 4f),
                    Main.rand.NextFloat(-4f, 4f));

                dust.noGravity = true;
                dust.scale = 1.5f;
            }

            // 💡 свет
            Lighting.AddLight(player.Center, 1f, 0.4f, 0f);

            // 🔊 звук
            SoundEngine.PlaySound(SoundID.Item29, player.Center);

            // ⚡ короткий "флэш"
            player.immune = false;
            player.immuneTime = 5;
        }
    }
    public class LavaDashPlayer : ModPlayer
    {
        public bool isDashing;
        private int dashDuration = 25;
        private Vector2 dashVelocity;
        private int dashFrameCounter;

        public override void PostUpdate()
        {
            if (!isDashing)
                return;

            dashFrameCounter++;
            if (dashFrameCounter <= dashDuration)
            {
                // Dense lava trail — 3 particles per frame
                for (int i = 0; i < 3; i++)
                {
                    Dust trail = Dust.NewDustDirect(Player.position, Player.width, Player.height,
                        DustID.SolarFlare,
                        -dashVelocity.X * Main.rand.NextFloat(0.1f, 0.35f),
                        -dashVelocity.Y * Main.rand.NextFloat(0.1f, 0.35f));
                    trail.scale = Main.rand.NextFloat(1.2f, 2.2f);
                    trail.noGravity = true;
                }
                // Smoke wisps
                if (Main.rand.NextBool(3))
                {
                    Dust smoke = Dust.NewDustDirect(Player.position, Player.width, Player.height,
                        DustID.Smoke, -dashVelocity.X * 0.15f, -dashVelocity.Y * 0.15f, 120, default, 1.1f);
                    smoke.noGravity = false;
                }

                Lighting.AddLight(Player.Center, 1f, 0.4f, 0f);
                Player.immune = true;
                Player.immuneTime = 15;
            }
            else
            {
                // End-of-dash burst
                for (int i = 0; i < 22; i++)
                {
                    Dust burst = Dust.NewDustDirect(Player.position, Player.width, Player.height,
                        DustID.SolarFlare, Main.rand.NextFloat(-6f, 6f), Main.rand.NextFloat(-6f, 6f));
                    burst.scale = Main.rand.NextFloat(1.3f, 2.4f);
                    burst.noGravity = true;
                }

                isDashing = false;
                dashFrameCounter = 0;
                Player.immune = false;
            }
        }

        public void StartDash()
        {
            isDashing = true;
            dashFrameCounter = 0;
            dashVelocity = Vector2.Normalize(Main.MouseWorld - Player.Center) * 20f;
            Player.velocity = dashVelocity;

            // Ignition burst at dash start
            for (int i = 0; i < 28; i++)
            {
                Dust ignite = Dust.NewDustDirect(Player.position, Player.width, Player.height,
                    DustID.InfernoFork, Main.rand.NextFloat(-7f, 7f), Main.rand.NextFloat(-7f, 7f));
                ignite.scale = Main.rand.NextFloat(1.5f, 2.8f);
                ignite.noGravity = true;
            }
            SoundEngine.PlaySound(SoundID.Item74, Player.Center);
        }
    }
}
