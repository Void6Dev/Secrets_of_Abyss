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
using static SoA.Content.Projectiles.RoyalSpearProjectile;

namespace SoA.Content.Projectiles
{
    // ЛКМ Королевского копья. Живёт с нажатия кнопки до последнего удара:
    //   короткий клик (отпустил раньше TapTicks) — связка: укол, взмах, тяжёлый выпад;
    //   зажатие — замах: копьё отведено, игрок придержан, каждые TicksPerExtraStrike
    //   к шквалу прибавляется удар (от MinFlurryStrikes до MaxFlurryStrikes за 3 с);
    //   отпустил — шквал частых уколов веером, последним идёт тяжёлый выпад.
    // Сами удары — RoyalSpearProjectile; здесь только замах и расписание.
    //
    //   ai[0] — фаза (PhaseHold / PhaseStrikes)
    //   ai[1] — в замахе: сколько тиков держат кнопку; в ударах: тиков с начала серии
    //   ai[2] — сколько ударов в серии, фиксируется в момент отпускания
    // Решения (отпустил ли кнопку, куда целиться, когда бить) принимает владелец;
    // удары он же создаёт через NewProjectile, и те уходят остальным пакетом сами.
    public class RoyalSpearFlurry : ModProjectile
    {
        private const float PhaseHold = 0f;
        private const float PhaseStrikes = 1f;

        // Кнопку отпустили раньше — это клик, а не замах
        private const int TapTicks = 12;

        public const int MaxChargeTicks = 180;
        private const int MinFlurryStrikes = 3;
        private const int MaxFlurryStrikes = 12;
        private const float TicksPerExtraStrike =
            (float)MaxChargeTicks / (MaxFlurryStrikes - MinFlurryStrikes);

        // Шквал: удар каждые FlurryInterval тиков, каждый живёт дольше интервала, так что
        // в воздухе одновременно пара копий. Перед выпадом — пауза, чтобы он читался
        private const int FlurryInterval = 4;
        private const int FinisherDelay = 5;
        private const float FlurrySpread = 0.24f;

        // Связка из клика: формы по порядку
        private static readonly StrikeStyle[] TapCombo = { StrikeStyle.Jab, StrikeStyle.Sweep, StrikeStyle.Lunge };

        // Урон удара от урона предмета. Шквал бьёт часто, поэтому каждый укол слабее,
        // а финал растёт с зарядом: долгий замах обязан окупаться последним ударом
        private const float JabDamage = 1.15f;
        private const float SweepDamage = 1.25f;
        private const float LungeDamage = 1.7f;
        private const float FlurryDamage = 0.8f;
        private const float FlurryFinisherMinDamage = 1.8f;
        private const float FlurryFinisherMaxDamage = 3f;

        // Разброс прицела обычной связки: удары не должны выглядеть штамповкой
        private const float ComboAimJitter = 0.045f;

        // Копьё в замахе отходит назад по мере набора
        private const float HoldDistance = 22f;
        private const float PullbackDistance = 16f;

        // Капли, которыми на копье видно число накопленных ударов
        private const float CounterOrbitRadius = 20f;
        private static readonly Color CounterColor = new(130, 215, 255, 0);

        // Когда в серии уходит следующий удар и какой он по счёту — считает только
        // владелец, остальные видят готовые удары
        private int nextStrikeAt;
        private int strikesLaunched;

        // Шквал из трёх ударов (минимальный замах) по числу совпадает со связкой —
        // различаем их по тому, как отпустили кнопку
        private bool flurry;

        // Сила замаха в момент отпускания: после смены фазы Power уже не посчитать
        private float releasedPower;

        private int announcedStrikes = MinFlurryStrikes;
        private bool fullChargeAnnounced;

        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        private float Phase => Projectile.ai[0];

        private int HeldTicks => (int)Projectile.ai[1];

        // Замах пошёл всерьёз: до TapTicks это ещё может оказаться кликом
        private bool Charging => Phase == PhaseHold && HeldTicks >= TapTicks;

