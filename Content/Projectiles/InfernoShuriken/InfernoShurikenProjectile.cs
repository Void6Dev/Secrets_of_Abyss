using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Common.Utils;
using SoA.Content.Buffs;
using SoA.Content.Items.Weapons;

namespace SoA.Content.Projectiles
{
    public enum ShurikenThrow
    {
        Light,      // отпущен до окна
        Perfect,    // отпущен в окне
        Overheat,   // передержан
    }

    // Инферно-сюрикен. Вид броска задаёт ShurikenChargePlayer:
    //   Light    — дугой; рикошетит между врагами (1–3 прыжка по заряду), от стен отскакивает;
    //   Perfect  — почти прямо; вгрызается в первую цель, пилит её, вырывается со взрывом
    //              и летит обратно. Пойманный сбрасывает откат и даёт стак Жара;
    //   Overheat — прямо, пробивает врагов насквозь, у стены взрывается.
    // Решения (попадание, вырывание, поимка) принимает владелец и рассылает через netUpdate.
    //   ai[0] — ShurikenThrow, ai[1] — доля заряда лёгкого броска, ai[2] — стаки Жара при броске
    public class InfernoShurikenProjectile : ModProjectile
    {
        public const string TexturePath = "SoA/Content/Projectiles/InfernoShuriken/InfernoShurikenProjectile";
        public const string ActiveTexturePath = TexturePath + "_active";
        public override string Texture => TexturePath;

        private enum Phase : byte
        {
            Flying,
            HitStop,    // замер в момент идеального попадания — удар ощущается тяжелее
            Grinding,   // застрял в цели и пилит её
            Returning,
        }

        // ---------- Полёт ----------
        private const float MaxRange = 1500f;
        private const float MaxFallSpeed = 18f;
        private const float LightMaxGravity = 0.35f;
        private const float LightMinGravity = 0.12f;
        private const float PerfectGravity = 0.04f;
        private const int TrailLength = 14;

        // ---------- Лёгкий бросок: рикошеты и отскоки ----------
        private const float RicochetRange = 250f;
        private const float RicochetSpeed = 15f;
        private const float RicochetSteer = 0.25f;
        private const int LightTileBounces = 2;
        private const int PerfectTileBounces = 1;
        private const float BounceDamping = 0.8f;

        // ---------- Идеальный бросок ----------
        private const int HitStopTicks = 3;
        private const int GrindTicks = 24;
        private const int GrindHitCooldown = 6;
        private const int DefaultHitCooldown = 10;
        private const float EmbedDepth = 0.5f;          // доля полуразмера цели: сюрикен сидит в теле, а не на краю
        private const float RipOutSpeed = 9f;
        private const float ReturnStartSpeed = 8f;
        private const float ReturnMaxSpeed = 26f;
        private const float ReturnAcceleration = 0.4f;
        private const float ReturnSteer = 0.14f;
        private const float CatchDistance = 40f;
        private const int ReturnMaxTicks = 180;
        private const float ReturnGiveUpDistance = 2000f;

        // ---------- Передержка ----------
        private const int OverheatPierce = 5;

        // ---------- Урон: доли урона оружия ----------
        private const float PerfectImpactMult = 1.5f;
        private const float GrindMult = 0.45f;
        private const float ReturnMult = 0.6f;
        private const float OverheatMult = 1.2f;
        private const float LightBurstMult = 0.5f;
        private const float OverheatBurstMult = 0.8f;
        private const float PerfectBurstMult = 1f;
        private const float HeatBurstBonus = 0.15f;      // +урона взрыва за стак Жара
        private const float LightBurstRadius = 48f;
        private const float OverheatBurstRadius = 64f;
        private const float PerfectBurstRadius = 72f;
        private const float HeatRadiusBonus = 26f;       // +радиуса взрыва за стак
        private const int MoltenShardCount = 4;
        private const float MoltenShardMult = 0.3f;
        private const int HellFireTicks = 300;

