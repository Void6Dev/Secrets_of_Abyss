using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Players;
using SoA.Common.Utils;
using SoA.Common.Weapons;
using SoA.Content.Items.Weapons;

namespace SoA.Content.Projectiles
{
    // Инферно-сюрикен в руке: весь цикл от зажатия до броска.
    //  1. Зарядка: сюрикен «выпрыгивает» в ладонь, рука отводится за голову по кривой,
    //     свободная рука тянется к цели. Сюрикен раскаляется и крутится тем быстрее,
    //     чем сильнее накал; перед окном идеального броска к нему сжимается кольцо-подсказка,
    //     в момент окна — белая вспышка силуэта и звёздный блик. Передержал — перегрев:
    //     дрожь, дым, багровое мерцание.
    //  2. Бросок: замах через голову к курсору; на середине дуги сюрикен срывается
    //     с ладони (InfernoShurikenProjectile), рука доводит движение до конца.
    // Вид броска решает момент отпускания: раньше окна — лёгкий, в окне — идеальный,
    // позже — перегрев. Стаки Жара и откат живут на игроке (InfernoShurikenPlayer).
    // ai[0] — HeldState, ai[1] — направление игрока, ai[2] — стаки Жара на момент замаха
    public class InfernoShurikenHeld : ModProjectile
    {
        private enum HeldState { Charging, Throwing }

        public const int PerfectWindowStart = 25;
        public const int PerfectWindowEnd = 40;
        public const int MaxCharge = 60;
        private const int RingLeadTicks = 18;        // кольцо-подсказка появляется за столько тиков до окна
        private const int GlintTicks = 8;
        private const int FlashTicks = 10;
        private const int PopTicks = 8;              // сюрикен «выпрыгивает» в ладонь
        private const int ThrowTicks = 11;
        private const float ReleaseAt = 0.35f;       // доля замаха, на которой сюрикен срывается с ладони

        private const int CooldownLight = 20;
        private const int CooldownPerfect = 80;      // пойманный сюрикен обнуляет
        private const int CooldownOverheat = 30;
        private const int OverheatBurnTicks = 120;

        private const float LightMinSpeed = 9f;
        private const float LightMaxSpeed = 17f;
        private const float PerfectSpeed = 21f;
        private const float OverheatSpeed = 24f;

        // Повороты рук для взгляда вправо: 0 — вниз, -π/2 — вперёд, -π — вверх
        private const float WindupArm = -MathHelper.PiOver4;
        private const float PulledBackArm = -MathHelper.Pi * 0.82f;
        private const float BackArmRest = -MathHelper.Pi * 0.2f;
        private const float BackArmReach = -MathHelper.Pi * 0.48f;   // свободная рука тянется к цели
        private const float BackArmThrown = MathHelper.Pi * 0.15f;   // и уходит назад при броске
        private const float FollowThrough = 0.4f;                     // рука доводит бросок дальше прицела
        private const float MinThrowArc = 0.6f;
        private const float StrainShake = 0.03f;

        // Отрисовка
        private const int RingSegments = 28;
        private const float RingStartRadius = 72f;
        private const float RingEndRadius = 16f;     // совпадает с краем спрайта
        private const float GlintLength = 96f;
        private const float HeatOrbitRadius = 22f;
        private const string HeldTexturePath = "SoA/Content/Items/Weapons/InfernoShuriken";

        private static readonly Color EmberColor = new(120, 22, 10);
        private static readonly Color HeatOrange = new(255, 120, 30);
        private static readonly Color WhiteHot = new(255, 236, 160);
        private static readonly Color OverheatRed = new(255, 60, 30);
        private static readonly Color SmokeColor = new(55, 40, 36);

        private int _chargeTime;
        private int _throwTimer;
        private float _releasedArm;
        private Vector2 _aim;
        private ShurikenThrow _kind;
        private float _lightRatio;
        private bool _launched;

        private float _arm;          // поворот передней руки для взгляда вправо
        private float _backArm;
        private float _spin;
        private int _flash;