        private float Power => MathHelper.Clamp(HeldTicks / (float)MaxChargeTicks, 0f, 1f);

        private int ChargedStrikes => Math.Min(MaxFlurryStrikes,
            MinFlurryStrikes + (int)(HeldTicks / TicksPerExtraStrike));

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

            // Оглушили, разоружили, умер — серия обрывается
            if (!owner.active || owner.dead || owner.noItems || owner.CCed)
            {
                Projectile.Kill();
                return;
            }

            Projectile.timeLeft = 60;
            owner.SetDummyItemTime(2);

            if (Projectile.owner == Main.myPlayer)
                RoyalSpearPlayer.AimAtMouse(owner, Projectile);

            if (Phase == PhaseHold)
                UpdateHold(owner);
            else
                UpdateStrikes(owner);
        }

        private void UpdateHold(Player owner)
        {
            owner.heldProj = Projectile.whoAmI;

            if (Projectile.owner == Main.myPlayer && !owner.channel)
            {
                Release();
                return;
            }

            // Счётчик крутят все стороны: иначе на чужих экранах замах набирался бы
            // только в тики, когда владелец прислал пакет
            Projectile.ai[1]++;

            float shake = Power >= 1f ? Main.rand.NextFloat(-1.2f, 1.2f) : 0f;
            RoyalSpearPlayer.HoldSpearInHand(owner, Projectile, Aim,
                HoldDistance - PullbackDistance * Power + shake);

            if (!Charging)
                return;

            owner.GetModPlayer<RoyalSpearPlayer>().KeepStrikeCharge();
            AnnounceStages();
            RoyalSpearCharge.EmitGatherDust(Projectile.Center, Power);
            Lighting.AddLight(Projectile.Center, 0.14f * Power, 0.26f * Power, 0.38f * Power);
        }

        // Каждый прибавленный удар — звон с растущим тоном и всплеск капель,
        // полный замах — тот же сигнал, что у броска ПКМ
        private void AnnounceStages()
        {
            int strikes = ChargedStrikes;
            if (strikes > announcedStrikes)
            {
                announcedStrikes = strikes;
                if (!Main.dedServ)
                {
                    float stage = (strikes - MinFlurryStrikes) / (float)(MaxFlurryStrikes - MinFlurryStrikes);
                    SoundEngine.PlaySound(SoundID.MaxMana with { Volume = 0.55f, Pitch = -0.3f + 0.9f * stage },
                        Projectile.Center);
                    for (int i = 0; i < 6; i++)
                    {
                        Dust drop = Dust.NewDustPerfect(Projectile.Center, DustID.Water,
                            Main.rand.NextVector2CircularEdge(2f, 2f));
                        drop.noGravity = true;
                        drop.scale = Main.rand.NextFloat(0.9f, 1.3f);
                    }
                }
            }

            if (!fullChargeAnnounced && Power >= 1f)
            {
                fullChargeAnnounced = true;
                RoyalSpearCharge.EmitReadyBurst(Projectile.Center);
            }
        }

        // Отпустил кнопку: клик превращается в связку, замах — в шквал
        private void Release()
        {
            // Всё, что зависит от замаха, читаем ДО смены фазы: Charging и Power смотрят
            // на ai[0] и ai[1], и после переключения замах для них уже кончился
            flurry = Charging;
            releasedPower = Power;
            int strikes = flurry ? ChargedStrikes : TapCombo.Length;
            if (flurry)
                SoundEngine.PlaySound(SoundID.Item7 with { Pitch = -0.1f + 0.4f * Power, Volume = 0.7f },
                    Projectile.Center);

            Projectile.ai[0] = PhaseStrikes;
            Projectile.ai[1] = 0f;
            Projectile.ai[2] = strikes;
            Projectile.netUpdate = true;

            nextStrikeAt = 0;
            strikesLaunched = 0;
        }

