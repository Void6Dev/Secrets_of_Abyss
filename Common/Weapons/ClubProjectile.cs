using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Utils;

namespace SoA.Common.Weapons
{
    // Основа тяжёлого оружия с зарядкой в духе Spirit Reforged — дубины, молоты, косы.
    // Весь цикл — один снаряд в руке:
    //  1. Замах (WindupTicks): оружие «выпрыгивает» из руки и уходит за спину. Отпустить раньше
    //     можно, но удар всё равно дождётся конца замаха.
    //  2. Зарядка (ChargeTicks): оружие дотягивается назад и дрожит от натуги. Заряд делится на
    //     ChargeStages ступеней; каждая — вспышка силуэта и звон, звон выше с каждой ступенью.
    //  3. Удар: резкий старт, торможение к концу. Если SmashesGround, ударная грань ищет землю
    //     подшагами и оружие встаёт на поверхность, а не проваливается в блок под ней.
    //  4. Оружие лежит на земле, отпружинив, и тает. Промах или коса — проходит дугу и тает в воздухе.
    // Наследник задаёт геометрию спрайта и цифры, эффекты добавляет через хуки.
    // ai[0] — заряд 0..1, ai[1] — ClubState, ai[2] — направление игрока
    public abstract class ClubProjectile : ModProjectile
    {
        protected enum ClubState { Charging, Swinging, Smashed }

        private const int FlashTicks = 20;
        private const int TrailLength = 12;
        private const int AfterimageCount = 5;
        private const int SurfaceSubsteps = 8;
        private const int RecoilTicks = 6;
        private const int TrailFadeTicks = 8;
        private const float BackArmOffset = 0.3f;
        // Пока рука выше этого поворота (для взгляда вправо), боёк ещё над плечом или за спиной:
        // землю там не ищем, иначе удар зацепит потолок позади игрока
        private const float SurfaceSearchFromArm = -MathHelper.Pi * 0.6f;
        private const float MissShrinkFrom = 0.75f;
        private const float LingerShrinkFrom = 0.55f;
        private const float StrainShake = 0.03f;
        private const float StagePitchStep = 0.2f;

        #region Настройка наследника

        // Геометрия спрайта в пикселях: точка хвата, центр бойка, ударная грань (ею оружие
        // встречает землю) и дальняя точка (по ней тянется трейл и считается досягаемость).
        // Спрайт рисуется рукоятью вниз-влево, бойком вверх-вправо
        protected abstract Vector2 GripPixel { get; }
        protected abstract Vector2 HeadPixel { get; }
        protected virtual Vector2 FacePixel => HeadPixel;
        protected virtual Vector2 TipPixel => HeadPixel;
        protected virtual float DrawScale => 1f;
        protected virtual float HitWidth => 26f;
        protected virtual bool SmashesGround => true;

        protected virtual int WindupTicks => 16;
        protected virtual int ChargeTicks => 36;
        protected virtual int ChargeStages => 1;
        protected virtual int SwingTicks => 16;
        protected virtual int LingerTicks => 20;
        protected virtual float FullChargeSwingSpeed => 1.25f;

        protected virtual float MinDamageMult => 0.6f;
        protected virtual float FullDamageMult => 1.6f;
        protected virtual float MinKnockbackMult => 0.6f;
        protected virtual float FullKnockbackMult => 1.5f;

        // Повороты передней руки для взгляда вправо: 0 — вниз, -π/2 — вперёд, -π — вверх
        protected virtual float WindupStartArm => -MathHelper.Pi * 0.55f;
        protected virtual float PulledBackArm => -MathHelper.Pi * 1.2f;
        protected virtual float SwingEndArm => MathHelper.Pi * 0.1f;
        // Какую долю оттяжки оружие проходит за замах; остальное дотягивается зарядом
        protected virtual float PullbackOnWindup => 0.7f;
        protected virtual float RecoilAngle => 0.06f + 0.08f * Charge;

        // Цвет вспышек, ободка, копий на ударе; может зависеть от Charge (нагрев)
        protected virtual Color ChargeColor => Color.White;
        // Сила пульсирующего ободка и ровного свечения поверх спрайта, 0..1
        protected virtual float RimStrength => State == ClubState.Charging && FullCharge ? 1f : 0f;
        protected virtual float OverlayStrength => 0f;
        protected virtual Color SmashDustColor => Color.LightGray;
        protected virtual TrailStyle? SwingTrailStyle => null;
        protected virtual float SwingTrailWidth => 18f;