        private HeldState State { get => (HeldState)Projectile.ai[0]; set => Projectile.ai[0] = (float)value; }
        private int Direction => Projectile.ai[1] < 0 ? -1 : 1;
        private int HeatStacks => (int)Projectile.ai[2];
        private Player Owner => Main.player[Projectile.owner];

        public override string Texture => HeldTexturePath;

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = false;   // бьёт брошенный сюрикен, а не этот
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 2;       // продлевается каждый тик, пока цикл не кончится
        }

        #region Накал

        // Отвод руки: 0..1 к началу окна и дальше держится
        private static float ChargeT(int ct) => Math.Min(ct / (float)PerfectWindowStart, 1f);

        // Перегрев: 0 до конца окна, 1 на полной передержке
        private static float OverheatT(int ct)
            => ct <= PerfectWindowEnd ? 0f : Math.Min((ct - PerfectWindowEnd) / (float)(MaxCharge - PerfectWindowEnd), 1f);

        private static bool IsPerfectWindow(int ct) => ct >= PerfectWindowStart && ct <= PerfectWindowEnd;

        // Цвет накала: тёмно-красный → оранжевый → бело-жёлтый в окне → багровое мерцание перегрева
        private static Color HeatColor(int ct)
        {
            if (ct < PerfectWindowStart)
                return Color.Lerp(EmberColor, HeatOrange, ct / (float)PerfectWindowStart);
            if (ct <= PerfectWindowEnd)
                return WhiteHot;
            float flicker = 0.5f + 0.5f * MathF.Sin(Main.GameUpdateCount * 0.9f);
            return Color.Lerp(WhiteHot, OverheatRed, OverheatT(ct) * (0.55f + 0.45f * flicker));
        }

        #endregion