        // ---------- Вид ----------
        private const int BlurCopies = 3;
        private const float BlurLag = 0.45f;             // отставание копии по углу, в долях угловой скорости

        private static readonly Color LightGlow = new(255, 130, 40);
        private static readonly Color PerfectGlow = new(255, 214, 130);
        private static readonly Color OverheatGlow = new(255, 80, 36);
        private static readonly Color HotWhite = new(255, 240, 200);
        private static readonly Color SparkRed = new(220, 50, 20);
        private static readonly Color SmokeColor = new(55, 40, 36);

        // Синхронизируются через ExtraAI; меняет только владелец (кроме хода времени и отскоков от стен)
        private Phase _phase;
        private int _phaseTimer;
        private int _targetNPC = -1;       // в кого вгрызся / к кому летит рикошетом
        private Vector2 _embedOffset;
        private int _ricochetsLeft;
        private int _tileBounces;
        private int _pierceLeft;

        // Локальные
        private bool _initialized;
        private Vector2 _spawn;
        private Phase _seenPhase;          // смену фазы отыгрывают эффектами все клиенты
        private float _spin;               // угловая скорость этого тика
        private float _spinDir = 1f;
        private bool _caught;              // только у владельца
        private bool _dropped;             // только у владельца

        private ShurikenThrow Kind => (ShurikenThrow)(int)Projectile.ai[0];
        private float LightCharge => Projectile.ai[1];
        private int Heat => (int)Projectile.ai[2];
        private bool IsOwner => Projectile.owner == Main.myPlayer;
        private Player Owner => Main.player[Projectile.owner];

        private Color GlowColor => Kind switch
        {
            ShurikenThrow.Perfect => Color.Lerp(PerfectGlow, HotWhite, Heat / (float)ShurikenChargePlayer.MaxHeat),
            ShurikenThrow.Overheat => OverheatGlow,
            _ => LightGlow,
        };

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailCacheLength[Type] = TrailLength;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override void SetDefaults()
        {
            Projectile.width = 26;
            Projectile.height = 26;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Ranged;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = true;
            Projectile.aiStyle = 0;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = DefaultHitCooldown;
        }

        #region Поведение

        public override void AI()
        {
            if (!_initialized)
                Initialize();

            if (_phase != _seenPhase)
            {
                _seenPhase = _phase;
                if (!Main.dedServ)
                    OnPhaseEntered(_phase);
            }

            _phaseTimer++;
            switch (_phase)
            {
                case Phase.Flying:
                    FlyTick();
                    break;
                case Phase.HitStop:
                    HitStopTick();
                    break;
                case Phase.Grinding:
                    GrindTick();
                    break;
                case Phase.Returning:
                    ReturnTick();
                    break;
            }

            Projectile.rotation += _spin;

            if (!Main.dedServ)
            {
                EmitParticles();
                Lighting.AddLight(Projectile.Center, GlowColor.ToVector3() * (Kind == ShurikenThrow.Light ? 0.7f : 1.1f));
            }
        }

        private void Initialize()
        {
            _initialized = true;
            _spawn = Projectile.Center;
            _spinDir = Projectile.velocity.X < 0f ? -1f : 1f;
            _ricochetsLeft = Kind == ShurikenThrow.Light ? 1 + (int)(LightCharge * 2.99f) : 0;
            _pierceLeft = Kind == ShurikenThrow.Overheat ? OverheatPierce : 0;

            if (!Main.dedServ)
                OnThrown();
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseTimer = 0;
            if (IsOwner)
                Projectile.netUpdate = true;
        }

        private void FlyTick()
        {
            Projectile.friendly = true;
            Projectile.tileCollide = true;

            bool homing = Kind == ShurikenThrow.Light && TryGetNPC(_targetNPC, out _);
            float gravity = Kind switch
            {
                ShurikenThrow.Light => homing ? 0f : MathHelper.Lerp(LightMaxGravity, LightMinGravity, LightCharge),
                ShurikenThrow.Perfect => PerfectGravity,
                _ => 0f,
            };
            Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + gravity, MaxFallSpeed);