        protected virtual SoundStyle ReadySound => SoundID.MaxMana with { Pitch = -0.3f, Volume = 0.8f };
        protected virtual SoundStyle SwingSound => SoundID.DD2_MonkStaffSwing with { Pitch = -0.3f };

        // Что светится у оружия: по умолчанию весь силуэт. Коса светит только лезвием
        protected virtual Texture2D GlowMask(Texture2D texture) => SilhouetteCache.Get(texture);

        // Цвет ленты удара по длине: 0 — у оружия, 1 — хвост
        protected virtual Color SwingTrailColorAt(float progress) => ChargeColor * (1f - progress);

        protected virtual void OnChargeTick(Player owner) { }
        protected virtual void OnChargeStage(Player owner, int stage) { }
        protected virtual void OnSwingStart(Player owner) { }
        // progress 0..1 по дуге удара; вызывается каждый тик удара у всех клиентов
        protected virtual void OnSwingTick(Player owner, float progress) { }
        // Все клиенты и сервер; снаряды из удара порождать только у владельца
        protected virtual void OnSmash(Player owner, Vector2 face) { }
        protected virtual void OnClubHit(NPC target) { }

        #endregion

        public float Charge { get => Projectile.ai[0]; private set => Projectile.ai[0] = value; }
        public bool FullCharge => Charge >= 1f;
        // 0 — ни одной ступени, ChargeStages — полный заряд
        public int ChargeStage => (int)(Charge * ChargeStages + 0.0001f);
        protected ClubState State { get => (ClubState)Projectile.ai[1]; private set => Projectile.ai[1] = (float)value; }
        protected int Direction => Projectile.ai[2] < 0 ? -1 : 1;
        protected Player Owner => Main.player[Projectile.owner];

        private int _windupTimer;
        private int _swingTimer;
        private int _lingerTimer;
        private float _smashArm;
        private bool _smashEffectsDone;
        private int _flashTimer;
        private int _strainTimer;

        private float _armRotation;   // для взгляда вправо
        private float _animScale;
        private readonly Vector2[] _tipTrail = new Vector2[TrailLength];
        private readonly float[] _armTrail = new float[TrailLength];
        private int _trailCount;

        private float SpriteAxisAngle => (HeadPixel - GripPixel).ToRotation();
        private float SwingSpeed => FullCharge ? FullChargeSwingSpeed : 1f;

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.ownerHitCheck = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1; // каждого врага — один раз за удар
            Projectile.timeLeft = 2;              // продлевается каждый тик, пока цикл не кончится
            SafeSetDefaults();
        }

        protected virtual void SafeSetDefaults() { }

        #region Поза

        // Как оружие сидит в руке при повороте руки rightArm (для взгляда вправо)
        protected readonly struct ClubPose
        {
            public readonly Vector2 Grip;
            public readonly float Rotation;
            public readonly float Scale;
            public readonly int Direction;
            public SpriteEffects Effects => Direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            public ClubPose(Vector2 grip, float rotation, float scale, int direction)
            {
                Grip = grip;
                Rotation = rotation;
                Scale = scale;
                Direction = direction;
            }
        }

