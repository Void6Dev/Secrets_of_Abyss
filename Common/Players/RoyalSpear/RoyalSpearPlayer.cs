using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Common.Players
{
    // Состояние Королевского копья на игроке:
    //   SlamDamage — счётчик нанесённого урона, копится под игроком в полоску;
    //                на полной шкале бросок ПКМ становится приливным рывком: игрок
    //                за DashTicks проносится к точке над курсором, оттуда копьё бьёт
    //                вниз, игрок пикирует вместе с ним, цель оглушена
    //                (RoyalSpearCharge → StartDash → LaunchDive → RoyalSpearThrown)
    // Шкала, прицел и рывок живут только у владельца персонажа: курсор есть только у
    // него, а позицию игрока остальным разносит обычная синхронизация.
    public class RoyalSpearPlayer : ModPlayer
    {
        public const int SlamDamageRequired = 2000;

        // Замах шквала (зажатая ЛКМ) придерживает игрока: копьё отведено для удара,
        // бегать с ним в полную силу нельзя, но и в столб он не превращается
        private const float StrikeChargeRunMultiplier = 0.65f;

        // Рывок ставит игрока над целью: ногами на такой высоте над точкой курсора.
        // Если потолок ниже — опускаем, пока персонаж не поместится
        private const float TeleportHover = 56f;
        private const float TeleportHoverStep = 8f;

        // Рывок — не мгновенный телепорт, а бросок сквозь воздух за DashTicks: глаз
        // успевает увидеть полёт и водяную ленту за ним (RoyalSpearDashTrail)
        private const int DashTicks = 8;

        // Над целью копьё бьёт вниз, и игрок пикирует вместе с ним на той же скорости,
        // пока копьё не воткнётся или игрок не упрётся в землю
        public const float DiveSpeed = 30f;
        private const int MaxDiveTicks = 45;

        // Весь приём и немного после — неуязвимость и без урона от падения: пике в упор
        // к цели иначе сразу стоило бы удара об неё
        private const int DashImmunity = 40;
        private const int LandingGraceTicks = 10;

        public int SlamDamage { get; private set; }
        public bool SlamReady => SlamDamage >= SlamDamageRequired;
        public float SlamProgress => MathHelper.Clamp(SlamDamage / (float)SlamDamageRequired, 0f, 1f);

        // Прицел рывка: зажата ПКМ с полной шкалой. Держит его RoyalSpearCharge у владельца,
        // читает RoyalSpearReticle, чтобы подменить курсор
        public bool AimingTeleport => aimTicks > 0;
        public bool TeleportValid { get; private set; }
        public Vector2 TeleportDestination { get; private set; }

        private int aimTicks;

        // Рывок и пике. Живут только у владельца: он двигает своего игрока, остальным
        // позицию разносит обычная синхронизация, а ленту рисует снаряд-след
        private Vector2 dashFrom;
        private Vector2 dashTo;
        private int dashTick = -1;
        private bool diving;
        private int diveTicks;
        private Vector2 diveVelocity;
        private int landingGrace;

        // Чем бить, когда рывок долетит: запоминаем при старте, копьё создаётся на месте
        private Vector2 strikeTarget;
        private int strikeDamage;
        private float strikeKnockback;

        public bool Dashing => dashTick >= 0 || diving;

        // Замах шквала жив: снаряд продлевает флаг каждый тик. Счётчик, а не bool, потому
        // что снаряды обновляются после игрока — флаг должен дожить до следующего тика
        private int strikeChargeTicks;

        // player.channel держится только левой кнопкой: Player.ItemCheck сбрасывает его
        // в тот же тик, когда controlUseItem == false, а ПКМ-альтфункция поднимает
        // controlUseItem ровно на кадр нажатия. Поэтому зажатую ПКМ читаем прямо по
        // кнопке — вызывать только у владельца, у остальных мыши нет
        public static bool HoldingAltFire(Player owner)
        {
            return Main.mouseRight && !owner.mouseInterface;
        }

        // Прицел ведёт только владелец: мышь есть только у него. Остальным шлём
        // обновление, когда копьё заметно провернулось
        public static void AimAtMouse(Player owner, Projectile spear)
        {
            Vector2 aim = (Main.MouseWorld - owner.MountedCenter).SafeNormalize(Vector2.UnitX);
            if (Vector2.Dot(aim, spear.velocity.SafeNormalize(Vector2.UnitX)) < 0.999f)
                spear.netUpdate = true;
            spear.velocity = aim;
        }

        // Копьё в руке (замах или пересборка): и снаряд, и рука игрока смотрят по aim,
        // reach — насколько древко вынесено вперёд от центра игрока
        public static void HoldSpearInHand(Player owner, Projectile spear, Vector2 aim, float reach)
        {
            spear.Center = owner.MountedCenter + aim * reach;
            spear.rotation = aim.ToRotation() + MathHelper.PiOver2;
            spear.direction = spear.spriteDirection = aim.X >= 0f ? 1 : -1;

            owner.ChangeDir(spear.direction);
            owner.itemRotation = aim.ToRotation();
            if (spear.spriteDirection == -1)
                owner.itemRotation += MathHelper.Pi;
        }

        // Шкалу набивает любой урон от копья — удары ЛКМ и брошенное копьё
        public void AddSlamDamage(int damage)
        {
            if (SlamDamage < SlamDamageRequired)
                SlamDamage = System.Math.Min(SlamDamage + damage, SlamDamageRequired);
        }

        public bool ConsumeSlam()
        {
            if (!SlamReady)
                return false;
            SlamDamage = 0;
            return true;
        }

        // Зовёт RoyalSpearFlurry каждый тик замаха на всех клиентах: замедление должно
        // совпадать у всех, иначе чужой игрок на экране бежал бы быстрее, чем на самом деле
        public void KeepStrikeCharge()
        {
            strikeChargeTicks = 2;
        }

        // Зовёт RoyalSpearCharge у владельца каждый тик прицеливания рывка
        public void KeepTeleportAim(Vector2 target)
        {
            aimTicks = 2;
            TeleportValid = TryFindTeleport(Player, target, out Vector2 destination);
            TeleportDestination = destination;
        }

        // Куда встанет игрок, если рвануть к target. Правило пользователя: только туда,
        // куда персонаж может попасть, не проходя сквозь блоки, — поэтому нужна прямая
        // видимость от игрока до места прибытия, и само место должно быть свободно.
        // destination — левый верхний угол хитбокса игрока (как Player.position)
        public static bool TryFindTeleport(Player player, Vector2 target, out Vector2 destination)
        {
            destination = Vector2.Zero;
            if (!WorldGen.InWorld((int)(target.X / 16f), (int)(target.Y / 16f), 10))
                return false;

            for (float hover = TeleportHover; hover >= 0f; hover -= TeleportHoverStep)
            {
                Vector2 candidate = new(target.X - player.width * 0.5f, target.Y - hover - player.height);
                if (Collision.SolidCollision(candidate, player.width, player.height))
                    continue;

                if (!Collision.CanHitLine(player.position, player.width, player.height,
                        candidate, player.width, player.height))
                    return false;

                destination = candidate;
                return true;
            }
            return false;
        }

        // Старт рывка. Зовётся у владельца из RoyalSpearCharge: дальше игрока ведёт
        // PreUpdateMovement, а копьё появится, когда рывок долетит
        public void StartDash(Vector2 destination, Vector2 target, int damage, float knockback)
        {
            dashFrom = Player.position;
            dashTo = destination;
            dashTick = 0;
            diving = false;
            strikeTarget = target;
            strikeDamage = damage;
            strikeKnockback = knockback;

            Player.RemoveAllGrapplingHooks();
            Player.immune = true;
            Player.immuneNoBlink = true;
            Player.immuneTime = System.Math.Max(Player.immuneTime, DashImmunity);

            Projectile.NewProjectile(Player.GetSource_ItemUse(Player.HeldItem), Player.Center, Vector2.Zero,
                ModContent.ProjectileType<Content.Projectiles.RoyalSpearDashTrail>(), 0, 0f, Player.whoAmI);

            if (Main.dedServ)
                return;

            EmitTeleportSplash(Player.Center);
            SoundEngine.PlaySound(SoundID.Item8 with { Pitch = -0.2f }, Player.Center);
            SoundEngine.PlaySound(SoundID.Item7 with { Pitch = -0.4f, Volume = 0.9f }, Player.Center);
        }

        // Долетел: копьё бьёт вниз в цель, игрок пикирует вместе с ним
        private void LaunchDive()
        {
            Vector2 direction = (strikeTarget - Player.Center).SafeNormalize(Vector2.UnitY);
            diveVelocity = direction * DiveSpeed;
            diving = true;
            diveTicks = 0;
            Player.velocity = diveVelocity; // этот же тик уже летим, иначе PostUpdate решит, что упёрлись

            int index = Projectile.NewProjectile(Player.GetSource_ItemUse(Player.HeldItem), Player.Center,
                diveVelocity, ModContent.ProjectileType<Content.Projectiles.RoyalSpearThrown>(),
                strikeDamage, strikeKnockback, Player.whoAmI);
            if (Main.projectile[index].ModProjectile is Content.Projectiles.RoyalSpearThrown thrown)
                thrown.MarkSlam();

            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.4f }, Player.Center);
            SoundEngine.PlaySound(SoundID.Splash with { Pitch = 0.2f }, Player.Center);
        }

        private void EndDive(bool landed)
        {
            diving = false;
            landingGrace = LandingGraceTicks;

            if (!landed || Main.dedServ)
                return;

            EmitTeleportSplash(Player.Bottom);
            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.3f, Volume = 0.7f }, Player.Bottom);
        }

        // Скорость рывка и пике выставляем здесь, а не в снаряде: этот хук идёт после
        // ванильного ограничения скорости падения (10 px/тик), и пике со скоростью копья
        // им не срезается
        public override void PreUpdateMovement()
        {
            if (dashTick >= 0)
            {
                dashTick++;
                float t = MathHelper.Clamp(dashTick / (float)DashTicks, 0f, 1f);
                float eased = 1f - (1f - t) * (1f - t);
                Vector2 previous = Player.position;
                Player.position = Vector2.Lerp(dashFrom, dashTo, eased);
                Player.velocity = Vector2.Zero;
                if (System.Math.Abs(Player.position.X - previous.X) > 0.5f)
                    Player.ChangeDir(Player.position.X > previous.X ? 1 : -1);

                if (dashTick >= DashTicks)
                {
                    dashTick = -1;
                    if (Player.whoAmI == Main.myPlayer)
                        LaunchDive();
                }
            }
            else if (diving)
            {
                Player.velocity = diveVelocity;
            }
        }

        private void EmitTeleportSplash(Vector2 at)
        {
            for (int i = 0; i < 24; i++)
            {
                Dust drop = Dust.NewDustPerfect(at + Main.rand.NextVector2Circular(Player.width, Player.height * 0.5f),
                    DustID.Water, Main.rand.NextVector2Circular(3.5f, 3.5f));
                drop.noGravity = true;
                drop.scale = Main.rand.NextFloat(1.1f, 1.7f);
            }
        }

        public override void UpdateDead()
        {
            strikeChargeTicks = 0;
            aimTicks = 0;
            dashTick = -1;
            diving = false;
        }

        public override void PreUpdate()
        {
            if (!Dashing && landingGrace <= 0)
                return;

            Player.noFallDmg = true;
            Player.fallStart = (int)(Player.position.Y / 16f);
        }

        public override void PostUpdateRunSpeeds()
        {
            if (strikeChargeTicks <= 0)
                return;

            Player.maxRunSpeed *= StrikeChargeRunMultiplier;
            Player.accRunSpeed *= StrikeChargeRunMultiplier;
            Player.runAcceleration *= StrikeChargeRunMultiplier;
        }

        public override void PostUpdate()
        {
            if (strikeChargeTicks > 0)
                strikeChargeTicks--;
            if (aimTicks > 0)
                aimTicks--;
            if (landingGrace > 0)
                landingGrace--;

            if (!diving)
                return;

            // Пике кончается, когда игрок упёрся (скорость после столкновения упала
            // вдвое), когда копьё уже воткнулось или по таймеру
            diveTicks++;
            bool blocked = Player.velocity.LengthSquared() < diveVelocity.LengthSquared() * 0.25f;
            var spear = Content.Projectiles.RoyalSpearThrown.FindOwned(Player);
            bool spearLanded = spear == null || !spear.InFlight;
            if (blocked || spearLanded || diveTicks >= MaxDiveTicks)
                EndDive(landed: blocked);
        }
    }
}
