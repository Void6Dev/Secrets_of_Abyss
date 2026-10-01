using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Players;
using SoA.Common.Utils;

namespace SoA.Content.Projectiles
{
    // Замах броска: копьё в руке, пока зажат ПКМ. Игрок отводит его назад, копит силу,
    // и на отпускании оно уходит в RoyalSpearThrown — чем дольше держал, тем быстрее
    // летит и тем больнее бьёт. За порогом скорости у брошенного копья включается
    // шлейф разгона, так что заряд читается прямо в полёте.
    // На полной шкале прилива замах ведёт прицел приливного рывка (см. LaunchSlam).
    //
    // Живёт только пока игрок канальит: снаряд каждый тик продлевает себе timeLeft и
    // держит анимацию использования предмета.
    public class RoyalSpearCharge : ModProjectile
    {
        public const int ChargeTicks = 48;

        // Скорость броска: без заряда медленнее прежнего фиксированного, на полном —
        // заметно быстрее. Порог шлейфа (RoyalSpearThrown.SpeedFxThreshold) лежит между
        private const float MinThrowSpeed = 11f;
        private const float MaxThrowSpeed = 27f;
        private const float MaxDamageBonus = 1.6f;

        // Приливный рывок (полная шкала): урон выше обычного броска — это добивающий
        // приём, ради которого копилась шкала. Скорость удара — RoyalSpearPlayer.DiveSpeed
        private const float SlamDamageMultiplier = 2.5f;

        // Копьё в замахе отходит назад и уходит вперёд в момент броска
        private const float HoldDistance = 24f;
        private const float PullbackDistance = 18f;

        private const float ArcLength = 70f;
        private const float ArcBow = 11f;

        // Копьё «выпрыгивает» в руку, набитый замах вспыхивает силуэтом (общее со шквалом ЛКМ)
        public const int PopTicks = 6;
        public const int FlashTicks = 12;

        private bool fullChargeAnnounced;
        private int _flash;

        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        private float Power => MathHelper.Clamp(Projectile.ai[0] / ChargeTicks, 0f, 1f);

        private Vector2 Aim => Projectile.velocity.SafeNormalize(Vector2.UnitX);

        public override void SetDefaults()
        {
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.aiStyle = -1;
            Projectile.friendly = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.scale = 1.2f;
            Projectile.netImportant = true;
            Projectile.timeLeft = 60;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];

            // Оглушили, разоружили, умер — замах просто пропадает, бросок не уходит
            if (!owner.active || owner.dead || owner.noItems || owner.CCed)
            {
                Projectile.Kill();
                return;
            }

            Projectile.timeLeft = 60;
            owner.heldProj = Projectile.whoAmI;
            owner.SetDummyItemTime(2);

            if (Projectile.owner == Main.myPlayer)
            {
                RoyalSpearPlayer.AimAtMouse(owner, Projectile);

                // Полная шкала — прицел рывка: курсор подменяет RoyalSpearReticle
                var spearPlayer = owner.GetModPlayer<RoyalSpearPlayer>();
                if (spearPlayer.SlamReady)
                    spearPlayer.KeepTeleportAim(Main.MouseWorld);

                if (!RoyalSpearPlayer.HoldingAltFire(owner))
                {
                    Launch(owner);
                    Projectile.Kill();
                    return;
                }
            }

            // Счётчик крутят все стороны: иначе на чужих экранах копьё копило бы силу
            // только в тики, когда владелец повернул прицел и прислал пакет
            Projectile.ai[0]++;
            if (_flash > 0)
                _flash--;

            HoldInHand(owner);
            if (!fullChargeAnnounced && Power >= 1f)
            {
                fullChargeAnnounced = true;
                _flash = FlashTicks;
                EmitReadyBurst(Projectile.Center);
            }
            EmitGatherDust(Projectile.Center, Power);

            Lighting.AddLight(Projectile.Center, 0.14f * Power, 0.26f * Power, 0.38f * Power);
        }

        private void HoldInHand(Player owner)
        {
            RoyalSpearPlayer.HoldSpearInHand(owner, Projectile, Aim,
                RoyalSpearPlayer.PulledReach(HoldDistance, PullbackDistance, Power)
                + RoyalSpearPlayer.StrainShake(Power, (int)Projectile.ai[0]));
        }

        // Эффекты замаха общие для броска ПКМ и шквала ЛКМ (RoyalSpearFlurry): игрок
        // должен узнавать «копьё копит силу» одинаково, какой кнопкой он его ни держит