            // Рикошет доводится к цели: иначе дуга гравитации проносит мимо
            if (homing && TryGetNPC(_targetNPC, out NPC prey))
            {
                Vector2 want = (prey.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * RicochetSpeed;
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, want, RicochetSteer);
            }

            if (Projectile.velocity.X != 0f)
                _spinDir = Math.Sign(Projectile.velocity.X);
            _spin = (Kind == ShurikenThrow.Light ? 0.3f : 0.45f) * _spinDir;

            if (IsOwner && Vector2.DistanceSquared(Projectile.Center, _spawn) > MaxRange * MaxRange)
                Projectile.Kill();
        }

        private void HitStopTick()
        {
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            _spin = 0f;
            StickToTarget();

            if (_phaseTimer >= HitStopTicks)
                SetPhase(Phase.Grinding);
        }

        private void GrindTick()
        {
            Projectile.friendly = true;
            Projectile.tileCollide = false;
            Projectile.localNPCHitCooldown = GrindHitCooldown;
            _spin = 0.7f * _spinDir;
            bool targetAlive = StickToTarget();

            // Цель погибла — владелец вырывает сюрикен сразу, остальные дождутся его решения
            if (_phaseTimer >= GrindTicks || (IsOwner && !targetAlive))
                RipOut();
        }

        private void RipOut()
        {
            Projectile.localNPCHitCooldown = DefaultHitCooldown;
            Vector2 toOwner = (Owner.Center - Projectile.Center).SafeNormalize(-Vector2.UnitY);
            Projectile.velocity = (toOwner * 0.5f - Vector2.UnitY).SafeNormalize(-Vector2.UnitY) * RipOutSpeed;
            SetPhase(Phase.Returning);

            if (!IsOwner)
                return;

            SpawnBurst(PerfectBurstMult + HeatBurstBonus * Heat, PerfectBurstRadius + HeatRadiusBonus * Heat,
                0.3f + 0.2f * Heat);
            if (Heat >= ShurikenChargePlayer.MaxHeat)
                SpawnMoltenShards();
        }

        private void ReturnTick()
        {
            Projectile.friendly = true;
            Projectile.tileCollide = false;

            Player owner = Owner;
            float speed = Math.Min(ReturnStartSpeed + _phaseTimer * ReturnAcceleration, ReturnMaxSpeed);
            Vector2 want = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * speed;
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, want, ReturnSteer);
            _spin = 0.5f * _spinDir;

            if (!IsOwner)
                return;