        private void UpdateStrikes(Player owner)
        {
            // Копьё рисуют сами удары; контроллер только держит предмет занятым
            if (Projectile.owner != Main.myPlayer)
                return;

            int ticks = (int)Projectile.ai[1]++;
            if (ticks < nextStrikeAt)
                return;

            int total = (int)Projectile.ai[2];
            if (strikesLaunched >= total)
            {
                Projectile.Kill();
                return;
            }

            LaunchStrike(owner, strikesLaunched, total);
            strikesLaunched++;
        }

        private void LaunchStrike(Player owner, int index, int total)
        {
            bool last = index == total - 1;

            StrikeStyle style;
            float damageMultiplier;
            float aimOffset;
            int duration;

            if (!flurry)
            {
                style = TapCombo[Math.Min(index, TapCombo.Length - 1)];
                damageMultiplier = style switch
                {
                    StrikeStyle.Sweep => SweepDamage,
                    StrikeStyle.Lunge => LungeDamage,
                    _ => JabDamage,
                };
                aimOffset = Main.rand.NextFloat(-1f, 1f) * ComboAimJitter;
                duration = DefaultDuration(style);
                nextStrikeAt += duration;
            }
            else if (last)
            {
                style = StrikeStyle.Lunge;
                damageMultiplier = MathHelper.Lerp(FlurryFinisherMinDamage, FlurryFinisherMaxDamage, releasedPower);
                aimOffset = 0f;
                duration = DefaultDuration(style);
                nextStrikeAt += duration;
            }
            else
            {
                style = StrikeStyle.Flurry;
                damageMultiplier = FlurryDamage;
                aimOffset = Main.rand.NextFloat(-1f, 1f) * FlurrySpread;
                duration = DefaultDuration(style);
                nextStrikeAt += index == total - 2 ? FlurryInterval + FinisherDelay : FlurryInterval;
            }

            // Разброс вылета бросаем здесь, у владельца: значение уезжает в ai удара
            // вместе с пакетом создания, и копьё машет одинаково у всех клиентов
            float variance = Main.rand.NextFloat(-1f, 1f);

            Projectile.NewProjectile(Projectile.GetSource_FromThis(), owner.MountedCenter,
                Aim.RotatedBy(aimOffset), ModContent.ProjectileType<RoyalSpearProjectile>(),
                (int)(Projectile.damage * damageMultiplier), Projectile.knockBack, Projectile.owner,
                (float)style, variance, duration);
        }

        public override Color? GetAlpha(Color lightColor)
        {
            return Color.Lerp(lightColor, new Color(200, 235, 255), 0.35f * Power);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // В серии копьё в руке рисуют удары — контроллер невидим
            if (Phase != PhaseHold)
                return false;

            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 origin = new(tex.Width / 2f, tex.Height - GripOffset);
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            if (Charging)
                RoyalSpearCharge.DrawChargeGlow(Projectile, tex, drawPos, origin, Power, Aim);

            Main.EntitySpriteDraw(tex, drawPos, null, Projectile.GetAlpha(lightColor),
                Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);

            if (Charging)
                DrawStrikeCounter();
            return false;
        }

        // Накопленные сверх минимума удары кружат каплями вокруг острия: игрок видит,
        // сколько получит, не считая звонки. Батч не переключаем — копьё рисуется внутри
        // прохода игрока; аддитив даёт цвет с A = 0
        private void DrawStrikeCounter()
        {
            int extra = ChargedStrikes - MinFlurryStrikes;
            if (extra <= 0)
                return;

            Vector2 tip = Projectile.Center + Aim * ((SpriteLength - GripOffset) * Projectile.scale);
            float spin = Main.GlobalTimeWrappedHourly * 3f;
            int slots = MaxFlurryStrikes - MinFlurryStrikes;

            for (int i = 0; i < extra; i++)
            {
                float angle = spin + MathHelper.TwoPi * i / slots;
                Vector2 at = tip + angle.ToRotationVector2() * CounterOrbitRadius;
                SoAVfx.DrawTintedGlow(Main.spriteBatch, at, new Vector2(12f), CounterColor * 0.8f);
                SoAVfx.DrawTintedGlow(Main.spriteBatch, at, new Vector2(5f), new Color(255, 255, 255, 0) * 0.9f);
            }
        }
    }
}
