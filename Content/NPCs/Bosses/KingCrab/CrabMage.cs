using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Animation;
using SoA.Content.Projectiles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Краб-маг: вторая половина свиты. Сам почти не бьёт и мрёт быстро, но держит
    // дистанцию, уходит в песок при сближении и прячется за спину паладина.
    // Вся связка строится на конфликте: пока разбираешь щит паладина, маг бьёт
    // сверху; пока гонишься за магом, паладин догоняет тараном.
    //
    // Спрайта пока нет — временно берём лист паладина (та же раскладка 66x48).
    public class CrabMage : ModNPC
    {
        private enum MageState
        {
            Drift,     // держит дистанцию и ищет укрытие за паладином
            Volley,    // веер самонаводящихся пузырей
            Geyser,    // столб воды из грунта под игроком
            Blink      // уход в песок и всплытие поодаль
        }

        // ---------- Статы ----------
        private const int LifeMax = 260;
        private const int DefenseNormal = 6;
        private const int ContactDamage = 18;
        private const int BubbleDamage = 20;
        private const int GeyserDamage = 26;

        // ---------- Дистанция ----------
        private const float PreferredRange = 300f;
        private const float RangeTolerance = 70f;
        private const float PanicRange = 150f;    // ближе этого — уходит в песок
        private const float CoverOffset = 110f;   // насколько прячется за паладина
        private const float DriftSpeed = 2.6f;
        private const float Gravity = 0.35f;
        private const float MaxFallSpeed = 11f;
        private const float StepUpSpeed = 6f;

        // ---------- Тайминги ----------
        private const int DriftMinTicks = 55;
        private const int DriftMaxTicks = 110;
        private const int VolleyWindupTicks = 26;
        private const int VolleyShotGap = 7;
        private const int VolleyShots = 4;
        private const int GeyserTelegraphTicks = 42;
        private const int GeyserColumnTicks = 30;
        private const int BlinkOutTicks = 18;
        private const int BlinkInTicks = 16;
        private const int BlinkCooldownTicks = 150;

        // ---------- Геометрия способностей ----------
        private const float VolleySpread = 0.42f;
        private const float VolleySpeed = 7f;
        private const float GeyserRiseSpeed = 9f;
        private const int GeyserJets = 5;
        private const float BlinkMinDistance = 320f;
        private const float BlinkMaxDistance = 470f;

        // ---------- Лист ----------
        private const int FullSheetFrames = 19;   // полный лист мага; раскладка в Docs/SpriteSpecs.md

        private static readonly SheetClip ClipIdle = new(0, 2, 14);
        private static readonly SheetClip ClipWalk = new(2, 6, 6);
        private static readonly SheetClip ClipCastWindup = new(8, 3, 8, loop: false);
        private static readonly SheetClip ClipCastShot = new(11, 2, 5, loop: false);
        private static readonly SheetClip ClipChannel = new(13, 3, 7);
        private static readonly SheetClip ClipBlink = new(16, 3, 6, loop: false);

        private int _clipStart = -1;

        private MageState State
        {
            get => (MageState)(int)NPC.ai[0];
            set => NPC.ai[0] = (int)value;
        }

        private float Timer
        {
            get => NPC.ai[1];
            set => NPC.ai[1] = value;
        }

        private float SubState
        {
            get => NPC.ai[2];
            set => NPC.ai[2] = value;
        }

        // Точка гейзера по X: считается один раз на телеграфе, чтобы столб не ездил
        // за игроком — иначе от него нельзя уйти
        private float StateData
        {
            get => NPC.ai[3];
            set => NPC.ai[3] = value;
        }

        private int _blinkCooldown;
        private float _geyserGroundY;

        public override string Texture => "SoA/Content/NPCs/Bosses/KingCrab/CrabKnight";

        public override void SetStaticDefaults()
        {
            // Считаем кадры по файлу — тем же способом, что паладин: пока маг рисуется
            // чужим листом, это ещё и держит оба NPC в согласии
            Main.npcFrameCount[Type] = CrabKnight.CountSheetFrames(Texture);
            NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
        }

        public override void SetDefaults()
        {
            NPC.width = 44;
            NPC.height = 34;
            DrawOffsetY = -4f;

            NPC.damage = ContactDamage;
            NPC.defense = DefenseNormal;
            NPC.lifeMax = LifeMax;
            NPC.knockBackResist = 0.35f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = Item.buyPrice(silver: 10);
            NPC.lavaImmune = true;
        }

        public override void AI()
        {
            // Разворотом заведует только FaceTarget: ванильный faceTarget затирает инверсию
            NPC.TargetClosest(false);
            Player target = Main.player[NPC.target];
            if (!target.active || target.dead)
            {
                NPC.EncourageDespawn(30);
                ApplyGravity();
                return;
            }

            // Как и у паладина: нет короля — торопим деспавн, но ИИ продолжает работать,
            // иначе призванный в одиночку маг зависает и не падает на землю
            if (!NPC.AnyNPCs(ModContent.NPCType<King_crab>()))
                NPC.EncourageDespawn(30);

            if (_blinkCooldown > 0)
                _blinkCooldown--;
            if (Timer > 0f)
                Timer--;

            NPC.defense = DefenseNormal;
            FaceTarget(target);

            if (State != MageState.Blink)
                ApplyGravity();

            switch (State)
            {
                case MageState.Volley: AIVolley(target); break;
                case MageState.Geyser: AIGeyser(target); break;
                case MageState.Blink: AIBlink(target); break;
                default: AIDrift(target); break;
            }
        }

        private void ApplyGravity()
        {
            NPC.velocity.Y += Gravity;
            if (NPC.velocity.Y > MaxFallSpeed)
                NPC.velocity.Y = MaxFallSpeed;
        }

        private bool OnGround => NPC.velocity.Y == 0f || NPC.collideY;

        private void FaceTarget(Player target)
        {
            int look = Math.Sign(target.Center.X - NPC.Center.X);
            if (look == 0)
                return;
            NPC.direction = look;
            // Лист нарисован мордой вправо, ваниль отражает при spriteDirection == 1
            NPC.spriteDirection = -look;
        }

        private void Walk(float speed)
        {
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, speed, 0.15f);
            if (speed != 0f && NPC.collideX && OnGround)
                NPC.velocity.Y = -StepUpSpeed;
        }

        // --- Дистанция и укрытие ---

        private void AIDrift(Player target)
        {
            float desiredX = DesiredX(target);
            float delta = desiredX - NPC.Center.X;
            Walk(Math.Abs(delta) < 24f ? 0f : Math.Sign(delta) * DriftSpeed);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            // Игрок в упор — уходим в песок, не дожидаясь конца паузы
            if (Vector2.Distance(target.Center, NPC.Center) < PanicRange && _blinkCooldown <= 0)
            {
                EnterState(MageState.Blink, BlinkOutTicks);
                return;
            }

            if (Timer > 0f)
                return;

            // Гейзер требует, чтобы игрок стоял на грунте: по летящему он не попадает
            bool grounded = target.velocity.Y == 0f;
            if (grounded && Main.rand.NextBool(2))
                BeginGeyser(target);
            else
                EnterState(MageState.Volley, VolleyWindupTicks);
        }

        // Маг жмётся за паладина: если гвардеец жив, стоим по другую сторону от него.
        // Отсюда всё поведение связки — игрок не может бить обоих одновременно
        private float DesiredX(Player target)
        {
            NPC guard = NearestPaladin();
            if (guard != null)
            {
                float side = Math.Sign(guard.Center.X - target.Center.X);
                if (side == 0f)
                    side = 1f;
                return guard.Center.X + side * CoverOffset;
            }

            float away = Math.Sign(NPC.Center.X - target.Center.X);
            if (away == 0f)
                away = NPC.direction;

            float distance = Math.Abs(NPC.Center.X - target.Center.X);
            if (distance < PreferredRange - RangeTolerance)
                return target.Center.X + away * PreferredRange;
            if (distance > PreferredRange + RangeTolerance)
                return target.Center.X + away * PreferredRange;
            return NPC.Center.X;
        }

        private NPC NearestPaladin()
        {
            int type = ModContent.NPCType<CrabKnight>();
            NPC best = null;
            float bestDistance = float.MaxValue;

            foreach (NPC other in Main.ActiveNPCs)
            {
                if (other.type != type)
                    continue;
                float distance = Vector2.DistanceSquared(other.Center, NPC.Center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = other;
                }
            }
            return best;
        }

        // --- Пузырьковый веер ---

        private void AIVolley(Player target)
        {
            Walk(0f);

            if (SubState == 0f)
            {
                if (!Main.dedServ && Timer % 2f == 0f)
                {
                    // Пыль сходится к клешне — видно, что маг набирает залп
                    Vector2 muzzle = Muzzle();
                    Vector2 offset = Main.rand.NextVector2CircularEdge(40f, 40f);
                    Dust gather = Dust.NewDustPerfect(muzzle + offset, DustID.Water, -offset * 0.06f);
                    gather.noGravity = true;
                }
                Lighting.AddLight(Muzzle(), 0.15f, 0.3f, 0.5f);

                if (Timer <= 0f)
                {
                    SoundEngine.PlaySound(SoundID.Item21 with { Pitch = 0.5f }, NPC.Center);
                    EnterSubState(1f, VolleyShotGap);
                }
                return;
            }

            if (Timer > 0f)
                return;

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                int shot = (int)StateData;
                float t = VolleyShots <= 1 ? 0.5f : shot / (float)(VolleyShots - 1);
                float spread = MathHelper.Lerp(-VolleySpread, VolleySpread, t);
                Vector2 muzzle = Muzzle();
                Vector2 direction = (target.Center - muzzle).SafeNormalize(new Vector2(NPC.direction, 0f));

                int bubble = Projectile.NewProjectile(NPC.GetSource_FromAI(), muzzle,
                    direction.RotatedBy(spread) * VolleySpeed,
                    ModContent.ProjectileType<KingCrabBubble>(), ProjDamage(BubbleDamage), 2f, Main.myPlayer,
                    ai0: 1f);   // режим 1: слабое самонаведение
                if (bubble < Main.maxProjectiles && Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncProjectile, number: bubble);
            }

            StateData++;
            if (StateData >= VolleyShots)
                EnterState(MageState.Drift, Main.rand.Next(DriftMinTicks, DriftMaxTicks));
            else
                Timer = VolleyShotGap;
        }

        private Vector2 Muzzle() => NPC.Center + new Vector2(NPC.direction * 20f, -10f);

        // --- Гейзер ---

        private void BeginGeyser(Player target)
        {
            StateData = target.Center.X;
            _geyserGroundY = GroundBelow(target.Center.X, target.Bottom.Y);
            EnterState(MageState.Geyser, GeyserTelegraphTicks);
            // SubState 0 = телеграф; точка уже зафиксирована и за игроком не поедет
        }

        private void AIGeyser(Player target)
        {
            Walk(0f);

            if (SubState == 0f)
            {
                if (!Main.dedServ)
                {
                    // Пузырьки из грунта на месте будущего столба — вот и весь телеграф
                    var spot = new Vector2(StateData, _geyserGroundY);
                    for (int i = 0; i < 2; i++)
                    {
                        Dust warn = Dust.NewDustPerfect(spot + new Vector2(Main.rand.NextFloat(-20f, 20f), -2f),
                            DustID.Water, new Vector2(0f, -Main.rand.NextFloat(1f, 3f)));
                        warn.noGravity = true;
                        warn.scale = Main.rand.NextFloat(1f, 1.6f);
                    }
                    Lighting.AddLight(spot, 0.1f, 0.25f, 0.45f);
                }

                if (Timer <= 0f)
                {
                    SoundEngine.PlaySound(SoundID.Item96 with { Pitch = -0.3f }, new Vector2(StateData, _geyserGroundY));
                    EnterSubState(1f, GeyserColumnTicks);
                }
                return;
            }

            // Столб идёт снизу вверх порциями: сплошная стена не даёт шанса отпрыгнуть
            if (Main.netMode != NetmodeID.MultiplayerClient && Timer % 5f == 0f)
            {
                int jet = (int)((GeyserColumnTicks - Timer) / 5f);
                if (jet < GeyserJets)
                {
                    var origin = new Vector2(StateData + Main.rand.NextFloat(-10f, 10f), _geyserGroundY - 4f);
                    int column = Projectile.NewProjectile(NPC.GetSource_FromAI(), origin,
                        new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -GeyserRiseSpeed),
                        ModContent.ProjectileType<KingCrabBubble>(), ProjDamage(GeyserDamage), 3f, Main.myPlayer,
                        ai0: 2f);   // режим 2: летит по прямой сквозь тайлы
                    if (column < Main.maxProjectiles && Main.netMode == NetmodeID.Server)
                        NetMessage.SendData(MessageID.SyncProjectile, number: column);
                }
            }

            if (Timer <= 0f)
                EnterState(MageState.Drift, Main.rand.Next(DriftMinTicks, DriftMaxTicks));
        }

        // --- Уход в песок ---

        private void AIBlink(Player target)
        {
            if (SubState == 0f)
            {
                NPC.velocity *= 0.8f;
                NPC.alpha = (int)MathHelper.Lerp(255f, 0f, Timer / BlinkOutTicks);
                if (!Main.dedServ && Timer % 2f == 0f)
                    SandPuff(6);

                if (Timer > 0f)
                    return;

                NPC.alpha = 255;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Teleport(target);
                    NPC.netUpdate = true;
                }
                EnterSubState(1f, BlinkInTicks);
                return;
            }

            NPC.velocity = Vector2.Zero;
            NPC.alpha = (int)MathHelper.Lerp(0f, 255f, Timer / BlinkInTicks);
            if (!Main.dedServ && Timer % 2f == 0f)
                SandPuff(4);

            if (Timer <= 0f)
            {
                NPC.alpha = 0;
                _blinkCooldown = BlinkCooldownTicks;
                // Всплыл — сразу залп: иначе телепорт читается как бесплатный побег
                EnterState(MageState.Volley, VolleyWindupTicks);
            }
        }

        private void Teleport(Player target)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int side = Main.rand.NextBool() ? -1 : 1;
                float distance = Main.rand.NextFloat(BlinkMinDistance, BlinkMaxDistance);
                float x = target.Center.X + side * distance;
                float groundY = GroundBelow(x, target.Center.Y - 200f);
                if (float.IsNaN(groundY))
                    continue;

                NPC.Center = new Vector2(x, groundY - NPC.height / 2f - 2f);
                NPC.velocity = Vector2.Zero;
                return;
            }

            // Место не нашлось — остаёмся где были, но кулдаун всё равно тикает:
            // иначе маг застрянет в попытках телепорта каждый кадр
            NPC.velocity = Vector2.Zero;
        }

        // Первый твёрдый тайл вниз от точки. Нужен и телепорту, и гейзеру:
        // столб обязан бить из грунта, а не из воздуха
        private static float GroundBelow(float worldX, float startY)
        {
            int tileX = (int)(worldX / 16f);
            int tileY = (int)(startY / 16f);
            if (tileX < 5 || tileX >= Main.maxTilesX - 5)
                return float.NaN;

            tileY = Math.Clamp(tileY, 5, Main.maxTilesY - 5);
            for (int y = tileY; y < Math.Min(tileY + 90, Main.maxTilesY - 5); y++)
            {
                Tile tile = Main.tile[tileX, y];
                if (tile.HasTile && Main.tileSolid[tile.TileType] && !tile.IsActuated)
                    return y * 16f;
            }
            return float.NaN;
        }

        private void SandPuff(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Dust sand = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Sand);
                sand.velocity = new Vector2(Main.rand.NextFloatDirection() * 2f, -Main.rand.NextFloat(0.5f, 3f));
                sand.scale = Main.rand.NextFloat(1f, 1.7f);
            }
        }

        // --- Служебное ---

        private void EnterState(MageState state, float duration)
        {
            State = state;
            Timer = duration;
            SubState = 0f;
            StateData = state == MageState.Geyser ? StateData : 0f;
            NPC.netUpdate = true;
        }

        private void EnterSubState(float subState, float duration)
        {
            SubState = subState;
            Timer = duration;
            NPC.netUpdate = true;
        }

        private int ProjDamage(int normalDamage)
            => NPC.GetAttackDamage_ForProjectiles(normalDamage, normalDamage * 0.75f);

        public override void FindFrame(int frameHeight)
        {
            if (Main.npcFrameCount[Type] < FullSheetFrames)
            {
                LegacyFrame(frameHeight);
                return;
            }

            SheetClip clip = ClipForState(out bool walking);
            if (clip.Start != _clipStart)
            {
                _clipStart = clip.Start;
                NPC.frameCounter = 0;
            }

            double delta = walking ? Math.Abs(NPC.velocity.X) * 0.6 : 1.0;
            NPC.frame.Y = clip.Advance(ref NPC.frameCounter, delta) * frameHeight;
        }

        private SheetClip ClipForState(out bool walking)
        {
            walking = false;

            switch (State)
            {
                case MageState.Blink:
                    return ClipBlink;
                case MageState.Volley:
                    return SubState == 0f ? ClipCastWindup : ClipCastShot;
                case MageState.Geyser:
                    return ClipChannel;
                default:
                    if (Math.Abs(NPC.velocity.X) < 0.15f)
                        return ClipIdle;
                    walking = true;
                    return ClipWalk;
            }
        }

        private void LegacyFrame(int frameHeight)
        {
            if (Math.Abs(NPC.velocity.X) < 0.1f || State == MageState.Blink)
            {
                NPC.frameCounter = 0;
                NPC.frame.Y = 0;
                return;
            }

            NPC.frameCounter += Math.Abs(NPC.velocity.X) * 0.35;
            if (NPC.frameCounter > 1000)
                NPC.frameCounter = 0;

            int index = 1 + (int)NPC.frameCounter % (Main.npcFrameCount[Type] - 1);
            NPC.frame.Y = index * frameHeight;
        }

        public override Color? GetAlpha(Color drawColor)
        {
            // Пока нет своего спрайта, маг отличается от паладина цветом:
            // холодная бирюза против багрового. Со своим листом строку убрать
            Color tinted = Color.Lerp(drawColor, new Color(70, 190, 210), 0.55f);

            if (State == MageState.Volley || State == MageState.Geyser)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
                return Color.Lerp(tinted, new Color(150, 240, 255), 0.4f * pulse);
            }
            return tinted;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server || NPC.life > 0)
                return;

            for (int i = 0; i < 18; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.DungeonWater);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 3f, -Main.rand.NextFloat(1f, 4f));
                d.noGravity = true;
            }
        }

        public override void SendExtraAI(System.IO.BinaryWriter writer)
        {
            writer.Write(_geyserGroundY);
            writer.Write((byte)Math.Min(_blinkCooldown, 255));
        }

        public override void ReceiveExtraAI(System.IO.BinaryReader reader)
        {
            _geyserGroundY = reader.ReadSingle();
            _blinkCooldown = reader.ReadByte();
        }
    }
}
