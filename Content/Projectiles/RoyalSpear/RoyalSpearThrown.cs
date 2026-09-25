using System;
using System.IO;
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
    // Бросок Королевского трезубца (ПКМ). Летит по дуге и остаётся там, куда воткнулся,
    // пока игрок не подойдёт и не выдернет его руками — притянуть копьё издалека нельзя.
    // Фазы через ai[0]:
    //   0 — полёт по дуге: втыкается в первую цель или в тайл
    //   1 — гейзер: столб воды от точки удара, бьёт по площади
    //   2 — возврат: прыгает в руку с расстояния вытянутой руки, пойманный трезубец
    //       сразу заряжен на королевский выпад
    //   3 — стоит воткнутым и ждёт, когда за ним придут
    // Пока трезубец не в руке, выпад недоступен — оружия у игрока буквально нет.
    //
    // На полной шкале приливного удара бросок идёт в режиме «крюка» (_slam): трезубец
    // тянет игрока за собой на водяной верёвке, а приземление игрока рвёт взрывом
    // (это уже забота RoyalSpearPlayer — он один знает, когда игрок коснулся земли).
    public class RoyalSpearThrown : ModProjectile
    {
        private const float PhaseFlight = 0f;
        private const float PhaseGeyser = 1f;
        private const float PhaseReturn = 2f;
        private const float PhaseAnchored = 3f;

        // 8 кадров листа раскладываются на всю жизнь гейзера: подъём, пик, спад
        private const int GeyserTicks = 64;
        private const int AnchoredTicks = 1800;
        private const int CatchRadius = 28;

        // На таком расстоянии игрок дотягивается до древка и выдёргивает копьё.
        // Меньше половины ширины экрана быть обязано: это подбор, а не притягивание
        private const float PickupRadius = 44f;

        // Копьё, брошенное и забытое за краем экрана, растворяется само: иначе игрок,
        // закинувший его в лаву или за спину боссу, остаётся без оружия навсегда
        private const float AbandonDistance = 1400f;

        // Отпущенная пересборка возвращает песок не мгновенно — за ~25 тиков
        private const float ReforgeRestoreSpeed = 0.08f;

        private const int FlightWidth = 20;
        private const int FlightHeight = 20;
        private const int GeyserWidth = 46;
        private const int GeyserHeight = 104;
        private const float GeyserBaseOffset = 10f;

        // Спрайт струи шире хитбокса: пена и капли на листе выходят за колонну урона
        private const float JetDrawWidth = 1.5f;

        // Устье рисуется ниже точки входа: копьё вошло в грунт остриём, а вода бьёт
        // из-под него, а не из середины древка
        private const float GeyserVisualDrop = 10f;

        // Дуга пологая: брошенное копьё должно доставать через экран, а не втыкаться
        // под ноги. Разгон гасит просадку ещё сильнее — быстрый бросок летит почти прямо
        private const float FlightGravity = 0.11f;
        private const float FlightMaxFall = 14f;
        private const float SlamGravity = 0.05f; // крюк летит дальше: дугу распрямляем

        // Насколько скорость выпрямляет дугу: на пороге шлейфа гравитация уже вдвое слабее
        private const float SpeedGravityRelief = 0.55f;

        // Порог шлейфа разгона: обычный бросок сюда не дотягивает, заряженный — да
        private const float SpeedFxThreshold = 15f;
        private const float SpeedFxFull = 27f;

        private const float RushLength = 150f;
        private const float RushWidth = 66f;
        private const float ArcLength = 78f;
        private const float ArcBow = 13f;

        private const float ReturnAccel = 1.4f;
        private const float ReturnMaxSpeed = 17f;

        // Верёвка: считается натянутой, когда копьё отлетело на TetherEngageDistance.
        // Пока не натянулась — игрока не трогаем вообще, иначе тяга включалась бы в тот
        // же тик, когда трезубец ещё в руке.
        private const float TetherEngageDistance = 100f;
        private const float JoltSpeed = 10f;
        private const float ReelSpeed = 1.4f;
        private const float TensionAccel = 1.15f;
        private const float MaxRideSpeed = 22f;
        private const float PullStopDistance = 56f;
        private const int TetherSegments = 14;

        private const float PlantTilt = 0.35f;

        // Прыжок цели больше этого за один тик — телепорт, а не бег: 120 px/тик обычным
        // движением не набирает никто, так что ложных срабатываний быть не должно
        private const float TeleportJump = 120f;

        private const float GeyserDamageMultiplier = 0.55f;
        private const float ReturnDamageMultiplier = 0.8f;
        private const float StuckDamageMultiplier = 0.4f;
        private const float SoakedDamageMultiplier = 1.25f;

        private bool slam;

        // Куда воткнулся: -1 — в грунт, иначе индекс NPC и смещение от его центра.
        // Ездит в ExtraAI, потому что следовать за целью должны все клиенты.
        private int stuckNpc = -1;
        private int stuckNpcType;
        private Vector2 stuckOffset;

        // Где цель была в прошлом тике — по этому ловим телепорт
        private Vector2 lastTargetCenter;

        // Состояние верёвки считает только клиент владельца — он же двигает игрока
        private float ropeLength = -1f;

        // Насколько копьё утекло песком в руку хозяина: 0 — целое, 1 — рассыпалось
        private float reforgeProgress;

        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        private float Phase => Projectile.ai[0];

        // Копьё стоит воткнутым — только такое можно позвать песком: летящее ещё
        // никуда не воткнулось, а возвращающееся и так в руке
        public bool Stuck => Phase == PhaseGeyser || Phase == PhaseAnchored;

        // Своё брошенное копьё — одно на игрока, но перебираем честно: после
        // смерти/перезахода индекс снаряда не сохраняется
        public static RoyalSpearThrown FindOwned(Player player, bool stuckOnly = false)
        {
            int type = ModContent.ProjectileType<RoyalSpearThrown>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile proj = Main.projectile[i];
                if (proj.active && proj.type == type && proj.owner == player.whoAmI
                    && proj.ModProjectile is RoyalSpearThrown thrown && (!stuckOnly || thrown.Stuck))
                    return thrown;
            }
            return null;
        }

        // 0 — копьё идёт обычным броском, 1 — предел разгона. Считается по фактической
        // скорости, поэтому заряд не нужно синхронизировать отдельно: у всех клиентов
        // скорость снаряда одна и та же
        private float SpeedIntensity => MathHelper.Clamp(
            (Projectile.velocity.Length() - SpeedFxThreshold) / (SpeedFxFull - SpeedFxThreshold),
            0f, 1f);

        private bool SpeedFxActive => Projectile.velocity.Length() > SpeedFxThreshold;

        // Смещение центра хитбокса от точки входа. В фазе гейзера колонна тянется вверх,
        // и её высота меняется по кадру струи — поэтому считаем от ТЕКУЩЕЙ высоты.
        // AnchorPoint и SetAnchor обязаны быть строго обратны друг другу, иначе копьё
        // прыгает на разницу высот каждый кадр
        private Vector2 AnchorOffset => Phase == PhaseGeyser
            ? new Vector2(0f, Projectile.height * 0.5f - GeyserBaseOffset)
            : Vector2.Zero;

        // Точка, куда трезубец вошёл
        private Vector2 AnchorPoint => Projectile.Center + AnchorOffset;

        // Устье гейзера: та же точка, но ниже — вода бьёт из-под острия
        private Vector2 GeyserMouth => AnchorPoint + new Vector2(0f, GeyserVisualDrop);

        // 0..1 по жизни гейзера — гоняет и кадр спрайта, и высоту хитбокса
        private float GeyserProgress => MathHelper.Clamp(Projectile.ai[1] / GeyserTicks, 0f, 1f);

        // Трезубец бьёт холодной водой; горячий вариант оставлен биому и боссу
        private const GeyserStyle GeyserVariant = GeyserStyle.Strong;

        public override void SetDefaults()
        {
            Projectile.width = FlightWidth;
            Projectile.height = FlightHeight;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.scale = 1.2f;
            Projectile.timeLeft = AnchoredTicks;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 14;
            Projectile.knockBack = 0f;
        }

        // Режим крюка задаётся при спавне и не меняется — гоняем его отдельным полем,
        // потому что ванильная синхронизация снаряда возит только ai[0] и ai[1]
        public void MarkSlam()
        {
            slam = true;
            Projectile.netUpdate = true;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(slam);
            writer.Write((short)stuckNpc);
            writer.Write(stuckNpcType);
            writer.WriteVector2(stuckOffset);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            slam = reader.ReadBoolean();
            stuckNpc = reader.ReadInt16();
            stuckNpcType = reader.ReadInt32();
            stuckOffset = reader.ReadVector2();
        }

        // Цель, в которой сидит трезубец. Слот мог освободиться или достаться другому
        // NPC — сверяем ещё и с запомненным типом
        private NPC StuckTarget
        {
            get
            {
                if (stuckNpc < 0)
                    return null;
                NPC candidate = Main.npc[stuckNpc];
                return candidate.active && candidate.type == stuckNpcType ? candidate : null;
            }
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (!owner.active || owner.dead)
            {
                Projectile.Kill();
                return;
            }

            UpdateReforge(owner);

            if (Phase == PhaseGeyser)
                UpdateGeyser();
            else if (Phase == PhaseReturn)
                UpdateReturn(owner);
            else if (Phase == PhaseAnchored)
                UpdateAnchored(owner);
            else
                UpdateFlight();

            // Крюк тянет игрока, пока тот не подтянулся к трезубцу
            if (slam && Phase != PhaseReturn)
                PullOwner(owner);

            Lighting.AddLight(Projectile.Center, 0.18f, 0.32f, 0.45f);
        }

        // Пересборка тянет песок с воткнутого копья: пока хозяин держит ПКМ, древко
        // разлетается зерно за зерном тем же шейдером, что собирает его в руке;
        // отпустил — песчинки садятся обратно и копьё цело
        private void UpdateReforge(Player owner)
        {
            float target = RoyalSpearReforge.ProgressFor(owner);
            reforgeProgress = target > reforgeProgress
                ? target
                : MathHelper.Max(reforgeProgress - ReforgeRestoreSpeed, 0f);
        }

        // Копьё утекло в руку хозяина. Зовётся с клиента владельца, остальным смерть
        // снаряда приходит пакетом
        public void CrumbleAway()
        {
            if (!Main.dedServ)
                SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.3f }, AnchorPoint);

            Projectile.Kill();
        }

        private void UpdateFlight()
        {
            // Дуга: копьё теряет высоту, а не летит по линейке. Разогнанное копьё
            // просаживается медленнее — заряд читается как настильная траектория
            float gravity = (slam ? SlamGravity : FlightGravity)
                * (1f - SpeedGravityRelief * SpeedIntensity);
            Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + gravity, FlightMaxFall);
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            if (Main.netMode == NetmodeID.Server || !Main.rand.NextBool(2))
                return;

            Dust trail = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
            trail.velocity = Projectile.velocity * 0.15f;
            trail.noGravity = true;
            trail.scale = Main.rand.NextFloat(0.9f, 1.4f);
        }

        private void UpdateGeyser()
        {
            Projectile.velocity = Vector2.Zero;

            if (!FollowStuckTarget())
                return;

            if (++Projectile.ai[1] >= GeyserTicks)
            {
                EnterAnchored();
                return;
            }

            // Хитбокс живёт по кадру спрайта: пока струя вспучивается или оседает,
            // колонна не должна бить на всю высоту
            ResizeJet();

            TideGeyserFx.Emit(GeyserMouth, GeyserProgress, GeyserVariant, GeyserWidth);
        }

        // Держит высоту хитбокса равной высоте струи на текущем кадре, сохраняя устье
        // на месте: сначала читаем анкер по старой высоте, потом ставим центр по новой
        private void ResizeJet()
        {
            Vector2 anchor = AnchorPoint;
            int jetHeight = (int)(GeyserHeight * TideGeyserFx.JetHeight(GeyserProgress));
            Projectile.height = Math.Max(jetHeight, FlightHeight);
            Projectile.width = GeyserWidth;
            Projectile.Center = anchor - new Vector2(0f, Projectile.height * 0.5f - GeyserBaseOffset);
        }

        // Воткнутый трезубец стоит на месте, едет на цели и продолжает рвать её водой:
        // урон идёт сам по себе, хитбокс сидит внутри врага, а localNPCHitCooldown
        // задаёт частоту тиков. Забрать его можно только придя за ним ногами
        private void UpdateAnchored(Player owner)
        {
            Projectile.velocity = Vector2.Zero;

            // Пока хозяин в пределах разумного — копьё торчит сколько угодно долго.
            // Уехал далеко и забыл — таймер идёт своим ходом и вернёт ему оружие
            if (Vector2.DistanceSquared(owner.MountedCenter, AnchorPoint) < AbandonDistance * AbandonDistance)
                Projectile.timeLeft = AnchoredTicks;

            if (!FollowStuckTarget())
                return;

            if (TryPickup(owner))
                return;

            if (Main.netMode == NetmodeID.Server || !Main.rand.NextBool(StuckTarget != null ? 4 : 12))
                return;

            Dust drip = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(8f, 10f),
                DustID.Water, Vector2.Zero);
            drip.scale = Main.rand.NextFloat(0.7f, 1.1f);
        }

        // Игрок дошёл и выдернул копьё: решает клиент владельца, остальные узнают о смене
        // фазы пакетом. Дальше короткий рывок в руку — расстояние тут уже вытянутой руки
        private bool TryPickup(Player owner)
        {
            if (Projectile.owner != Main.myPlayer)
                return false;
            if (Vector2.DistanceSquared(owner.MountedCenter, AnchorPoint) > PickupRadius * PickupRadius)
                return false;

            EnterReturn();
            SoundEngine.PlaySound(SoundID.Item8 with { Pitch = 0.5f }, Projectile.Center);
            return true;
        }

        private void UpdateReturn(Player owner)
        {
            Projectile.tileCollide = false;

            Vector2 toOwner = owner.MountedCenter - Projectile.Center;
            if (toOwner.Length() < CatchRadius)
            {
                Catch(owner);
                return;
            }

            float speed = MathHelper.Min(Projectile.velocity.Length() + ReturnAccel, ReturnMaxSpeed);
            Projectile.velocity = Vector2.Normalize(toOwner) * speed;
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            if (Main.netMode == NetmodeID.Server || !Main.rand.NextBool(3))
                return;

            Dust trail = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
            trail.velocity = -Projectile.velocity * 0.1f;
            trail.noGravity = true;
            trail.scale = Main.rand.NextFloat(0.7f, 1.1f);
        }

        // Верёвочная физика: гравитацию игроку считает ваниль, мы только держим связь.
        // Натянутая верёвка не даёт удаляться от анкера (гасим радиальную составляющую
        // «от себя») и тянет вдоль себя, а тангенциальная скорость остаётся — отсюда
        // маятник вместо прежнего магнита, который просто подменял скорость каждый тик.
        // Считает всё клиент владельца: скорость игрока симулирует он сам.
        private void PullOwner(Player owner)
        {
            if (Projectile.owner != Main.myPlayer)
                return;

            var mp = owner.GetModPlayer<RoyalSpearPlayer>();
            if (!mp.RidingSlam || mp.SlamArmed)
                return; // взведённого игрока больше не держим: ему нужно упасть

            Vector2 toAnchor = AnchorPoint - owner.MountedCenter;
            float distance = toAnchor.Length();
            if (distance < 1f)
                return;
            Vector2 dir = toAnchor / distance;

            if (ropeLength < 0f)
            {
                // Копьё ещё не отлетело — верёвка провисает, игрок стоит свободно
                if (distance < TetherEngageDistance)
                {
                    // Воткнулось под самым носом: ехать некуда, сразу ждём земли
                    if (Phase != PhaseFlight)
                        mp.ArmSlam();
                    return;
                }

                // Верёвка кончилась и рвёт игрока с места — рывок вдоль неё
                ropeLength = distance;
                owner.velocity = dir * JoltSpeed - Vector2.UnitY * 3f;
                SoundEngine.PlaySound(SoundID.Item17 with { Pitch = -0.3f }, owner.Center);
                return;
            }

            // Трезубец подтягивает: верёвка укорачивается, дуга сжимается
            ropeLength = MathHelper.Max(ropeLength - ReelSpeed, PullStopDistance);

            if (distance <= PullStopDistance)
            {
                // Долетел до анкера — отпускаем С НАБРАННОЙ скоростью, дальше он падает сам
                mp.ArmSlam();
                return;
            }

            if (distance <= ropeLength)
                return; // верёвка провисла, игрок в свободном полёте

            float radial = Vector2.Dot(owner.velocity, dir);
            if (radial < 0f)
                owner.velocity -= dir * radial;

            float overshoot = MathHelper.Clamp((distance - ropeLength) / 40f, 0f, 1f);
            owner.velocity += dir * TensionAccel * (0.35f + 0.65f * overshoot);

            if (owner.velocity.Length() > MaxRideSpeed)
                owner.velocity = Vector2.Normalize(owner.velocity) * MaxRideSpeed;
        }

        // Едет на цели, сохраняя точку входа. Возвращает false, если цель кончилась и
        // трезубец сорвался — вызывающему в этот тик делать больше нечего.
        private bool FollowStuckTarget()
        {
            if (stuckNpc < 0)
                return true; // сидит в грунте, ехать не за кем

            NPC target = StuckTarget;
            if (target == null)
            {
                // Цель кончилась — копьё падает там, где было
                DropFromTarget(Projectile.Center);
                return false;
            }

            // Телепорт цели: копьё физически не может уехать вместе с ней, оно
            // остаётся там, откуда враг исчез. Решает клиент владельца, остальные
            // получат новое состояние пакетом
            if (lastTargetCenter != Vector2.Zero
                && Vector2.DistanceSquared(target.Center, lastTargetCenter) > TeleportJump * TeleportJump)
            {
                if (Projectile.owner == Main.myPlayer)
                {
                    DropFromTarget(lastTargetCenter + stuckOffset);
                    return false;
                }
                return true;
            }

            lastTargetCenter = target.Center;
            SetAnchor(target.Center + stuckOffset);
            return true;
        }

        // Сорвался с цели: дальше просто падает и втыкается в землю
        private void DropFromTarget(Vector2 dropAt)
        {
            stuckNpc = -1;
            stuckNpcType = 0;
            lastTargetCenter = Vector2.Zero;
            Projectile.ai[0] = PhaseFlight;
            Projectile.ai[1] = 0f;
            Projectile.width = FlightWidth;
            Projectile.height = FlightHeight;
            Projectile.Center = dropAt;
            Projectile.friendly = true;
            Projectile.tileCollide = true;
            Projectile.velocity = new Vector2(0f, 1f);
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;
        }

        // Ставит снаряд так, чтобы точка входа копья попала в anchor
        private void SetAnchor(Vector2 anchor)
        {
            Projectile.Center = anchor - AnchorOffset;
        }

        private void EnterGeyser(Vector2 anchor, bool plantInGround = false)
        {
            if (Phase != PhaseFlight)
                return;

            // Воткнувшийся в землю трезубец доворачиваем острием вниз: сохранённый угол
            // плоского броска читается как бревно, лежащее на траве
            if (plantInGround)
            {
                float flightAngle = Projectile.rotation - MathHelper.PiOver2;
                Projectile.rotation = MathHelper.Lerp(flightAngle, MathHelper.PiOver2, PlantTilt)
                    + MathHelper.PiOver2;
            }

            Projectile.ai[0] = PhaseGeyser;
            Projectile.ai[1] = 0f;
            Projectile.velocity = Vector2.Zero;
            Projectile.tileCollide = false;
            Projectile.width = GeyserWidth;
            Projectile.height = GeyserHeight;
            Projectile.Center = anchor - new Vector2(0f, GeyserHeight * 0.5f - GeyserBaseOffset);
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;

            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.2f }, anchor);
        }

        // Гейзер выдохся — трезубец остаётся торчать в цели или в грунте и продолжает
        // резать: урон в этой фазе ослаблен, но идёт, пока копьё не позовут назад
        private void EnterAnchored()
        {
            Vector2 anchor = AnchorPoint;
            Projectile.ai[0] = PhaseAnchored;
            Projectile.ai[1] = 0f;
            Projectile.width = FlightWidth;
            Projectile.height = FlightHeight;
            Projectile.Center = anchor;
            Projectile.friendly = true;
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;
        }

        private void EnterReturn()
        {
            Vector2 anchor = AnchorPoint;
            Projectile.ai[0] = PhaseReturn;
            Projectile.ai[1] = 0f;
            Projectile.width = FlightWidth;
            Projectile.height = FlightHeight;
            Projectile.Center = anchor;
            Projectile.friendly = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = AnchoredTicks;
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;

            // Трезубец пошёл в руку — тянуть больше нечем, взрыв ждёт земли
            if (slam && Projectile.owner == Main.myPlayer)
                Main.player[Projectile.owner].GetModPlayer<RoyalSpearPlayer>().ArmSlam();
        }

        private void Catch(Player owner)
        {
            if (Projectile.owner == Main.myPlayer)
                SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.3f }, owner.Center);
            Projectile.Kill();
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Влетел в потолок снизу — там доворот вниз выглядел бы нелепо
            bool plant = oldVelocity.Y > -0.5f;
            EnterGeyser(Projectile.Center - Vector2.Normalize(oldVelocity) * 4f, plant);
            return false;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (Phase == PhaseGeyser)
                modifiers.FinalDamage *= GeyserDamageMultiplier;
            else if (Phase == PhaseReturn)
                modifiers.FinalDamage *= ReturnDamageMultiplier;
            else if (Phase == PhaseAnchored)
                modifiers.FinalDamage *= StuckDamageMultiplier;

            // Пока копьё сидит в цели, тики DoT не должны её пинать: иначе враг
            // отлетает каждые 14 тиков и катается по экрану вместе с трезубцем.
            // Отбрасывание остаётся только у самого входа (фаза полёта) и у возврата.
            if (Phase == PhaseGeyser || Phase == PhaseAnchored)
                modifiers.Knockback *= 0f;

            if (SoACombat.IsSoaked(target))
                modifiers.FinalDamage *= SoakedDamageMultiplier;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            SoACombat.Soak(target);

            if (Projectile.owner == Main.myPlayer)
                Main.player[Projectile.owner].GetModPlayer<RoyalSpearPlayer>().AddSlamDamage(damageDone);

            // Первая же цель на пути принимает трезубец: он входит в неё, поднимает
            // гейзер и дальше едет вместе с ней
            if (Phase == PhaseFlight)
            {
                StickInto(target);
                EnterGeyser(Projectile.Center);
            }
        }

        // Точку входа запоминаем смещением от центра цели: так копьё сидит в той же
        // части туши, а не съезжает в середину
        private void StickInto(NPC target)
        {
            stuckNpc = target.whoAmI;
            stuckNpcType = target.type;
            stuckOffset = Projectile.Center - target.Center;
            lastTargetCenter = target.Center;

            // Слишком глубоко внутрь крупной туши уходить не даём, иначе копьё пропадает
            float maxOffset = Math.Max(target.width, target.height) * 0.5f;
            if (stuckOffset.Length() > maxOffset)
                stuckOffset = Vector2.Normalize(stuckOffset) * maxOffset;
        }

        public override Color? GetAlpha(Color lightColor)
        {
            return Color.Lerp(lightColor, new Color(200, 235, 255), 0.35f);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (Phase == PhaseGeyser)
                DrawGeyserColumn();

            if (slam)
                DrawTether();

            if (SpeedFxActive)
                DrawSpeedFx();

            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 drawPos = AnchorPoint - Main.screenPosition;

            // Копьё разбирают песком: та же сборка, только прогресс идёт назад
            if (reforgeProgress > 0.01f)
            {
                float intact = 1f - reforgeProgress;
                SoAVfx.DrawSandForged(Main.spriteBatch, tex, drawPos, Projectile.GetAlpha(lightColor),
                    Projectile.rotation, tex.Size() / 2f, Projectile.scale, intact,
                    Projectile.whoAmI, Projectile.identity);
                return false;
            }

            Main.EntitySpriteDraw(tex, drawPos, null, Projectile.GetAlpha(lightColor),
                Projectile.rotation, tex.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }

        // Струю рисует спрайтовый компонент: кадр берётся из прогресса гейзера,
        // ширина в мире задаётся здесь и от неё же считается масштаб листа
        private void DrawGeyserColumn()
        {
            TideGeyserFx.Draw(Main.spriteBatch, GeyserMouth, GeyserProgress,
                GeyserVariant, GeyserWidth * JetDrawWidth, 1f, Projectile.identity);
        }

        // Разгон за порогом: процедурный шлейф (SoA:SpeedRush) позади острия плюс две
        // дуги, бегущие вдоль древка от пятки к наконечнику. Обе вещи держатся на
        // фактической скорости, поэтому по мере просадки в конце дуги гаснут сами
        private void DrawSpeedFx()
        {
            float intensity = SpeedIntensity;
            float aim = Projectile.velocity.ToRotation();

            // Квад шлейфа острием вперёд: центр сдвинут назад, чтобы конус тянулся
            // за копьём, а не накрывал его
            Vector2 rushCenter = Projectile.Center - Projectile.velocity.SafeNormalize(Vector2.UnitX)
                * (RushLength * 0.5f - 18f);

            SoAVfx.BeginAdditive(Main.spriteBatch);
            SoAVfx.DrawSpeedRush(Main.spriteBatch, rushCenter, aim,
                RushLength * (0.75f + 0.35f * intensity), RushWidth, intensity, 0.55f + 0.45f * intensity);

            // Дуги бегут тем быстрее, чем сильнее разгон
            float phase = Main.GlobalTimeWrappedHourly * (1.4f + 1.6f * intensity) % 1f;
            SoAVfx.DrawTravellingArcs(Main.spriteBatch, Projectile.Center, aim,
                ArcLength, ArcBow * (0.7f + 0.5f * intensity), phase,
                new Color(140, 220, 255, 0) * (0.55f + 0.45f * intensity));
            SoAVfx.EndAdditive(Main.spriteBatch);
        }

        // Верёвка — не цепь, а провисающая струя воды: цепочка аддитивных капель
        // по линии «рука игрока → трезубец» с провисом по синусу
        private void DrawTether()
        {
            Player owner = Main.player[Projectile.owner];
            Vector2 hand = owner.MountedCenter;
            Vector2 toProjectile = AnchorPoint - hand;
            float sag = MathHelper.Clamp(toProjectile.Length() * 0.12f, 0f, 26f);

            SoAVfx.BeginAdditive(Main.spriteBatch);
            for (int i = 1; i <= TetherSegments; i++)
            {
                float t = i / (float)TetherSegments;
                Vector2 point = hand + toProjectile * t;
                point.Y += (float)Math.Sin(t * MathHelper.Pi) * sag;
                point += new Vector2(0f, (float)Math.Sin(t * 9f - Main.GlobalTimeWrappedHourly * 12f) * 2.5f);

                float thickness = MathHelper.Lerp(7f, 12f, (float)Math.Sin(t * MathHelper.Pi));
                SoAVfx.DrawTintedGlow(Main.spriteBatch, point, new Vector2(thickness),
                    new Color(120, 200, 255) * 0.55f);
            }
            SoAVfx.EndAdditive(Main.spriteBatch);
        }
    }
}