        private Vector2 HandPosition
            => Owner.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, HeldProjectiles.Mirror(_arm, Direction));

        public override void AI()
        {
            Player owner = Owner;
            bool holdingShuriken = owner.HeldItem.type == ModContent.ItemType<InfernoShuriken>();
            if (!owner.active || owner.dead || owner.CCed || owner.noItems || (State == HeldState.Charging && !holdingShuriken))
            {
                Projectile.Kill();
                return;
            }

            Projectile.timeLeft = 2;
            HeldProjectiles.Hold(Projectile, owner, Direction);
            if (_flash > 0)
                _flash--;

            if (State == HeldState.Charging)
                Charging(owner);
            else
                Throwing(owner);

            if (!Projectile.active)
                return;

            HeldProjectiles.SetArms(owner, Direction, _arm, _backArm);
            Projectile.Center = HandPosition;
        }

        #region Зарядка

        private void Charging(Player owner)
        {
            if (_chargeTime < MaxCharge)
                _chargeTime++;

            float t = ChargeT(_chargeTime);
            float overheat = OverheatT(_chargeTime);
            float pull = SoAEasing.CircOut(t);
            _arm = MathHelper.Lerp(WindupArm, PulledBackArm, pull);
            _backArm = MathHelper.Lerp(BackArmRest, BackArmReach, pull);

            // Натуга: рука мелко дрожит, при перегреве — ходит ходуном
            _arm += (float)Math.Sin(_chargeTime * 1.9f) * StrainShake * t;
            if (overheat > 0f)
                _arm += Main.rand.NextFloatDirection() * 0.06f * overheat;

            // Крутится в ладони: медленно на старте, быстро в окне, рвано при перегреве
            _spin += 0.04f + 0.3f * t * t + (IsPerfectWindow(_chargeTime) ? 0.15f : 0f) + 0.2f * overheat;

            if (_chargeTime == PerfectWindowStart)
                _flash = FlashTicks;

            if (!Main.dedServ)
                ChargeEffects(t, overheat);

            if (Projectile.owner != Main.myPlayer)
                return;

            // Пока заряжаешь, можно развернуться к курсору
            int facing = Main.MouseWorld.X >= owner.MountedCenter.X ? 1 : -1;
            if (facing != Direction)
            {
                Projectile.ai[1] = facing;
                Projectile.netUpdate = true;
            }

            if (!owner.channel)
                Release(owner);
        }

        // Отпустил: вид броска по моменту, откат и Жар — на игроке. Сам бросок — в Throwing
        private void Release(Player owner)
        {
            var shurikenPlayer = owner.GetModPlayer<InfernoShurikenPlayer>();
            int ct = _chargeTime;

            if (ct < PerfectWindowStart)
            {
                _kind = ShurikenThrow.Light;
                _lightRatio = ct / (float)PerfectWindowStart;
                shurikenPlayer.cooldown = CooldownLight;
                shurikenPlayer.ResetHeat(); // промах по окну сбрасывает Жар
            }
            else if (ct <= PerfectWindowEnd)
            {
                _kind = ShurikenThrow.Perfect;
                shurikenPlayer.cooldown = CooldownPerfect;
            }
            else
            {
                _kind = ShurikenThrow.Overheat;
                shurikenPlayer.cooldown = CooldownOverheat;
                shurikenPlayer.ResetHeat();
                owner.AddBuff(BuffID.OnFire, OverheatBurnTicks); // передержал — обжёгся
            }

            _aim = (Main.MouseWorld - owner.MountedCenter).SafeNormalize(Vector2.UnitX * Direction);
            Projectile.ai[1] = _aim.X >= 0f ? 1 : -1;
            _releasedArm = _arm;
            _throwTimer = 0;
            State = HeldState.Throwing;
            Projectile.netUpdate = true;
        }

        private void ChargeEffects(float t, float overheat)
        {
            Vector2 hand = HandPosition;
            Color color = HeatColor(_chargeTime);
            Lighting.AddLight(hand, color.ToVector3() * (0.3f + 0.9f * t));

            if (_chargeTime == PerfectWindowStart)
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
            if (_chargeTime == PerfectWindowEnd + 1)
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
            else if (Main.GameUpdateCount % 3 == 0 && _chargeTime > 4)
            {
                // Жар стягивается к руке, в окне — искры отлетают от белого накала
                if (IsPerfectWindow(_chargeTime))
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

        #endregion

        #region Бросок

        // Замах через голову к прицелу; на ReleaseAt сюрикен срывается с ладони
        private void Throwing(Player owner)
        {
            _throwTimer++;
            float p = Math.Min(_throwTimer / (float)ThrowTicks, 1f);

            float target = Math.Max(AimArm(_aim) + FollowThrough, _releasedArm + MinThrowArc);
            _arm = MathHelper.Lerp(_releasedArm, target, SoAEasing.QuadOut(p));
            _backArm = MathHelper.Lerp(BackArmReach, BackArmThrown, SoAEasing.QuadOut(p));
            _spin += 0.6f;

            if (!_launched && p >= ReleaseAt)
            {
                _launched = true;
                Launch(owner);
            }

            if (_throwTimer >= ThrowTicks)
                Projectile.Kill();
        }

        // Поворот руки (для взгляда вправо), при котором она смотрит вдоль прицела
        private float AimArm(Vector2 aim)
            => new Vector2(aim.X * Direction, aim.Y).ToRotation() - MathHelper.PiOver2;

        private void Launch(Player owner)
        {
            Vector2 hand = HandPosition;
            if (!Main.dedServ)
            {
                // Хлёсткий росчерк от ладони по ходу броска
                Color color = _kind switch
                {
                    ShurikenThrow.Perfect => WhiteHot,
                    ShurikenThrow.Overheat => OverheatRed,
                    _ => HeatOrange,
                };
                for (int i = 0; i < 6; i++)
                {
                    Vector2 velocity = _aim.RotatedByRandom(0.35f) * Main.rand.NextFloat(4f, 9f);
                    SoAParticles.SpawnStreak(hand, velocity, SoAVfx.Additive(color), 2f, gravity: 0f, life: 12, lengthPerSpeed: 2.4f);
                }
            }

            if (Projectile.owner != Main.myPlayer)
                return;

            // Из ладони, а не из центра игрока; ладонь в стене — тогда из центра
            Vector2 from = Collision.CanHitLine(owner.MountedCenter, 1, 1, hand, 1, 1) ? hand : owner.MountedCenter;
            float speed = _kind switch
            {
                ShurikenThrow.Light => MathHelper.Lerp(LightMinSpeed, LightMaxSpeed, _lightRatio),
                ShurikenThrow.Perfect => PerfectSpeed,
                _ => OverheatSpeed,
            };
            int heat = _kind == ShurikenThrow.Perfect ? owner.GetModPlayer<InfernoShurikenPlayer>().heat : 0;
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), from, _aim * speed,
                ModContent.ProjectileType<InfernoShurikenProjectile>(), Projectile.damage, Projectile.knockBack,
                Projectile.owner, (float)_kind, _lightRatio, heat);
        }

        #endregion

        #region Сеть

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)_chargeTime);
            writer.Write((byte)_throwTimer);
            writer.Write((byte)_kind);
            writer.Write(_lightRatio);
            writer.Write(_releasedArm);
            writer.WriteVector2(_aim);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _chargeTime = reader.ReadByte();
            _throwTimer = reader.ReadByte();
            _kind = (ShurikenThrow)reader.ReadByte();
            _lightRatio = reader.ReadSingle();
            _releasedArm = reader.ReadSingle();
            _aim = reader.ReadVector2();
        }

        #endregion

        #region Отрисовка

        // Сюрикен светится сам: цвет не зависит от освещения. Батч не переключаем — снаряд
        // держится через heldProj; свечение — цвет с A = 0, в обычном батче это аддитив
        public override bool PreDraw(ref Color lightColor)
        {
            if (_launched)
                return false;

            int charge = _chargeTime;
            float t = ChargeT(charge);
            float overheat = OverheatT(charge);
            bool inWindow = IsPerfectWindow(charge);
            Color heat = HeatColor(charge);
            float pop = SoAEasing.BackOut(Math.Min(charge / (float)PopTicks, 1f));

            Vector2 hand = HandPosition + new Vector2(0f, Owner.gfxOffY);
            if (overheat > 0f)
                hand += Main.rand.NextVector2Circular(1f + 2f * overheat, 1f + 2f * overheat);
            Vector2 drawPos = hand - Main.screenPosition;

            Texture2D texNormal = ModContent.Request<Texture2D>(HeldTexturePath).Value;
            Texture2D texActive = ModContent.Request<Texture2D>(InfernoShurikenProjectile.ActiveTexturePath).Value;
            Texture2D glow = SoAVfx.SoftGlow;
            Vector2 origin = texNormal.Size() / 2f;
            float rotation = HeldProjectiles.Mirror(_arm, Direction) + MathHelper.PiOver4 * Direction + _spin * Direction;

            // Ореол накала за спрайтом
            float pulse = inWindow ? 0.85f + 0.15f * MathF.Sin(Main.GameUpdateCount * 0.4f) : 1f;
            float haloSize = (34f + 18f * t + 14f * overheat) * pulse * pop;
            Main.EntitySpriteDraw(glow, drawPos, null, SoAVfx.Additive(heat * (0.25f + 0.45f * t)), 0f, glow.Size() / 2f,
                haloSize / glow.Width, SpriteEffects.None, 0);

            DrawTimingRing(drawPos, charge);

            if (t < 1f)
                Main.EntitySpriteDraw(texNormal, drawPos, null, Color.White * (1f - t), rotation, origin, pop, SpriteEffects.None, 0);
            if (t > 0f)
                Main.EntitySpriteDraw(texActive, drawPos, null, Color.White * t, rotation, origin, pop, SpriteEffects.None, 0);

            // Накал поверх спрайта: в окне — белое свечение, при перегреве — мерцание
            float overlay = inWindow ? 0.35f * pulse : 0.2f * t + 0.45f * overheat;
            if (overlay > 0f)
                Main.EntitySpriteDraw(texActive, drawPos, null, SoAVfx.Additive(heat * overlay), rotation, origin, 1.08f * pop,
                    SpriteEffects.None, 0);

            // Окно открылось — силуэт вспыхивает белым
            if (_flash > 0)
            {
                float flash = SoAEasing.QuadIn(_flash / (float)FlashTicks);
                Main.EntitySpriteDraw(SilhouetteCache.Get(texActive), drawPos, null, SoAVfx.Additive(Color.White) * (flash * 1.4f),
                    rotation, origin, pop, SpriteEffects.None, 0);
            }

            DrawGlint(drawPos, charge);
            DrawHeatStacks(drawPos, HeatStacks, heat);
            return false;
        }

        // Кольцо сжимается к спрайту и касается его ровно в момент открытия окна:
        // игрок видит, КОГДА откроется окно, а не реагирует на звук постфактум
        private static void DrawTimingRing(Vector2 center, int charge)
        {
            int ringStart = PerfectWindowStart - RingLeadTicks;
            if (charge < ringStart || charge >= PerfectWindowStart)
                return;

            float p = (charge - ringStart) / (float)RingLeadTicks;
            float radius = MathHelper.Lerp(RingStartRadius, RingEndRadius, p * p);
            Color color = SoAVfx.Additive(Color.Lerp(new Color(255, 110, 30), new Color(255, 236, 170), p) * (0.2f + 0.7f * p));
            float thickness = MathHelper.Lerp(2f, 4f, p);

            Texture2D streak = SoAVfx.SoftStreak;
            float segment = MathHelper.TwoPi * radius / RingSegments + 3f;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = MathHelper.TwoPi * i / RingSegments + Main.GameUpdateCount * 0.05f;
                Vector2 pos = center + angle.ToRotationVector2() * radius;
                Main.EntitySpriteDraw(streak, pos, null, color, angle + MathHelper.PiOver2, streak.Size() / 2f,
                    new Vector2(segment / streak.Width, thickness / streak.Height), SpriteEffects.None, 0);
            }
        }

        // Звёздный блик крестом в момент открытия окна
        private static void DrawGlint(Vector2 center, int charge)
        {
            int since = charge - PerfectWindowStart;
            if (since < 0 || since >= GlintTicks)
                return;

            float g = 1f - since / (float)GlintTicks;
            g *= g;
            Texture2D streak = SoAVfx.SoftStreak;
            Color color = SoAVfx.Additive(new Color(255, 245, 210) * g);
            Vector2 origin = streak.Size() / 2f;
            float length = 20f + GlintLength * g;
            float thickness = 2f + 6f * g;
            float spin = since * 0.06f;
            var big = new Vector2(length / streak.Width, thickness / streak.Height);
            var small = new Vector2(length * 0.45f / streak.Width, thickness * 0.7f / streak.Height);

            Main.EntitySpriteDraw(streak, center, null, color, spin, origin, big, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(streak, center, null, color, spin + MathHelper.PiOver2, origin, big, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(streak, center, null, color * 0.5f, spin + MathHelper.PiOver4, origin, small, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(streak, center, null, color * 0.5f, spin - MathHelper.PiOver4, origin, small, SpriteEffects.None, 0);
        }

        // Стаки Жара — угольки на орбите вокруг сюрикена
        private static void DrawHeatStacks(Vector2 center, int stacks, Color heat)
        {
            if (stacks <= 0)
                return;

            Texture2D glow = SoAVfx.SoftGlow;
            for (int i = 0; i < stacks; i++)
            {
                float angle = Main.GameUpdateCount * 0.12f + MathHelper.TwoPi * i / stacks;
                Vector2 pos = center + angle.ToRotationVector2() * HeatOrbitRadius;
                Main.EntitySpriteDraw(glow, pos, null, SoAVfx.Additive(Color.Lerp(heat, Color.White, 0.4f) * 0.9f), 0f,
                    glow.Size() / 2f, 12f / glow.Width, SpriteEffects.None, 0);
            }
        }

        #endregion
    }
}
