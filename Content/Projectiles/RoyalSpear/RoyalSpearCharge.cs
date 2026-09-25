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

namespace SoA.Content.Projectiles
{
    // Замах броска: копьё в руке, пока зажат ПКМ. Игрок отводит его назад, копит силу,
    // и на отпускании оно уходит в RoyalSpearThrown — чем дольше держал, тем быстрее
    // летит и тем больнее бьёт. За порогом скорости у брошенного копья включается
    // шлейф разгона, так что заряд читается прямо в полёте.
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
        private const float SlamSpeedBonus = 1.15f;

        private const float MaxDamageBonus = 1.6f;

        // Взрыв приливного слэма считается от урона самого броска: полный заряд бьёт
        // сильнее не только копьём, но и приземлением
        private const float SlamBurstDamageMultiplier = 4.2f;

        // Копьё в замахе отходит назад и уходит вперёд в момент броска
        private const float HoldDistance = 24f;
        private const float PullbackDistance = 18f;

        private const float ArcLength = 70f;
        private const float ArcBow = 11f;

        private bool fullChargeAnnounced;

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

            HoldInHand(owner);
            AnnounceFullCharge(owner);
            EmitChargeDust();

            Lighting.AddLight(Projectile.Center, 0.14f * Power, 0.26f * Power, 0.38f * Power);
        }

        private void HoldInHand(Player owner)
        {
            // Дрожь набитого до предела замаха
            float shake = Power >= 1f ? Main.rand.NextFloat(-1.2f, 1.2f) : 0f;
            RoyalSpearPlayer.HoldSpearInHand(owner, Projectile, Aim,
                HoldDistance - PullbackDistance * Power + shake);
        }

        private void AnnounceFullCharge(Player owner)
        {
            if (fullChargeAnnounced || Power < 1f)
                return;
            fullChargeAnnounced = true;

            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = 0.5f, Volume = 0.7f }, Projectile.Center);
            for (int i = 0; i < 16; i++)
            {
                Dust burst = Dust.NewDustPerfect(Projectile.Center,
                    DustID.Water, Main.rand.NextVector2CircularEdge(3.5f, 3.5f));
                burst.noGravity = true;
                burst.scale = Main.rand.NextFloat(1.1f, 1.6f);
            }
        }

        // Вода стягивается к копью: видно, что оружие набирает, а не просто висит
        private void EmitChargeDust()
        {
            if (Main.dedServ || Power <= 0.05f)
                return;
            if (!Main.rand.NextBool(Power >= 1f ? 1 : 3))
                return;

            Vector2 spawn = Projectile.Center + Main.rand.NextVector2CircularEdge(46f, 46f);
            Dust pull = Dust.NewDustPerfect(spawn, DustID.Water,
                (Projectile.Center - spawn) * 0.05f);
            pull.noGravity = true;
            pull.scale = Main.rand.NextFloat(0.8f, 1.3f) * (0.6f + 0.6f * Power);
        }

        private void Launch(Player owner)
        {
            float power = Power;
            var spearPlayer = owner.GetModPlayer<RoyalSpearPlayer>();

            bool slam = spearPlayer.ConsumeSlam();
            float speed = MathHelper.Lerp(MinThrowSpeed, MaxThrowSpeed, power) * (slam ? SlamSpeedBonus : 1f);
            int damage = (int)(Projectile.damage * MathHelper.Lerp(1f, MaxDamageBonus, power));

            int index = Projectile.NewProjectile(Projectile.GetSource_FromThis(),
                owner.MountedCenter, Aim * speed, ModContent.ProjectileType<RoyalSpearThrown>(),
                damage, Projectile.knockBack, Projectile.owner);

            if (slam && Main.projectile[index].ModProjectile is RoyalSpearThrown thrown)
            {
                thrown.MarkSlam();
                spearPlayer.BeginSlamRide((int)(damage * SlamBurstDamageMultiplier));
                SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.4f }, owner.Center);
            }

            SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.2f + 0.4f * power }, owner.Center);
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

            DrawChargeGlow(tex, drawPos, origin);

            Main.EntitySpriteDraw(tex, drawPos, null, Projectile.GetAlpha(lightColor),
                Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }

        // Пока копится — синий ореол по силуэту, на полном заряде вдоль древка уже
        // бегут те самые дуги, которые игрок увидит на разогнанном броске
        private void DrawChargeGlow(Texture2D tex, Vector2 drawPos, Vector2 origin)
        {
            float power = Power;
            if (power <= 0.05f)
                return;

            const int RimCopies = 6;
            float pulse = 0.6f + 0.4f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
            Color rim = new Color(110, 200, 255, 0) * (power * pulse * 0.8f);

            for (int i = 0; i < RimCopies; i++)
            {
                Vector2 offset = (MathHelper.TwoPi / RimCopies * i).ToRotationVector2() * (2f + 2f * power);
                Main.EntitySpriteDraw(tex, drawPos + offset, null, rim,
                    Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            }

            if (power < 1f)
                return;

            Vector2 aim = Aim;
            Vector2 shaftCenter = Projectile.Center + aim * (ArcLength * 0.5f - RoyalSpearProjectile.GripOffset * 0.5f);
            // Батч не переключаем: копьё в руке рисуется внутри прохода игрока, и End/Begin
            // посреди него сбрасывал бы состояние персонажу и его слоям — отсюда мерцание.
            // Дуги и так идут цветом с A = 0, а это уже аддитив на премультиплированной альфе
            SoAVfx.DrawTravellingArcs(Main.spriteBatch, shaftCenter, aim.ToRotation(),
                ArcLength, ArcBow, Main.GlobalTimeWrappedHourly * 1.6f % 1f,
                new Color(120, 210, 255, 0) * 0.7f);
        }
    }
}
