using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Utils;
using SoA.Common.Weapons;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Удар Когтей Лавовой Тени: коготь рывком проходит дугу к курсору, попеременно правой
    // и левой рукой, и оставляет три параллельных раскалённых следа. Каждый четвёртый удар —
    // тяжёлый: дуга шире, дальше и ярче, коготь вспыхивает белым. Попадание ставит лавовую
    // метку (LavaExplosionGlobalNPC). Свободная рука держит стойку у груди, бьющая отскакивает
    // от цели при попадании и плавно опускается после удара; за когтем — огненные копии.
    //   ai[0] — сторона взмаха (±1), ai[1] — 1 для тяжёлого, ai[2] — угол прицела
    public class LavaClawSlash : ModProjectile
    {
        public override string Texture => "SoA/Content/Items/Weapons/ClawsOfLavaShadow";

        // ---------- ДУГА ----------
        private const int SlashUpdates = 16;          // 8 тиков × 2 подшага: быстрый рывок, дуга без «лесенки»
        private const float ArcHalf = 1.0f;           // полураствор дуги, рад
        private const float ArcHalfHeavy = 1.3f;
        private const float Reach = 60f;              // от центра игрока до кончиков когтей
        private const float ReachHeavy = 78f;
        private const float HandOffset = 14f;         // где кисть: от центра к кончику
        private const float ActiveFrom = 0.12f;       // доля дуги, на которой коготь режет
        private const float ActiveTo = 0.8f;
        private const float HitLineWidth = 22f;

        private const int LingerUpdates = 12;         // после удара царапины ещё 6 тиков висят и остывают
        private const int ReturnUpdates = 8;          // бьющая рука плавно опускается после удара

        // ---------- РУКИ ----------
        private const float GuardArm = 0.5f;          // свободная рука у груди, локоть назад (для взгляда вправо)
        private const float RecoilReach = 0.18f;      // отскок от цели: доля вылета
        private const float RecoilDecay = 0.82f;

        // ---------- ВИД ----------
        private const int TrailLength = SlashUpdates; // след лежит на всей дуге удара, а не только за когтем
        private const float MarkSpacing = 9f;         // расстояние между тремя следами когтей
        private const float MarkFan = 0.35f;          // к концу царапины расходятся веером, как пальцы
        private const float MarkWidth = 8f;
        private const float MarkWidthHeavy = 10.5f;
        private const float CoolUpdates = 10f;        // за сколько подшагов участок остывает до красного
        private static readonly Color ScorchColor = new(70, 12, 6); // тёмная подложка: след читается и на светлом
        private const float ClawScale = 1.45f;
        private const float ClawScaleHeavy = 1.75f;
        private static readonly Vector2 WristOrigin = new(6f, 22f); // запястье на спрайте; когти смотрят вправо-вверх
        private const float SpriteForwardAngle = -MathHelper.PiOver4;
        private const int PopUpdates = 3;             // коготь «выпрыгивает» в начале взмаха
        private const int GhostCount = 3;             // огненные копии когтя по дуге
        private const int HeavyFlashUpdates = 8;

        private static readonly Color HotWhite = new(255, 240, 190);
        private static readonly Color LavaOrange = new(255, 140, 40);
        private static readonly Color DeepRed = new(180, 30, 15);

        // ---------- МЕТКА ----------
        public const int MarkTicks = 30; // сколько после последнего удара метка ждёт взрыва

        private float Side => Projectile.ai[0] >= 0f ? 1f : -1f;
        private bool Heavy => Projectile.ai[1] == 1f;
        private float Aim => Projectile.ai[2];
        private ref float Age => ref Projectile.localAI[0];

        // След: (угол, радиус) подшагов, [0] — самый свежий; _trailBorn — когда записан
        // (для остывания). Точки считаем от текущего центра игрока, поэтому след едет
        // вместе с ним и не отрывается на беге
        private readonly Vector2[] _trail = new Vector2[TrailLength];
        private readonly float[] _trailBorn = new float[TrailLength];
        private int _trailCount;

        private float _recoil;        // 1 сразу после попадания, гаснет
        private int _flash;           // вспышка тяжёлого удара

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.ownerHitCheck = true;       // сквозь стену не режет
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;   // один взмах — одно попадание по каждому
            Projectile.extraUpdates = 1;
            Projectile.timeLeft = SlashUpdates + LingerUpdates;
        }

        private float Progress => MathHelper.Clamp(Age / SlashUpdates, 0f, 1f);
        private bool Lingering => Age > SlashUpdates;

        // 1 во время удара, дальше царапины гаснут
        private float MarkFade => Lingering ? MathHelper.Clamp(1f - (Age - SlashUpdates) / LingerUpdates, 0f, 1f) : 1f;

        // Медленный старт, быстрая середина, мягкий дохлёст — взмах, а не поворот стрелки
        private static float EaseSlash(float t) => t * t * t * (t * (6f * t - 15f) + 10f);

        private float AngleAt(float t)
        {
            float arc = Heavy ? ArcHalfHeavy : ArcHalf;
            return Aim + Side * arc * (1f - 2f * EaseSlash(t));
        }

        // Рука выбрасывается вперёд в середине взмаха и подбирается к концу
        private float ReachAt(float t) => (Heavy ? ReachHeavy : Reach) * (0.82f + 0.28f * (float)Math.Sin(t * MathHelper.Pi));

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (!owner.active || owner.dead)
            {
                Projectile.Kill();
                return;
            }

            if (Age == 0f)
                OnSlashStart();
            Age++;
            _recoil *= RecoilDecay;
            if (_flash > 0)
                _flash--;

            // Удар закончен: коготь не режет, царапины остывают, рука плавно опускается
            if (Lingering)
            {
                Projectile.friendly = false;
                Projectile.Center = owner.MountedCenter;
                Projectile.velocity = Vector2.Zero;
                ReturnArm(owner);
                return;
            }

            float t = Progress;
            float angle = AngleAt(t);
            float reach = ReachAt(t) * (1f - RecoilReach * _recoil);

            Projectile.Center = owner.MountedCenter;
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation = angle;
            Projectile.friendly = t >= ActiveFrom && t <= ActiveTo;

            // Игрок смотрит туда, куда бьёт; руки чередуются вместе со стороной взмаха,
            // свободная рука собирается в стойку у груди
            owner.heldProj = Projectile.whoAmI;
            owner.ChangeDir(Math.Cos(Aim) >= 0.0 ? 1 : -1);
            SetStrikingArm(owner, angle - MathHelper.PiOver2);
            float guard = HeldProjectiles.Mirror(GuardArm * SoAEasing.CircOut(Math.Min(Age / (float)PopUpdates, 1f)), owner.direction);
            if (Side > 0f)
                owner.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Quarter, guard);
            else
                owner.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Quarter, guard);

            PushTrail(angle, reach);

            if (Main.dedServ)
                return;

            Vector2 tip = owner.MountedCenter + angle.ToRotationVector2() * reach;
            Lighting.AddLight(tip, LavaOrange.ToVector3() * (Heavy ? 0.9f : 0.6f));
            if (Projectile.friendly && Main.rand.NextBool(Heavy ? 1 : 2))
            {
                Vector2 sweep = (angle - Side * MathHelper.PiOver2).ToRotationVector2();
                SoAParticles.SpawnStreak(tip + Main.rand.NextVector2Circular(8f, 8f),
                    sweep * Main.rand.NextFloat(1.5f, 4f) + new Vector2(0f, -Main.rand.NextFloat(0.5f, 1.5f)),
                    Color.Lerp(LavaOrange, HotWhite, Main.rand.NextFloat(0.4f)), Main.rand.NextFloat(1.8f, 3f),
                    gravity: -0.03f, life: Main.rand.Next(14, 24), lengthPerSpeed: 2.2f);
            }
        }

        private void SetStrikingArm(Player owner, float rotation)
        {
            if (Side > 0f)
                owner.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, rotation);
            else
                owner.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Full, rotation);
        }

        // После удара бьющая рука опускается к телу, а не падает рывком. Только если это
        // последний взмах: следующий уже сам ведёт руки
        private void ReturnArm(Player owner)
        {
            float since = Age - SlashUpdates;
            if (since > ReturnUpdates || _trailCount == 0 || owner.ownedProjectileCounts[Type] > 1)
                return;

            float from = _trail[0].X - MathHelper.PiOver2;
            float rest = HeldProjectiles.Mirror(GuardArm * 0.4f, owner.direction);
            SetStrikingArm(owner, from.AngleLerp(rest, SoAEasing.QuadOut(since / ReturnUpdates)));
        }

        private void OnSlashStart()
        {
            if (Heavy)
                _flash = HeavyFlashUpdates;
            if (Main.dedServ)
                return;

            // Тяжёлый удар: кольцо искр вокруг кисти на замахе
            if (Heavy)
            {
                Vector2 wrist = Main.player[Projectile.owner].MountedCenter + AngleAt(0f).ToRotationVector2() * HandOffset;
                for (int i = 0; i < 12; i++)
                {
                    Vector2 dir = (MathHelper.TwoPi * i / 12f).ToRotationVector2();
                    SoAParticles.SpawnStreak(wrist + dir * 6f, dir * Main.rand.NextFloat(2.5f, 5f), HotWhite, 2f,
                        gravity: 0f, life: 12, lengthPerSpeed: 2f);
                }
            }
            SoundEngine.PlaySound((Heavy ? SoundID.Item74 : SoundID.Item71) with
            {
                Pitch = Heavy ? -0.35f : 0.15f + Side * 0.12f,
                PitchVariance = 0.1f,
                Volume = Heavy ? 0.7f : 0.55f,
                MaxInstances = 3,
            }, Projectile.Center);
        }

        private void PushTrail(float angle, float reach)
        {
            for (int i = Math.Min(_trailCount, TrailLength - 1); i > 0; i--)
            {
                _trail[i] = _trail[i - 1];
                _trailBorn[i] = _trailBorn[i - 1];
            }
            _trail[0] = new Vector2(angle, reach);
            _trailBorn[0] = Age;
            _trailCount = Math.Min(_trailCount + 1, TrailLength);
        }

        // Режет всё, через что прошли когти: текущее положение, прошлое и середина между ними
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (!Projectile.friendly || _trailCount == 0)
                return false;

            Vector2 center = Main.player[Projectile.owner].MountedCenter;
            float previous = _trailCount > 1 ? _trail[1].X : _trail[0].X;
            float[] angles = { _trail[0].X, previous, (_trail[0].X + previous) * 0.5f };
            float collisionPoint = 0f;
            foreach (float angle in angles)
            {
                Vector2 tip = center + angle.ToRotationVector2() * _trail[0].Y;
                if (Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), center, tip,
                        HitLineWidth, ref collisionPoint))
                    return true;
            }
            return false;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
            => modifiers.HitDirectionOverride = Main.player[Projectile.owner].direction;

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            target.AddBuff(ModContent.BuffType<LavaExplosionDebuff>(), MarkTicks);
            target.GetGlobalNPC<LavaExplosionGlobalNPC>().AddMark(Projectile.owner, Projectile.damage);
            _recoil = 1f;   // рука отскакивает от цели
            HitSparks(target);

            // Тяжёлый удар отдаётся в камеру: один короткий толчок на каждый четвёртый удар
            if (Heavy && Projectile.owner == Main.myPlayer)
                ScreenShake.Punch(target.Center, (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX),
                    2.5f, 8f, 8, 700f, "SoA:LavaClawHeavy");
        }

        // Искры по ходу взмаха, вспышка и шипение раскалённого металла
        private void HitSparks(NPC target)
        {
            if (Main.dedServ)
                return;

            Vector2 at = Vector2.Lerp(target.Center, Projectile.Center, 0.25f);
            Vector2 sweep = (Projectile.rotation - Side * MathHelper.PiOver2).ToRotationVector2();
            int count = Heavy ? 16 : 10;
            for (int i = 0; i < count; i++)
            {
                SoAParticles.SpawnStreak(at, sweep.RotatedByRandom(0.8f) * Main.rand.NextFloat(4f, 11f),
                    Color.Lerp(LavaOrange, HotWhite, Main.rand.NextFloat(0.6f)), Main.rand.NextFloat(2f, 3.2f),
                    gravity: 0.15f, life: Main.rand.Next(14, 26), lengthPerSpeed: 2.4f);
            }
            SoAParticles.SpawnGlow(at, Vector2.Zero, LavaOrange * 0.8f, 30f, Heavy ? 120f : 85f, 10);
            SoAParticles.AddLight(at, LavaOrange, Heavy ? 1.8f : 1.2f, 8);
            SoundEngine.PlaySound(SoundID.Item20 with { Pitch = 0.2f, PitchVariance = 0.2f, Volume = 0.45f, MaxInstances = 4 }, at);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (_trailCount == 0)
                return false;

            SpriteBatch sb = Main.spriteBatch;
            Vector2 center = Main.player[Projectile.owner].MountedCenter;
            float fade = MarkFade;

            // Тёмная подложка под царапинами — обычным смешиванием, до свечения
            if (_trailCount > 1)
                DrawClawMarks(sb, center, fade, MarkLayer.Scorch);

            SoAVfx.BeginAdditive(sb);
            if (_trailCount > 1)
            {
                DrawClawMarks(sb, center, fade, MarkLayer.Glow);
                DrawClawMarks(sb, center, fade, MarkLayer.Core);
            }

            Texture2D tex = TextureAssets.Projectile[Type].Value;
            float pop = SoAEasing.BackOut(Math.Min(Age / PopUpdates, 1f));

            // Жар вокруг когтя, огненные копии по дуге и светящийся двойник под спрайтом —
            // только пока идёт удар
            if (!Lingering)
            {
                Vector2 tip = TrailPoint(center, _trail[0], 0f);
                SoAVfx.DrawTintedGlow(sb, tip, new Vector2(Heavy ? 120f : 85f), LavaOrange * 0.55f);
                for (int i = Math.Min(_trailCount, GhostCount + 1) - 1; i >= 1; i--)
                {
                    float ghost = 1f - i / (float)(GhostCount + 1);
                    DrawClaw(sb, tex, center, _trail[i].X, LavaOrange * (ghost * ghost * 0.6f), pop);
                }
                DrawClaw(sb, tex, center, _trail[0].X, LavaOrange * 0.5f, 1.12f * pop);
            }
            SoAVfx.EndAdditive(sb);

            // Сам коготь — раскалённый, свет мира ему не нужен
            if (!Lingering)
            {
                DrawClaw(sb, tex, center, _trail[0].X, Color.White, pop);
                if (_flash > 0)
                {
                    float flash = SoAEasing.QuadIn(_flash / (float)HeavyFlashUpdates);
                    DrawClaw(sb, SilhouetteCache.Get(tex), center, _trail[0].X,
                        SoAVfx.Additive(Color.White) * (flash * 1.2f), pop);
                }
            }
            return false;
        }

        private enum MarkLayer { Scorch, Glow, Core }

        // Три царапины когтей. Каждая — веретено: острая на концах, толстая в середине.
        // Средняя — во всю дугу, крайние короче и начинаются позже, к концу все три расходятся
        // веером. Цвет — по возрасту участка: свежий белый, затем оранжевый, остывший красный
        private void DrawClawMarks(SpriteBatch sb, Vector2 center, float fade, MarkLayer layer)
        {
            float spacing = MarkSpacing * (Heavy ? 1.25f : 1f);
            float maxWidth = Heavy ? MarkWidthHeavy : MarkWidth;
            int last = _trailCount - 1;

            for (int mark = -1; mark <= 1; mark++)
            {
                // Где на дуге (0 — начало, 1 — коготь) лежит эта царапина
                float from = mark == 0 ? 0f : 0.14f;
                float to = mark == 0 ? 1f : 0.9f;
                float markWidth = maxWidth * (mark == 0 ? 1.15f : 0.85f);
                Vector2 head = Vector2.Zero;
                float headHeat = 0f;

                for (int i = 0; i < last; i++)
                {
                    float sA = 1f - i / (float)last;         // i — новее, ближе к когтю
                    float sB = 1f - (i + 1) / (float)last;
                    float uA = (sA - from) / (to - from);
                    float uB = (sB - from) / (to - from);
                    if ((uA < 0f && uB < 0f) || (uA > 1f && uB > 1f))
                        continue;
                    uA = MathHelper.Clamp(uA, 0f, 1f);
                    uB = MathHelper.Clamp(uB, 0f, 1f);

                    Vector2 a = TrailPoint(center, _trail[i], mark * spacing * (1f - MarkFan + MarkFan * uA));
                    Vector2 b = TrailPoint(center, _trail[i + 1], mark * spacing * (1f - MarkFan + MarkFan * uB));
                    float u = (uA + uB) * 0.5f;
                    float profile = (float)Math.Pow(Math.Sin(u * MathHelper.Pi), 0.6);
                    float width = markWidth * profile;
                    if (width < 0.4f)
                        continue;

                    float heat = 1f - MathHelper.Clamp((Age - _trailBorn[i]) / CoolUpdates, 0f, 1f);
                    if (uA >= 0.999f || head == Vector2.Zero)
                    {
                        head = a;
                        headHeat = heat;
                    }

                    Vector2 segment = b - a;
                    float length = segment.Length() + 3f;
                    float rotation = segment.ToRotation();
                    Vector2 middle = (a + b) * 0.5f;

                    switch (layer)
                    {
                        case MarkLayer.Scorch:
                            SoAVfx.DrawTintedQuad(sb, middle, new Vector2(length, width * 1.7f), rotation,
                                ScorchColor * (0.4f * fade));
                            break;
                        case MarkLayer.Glow:
                            SoAVfx.DrawTintedQuad(sb, middle, new Vector2(length, width * 1.3f), rotation,
                                MarkColor(heat) * (0.85f * fade));
                            break;
                        default:
                            SoAVfx.DrawTintedQuad(sb, middle, new Vector2(length, width * 0.4f), rotation,
                                HotWhite * ((float)Math.Pow(heat, 1.5) * fade));
                            break;
                    }
                }

                // Искра на кончике каждой царапины, пока он свежий
                if (layer == MarkLayer.Core && head != Vector2.Zero && headHeat > 0.05f)
                    SoAVfx.DrawTintedGlow(sb, head, new Vector2(18f + 10f * headHeat), HotWhite * (headHeat * fade));
            }
        }

        private static Vector2 TrailPoint(Vector2 center, Vector2 sample, float radiusOffset)
            => center + sample.X.ToRotationVector2() * (sample.Y + radiusOffset);

        // Остывание: 1 — только что прочерчен (белый), 0 — остыл (тёмно-красный)
        private static Color MarkColor(float heat) => heat > 0.6f
            ? Color.Lerp(LavaOrange, HotWhite, (heat - 0.6f) / 0.4f)
            : Color.Lerp(DeepRed, LavaOrange, heat / 0.6f);

        // Коготь в кисти: запястье на руке, кончики — по направлению взмаха (angle)
        private void DrawClaw(SpriteBatch sb, Texture2D tex, Vector2 center, float angle, Color color, float scaleMult)
        {
            bool flipped = Main.player[Projectile.owner].direction == -1;

            // Отражённый спрайт смотрит вверх-влево — доворот считаем от его «вперёд»
            float forward = flipped ? MathHelper.Pi - SpriteForwardAngle : SpriteForwardAngle;
            float rotation = angle - forward;
            Vector2 origin = flipped ? new Vector2(tex.Width - WristOrigin.X, WristOrigin.Y) : WristOrigin;
            float scale = (Heavy ? ClawScaleHeavy : ClawScale) * scaleMult;
            Vector2 wrist = center + angle.ToRotationVector2() * HandOffset;

            Main.EntitySpriteDraw(tex, wrist - Main.screenPosition, null, color, rotation, origin, scale,
                flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }
    }
}
