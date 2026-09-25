using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Projectiles;

namespace SoA.Common.Players
{
    // Состояние Королевского копья на игроке:
    //   ComboStep  — какой из трёх выпадов связки уходит сейчас
    //   SlamDamage — счётчик нанесённого урона, копится под игроком в полоску;
    //                на полной шкале следующий бросок ПКМ уносит игрока на верёвке
    //                за копьём, а приземление рвёт водным взрывом
    // Обе шкалы и вся поездка живут только у владельца персонажа: скорость игрока
    // симулирует он сам, поэтому пакеты тут не нужны.
    public class RoyalSpearPlayer : ModPlayer
    {
        public const int ComboLength = 3;
        public const int SlamDamageRequired = 2000;


        // Копьё чаще всего втыкается в землю, поэтому «подтянулся к копью» и «коснулся
        // земли» приходятся на один тик — без нижнего порога взрыв рвал бы прямо в точке
        // втыкания, никакого полёта игрок не видел.
        // Рывок с места даёт натянувшаяся верёвка (RoyalSpearThrown), не прыжок здесь.
        private const int MinRideTicks = 24;
        private const int MaxRideTicks = 500;

        public int ComboStep { get; private set; }
        public int SlamDamage { get; private set; }
        public bool SlamReady => SlamDamage >= SlamDamageRequired;
        public float SlamProgress => MathHelper.Clamp(SlamDamage / (float)SlamDamageRequired, 0f, 1f);

        public bool RidingSlam { get; private set; }

        // Взведён — верёвка отработала (подтянула к копью или копьё пошло назад),
        // дальше взрыв ждёт первого касания земли
        public bool SlamArmed { get; private set; }

        private int burstDamage;
        private int rideTicks;

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

        // Все три удара живут внутри одной анимации, поэтому шаг просто крутится по
        // кругу: связка не может оборваться на середине, а CanUseItem сбрасывает её
        // на нуле в начале каждого нажатия
        public int AdvanceCombo()
        {
            int step = ComboStep;
            ComboStep = (step + 1) % ComboLength;
            return step;
        }

        public void ResetCombo()
        {
            ComboStep = 0;
        }

        // Шкалу набивает любой урон от копья — выпад, вал, бросок, гейзер.
        // Взрыв себя не подпитывает, иначе цепочка была бы бесконечной.
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

        public void BeginSlamRide(int explosionDamage)
        {
            RidingSlam = true;
            SlamArmed = false;
            burstDamage = explosionDamage;
            rideTicks = 0;
        }

        // Зовёт снаряд, когда тянуть больше некуда
        public void ArmSlam()
        {
            if (RidingSlam)
                SlamArmed = true;
        }

        public void EndSlamRide()
        {
            RidingSlam = false;
            SlamArmed = false;
            rideTicks = 0;
        }

        public override void PreUpdate()
        {
            if (!RidingSlam)
                return;

            // Верёвка тащит игрока в упор к цели и роняет с высоты, так что за поездку
            // он неизбежно ловил и контактный урон, и падение. На время связки —
            // неуязвимость без мигания: это заряженный добивающий приём, а не прогулка
            Player.noFallDmg = true;
            Player.immune = true;
            Player.immuneNoBlink = true;
            if (Player.immuneTime < 2)
                Player.immuneTime = 2;
        }

        public override void PostUpdate()
        {
            if (!RidingSlam || Player.whoAmI != Main.myPlayer)
                return;

            rideTicks++;

            // Пока летим — обнуляем точку падения, иначе урон посчитается по всей дуге
            Player.fallStart = (int)(Player.position.Y / 16f);

            // Взрыв рвёт в момент касания земли, но не раньше MinRideTicks от броска:
            // иначе он срабатывал в тот же тик, когда копьё вошло в грунт под ногами
            if (SlamArmed && rideTicks >= MinRideTicks && Player.velocity.Y == 0f)
            {
                TriggerSlamBurst();
                return;
            }

            // Затянувшуюся поездку (застрял в воздухе, потерял копьё) всё равно
            // разрешаем ударом, иначе полная шкала сгорела бы впустую
            if (rideTicks >= MaxRideTicks)
                TriggerSlamBurst();
        }

        private void TriggerSlamBurst()
        {
            EndSlamRide();

            Vector2 impact = Player.Bottom;
            Projectile.NewProjectile(Player.GetSource_ItemUse(Player.HeldItem), impact, Vector2.Zero,
                ModContent.ProjectileType<RoyalTideBurst>(), burstDamage, 12f, Player.whoAmI);

            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.5f }, impact);
            SoundEngine.PlaySound(SoundID.Item21, impact);
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                impact, Main.rand.NextVector2Unit(), 14f, 7f, 22, 1400f, "RoyalTideSlam"));

            // Копьё в руку не зовём: связка приносит игрока к самому древку, и оно
            // подберётся само, как только гейзер отработает
        }
    }
}
