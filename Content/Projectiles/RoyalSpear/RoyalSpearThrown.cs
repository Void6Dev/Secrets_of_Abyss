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
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Бросок Королевского копья (ПКМ). Летит по дуге и остаётся там, куда воткнулось.
    // Вернуть его можно двумя путями: подойти и выдернуть руками или, держа ПКМ,
    // пересобрать песком прямо в руке (RoyalSpearReforge).
    // Фазы через ai[0]:
    //   0 — полёт по дуге: втыкается в первую цель или в тайл
    //   1 — стоит воткнутым: едет на цели и режет её, ждёт, когда за ним придут
    //   2 — возврат: прыгает в руку с расстояния вытянутой руки
    // Пока копьё не в руке, ЛКМ недоступна — оружия у игрока буквально нет.
    //
    // Приливный рывок (полная шкала): копьё запускается сверху, с точки, куда
    // долетел рывок игрока (RoyalSpearPlayer.LaunchDive), и оглушает цель, в которую вошло.
    public class RoyalSpearThrown : ModProjectile
    {
        private const float PhaseFlight = 0f;
        private const float PhaseAnchored = 1f;
        private const float PhaseReturn = 2f;

        private const int AnchoredTicks = 1800;
        private const int CatchRadius = 28;

        // На таком расстоянии игрок дотягивается до древка и выдёргивает копьё.
        // Меньше половины ширины экрана быть обязано: это подбор, а не притягивание
        private const float PickupRadius = 64f;

        // Копьё, брошенное и забытое за краем экрана, растворяется само: иначе игрок,
        // закинувший его в лаву или за спину боссу, остаётся без оружия навсегда
        private const float AbandonDistance = 1400f;

        // Отпущенная пересборка возвращает песок не мгновенно — за ~12 тиков
        private const float ReforgeRestoreSpeed = 0.08f;

        private const int SpearWidth = 20;
        private const int SpearHeight = 20;

        // Дуга пологая: брошенное копьё должно доставать через экран, а не втыкаться
        // под ноги. Разгон гасит просадку ещё сильнее — быстрый бросок летит почти прямо
        private const float FlightGravity = 0.11f;
        private const float FlightMaxFall = 14f;
        private const float SlamGravity = 0.05f; // удар сверху идёт почти по прямой

        // Насколько скорость выпрямляет дугу: на пороге шлейфа гравитация уже вдвое слабее
        private const float SpeedGravityRelief = 0.55f;

        // Порог шлейфа разгона: обычный бросок сюда не дотягивает, заряженный — да
        private const float SpeedFxThreshold = 15f;
        private const float SpeedFxFull = 27f;

        private const float RushLength = 150f;
        private const float RushWidth = 66f;
        private const float ArcLength = 78f;
        private const float ArcBow = 13f;

        // Возврат в руку быстрый: копьё уже у игрока под рукой, ждать нечего
        private const float ReturnAccel = 3f;
        private const float ReturnMaxSpeed = 28f;

        private const float PlantTilt = 0.35f;

        // Прыжок цели больше этого за один тик — телепорт, а не бег: 120 px/тик обычным
        // движением не набирает никто, так что ложных срабатываний быть не должно
        private const float TeleportJump = 120f;

        private const float ReturnDamageMultiplier = 0.8f;
        private const float StuckDamageMultiplier = 0.4f;

        // Бросок приливного рывка: оглушает цель, в которую вошёл
        private bool slam;

        // Куда воткнулось: -1 — в грунт, иначе индекс NPC и смещение от его центра.
        // Ездит в ExtraAI, потому что следовать за целью должны все клиенты.
        private int stuckNpc = -1;
        private int stuckNpcType;
        private Vector2 stuckOffset;

        // Где цель была в прошлом тике — по этому ловим телепорт
        private Vector2 lastTargetCenter;

        // Насколько копьё утекло песком в руку хозяина: 0 — целое, 1 — рассыпалось
        private float reforgeProgress;

        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        private float Phase => Projectile.ai[0];

        // Копьё стоит воткнутым — только такое можно позвать песком: летящее ещё
        // никуда не воткнулось, а возвращающееся и так в руке
        public bool Stuck => Phase == PhaseAnchored;

        public bool InFlight => Phase == PhaseFlight;

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

        private bool SpeedFxActive => Phase == PhaseFlight && Projectile.velocity.Length() > SpeedFxThreshold;

        public override void SetDefaults()
        {
            Projectile.width = SpearWidth;
            Projectile.height = SpearHeight;
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

        // Режим рывка задаётся при спавне и не меняется — гоняем его отдельным полем,
        // потому что ванильная синхронизация снаряда возит только ai
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

        // Цель, в которой сидит копьё. Слот мог освободиться или достаться другому
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

            if (Phase == PhaseReturn)
                UpdateReturn(owner);
            else if (Phase == PhaseAnchored)
                UpdateAnchored(owner);
            else
                UpdateFlight();

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
                SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.3f }, Projectile.Center);

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

        // Воткнутое копьё стоит на месте, едет на цели и продолжает резать её:
        // урон идёт сам по себе, хитбокс сидит внутри врага, а localNPCHitCooldown
        // задаёт частоту тиков. Забрать его можно, придя за ним или пересобрав песком
        private void UpdateAnchored(Player owner)
        {
            Projectile.velocity = Vector2.Zero;

            // Пока хозяин в пределах разумного — копьё торчит сколько угодно долго.
            // Уехал далеко и забыл — таймер идёт своим ходом и вернёт ему оружие
            if (Vector2.DistanceSquared(owner.MountedCenter, Projectile.Center) < AbandonDistance * AbandonDistance)
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
            if (Vector2.DistanceSquared(owner.MountedCenter, Projectile.Center) > PickupRadius * PickupRadius)
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

        // Едет на цели, сохраняя точку входа. Возвращает false, если цель кончилась и
        // копьё сорвалось — вызывающему в этот тик делать больше нечего.
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
            Projectile.Center = target.Center + stuckOffset;
            return true;
        }

        // Сорвалось с цели: дальше просто падает и втыкается в землю
        private void DropFromTarget(Vector2 dropAt)
        {
            stuckNpc = -1;
            stuckNpcType = 0;
            lastTargetCenter = Vector2.Zero;
            Projectile.ai[0] = PhaseFlight;
            Projectile.ai[1] = 0f;
            Projectile.Center = dropAt;
            Projectile.friendly = true;
            Projectile.tileCollide = true;
            Projectile.velocity = new Vector2(0f, 1f);
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;
        }

        private void EnterAnchored(bool plantInGround)
        {
            if (Phase != PhaseFlight)
                return;

            // Воткнувшееся в землю копьё доворачиваем остриём вниз: сохранённый угол
            // плоского броска читается как бревно, лежащее на траве
            if (plantInGround)
            {
                float flightAngle = Projectile.rotation - MathHelper.PiOver2;
                Projectile.rotation = MathHelper.Lerp(flightAngle, MathHelper.PiOver2, PlantTilt)
                    + MathHelper.PiOver2;
            }

            Projectile.ai[0] = PhaseAnchored;
            Projectile.ai[1] = 0f;
            Projectile.velocity = Vector2.Zero;
            Projectile.tileCollide = false;
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;

            if (Main.dedServ)
                return;

            SoundEngine.PlaySound(SoundID.Item21 with { Pitch = -0.2f, Volume = 0.6f }, Projectile.Center);
            for (int i = 0; i < (slam ? 26 : 12); i++)
            {
                Dust splash = Dust.NewDustPerfect(Projectile.Center, DustID.Water,
                    Main.rand.NextVector2Circular(4f, 4f) - Vector2.UnitY * 1.5f);
                splash.noGravity = true;
                splash.scale = Main.rand.NextFloat(1f, 1.6f);
            }
        }

        private void EnterReturn()
        {
            Projectile.ai[0] = PhaseReturn;
            Projectile.ai[1] = 0f;
            Projectile.friendly = true;
            Projectile.tileCollide = false;
            Projectile.timeLeft = AnchoredTicks;
            Array.Clear(Projectile.localNPCImmunity);
            Projectile.netUpdate = true;
        }

        private void Catch(Player owner)
        {
            if (Projectile.owner == Main.myPlayer)
                SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.3f }, owner.Center);
            Projectile.Kill();
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Влетело в потолок снизу — там доворот вниз выглядел бы нелепо
            bool plant = oldVelocity.Y > -0.5f;
            Projectile.Center -= Vector2.Normalize(oldVelocity) * 4f;
            EnterAnchored(plant);
            return false;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (Phase == PhaseReturn)
                modifiers.FinalDamage *= ReturnDamageMultiplier;
            else if (Phase == PhaseAnchored)
                modifiers.FinalDamage *= StuckDamageMultiplier;

            // Пока копьё сидит в цели, тики урона не должны её пинать: иначе враг
            // отлетает каждые 14 тиков и катается по экрану вместе с копьём.
            // Отбрасывание остаётся только у самого входа (полёт) и у возврата.
            if (Phase == PhaseAnchored)
                modifiers.Knockback *= 0f;

            SoACombat.ApplySoakBonus(target, ref modifiers);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            SoACombat.Soak(target);

            if (Projectile.owner == Main.myPlayer)
                Main.player[Projectile.owner].GetModPlayer<RoyalSpearPlayer>().AddSlamDamage(damageDone);

            if (Phase != PhaseFlight)
                return;

            // Первая же цель на пути принимает копьё: оно входит в неё и дальше едет
            // вместе с ней. Удар приливного рывка вдобавок вбивает цель на месте
            StickInto(target);
            if (slam)
                TideStunDebuff.Apply(target);
            EnterAnchored(false);
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
            if (SpeedFxActive)
                DrawSpeedFx();

            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

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

        // Разгон за порогом: процедурный шлейф (SoA:SpeedRush) позади острия плюс две
        // дуги, бегущие вдоль древка от пятки к наконечнику. Обе вещи держатся на
        // фактической скорости, поэтому по мере просадки в конце дуги гаснут сами
        private void DrawSpeedFx()
        {
            float intensity = SpeedIntensity;
            float aim = Projectile.velocity.ToRotation();

            // Квад шлейфа остриём вперёд: центр сдвинут назад, чтобы конус тянулся
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
    }
}