            float distance = Vector2.Distance(owner.Center, Projectile.Center);
            if (!owner.active || owner.dead || _phaseTimer > ReturnMaxTicks || distance > ReturnGiveUpDistance)
            {
                _dropped = true;
                Projectile.Kill();
                return;
            }
            if (distance < CatchDistance || Projectile.Hitbox.Intersects(owner.Hitbox))
            {
                _caught = true;
                owner.GetModPlayer<ShurikenChargePlayer>().OnShurikenCaught();
                Projectile.Kill();
            }
        }

        // Держится в теле цели; false — цели больше нет
        private bool StickToTarget()
        {
            Projectile.velocity = Vector2.Zero;
            if (!TryGetNPC(_targetNPC, out NPC npc))
                return false;
            Projectile.Center = npc.Center + _embedOffset;
            return true;
        }

        private static bool TryGetNPC(int whoAmI, out NPC npc)
        {
            npc = whoAmI >= 0 && whoAmI < Main.maxNPCs ? Main.npc[whoAmI] : null;
            return npc != null && npc.active && npc.life > 0;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            if (_phase != Phase.Flying)
                return false;

            int allowed = Kind switch
            {
                ShurikenThrow.Light => LightTileBounces,
                ShurikenThrow.Perfect => PerfectTileBounces,
                _ => 0,
            };
            if (_tileBounces >= allowed)
                return true; // OnKill взорвёт

            if (Projectile.velocity.X != oldVelocity.X)
                Projectile.velocity.X = -oldVelocity.X * BounceDamping;
            if (Projectile.velocity.Y != oldVelocity.Y)
                Projectile.velocity.Y = -oldVelocity.Y * BounceDamping;
            _tileBounces++;
            _targetNPC = -1; // рикошет в стену сбивает наведение

            if (!Main.dedServ)
                SpawnBounceSparks(Projectile.velocity);
            return false;
        }

        public override bool? CanHitNPC(NPC target)
        {
            if (_phase == Phase.HitStop)
                return false;
            if (_phase == Phase.Grinding)
                return target.whoAmI == _targetNPC ? null : false;
            return null;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            float mult = _phase switch
            {
                Phase.Grinding => GrindMult,
                Phase.Returning => ReturnMult,
                _ => Kind switch
                {
                    ShurikenThrow.Perfect => PerfectImpactMult,
                    ShurikenThrow.Overheat => OverheatMult,
                    _ => 1f,
                },
            };
            modifiers.SourceDamage *= mult;

            if (_phase == Phase.Grinding)
                modifiers.Knockback *= 0f; // пилит на месте, а не отталкивает
        }

        // Зовётся только у владельца
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            target.AddBuff(ModContent.BuffType<HellFireDebuff>(), HellFireTicks);

            if (_phase != Phase.Flying)
                return;

            switch (Kind)
            {
                case ShurikenThrow.Perfect:
                    EmbedInto(target);
                    break;
                case ShurikenThrow.Light:
                    RicochetFrom(target);
                    break;
                case ShurikenThrow.Overheat:
                    if (--_pierceLeft <= 0)
                        Projectile.Kill();
                    else
                        Projectile.netUpdate = true;
                    break;
            }
        }

        private void EmbedInto(NPC target)
        {
            _targetNPC = target.whoAmI;
            Vector2 half = target.Size * 0.5f * EmbedDepth;
            _embedOffset = Vector2.Clamp(Projectile.Center - target.Center, -half, half);
            Projectile.velocity = Vector2.Zero;
            SetPhase(Phase.HitStop);
        }

        private void RicochetFrom(NPC target)
        {
            NPC next = _ricochetsLeft > 0
                ? SoACombat.FindClosestNPC(target.Center, RicochetRange, requireLineOfSight: true, excludeWhoAmI: target.whoAmI)
                : null;
            if (next == null)
            {
                Projectile.Kill();
                return;
            }

            _ricochetsLeft--;
            _targetNPC = next.whoAmI;
            Projectile.velocity = (next.Center - Projectile.Center).SafeNormalize(Vector2.UnitX) * RicochetSpeed;
            Projectile.netUpdate = true;
        }

        public override void OnKill(int timeLeft)
        {
            if (!Main.dedServ)
                OnKillVisuals();

            if (!IsOwner)
                return;

            // Жар копится только цепочкой пойманных идеальных бросков
            if (Kind == ShurikenThrow.Perfect && !_caught)
                Owner.GetModPlayer<ShurikenChargePlayer>().ResetHeat();

            // Вернувшийся сюрикен уже взорвался при вырывании
            if (_caught || _dropped || _phase == Phase.Returning)
                return;

            switch (Kind)
            {
                case ShurikenThrow.Light:
                    SpawnBurst(LightBurstMult, LightBurstRadius, 0.05f);
                    break;
                case ShurikenThrow.Overheat:
                    SpawnBurst(OverheatBurstMult, OverheatBurstRadius, 0.2f);
                    break;
                default:
                    SpawnBurst(PerfectBurstMult, PerfectBurstRadius, 0.3f);
                    break;
            }
        }

        private void SpawnBurst(float damageMult, float radius, float intensity)
        {
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, Vector2.Zero,
                ModContent.ProjectileType<InfernoShurikenBurst>(), (int)(Projectile.damage * damageMult),
                Projectile.knockBack, Projectile.owner, radius, intensity);
        }

        private void SpawnMoltenShards()
        {
            int damage = (int)(Projectile.damage * MoltenShardMult);
            for (int i = 0; i < MoltenShardCount; i++)
            {
                float spreadX = MathHelper.Lerp(-5f, 5f, (i + Main.rand.NextFloat()) / MoltenShardCount);
                Vector2 velocity = new(spreadX, -Main.rand.NextFloat(5f, 9f));
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, velocity,
                    ModContent.ProjectileType<InfernoMoltenShard>(), damage, 0f, Projectile.owner);
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)_phase);
            writer.Write((short)_phaseTimer);
            writer.Write((short)_targetNPC);
            writer.WriteVector2(_embedOffset);
            writer.Write((byte)_ricochetsLeft);
            writer.Write((byte)_tileBounces);
            writer.Write((byte)_pierceLeft);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            _phase = (Phase)reader.ReadByte();
            _phaseTimer = reader.ReadInt16();
            _targetNPC = reader.ReadInt16();
            _embedOffset = reader.ReadVector2();
            _ricochetsLeft = reader.ReadByte();
            _tileBounces = reader.ReadByte();
            _pierceLeft = reader.ReadByte();
        }

        #endregion

        #region Эффекты

        // Выброс искр из руки и звук броска — у всех клиентов, а не только у бросившего
        private void OnThrown()
        {
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            Color color = GlowColor;
            int sparks = Kind == ShurikenThrow.Light ? 6 : 12;
            for (int i = 0; i < sparks; i++)
            {
                SoAParticles.SpawnStreak(Projectile.Center, dir.RotatedByRandom(0.5f) * Main.rand.NextFloat(3f, 8f),
                    color, 2f, gravity: 0.05f, life: Main.rand.Next(10, 18), lengthPerSpeed: 2.2f);
            }
            SoAParticles.SpawnGlow(Projectile.Center, dir * 2f, color * 0.8f, 16f, 50f, 10);

            SoundStyle sound = Kind switch
            {
                ShurikenThrow.Perfect => SoundID.Item73,
                ShurikenThrow.Overheat => SoundID.Item20 with { Pitch = -0.2f },
                _ => SoundID.Item1,
            };
            SoundEngine.PlaySound(sound, Projectile.Center);
        }

        private void OnPhaseEntered(Phase phase)
        {
            Vector2 at = Projectile.Center;
            switch (phase)
            {
                case Phase.HitStop:
                    // Вспышка удара и снопы искр
                    SoAParticles.SpawnGlow(at, Vector2.Zero, HotWhite, 24f, 90f, 10);
                    for (int i = 0; i < 16; i++)
                    {
                        Vector2 dir = Main.rand.NextVector2Unit();
                        SoAParticles.SpawnStreak(at, dir * Main.rand.NextFloat(4f, 10f),
                            Color.Lerp(PerfectGlow, HotWhite, Main.rand.NextFloat()), 2.4f, gravity: 0.12f,
                            life: Main.rand.Next(14, 24), lengthPerSpeed: 2.4f);
                    }
                    SoAParticles.AddLight(at, PerfectGlow, 2f, 12);
                    SoundEngine.PlaySound(SoundID.Tink with { Pitch = -0.5f, Volume = 1f }, at);
                    SoundEngine.PlaySound(SoundID.Item74 with { Pitch = 0.4f, Volume = 0.5f }, at);
                    if (IsOwner)
                    {
                        Main.instance.CameraModifiers.Add(new PunchCameraModifier(at, Main.rand.NextVector2Unit(),
                            2.5f, 10f, 8, 600f, "SoA:ShurikenBite"));
                    }
                    break;

                case Phase.Grinding:
                    SoundEngine.PlaySound(SoundID.Item22 with { Volume = 0.6f, Pitch = 0.3f }, at);
                    break;

                case Phase.Returning:
                    SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.5f }, at);
                    break;
            }
        }

        // Хвост из искр: одна искра через тик и мягкое свечение вместо каши из пыли
        private void EmitParticles()
        {
            Vector2 at = Projectile.Center;
            Color color = GlowColor;

            if (_phase == Phase.Grinding)
            {
                // Пила: искры летят по касательной к вращению
                for (int i = 0; i < 2; i++)
                {
                    Vector2 rim = Main.rand.NextVector2Unit();
                    Vector2 tangent = rim.RotatedBy(MathHelper.PiOver2 * _spinDir);
                    SoAParticles.SpawnStreak(at + rim * 11f, (tangent * Main.rand.NextFloat(4f, 9f)) + new Vector2(0f, -1f),
                        Color.Lerp(color, HotWhite, Main.rand.NextFloat(0.6f)), Main.rand.NextFloat(1.6f, 2.6f),
                        gravity: 0.2f, life: Main.rand.Next(10, 18), lengthPerSpeed: 2.2f);
                }
                if (_phaseTimer % 6 == 0)
                    SoAParticles.SpawnGlow(at, Vector2.Zero, color * 0.6f, 20f, 44f, 8);
                return;
            }

            if (_phase == Phase.HitStop)
                return;

            if (_phaseTimer % 2 == 0)
            {
                SoAParticles.SpawnStreak(at + Main.rand.NextVector2Circular(6f, 6f),
                    -Projectile.velocity * 0.12f + Main.rand.NextVector2Circular(1f, 1f),
                    Color.Lerp(color, SparkRed, Main.rand.NextFloat(0.5f)), 1.8f, gravity: 0.05f,
                    life: Main.rand.Next(12, 20), lengthPerSpeed: 2f);
            }
            if (_phaseTimer % 3 == 0)
                SoAParticles.SpawnGlow(at, Projectile.velocity * 0.1f, color * 0.3f, 20f, 8f, 14);
            if (Kind == ShurikenThrow.Overheat && _phaseTimer % 4 == 0)
                SoAParticles.SpawnSmoke(at, -Projectile.velocity * 0.05f, SmokeColor, 10f, 30f, 0.35f, 30);
        }

        // Веер искр по отражённому направлению, а не пыль во все стороны
        private void SpawnBounceSparks(Vector2 reflected)
        {
            Vector2 dir = reflected.SafeNormalize(-Vector2.UnitY);
            Color color = GlowColor;
            for (int i = 0; i < 10; i++)
            {
                SoAParticles.SpawnStreak(Projectile.Center, dir.RotatedByRandom(0.6f) * Main.rand.NextFloat(3f, 9f),
                    Color.Lerp(color, HotWhite, Main.rand.NextFloat(0.5f)), 2f, gravity: 0.15f,
                    life: Main.rand.Next(10, 18), lengthPerSpeed: 2.2f);
            }
            SoAParticles.SpawnGlow(Projectile.Center, Vector2.Zero, color * 0.6f, 14f, 40f, 8);
            SoundEngine.PlaySound(SoundID.Tink, Projectile.Center);
        }

        private void OnKillVisuals()
        {
            if (_phase != Phase.Returning)
                return; // взрыв рисует InfernoShurikenBurst

            Vector2 at = Projectile.Center;
            bool caught = _caught || Vector2.Distance(Owner.Center, at) < CatchDistance * 2f;
            if (caught)
            {
                // Поймал: короткая вспышка в руке, на новом стаке Жара — звон
                SoAParticles.SpawnGlow(at, Vector2.Zero, HotWhite * 0.8f, 16f, 56f, 10);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 dir = Main.rand.NextVector2Unit();
                    SoAParticles.SpawnStreak(at, dir * Main.rand.NextFloat(2f, 5f), GlowColor, 1.8f,
                        gravity: 0f, life: 12, lengthPerSpeed: 2f);
                }
                SoundEngine.PlaySound(SoundID.Grab with { Pitch = 0.2f }, at);
                SoundEngine.PlaySound(SoundID.Item29 with { Volume = 0.4f, Pitch = -0.3f + 0.3f * Heat }, at);
            }
            else
            {
                // Не поймал: гаснет с дымком
                SoAParticles.SpawnSmoke(at, new Vector2(0f, -0.6f), SmokeColor, 14f, 40f, 0.5f, 40);
                SoundEngine.PlaySound(SoundID.Item20 with { Volume = 0.4f, Pitch = -0.6f }, at);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = Kind == ShurikenThrow.Light
                ? TextureAssets.Projectile[Type].Value
                : ModContent.Request<Texture2D>(ActiveTexturePath).Value;
            Vector2 origin = tex.Size() / 2f;
            Vector2 center = Projectile.Center - Main.screenPosition;
            Color glowColor = GlowColor;

            DrawRibbon();

            // Ореол
            Texture2D glow = SoAVfx.SoftGlow;
            float haloSize = (Kind == ShurikenThrow.Light ? 44f : 60f) + 6f * Heat;
            Main.EntitySpriteDraw(glow, center, null, SoAVfx.Additive(glowColor * 0.5f), 0f, glow.Size() / 2f,
                haloSize / glow.Width, SpriteEffects.None, 0);

            // Размытие вращения: копии в той же точке отстают по углу — видно, что он крутится
            for (int i = BlurCopies; i >= 1; i--)
            {
                float lag = _spin * i * BlurLag;
                Main.EntitySpriteDraw(tex, center, null, SoAVfx.Additive(glowColor * (0.4f / i)), Projectile.rotation - lag,
                    origin, Projectile.scale, SpriteEffects.None, 0);
            }

            // Спрайт светится сам: адский сюрикен не должен тонуть в темноте пещеры
            float punch = _phase == Phase.HitStop ? 1f + 0.3f * (1f - _phaseTimer / (float)HitStopTicks) : 1f;
            Vector2 jitter = _phase == Phase.Grinding ? Main.rand.NextVector2Circular(1.5f, 1.5f) : Vector2.Zero;
            Main.EntitySpriteDraw(tex, center + jitter, null, Color.White, Projectile.rotation, origin,
                Projectile.scale * punch, SpriteEffects.None, 0);

            return false;
        }

        // Хвост-лента расплава по пройденному пути
        private void DrawRibbon()
        {
            if (_phase == Phase.HitStop || _phase == Phase.Grinding)
                return;

            Span<Vector2> points = stackalloc Vector2[TrailLength + 1];
            int count = 0;
            points[count++] = Projectile.Center;
            float length = 0f;
            for (int i = 0; i < Projectile.oldPos.Length; i++)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    break;
                Vector2 point = Projectile.oldPos[i] + Projectile.Size / 2f;
                float step = Vector2.Distance(point, points[count - 1]);
                if (step < 1f)
                    continue;
                length += step;
                points[count++] = point;
            }
            if (count < 3 || length < 16f)
                return;

            float headWidth = (Kind switch
            {
                ShurikenThrow.Perfect => 11f,
                ShurikenThrow.Overheat => 13f,
                _ => 8f,
            }) + 1.5f * Heat;
            float heat = Kind switch
            {
                ShurikenThrow.Perfect => 0.5f + Heat / 6f,
                ShurikenThrow.Overheat => 0.3f,
                _ => 0f,
            };

            SoATrail.Draw(points[..count],
                progress => headWidth * (float)Math.Pow(1f - progress, 0.8f),
                progress => TrailStyle.MagmaBody(progress, heat),
                TrailStyle.Magma);
        }

        #endregion
    }
}