        // Замах набит до предела: всплеск воды вокруг копья и звонкий плеск
        public static void EmitReadyBurst(Vector2 center)
        {
            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = 0.5f, Volume = 0.7f }, center);
            for (int i = 0; i < 16; i++)
            {
                Dust burst = Dust.NewDustPerfect(center,
                    DustID.Water, Main.rand.NextVector2CircularEdge(3.5f, 3.5f));
                burst.noGravity = true;
                burst.scale = Main.rand.NextFloat(1.1f, 1.6f);
            }
        }

        // Вода стягивается к копью: видно, что оружие набирает, а не просто висит
        public static void EmitGatherDust(Vector2 center, float power)
        {
            if (Main.dedServ || power <= 0.05f)
                return;
            if (!Main.rand.NextBool(power >= 1f ? 1 : 3))
                return;

            Vector2 spawn = center + Main.rand.NextVector2CircularEdge(46f, 46f);
            Dust pull = Dust.NewDustPerfect(spawn, DustID.Water, (center - spawn) * 0.05f);
            pull.noGravity = true;
            pull.scale = Main.rand.NextFloat(0.8f, 1.3f) * (0.6f + 0.6f * power);
        }

        private void Launch(Player owner)
        {
            float power = Power;
            var spearPlayer = owner.GetModPlayer<RoyalSpearPlayer>();
            int damage = (int)(Projectile.damage * MathHelper.Lerp(1f, MaxDamageBonus, power));

            if (spearPlayer.SlamReady)
            {
                LaunchSlam(owner, spearPlayer, damage);
                return;
            }

            // Бросок уходит из руки, оттуда, где копьё было в замахе; рука в стене — из центра
            float speed = MathHelper.Lerp(MinThrowSpeed, MaxThrowSpeed, power);
            Vector2 from = Collision.CanHitLine(owner.MountedCenter, 1, 1, Projectile.Center, 1, 1)
                ? Projectile.Center
                : owner.MountedCenter;
            Projectile.NewProjectile(Projectile.GetSource_FromThis(),
                from, Aim * speed, ModContent.ProjectileType<RoyalSpearThrown>(),
                damage, Projectile.knockBack, Projectile.owner);

            SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.2f + 0.4f * power }, owner.Center);
        }

        // Приливный рывок: игрок проносится к точке над курсором, копьё бьёт оттуда
        // вниз в цель и оглушает её, игрок пикирует следом (RoyalSpearPlayer.StartDash).
        // Точка недостижима (стена на пути, нет места для персонажа) — рывок не
        // срабатывает, шкала остаётся полной, копьё в руке
        private void LaunchSlam(Player owner, RoyalSpearPlayer spearPlayer, int damage)
        {
            Vector2 target = Main.MouseWorld;
            if (!RoyalSpearPlayer.TryFindTeleport(owner, target, out Vector2 destination))
            {
                SoundEngine.PlaySound(SoundID.MenuClose with { Pitch = -0.3f }, owner.Center);
                return;
            }

            spearPlayer.ConsumeSlam();
            spearPlayer.StartDash(destination, target, (int)(damage * SlamDamageMultiplier), Projectile.knockBack);
        }

        public override Color? GetAlpha(Color lightColor)
        {
            return Color.Lerp(lightColor, new Color(200, 235, 255), 0.35f * Power);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 origin = new(tex.Width / 2f, tex.Height - RoyalSpearProjectile.GripOffset);
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            float scale = Projectile.scale * PopScale(Projectile.ai[0]);

            DrawChargeGlow(Projectile, tex, drawPos, origin, Power, Aim);

            Main.EntitySpriteDraw(tex, drawPos, null, Projectile.GetAlpha(lightColor),
                Projectile.rotation, origin, scale, SpriteEffects.None, 0);
            DrawFlash(Projectile, tex, drawPos, origin, scale, _flash / (float)FlashTicks);
            return false;
        }

        // Копьё «выпрыгивает» в руку в первые тики замаха
        public static float PopScale(float heldTicks) => SoAEasing.BackOut(Math.Min(heldTicks / PopTicks, 1f));

        // Белая вспышка силуэта: замах набит (или прибавил ступень шквала). strength 0..1
        public static void DrawFlash(Projectile spear, Texture2D tex, Vector2 drawPos, Vector2 origin, float scale, float strength)
        {
            if (strength <= 0f)
                return;
            float flash = SoAEasing.QuadIn(strength);
            Main.EntitySpriteDraw(SilhouetteCache.Get(tex), drawPos, null, SoAVfx.Additive(Color.White) * (flash * 1.3f),
                spear.rotation, origin, scale, SpriteEffects.None, 0);
        }

        // Пока копится — синий ореол по силуэту, на полном заряде вдоль древка уже
        // бегут те самые дуги, которые игрок увидит на разогнанном броске
        public static void DrawChargeGlow(Projectile spear, Texture2D tex, Vector2 drawPos, Vector2 origin,
            float power, Vector2 aim)
        {
            if (power <= 0.05f)
                return;

            const int RimCopies = 6;
            float pulse = 0.6f + 0.4f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
            Color rim = new Color(110, 200, 255, 0) * (power * pulse * 0.8f);

            for (int i = 0; i < RimCopies; i++)
            {
                Vector2 offset = (MathHelper.TwoPi / RimCopies * i).ToRotationVector2() * (2f + 2f * power);
                Main.EntitySpriteDraw(tex, drawPos + offset, null, rim,
                    spear.rotation, origin, spear.scale, SpriteEffects.None, 0);
            }

            if (power < 1f)
                return;

            Vector2 shaftCenter = spear.Center + aim * (ArcLength * 0.5f - RoyalSpearProjectile.GripOffset * 0.5f);
            // Батч не переключаем: копьё в руке рисуется внутри прохода игрока, и End/Begin
            // посреди него сбрасывал бы состояние персонажу и его слоям — отсюда мерцание.
            // Дуги и так идут цветом с A = 0, а это уже аддитив на премультиплированной альфе
            SoAVfx.DrawTravellingArcs(Main.spriteBatch, shaftCenter, aim.ToRotation(),
                ArcLength, ArcBow, Main.GlobalTimeWrappedHourly * 1.6f % 1f,
                new Color(120, 210, 255, 0) * 0.7f);
        }
    }
}