        protected ClubPose PoseAt(float rightArm)
        {
            float arm = MirrorForDirection(rightArm);
            Vector2 grip = Owner.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, arm);
            // Рукоять продолжает руку; отражённый спрайт смотрит зеркально, поэтому и поворот зеркалится целиком
            float rotation = MirrorForDirection(rightArm + MathHelper.PiOver2 - SpriteAxisAngle);
            return new ClubPose(grip, rotation, DrawScale * _animScale, Direction);
        }

        // Пиксель спрайта → мировая точка при данной позе
        protected Vector2 SpriteToWorld(in ClubPose pose, Vector2 pixel)
        {
            var local = new Vector2(pose.Direction * (pixel.X - GripPixel.X), pixel.Y - GripPixel.Y);
            return pose.Grip + local.RotatedBy(pose.Rotation) * pose.Scale;
        }

        // Пиксель спрайта → мировая точка в текущей позе
        protected Vector2 PixelToWorld(Vector2 pixel) => SpriteToWorld(PoseAt(_armRotation), pixel);

        private float MirrorForDirection(float rightFacing) => HeldProjectiles.Mirror(rightFacing, Direction);

        private Vector2 OriginFor(Texture2D tex)
            => Direction == -1 ? new Vector2(tex.Width - GripPixel.X, GripPixel.Y) : GripPixel;

        #endregion

        public override void AI()
        {
            Player owner = Owner;
            if (!owner.active || owner.dead || owner.CCed || owner.noItems)
            {
                Projectile.Kill();
                return;
            }

            Projectile.timeLeft = 2;
            HeldProjectiles.Hold(Projectile, owner, Direction);
            if (_flashTimer > 0)
                _flashTimer--;

            switch (State)
            {
                case ClubState.Charging:
                    Charging(owner);
                    break;
                case ClubState.Swinging:
                    Swinging(owner);
                    break;
                case ClubState.Smashed:
                    Smashed(owner);
                    break;
            }

            if (!Projectile.active)
                return;

            HeldProjectiles.SetArms(owner, Direction, _armRotation, _armRotation + BackArmOffset);
            Projectile.Center = PixelToWorld(HeadPixel);
        }

        private void Charging(Player owner)
        {
            if (_windupTimer < WindupTicks)
            {
                _windupTimer++;
            }
            else if (!FullCharge)
            {
                int stageBefore = ChargeStage;
                Charge = Math.Min(Charge + 1f / ChargeTicks, 1f);
                if (ChargeStage > stageBefore)
                    ReachStage(owner, ChargeStage);
            }

            float windup = _windupTimer / (float)WindupTicks;
            float pullback = MathHelper.Lerp(windup, Charge, 1f - PullbackOnWindup);
            _armRotation = MathHelper.Lerp(WindupStartArm, PulledBackArm, SoAEasing.CircOut(pullback));
            _animScale = SoAEasing.BackOut(windup);

            // Натуга: оружие мелко дрожит, чем сильнее заряд — тем заметнее
            if (windup >= 1f)
                _armRotation += (float)Math.Sin(++_strainTimer * 1.9f) * StrainShake * Charge;

            OnChargeTick(owner);

            if (Projectile.owner != Main.myPlayer)
                return;

            // Пока заряжаешь, можно развернуться к курсору
            int facing = Main.MouseWorld.X >= owner.MountedCenter.X ? 1 : -1;
            if (facing != Direction)
            {
                Projectile.ai[2] = facing;
                Projectile.netUpdate = true;
            }

            if (!owner.channel && windup >= 1f)
            {
                State = ClubState.Swinging;
                _swingTimer = 0;
                Projectile.netUpdate = true;
            }
        }

        private void ReachStage(Player owner, int stage)
        {
            _flashTimer = FlashTicks;
            if (!Main.dedServ)
            {
                SoundStyle ready = ReadySound;
                SoundEngine.PlaySound(ready with { Pitch = ready.Pitch + StagePitchStep * (stage - 1) },
                    PixelToWorld(HeadPixel));
            }
            OnChargeStage(owner, stage);
        }

        private void Swinging(Player owner)
        {
            if (_swingTimer == 0)
            {
                if (!Main.dedServ)
                    SoundEngine.PlaySound(SwingSound with { Pitch = SwingSound.Pitch + 0.25f * Charge }, owner.Center);
                OnSwingStart(owner);
            }
            _swingTimer++;

            float progress = _swingTimer * SwingSpeed / SwingTicks;
            float previous = _armRotation;
            float next = MathHelper.Lerp(PulledBackArm, SwingEndArm, SoAEasing.QuadOut(Math.Min(progress, 1f)));
            _animScale = progress > MissShrinkFrom
                ? 1f - SoAEasing.CubicIn(Math.Min((progress - MissShrinkFrom) / (1f - MissShrinkFrom), 1f))
                : 1f;

            if (SmashesGround && TryFindSurface(previous, next, out float contactArm))
            {
                _armRotation = contactArm;
                PushTrail();
                State = ClubState.Smashed;
                _lingerTimer = 0;
                _smashArm = contactArm;
                if (Projectile.owner == Main.myPlayer)
                    Projectile.netUpdate = true;
                Smashed(owner);
                return;
            }

            _armRotation = next;
            PushTrail();
            OnSwingTick(owner, Math.Min(progress, 1f));
            if (progress >= 1f)
                Projectile.Kill();
        }

        // Ударная грань идёт от previous к next; ищем первый подшаг, на котором она в земле,
        // и возвращаем последний свободный — оружие встаёт на поверхность
        private bool TryFindSurface(float previous, float next, out float contactArm)
        {
            float free = previous;
            for (int i = 1; i <= SurfaceSubsteps; i++)
            {
                float arm = MathHelper.Lerp(previous, next, i / (float)SurfaceSubsteps);
                if (arm >= SurfaceSearchFromArm && FaceInGround(arm))
                {
                    contactArm = free;
                    return true;
                }
                free = arm;
            }
            contactArm = next;
            return false;
        }

        private bool FaceInGround(float rightArm)
        {
            Vector2 face = SpriteToWorld(PoseAt(rightArm), FacePixel);
            return Collision.SolidCollision(face - new Vector2(3f), 6, 6, true);
        }

        private void Smashed(Player owner)
        {
            if (!_smashEffectsDone)
            {
                _smashEffectsDone = true;
                SmashEffects(owner);
            }

            _lingerTimer++;
            // Отдача: оружие подпрыгивает от земли и ложится обратно
            float recoil = _lingerTimer < RecoilTicks
                ? (float)Math.Sin(MathHelper.Pi * _lingerTimer / RecoilTicks) * RecoilAngle
                : 0f;
            _armRotation = _smashArm - recoil;

            float linger = _lingerTimer / (float)LingerTicks;
            _animScale = linger > LingerShrinkFrom
                ? 1f - SoAEasing.CircIn((linger - LingerShrinkFrom) / (1f - LingerShrinkFrom))
                : 1f;

            if (_lingerTimer >= LingerTicks)
                Projectile.Kill();
        }

        private void SmashEffects(Player owner)
        {
            Vector2 face = SpriteToWorld(PoseAt(_smashArm), FacePixel);

            if (!Main.dedServ)
            {
                float volume = MathHelper.Lerp(0.6f, 1f, Charge);
                SoundEngine.PlaySound(SoundID.Item70 with { Volume = volume * 0.8f }, face);
                SoundEngine.PlaySound(SoundID.DD2_MonkStaffGroundImpact with { Volume = volume, Pitch = -0.2f }, face);

                if (Projectile.owner == Main.myPlayer)
                    ScreenShake.Punch(face, Vector2.UnitY,
                        2f + 4f * Charge, 6f, (int)(12 + 10 * Charge), 1000f, FullName);

                Collision.HitTiles(face - new Vector2(12f), new Vector2(0f, -3f), 24, 24);

                // Пыль стелется по земле в обе стороны; чем сильнее заряд — тем больше и дальше
                int clouds = 3 + (int)(6 * Charge);
                for (int i = 0; i < clouds; i++)
                {
                    var velocity = new Vector2(Main.rand.NextFloat(-1f, 1f) * (1.5f + 3f * Charge),
                        -Main.rand.NextFloat(0.3f, 1.2f));
                    SoAParticles.SpawnSmoke(face + new Vector2(Main.rand.NextFloat(-12f, 12f), 0f), velocity,
                        SmashDustColor, 10f, 30f + 24f * Charge, 0.45f, 45 + (int)(15 * Charge));
                }
            }

            OnSmash(owner, face);
        }

        private void PushTrail()
        {
            for (int i = Math.Min(_trailCount, TrailLength - 1); i > 0; i--)
            {
                _tipTrail[i] = _tipTrail[i - 1];
                _armTrail[i] = _armTrail[i - 1];
            }
            _tipTrail[0] = PixelToWorld(TipPixel);
            _armTrail[0] = _armRotation;
            _trailCount = Math.Min(_trailCount + 1, TrailLength);
        }

        #region Урон

        public override bool? CanDamage() => State == ClubState.Swinging ? null : false;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            ClubPose pose = PoseAt(_armRotation);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Owner.MountedCenter, SpriteToWorld(pose, TipPixel), HitWidth * pose.Scale, ref collisionPoint);
        }

        public override void CutTiles()
        {
            if (State != ClubState.Swinging)
                return;
            DelegateMethods.tilecut_0 = TileCuttingContext.AttackProjectile;
            Terraria.Utils.PlotTileLine(Owner.MountedCenter, PixelToWorld(TipPixel), HitWidth * DrawScale,
                DelegateMethods.CutTiles);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            modifiers.SourceDamage *= MathHelper.Lerp(MinDamageMult, FullDamageMult, Charge);
            modifiers.Knockback *= MathHelper.Lerp(MinKnockbackMult, FullKnockbackMult, Charge);
            modifiers.HitDirectionOverride = Direction;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            OnClubHit(target);
            if (Main.dedServ)
                return;

            // Линии удара веером по направлению от игрока
            Vector2 hitPoint = Vector2.Lerp(Projectile.Center, target.Center, 0.6f);
            Vector2 away = (hitPoint - Owner.MountedCenter).SafeNormalize(Vector2.UnitX * Direction);
            int lines = FullCharge ? 12 : 8;
            for (int i = 0; i < lines; i++)
            {
                float spread = Main.rand.NextFloat(-1f, 1f);
                float speed = MathHelper.Lerp(11f, 3f, Math.Abs(spread)) * (FullCharge ? 1.4f : 1f);
                SoAParticles.SpawnStreak(hitPoint + away.RotatedBy(MathHelper.PiOver2) * spread * 14f,
                    away.RotatedBy(spread * MathHelper.PiOver4) * speed, SoAVfx.Additive(ChargeColor), 2f, 0f, 16);
            }
            SoAParticles.SpawnSmoke(hitPoint, away * 3f, SmashDustColor, 12f, 34f, 0.35f, 30);
        }

        #endregion

        #region Сеть

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)_windupTimer);
            writer.Write((byte)_swingTimer);
            writer.Write((byte)_lingerTimer);
            writer.Write(_smashArm);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _windupTimer = reader.ReadByte();
            _swingTimer = reader.ReadByte();
            _lingerTimer = reader.ReadByte();
            _smashArm = reader.ReadSingle();
        }

        #endregion

        #region Отрисовка

        // Батч не переключаем: оружие держится через heldProj и рисуется внутри отрисовки игрока.
        // Свечение — цвет с A = 0, в обычном батче это уже аддитив
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Texture2D glow = GlowMask(tex);
            Vector2 gfxOffset = new(0f, Owner.gfxOffY);

            float trailFade = State switch
            {
                ClubState.Swinging => 1f,
                ClubState.Smashed => 1f - Math.Min(_lingerTimer / (float)TrailFadeTicks, 1f),
                _ => 0f,
            };
            if (trailFade > 0f)
            {
                DrawSwingTrail(trailFade);
                DrawAfterimages(glow, gfxOffset, trailFade);
            }

            ClubPose pose = PoseAt(_armRotation);
            Vector2 drawPos = pose.Grip + gfxOffset - Main.screenPosition;
            Vector2 origin = OriginFor(tex);

            if (RimStrength > 0f)
                DrawRim(glow, drawPos, origin, pose, RimStrength);

            Main.EntitySpriteDraw(tex, drawPos, null, lightColor, pose.Rotation, origin, pose.Scale, pose.Effects, 0);

            if (OverlayStrength > 0f)
                Main.EntitySpriteDraw(glow, drawPos, null, SoAVfx.Additive(ChargeColor) * OverlayStrength,
                    pose.Rotation, origin, pose.Scale, pose.Effects, 0);

            if (_flashTimer > 0)
            {
                float flash = SoAEasing.QuadIn(_flashTimer / (float)FlashTicks);
                Color color = SoAVfx.Additive(Color.Lerp(ChargeColor, Color.White, flash)) * (flash * 1.6f);
                Main.EntitySpriteDraw(glow, drawPos, null, color, pose.Rotation, origin, pose.Scale, pose.Effects, 0);
            }
            return false;
        }

        private void DrawRim(Texture2D glow, Vector2 drawPos, Vector2 origin, in ClubPose pose, float strength)
        {
            const int copies = 6;
            float pulse = 0.55f + 0.25f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 7f);
            Color rim = SoAVfx.Additive(ChargeColor) * (pulse * 0.45f * strength);
            for (int i = 0; i < copies; i++)
            {
                Vector2 offset = (MathHelper.TwoPi * i / copies).ToRotationVector2() * 2f;
                Main.EntitySpriteDraw(glow, drawPos + offset, null, rim, pose.Rotation, origin,
                    pose.Scale, pose.Effects, 0);
            }
        }

        private void DrawSwingTrail(float fade)
        {
            if (SwingTrailStyle is not TrailStyle style || _trailCount < 2)
                return;

            float headWidth = SwingTrailWidth * DrawScale * MathHelper.Lerp(0.6f, 1.2f, Charge);
            float strength = fade * MathHelper.Lerp(0.5f, 1f, Charge);
            SoATrail.Draw(_tipTrail.AsSpan(0, _trailCount),
                progress => headWidth * (float)Math.Pow(1f - progress, 0.8f),
                progress => SwingTrailColorAt(progress) * strength,
                style);
        }

        // Затухающие копии оружия на прошлых тиках удара: тяжёлый замах читается глазом
        private void DrawAfterimages(Texture2D glow, Vector2 gfxOffset, float fade)
        {
            Vector2 origin = OriginFor(glow);
            for (int i = Math.Min(_trailCount, AfterimageCount) - 1; i >= 1; i--)
            {
                float t = 1f - i / (float)AfterimageCount;
                ClubPose ghost = PoseAt(_armTrail[i]);
                Main.EntitySpriteDraw(glow, ghost.Grip + gfxOffset - Main.screenPosition, null,
                    SoAVfx.Additive(ChargeColor) * (t * t * 0.35f * fade), ghost.Rotation, origin,
                    ghost.Scale, ghost.Effects, 0);
            }
        }

        #endregion
    }
}
