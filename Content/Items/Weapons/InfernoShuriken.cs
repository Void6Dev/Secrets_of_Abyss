using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;
using Terraria.DataStructures;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Players;
using SoA.Content.Projectiles;
using SoA.Content.Items.Materials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Items.Weapons
{
    // Инферно-сюрикен: зажал — сюрикен раскаляется в руке, отпустил — бросок. Вид броска
    // решает момент отпускания (ShurikenChargePlayer), поведение — InfernoShurikenProjectile
    public class InfernoShuriken : ModItem
    {
        public override void SetDefaults()
        {
            // Идеальный бросок бьёт несколько раз и взрывается, поэтому база ниже, чем у оружия этапа
            Item.damage = 36;
            Item.DamageType = DamageClass.Ranged;
            Item.width = 30;
            Item.height = 30;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.noMelee = true;
            Item.noUseGraphic = true;  // сюрикен в руке рисует ShurikenHeldDrawLayer
            Item.knockBack = 3;
            Item.value = Item.sellPrice(gold: 3);
            Item.rare = ItemRarityID.Orange;
            // Без UseSound: он звучал в начале зарядки. Звук броска — у снаряда
            Item.autoReuse = false;
            Item.shoot = ModContent.ProjectileType<InfernoShurikenProjectile>();
            Item.shootSpeed = 16f;
            Item.consumable = false;
            Item.maxStack = 1;
            Item.channel = true;
        }

        // Бросает ShurikenChargePlayer при отпускании
        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source,
            Vector2 position, Vector2 velocity, int type, int damage, float knockback) => false;

        public override void UseItemFrame(Player player) => ApplyPullBack(player);

        public override void HoldItemFrame(Player player)
        {
            // Раньше смотрели на Main.mouseLeft — мышь ЛОКАЛЬНОГО игрока: чужие руки дёргались от своих кликов
            if (player.GetModPlayer<ShurikenChargePlayer>().chargeTime > 0)
                ApplyPullBack(player);
        }

        internal static void ApplyPullBack(Player player)
        {
            int charge = player.GetModPlayer<ShurikenChargePlayer>().chargeTime;
            player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full,
                ShurikenChargePlayer.ArmRotation(charge, player.direction));
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            foreach (TooltipLine line in tooltips)
            {
                // Строка про идеальный бросок переливается от красного к белому накалу
                if (line.Name == "Tooltip2")
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

    // Сюрикен в руке во время зарядки: накал цветом, сужающееся кольцо-подсказка перед окном,
    // звёздный блик в момент открытия окна, дрожь и багровое мерцание при перегреве.
    // Всё рисуется данными игрока, поэтому видно и другим игрокам (зарядка синхронизирована)
    public class ShurikenHeldDrawLayer : PlayerDrawLayer
    {
        private const int RingSegments = 28;
        private const float RingStartRadius = 72f;
        private const float RingEndRadius = 16f;     // совпадает с краем спрайта
        private const float GlintLength = 96f;
        private const float HeatOrbitRadius = 22f;

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
            int charge = mp.chargeTime;
            float t = ShurikenChargePlayer.ChargeT(charge);
            float overheat = ShurikenChargePlayer.OverheatT(charge);
            bool inWindow = ShurikenChargePlayer.IsPerfectWindow(charge);
            Color heat = ShurikenChargePlayer.HeatColor(charge);

            Vector2 hand = ShurikenChargePlayer.HeldPosition(player, out float spriteRot);
            if (overheat > 0f)
                hand += Main.rand.NextVector2Circular(1f + 2f * overheat, 1f + 2f * overheat);
            Vector2 drawPos = hand - Main.screenPosition;

            Texture2D texNormal = ModContent.Request<Texture2D>("SoA/Content/Items/Weapons/InfernoShuriken").Value;
            Texture2D texActive = ModContent.Request<Texture2D>(InfernoShurikenProjectile.ActiveTexturePath).Value;
            Texture2D glow = SoAVfx.SoftGlow;
            Vector2 origin = texNormal.Size() / 2f;

            // Ореол накала за спрайтом
            float pulse = inWindow ? 0.85f + 0.15f * MathF.Sin(Main.GameUpdateCount * 0.4f) : 1f;
            float haloSize = (34f + 18f * t + 14f * overheat) * pulse;
            Add(ref drawInfo, glow, drawPos, SoAVfx.Additive(heat * (0.25f + 0.45f * t)), 0f, glow.Size() / 2f,
                new Vector2(haloSize / glow.Width));

            DrawTimingRing(ref drawInfo, drawPos, charge);

            // Сам сюрикен светится сам: цвет не зависит от освещения
            if (t < 1f)
                Add(ref drawInfo, texNormal, drawPos, Color.White * (1f - t), spriteRot, origin, Vector2.One);
            if (t > 0f)
                Add(ref drawInfo, texActive, drawPos, Color.White * t, spriteRot, origin, Vector2.One);

            // Накал поверх спрайта: в окне — белое свечение, при перегреве — мерцание
            float overlay = inWindow ? 0.35f * pulse : 0.2f * t + 0.45f * overheat;
            if (overlay > 0f)
                Add(ref drawInfo, texActive, drawPos, SoAVfx.Additive(heat * overlay), spriteRot, origin, new Vector2(1.08f));

            DrawGlint(ref drawInfo, drawPos, charge);
            DrawHeatStacks(ref drawInfo, drawPos, mp.heat, heat);
        }

        // Кольцо сжимается к спрайту и касается его ровно в момент открытия окна:
        // игрок видит, КОГДА откроется окно, а не реагирует на звук постфактум
        private static void DrawTimingRing(ref PlayerDrawSet drawInfo, Vector2 center, int charge)
        {
            int ringStart = ShurikenChargePlayer.PerfectWindowStart - ShurikenChargePlayer.RingLeadTicks;
            if (charge < ringStart || charge >= ShurikenChargePlayer.PerfectWindowStart)
                return;

            float p = (charge - ringStart) / (float)ShurikenChargePlayer.RingLeadTicks;
            float radius = MathHelper.Lerp(RingStartRadius, RingEndRadius, p * p);
            Color color = SoAVfx.Additive(Color.Lerp(new Color(255, 110, 30), new Color(255, 236, 170), p) * (0.2f + 0.7f * p));
            float thickness = MathHelper.Lerp(2f, 4f, p);

            Texture2D streak = SoAVfx.SoftStreak;
            float segment = MathHelper.TwoPi * radius / RingSegments + 3f;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = MathHelper.TwoPi * i / RingSegments + Main.GameUpdateCount * 0.05f;
                Vector2 pos = center + angle.ToRotationVector2() * radius;
                Add(ref drawInfo, streak, pos, color, angle + MathHelper.PiOver2, streak.Size() / 2f,
                    new Vector2(segment / streak.Width, thickness / streak.Height));
            }
        }

        // Звёздный блик крестом в момент открытия окна
        private static void DrawGlint(ref PlayerDrawSet drawInfo, Vector2 center, int charge)
        {
            int since = charge - ShurikenChargePlayer.PerfectWindowStart;
            if (since < 0 || since >= ShurikenChargePlayer.GlintTicks)
                return;

            float g = 1f - since / (float)ShurikenChargePlayer.GlintTicks;
            g *= g;
            Texture2D streak = SoAVfx.SoftStreak;
            Color color = SoAVfx.Additive(new Color(255, 245, 210) * g);
            Vector2 origin = streak.Size() / 2f;
            float length = 20f + GlintLength * g;
            float thickness = 2f + 6f * g;
            float spin = since * 0.06f;

            Add(ref drawInfo, streak, center, color, spin, origin, new Vector2(length / streak.Width, thickness / streak.Height));
            Add(ref drawInfo, streak, center, color, spin + MathHelper.PiOver2, origin, new Vector2(length / streak.Width, thickness / streak.Height));
            Add(ref drawInfo, streak, center, color * 0.5f, spin + MathHelper.PiOver4, origin,
                new Vector2(length * 0.45f / streak.Width, thickness * 0.7f / streak.Height));
            Add(ref drawInfo, streak, center, color * 0.5f, spin - MathHelper.PiOver4, origin,
                new Vector2(length * 0.45f / streak.Width, thickness * 0.7f / streak.Height));
        }

        // Стаки Жара — угольки на орбите вокруг сюрикена
        private static void DrawHeatStacks(ref PlayerDrawSet drawInfo, Vector2 center, int stacks, Color heat)
        {
            if (stacks <= 0)
                return;

            Texture2D glow = SoAVfx.SoftGlow;
            for (int i = 0; i < stacks; i++)
            {
                float angle = Main.GameUpdateCount * 0.12f + MathHelper.TwoPi * i / stacks;
                Vector2 pos = center + angle.ToRotationVector2() * HeatOrbitRadius;
                Add(ref drawInfo, glow, pos, SoAVfx.Additive(Color.Lerp(heat, Color.White, 0.4f) * 0.9f), 0f, glow.Size() / 2f,
                    new Vector2(12f / glow.Width));
            }
        }

        private static void Add(ref PlayerDrawSet drawInfo, Texture2D tex, Vector2 pos, Color color, float rotation,
            Vector2 origin, Vector2 scale)
        {
            drawInfo.DrawDataCache.Add(new DrawData(tex, pos, null, color, rotation, origin, scale, SpriteEffects.None, 0));
        }
    }

    // Зарядка сюрикена. Окно идеального броска — PerfectWindowStart..PerfectWindowEnd;
    // раньше — лёгкий бросок, позже — перегрев. Пойманный идеальный бросок даёт стак Жара
    public class ShurikenChargePlayer : ChargedWeaponPlayer
    {
        public const int PerfectWindowStart = 25;
        public const int PerfectWindowEnd = 40;
        public const int MaxCharge = 60;
        public const int RingLeadTicks = 18;        // кольцо-подсказка появляется за столько тиков до окна
        public const int GlintTicks = 8;
        public const int MaxHeat = 3;

        private const int CooldownLight = 20;
        private const int CooldownPerfect = 80;     // пойманный сюрикен обнуляет
        private const int CooldownOverheat = 30;
        private const int OverheatBurnTicks = 120;

        private const float LightMinSpeed = 9f;
        private const float LightMaxSpeed = 17f;
        private const float PerfectSpeed = 21f;
        private const float OverheatSpeed = 24f;

        private static readonly Color EmberColor = new(120, 22, 10);
        private static readonly Color HeatOrange = new(255, 120, 30);
        private static readonly Color WhiteHot = new(255, 236, 160);
        private static readonly Color OverheatRed = new(255, 60, 30);
        private static readonly Color SmokeColor = new(55, 40, 36);

        public int heat;

        protected override int WeaponType => ModContent.ItemType<InfernoShuriken>();
        protected override int MaxChargeTicks => MaxCharge;
        protected override SoAPacketType PacketType => SoAPacketType.ShurikenCharge;
        protected override byte ExtraSyncState { get => (byte)heat; set => heat = value; }

        public static bool IsPerfectWindow(int ct) => ct >= PerfectWindowStart && ct <= PerfectWindowEnd;

        // Отвод руки: 0..1 к началу окна и дальше держится
        public static float ChargeT(int ct) => Math.Min(ct / (float)PerfectWindowStart, 1f);

        // Перегрев: 0 до конца окна, 1 на полной передержке
        public static float OverheatT(int ct)
            => ct <= PerfectWindowEnd ? 0f : Math.Min((ct - PerfectWindowEnd) / (float)(MaxCharge - PerfectWindowEnd), 1f);

        // Цвет накала: тёмно-красный → оранжевый → бело-жёлтый в окне → багровое мерцание перегрева
        public static Color HeatColor(int ct)
        {
            if (ct < PerfectWindowStart)
                return Color.Lerp(EmberColor, HeatOrange, ct / (float)PerfectWindowStart);
            if (ct <= PerfectWindowEnd)
                return WhiteHot;
            float flicker = 0.5f + 0.5f * MathF.Sin(Main.GameUpdateCount * 0.9f);
            return Color.Lerp(WhiteHot, OverheatRed, OverheatT(ct) * (0.55f + 0.45f * flicker));
        }

        public static float ArmRotation(int ct, int direction)
            => MirrorForDirection(MathHelper.Lerp(-MathHelper.PiOver4, -MathHelper.Pi * 0.82f, ChargeT(ct)), direction);

        // Где сюрикен в руке (мир) и как повёрнут спрайт
        public static Vector2 HeldPosition(Player player, out float spriteRotation)
        {
            float rotation = ArmRotation(player.GetModPlayer<ShurikenChargePlayer>().chargeTime, player.direction);
            spriteRotation = rotation + MathHelper.PiOver4 * player.direction;
            return player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, rotation);
        }

        // Мышь смотрим только у владельца (базовый класс спрашивает только его)
        protected override bool ChargeInputHeld() => Main.mouseLeft && !Player.mouseInterface;

        public void OnShurikenCaught()
        {
            heat = Math.Min(heat + 1, MaxHeat);
            cooldownTimer = 0;
        }

        public void ResetHeat() => heat = 0;

        protected override void OnChargeTick()
        {
            Vector2 hand = HeldPosition(Player, out _);
            Color color = HeatColor(chargeTime);
            float t = ChargeT(chargeTime);
            float overheat = OverheatT(chargeTime);

            Lighting.AddLight(hand, color.ToVector3() * (0.3f + 0.9f * t));

            if (JustReached(PerfectWindowStart))
            {
                SoundEngine.PlaySound(SoundID.MaxMana with { Pitch = 0.3f }, hand);
                SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.35f, Pitch = 0.6f }, hand);
                SoAParticles.SpawnGlow(hand, Vector2.Zero, WhiteHot, 20f, 70f, 10);
                for (int i = 0; i < 10; i++)
                {
                    Vector2 dir = (MathHelper.TwoPi * i / 10f).ToRotationVector2();
                    SoAParticles.SpawnStreak(hand + dir * 8f, dir * 4f, WhiteHot, 2f, gravity: 0f, life: 12, lengthPerSpeed: 2f);
                }
            }
            if (JustReached(PerfectWindowEnd + 1))
            {
                // Окно упущено: сюрикен шипит и начинает дымить
                SoundEngine.PlaySound(SoundID.Item20 with { Volume = 0.5f, Pitch = -0.4f }, hand);
            }

            if (overheat > 0f)
            {
                if (Main.GameUpdateCount % 2 == 0)
                {
                    SoAParticles.SpawnStreak(hand + Main.rand.NextVector2Circular(6f, 6f),
                        Main.rand.NextVector2Circular(1.5f, 1.5f) + new Vector2(0f, -1.5f - 2f * overheat),
                        Color.Lerp(HeatOrange, OverheatRed, Main.rand.NextFloat()), 2f, gravity: -0.03f, life: 18, lengthPerSpeed: 2f);
                }
                if (Main.GameUpdateCount % 5 == 0)
                    SoAParticles.SpawnSmoke(hand, new Vector2(0f, -0.8f), SmokeColor, 10f, 34f, 0.35f + 0.25f * overheat, 40);
            }
            else if (Main.GameUpdateCount % 3 == 0 && chargeTime > 4)
            {
                // Жар стягивается к руке, в окне — искры отлетают от белого накала
                if (IsPerfectWindow(chargeTime))
                {
                    Vector2 dir = Main.rand.NextVector2Unit();
                    SoAParticles.SpawnStreak(hand + dir * 10f, dir * 2f + new Vector2(0f, -1f), WhiteHot, 1.8f,
                        gravity: 0f, life: 14, lengthPerSpeed: 2f);
                }
                else
                {
                    Vector2 offset = Main.rand.NextVector2CircularEdge(26f, 26f);
                    SoAParticles.SpawnStreak(hand + offset, -offset * 0.1f, color, 1.5f + t, gravity: 0f, life: 10, lengthPerSpeed: 2f);
                }
            }
        }

        protected override void OnRelease()
        {
            int ct = chargeTime;
            ShurikenThrow kind;
            float ratio = 0f;
            float speed;

            if (ct < PerfectWindowStart)
            {
                kind = ShurikenThrow.Light;
                ratio = ct / (float)PerfectWindowStart;
                speed = MathHelper.Lerp(LightMinSpeed, LightMaxSpeed, ratio);
                cooldownTimer = CooldownLight;
                heat = 0; // промах по окну сбрасывает Жар
            }
            else if (ct <= PerfectWindowEnd)
            {
                kind = ShurikenThrow.Perfect;
                speed = PerfectSpeed;
                cooldownTimer = CooldownPerfect;
            }
            else
            {
                kind = ShurikenThrow.Overheat;
                speed = OverheatSpeed;
                cooldownTimer = CooldownOverheat;
                heat = 0;
                Player.AddBuff(BuffID.OnFire, OverheatBurnTicks); // передержал — обжёгся
            }

            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX * Player.direction);
            Projectile.NewProjectile(Player.GetSource_ItemUse(Player.HeldItem), Player.MountedCenter, dir * speed,
                ModContent.ProjectileType<InfernoShurikenProjectile>(), Player.GetWeaponDamage(Player.HeldItem),
                Player.HeldItem.knockBack, Player.whoAmI,
                (float)kind, ratio, kind == ShurikenThrow.Perfect ? heat : 0f);
        }
    }
}
