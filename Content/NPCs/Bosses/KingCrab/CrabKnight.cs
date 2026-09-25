using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Animation;
using SoA.Content.Projectiles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{

    public class CrabKnight : ModNPC
    {
        private enum PaladinState
        {
            March,         // наступление за щитом
            ShieldBash,    // короткий удар щитом в упор
            ChargeWindup,  // роет грунт перед разгоном
            Charge,        // таран
            Stagger,       // застрял после тарана: щита нет, урон повышенный
            Slam           // прыжок с накрытием и волнами по земле
        }

        // ---------- Статы ----------
        private const int LifeMax = 220;
        private const int DefenseNormal = 14;
        private const int ContactDamage = 24;
        private const int ChargeContactDamage = 38;
        private const int BashContactDamage = 32;

        // ---------- Движение ----------
        private const float MarchSpeed = 1.7f;
        private const float BashSpeed = 7f;
        private const float ChargeSpeed = 9.5f;
        private const float Gravity = 0.4f;
        private const float MaxFallSpeed = 12f;
        private const float StepUpSpeed = 6.2f;   // подскок через уступ, если упёрся в стену

        // ---------- Тайминги ----------
        private const int MarchMinTicks = 40;
        private const int MarchMaxTicks = 95;
        private const int BashWindupTicks = 22;
        private const int BashLungeTicks = 12;
        private const int BashRecoverTicks = 18;
        private const int ChargeWindupTicks = 28;
        private const int ChargeMaxTicks = 75;
        private const int StaggerTicks = 180;
        private const int SlamCrouchTicks = 16;
        private const int SlamMaxAirTicks = 120;

        // ---------- Щит ----------
        private const float GuardDamageMult = 0.15f;   // лобовой урон режется вчетверо
        private const float StaggerDamageMult = 1.6f;  // зато в оглушении бьют больнее
        private const int GuardBreakThreshold = 45;    // сильный лобовой удар всё же качает щит

        // ---------- Выбор действия ----------
        private const float BashRange = 110f;
        private const float ChargeRangeMin = 150f;
        private const float ChargeRangeMax = 620f;
        private const float SlamRangeMin = 170f;
        private const float RepeatWeightMult = 0.2f;   // повтор не запрещён, а сильно ослаблен
        private const int FeintChancePercent = 25;     // разгон, который окажется прыжком

        // ---------- Лист ----------
        private const int FrameHeight = 48;
        private const int LegacySheetFrames = 6;   // текущий лист: 0 стойка, 1-5 ход
        private const int FullSheetFrames = 24;    // полный лист с анимациями атак

        // Раскладка полного листа. Порядок кадров описан в Docs/SpriteSpecs.md;
        // пока лист короткий, работает запасная ветка LegacyFrame
        private static readonly SheetClip ClipIdle = new(0, 2, 14);
        private static readonly SheetClip ClipWalk = new(2, 6, 6);
        private static readonly SheetClip ClipBashWindup = new(8, 2, 11, loop: false);
        private static readonly SheetClip ClipBashHit = new(10, 2, 6, loop: false);
        private static readonly SheetClip ClipChargeWindup = new(12, 3, 9, loop: false);
        private static readonly SheetClip ClipCharge = new(15, 3, 4);
        private static readonly SheetClip ClipStagger = new(18, 3, 10);
        private static readonly SheetClip ClipRise = new(21, 1, 6, loop: false);
        private static readonly SheetClip ClipFall = new(22, 1, 6, loop: false);
        private static readonly SheetClip ClipLand = new(23, 1, 6, loop: false);

        private int _clipStart = -1;

        private PaladinState State
        {
            get => (PaladinState)(int)NPC.ai[0];
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

        // Ложный замах: разгон, который на последнем тике превратится в прыжок
        private bool Feint
        {
            get => NPC.ai[3] > 0.5f;
            set => NPC.ai[3] = value ? 1f : 0f;
        }

        private PaladinState _lastAction = PaladinState.March;
        private bool _blockedLastHit;

        // Щит держится всегда, кроме разгона, оглушения и полёта в прыжке:
        // именно эти окна и есть ответ игрока на «непробиваемого» паладина
        private bool GuardUp => State is PaladinState.March or PaladinState.ShieldBash or PaladinState.ChargeWindup;

        public override void SetStaticDefaults()
        {
            // Число кадров берём из самого файла: как только нарисуется полный лист,
            // анимации атак включатся сами, править код не придётся
            Main.npcFrameCount[Type] = CountSheetFrames(Texture);
            NPCID.Sets.NPCBestiaryDrawModifiers hide = new() { Hide = true };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, hide);
        }

        internal static int CountSheetFrames(string texturePath)
        {
            if (Main.dedServ)
                return FullSheetFrames;   // на сервере кадры не рисуются, число не важно

            Asset<Texture2D> sheet = ModContent.Request<Texture2D>(texturePath, AssetRequestMode.ImmediateLoad);
            return Math.Max(1, sheet.Value.Height / FrameHeight);
        }

        public override void SetDefaults()
        {
            // Хитбокс под корпус, а не под весь кадр: рисунок занимает 66x46,
            // но по краям это разведённые ноги и клешня — по ним попадать нечестно
            NPC.width = 52;
            NPC.height = 36;
            // Ванильная отрисовка опускает спрайт на 4 px ниже коробки; возвращаем обратно,
            // иначе краб стоит утопленным в грунт
            DrawOffsetY = -4f;

            NPC.damage = ContactDamage;
            NPC.defense = DefenseNormal;
            NPC.lifeMax = LifeMax;
            NPC.knockBackResist = 0f;   // сбить с ноги паладина нельзя, это его суть
            NPC.aiStyle = -1;           // весь ИИ свой
            NPC.noGravity = true;       // гравитацию считаем сами: ванильная тут не нужна
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = Item.buyPrice(silver: 12);
            NPC.lavaImmune = true;
        }

        public override void AI()
        {
            // faceTarget: false обязателен. Ванильный TargetClosest() каждый тик ставит
            // spriteDirection = direction по своему правилу и затирает нашу инверсию —
            // паладин разворачивался лицом к игроку прямо посреди тарана
            NPC.TargetClosest(false);
            Player target = Main.player[NPC.target];
            if (!target.active || target.dead)
            {
                NPC.EncourageDespawn(30);
                Walk(0f);
                ApplyGravity();
                return;
            }

            // Без короля гвардии незачем сражаться — но ИИ на этом не обрывается.
            // Раньше здесь стоял return до гравитации, и паладин, призванный отдельно
            // (из консоли или чит-меню), висел в воздухе бревном до самого деспавна
            if (!NPC.AnyNPCs(ModContent.NPCType<King_crab>()))
                NPC.EncourageDespawn(30);

            UpdateStats();
            ApplyGravity();

            if (Timer > 0f)
                Timer--;

            switch (State)
            {
                case PaladinState.ShieldBash: AIShieldBash(target); break;
                case PaladinState.ChargeWindup: AIChargeWindup(target); break;
                case PaladinState.Charge: AICharge(target); break;
                case PaladinState.Stagger: AIStagger(); break;
                case PaladinState.Slam: AISlam(target); break;
                default: AIMarch(target); break;
            }
        }

        private void UpdateStats()
        {
            NPC.defense = State == PaladinState.Stagger ? 0 : DefenseNormal;
            NPC.damage = State switch
            {
                PaladinState.Charge => ChargeContactDamage,
                PaladinState.ShieldBash when SubState >= 1f => BashContactDamage,
                PaladinState.Slam when SubState >= 1f => BashContactDamage,
                _ => ContactDamage
            };
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
            // Лист нарисован мордой ВПРАВО, а ваниль отражает спрайт при spriteDirection == 1.
            // Инверсия и есть тот самый разворот: без неё краб идёт спиной вперёд
            NPC.spriteDirection = -look;
        }

        // Ход с подскоком через уступ: свой ИИ не умеет карабкаться, а застрять
        // на первом же камне для «прёт напролом» — приговор
        private void Walk(float speed)
        {
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, speed, 0.2f);
            if (speed != 0f && NPC.collideX && OnGround)
                NPC.velocity.Y = -StepUpSpeed;
        }

        // --- Наступление ---

        private void AIMarch(Player target)
        {
            FaceTarget(target);
            Walk(NPC.direction * MarchSpeed);

            if (Timer > 0f || Main.netMode == NetmodeID.MultiplayerClient)
                return;

            ChooseAction(target);
        }

        // Веса, а не рулетка по кругу: близко — щит в лицо, средняя дистанция — таран,
        // игрок выше или далеко — прыжок. Повтор последнего действия не запрещён,
        // а ослаблен: полный запрет читается игроком так же легко, как порядок по кругу
        private void ChooseAction(Player target)
        {
            float dx = Math.Abs(target.Center.X - NPC.Center.X);
            float dy = target.Center.Y - NPC.Center.Y;
            float wounded = 1f - NPC.life / (float)NPC.lifeMax;

            float bash = dx < BashRange ? 3.2f : 0.15f;
            float charge = dx > ChargeRangeMin && dx < ChargeRangeMax && Math.Abs(dy) < 140f
                ? 2.6f + wounded * 1.6f
                : 0.4f;
            float slam = dy < -60f || dx > SlamRangeMin ? 2.2f : 0.7f;
            float march = 1f;

            if (!OnGround)
            {
                // В воздухе решать нечего — доводим шаг до земли
                EnterState(PaladinState.March, 12f);
                return;
            }

            bash *= WeightFor(PaladinState.ShieldBash);
            charge *= WeightFor(PaladinState.ChargeWindup);
            slam *= WeightFor(PaladinState.Slam);

            float roll = Main.rand.NextFloat(bash + charge + slam + march);
            if ((roll -= bash) < 0f)
                BeginBash();
            else if ((roll -= charge) < 0f)
                BeginCharge();
            else if (roll - slam < 0f)
                BeginSlam();
            else
                EnterState(PaladinState.March, Main.rand.Next(MarchMinTicks, MarchMaxTicks));
        }

        private float WeightFor(PaladinState action) => _lastAction == action ? RepeatWeightMult : 1f;

        private void BeginBash()
        {
            _lastAction = PaladinState.ShieldBash;
            EnterState(PaladinState.ShieldBash, BashWindupTicks);
        }

        private void BeginCharge()
        {
            _lastAction = PaladinState.ChargeWindup;
            Feint = Main.rand.Next(100) < FeintChancePercent;
            EnterState(PaladinState.ChargeWindup, ChargeWindupTicks);
        }

        private void BeginSlam()
        {
            _lastAction = PaladinState.Slam;
            EnterState(PaladinState.Slam, SlamCrouchTicks);
        }

        // --- Удар щитом ---

        private void AIShieldBash(Player target)
        {
            if (SubState == 0f)
            {
                FaceTarget(target);
                Walk(NPC.direction * -0.6f);   // короткий замах назад
                if (!Main.dedServ && Timer % 6f == 0f)
                    GuardDust(2);

                if (Timer <= 0f)
                {
                    SoundEngine.PlaySound(SoundID.Item14 with { Pitch = 0.4f, Volume = 0.5f }, NPC.Center);
                    NPC.velocity.X = NPC.direction * BashSpeed;
                    EnterSubState(1f, BashLungeTicks);
                }
                return;
            }

            if (SubState == 1f)
            {
                if (Timer <= 0f)
                    EnterSubState(2f, BashRecoverTicks);
                return;
            }

            Walk(0f);
            if (Timer <= 0f)
                EnterState(PaladinState.March, Main.rand.Next(MarchMinTicks, MarchMaxTicks));
        }

        // --- Таран ---

        private void AIChargeWindup(Player target)
        {
            FaceTarget(target);
            Walk(0f);

            if (!Main.dedServ && Timer % 3f == 0f)
            {
                Dust sand = Dust.NewDustDirect(NPC.Bottom - new Vector2(NPC.width / 2f, 6f),
                    NPC.width, 6, DustID.Sand);
                sand.velocity = new Vector2(-NPC.direction * Main.rand.NextFloat(2f, 5f), -Main.rand.NextFloat(1f, 3f));
                sand.scale = Main.rand.NextFloat(1f, 1.6f);
            }

            if (Timer > 0f)
                return;

            // Обманка: замах был на таран, а вышел прыжок. Именно она не даёт
            // выучить «увидел рытьё — отошёл вбок»
            if (Feint)
            {
                Feint = false;
                BeginSlam();
                return;
            }

            SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.6f, Volume = 0.6f }, NPC.Center);
            NPC.velocity.X = NPC.direction * ChargeSpeed;
            EnterState(PaladinState.Charge, ChargeMaxTicks);
        }

        private void AICharge(Player target)
        {
            NPC.velocity.X = NPC.direction * ChargeSpeed;

            if (!Main.dedServ && Main.rand.NextBool(2))
            {
                Dust trail = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
                trail.velocity = new Vector2(-NPC.direction * 2f, -Main.rand.NextFloat(0.5f, 2f));
                trail.noGravity = true;
            }

            // Влетел в стену — застрял. Это единственное окно, где паладин открыт целиком
            if (NPC.collideX)
            {
                Stagger();
                return;
            }

            // Проскочил мимо — тормозит сам, без наказания
            bool overshot = Math.Sign(target.Center.X - NPC.Center.X) != NPC.direction
                            && Math.Abs(target.Center.X - NPC.Center.X) > 220f;
            if (overshot || Timer <= 0f)
            {
                NPC.velocity.X *= 0.4f;
                EnterState(PaladinState.March, Main.rand.Next(MarchMinTicks, MarchMaxTicks));
            }
        }

        private void Stagger()
        {
            NPC.velocity.X = 0f;
            SoundEngine.PlaySound(SoundID.NPCHit4 with { Pitch = -0.4f }, NPC.Center);
            if (!Main.dedServ)
            {
                for (int i = 0; i < 18; i++)
                {
                    Dust chip = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Stone);
                    chip.velocity = new Vector2(-NPC.direction * Main.rand.NextFloat(1f, 5f), -Main.rand.NextFloat(1f, 4f));
                    chip.scale = Main.rand.NextFloat(1f, 1.7f);
                }
            }
            EnterState(PaladinState.Stagger, StaggerTicks);
        }

        private void AIStagger()
        {
            Walk(0f);
            if (!Main.dedServ && Timer % 8f == 0f)
            {
                Dust daze = Dust.NewDustDirect(NPC.Top - new Vector2(NPC.width / 2f, 8f), NPC.width, 8,
                    DustID.GoldCoin);
                daze.noGravity = true;
                daze.velocity = new Vector2(Main.rand.NextFloatDirection(), -0.6f);
            }

            if (Timer <= 0f)
                EnterState(PaladinState.March, Main.rand.Next(MarchMinTicks, MarchMaxTicks));
        }

        // --- Прыжок с накрытием ---

        private void AISlam(Player target)
        {
            if (SubState == 0f)
            {
                FaceTarget(target);
                Walk(0f);
                if (Timer > 0f)
                    return;

                float toTarget = target.Center.X - NPC.Center.X;
                NPC.velocity = new Vector2(MathHelper.Clamp(toTarget * 0.035f, -9f, 9f), -11f);
                SoundEngine.PlaySound(SoundID.Item32 with { Pitch = -0.3f }, NPC.Center);
                EnterSubState(1f, SlamMaxAirTicks);
                return;
            }

            // В воздухе управления нет: прыжок читается заранее и по нему уходят вбок
            if (NPC.velocity.Y >= 0f && OnGround || Timer <= 0f)
                Land();
        }

        private void Land()
        {
            NPC.velocity.X = 0f;
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.5f }, NPC.Center);

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    int wave = Projectile.NewProjectile(NPC.GetSource_FromAI(),
                        NPC.Bottom + new Vector2(side * 20f, -8f), new Vector2(side * 5.5f, 0f),
                        ModContent.ProjectileType<KingCrabShockwave>(), BashContactDamage / 2, 3f, Main.myPlayer);
                    if (wave < Main.maxProjectiles && Main.netMode == NetmodeID.Server)
                        NetMessage.SendData(MessageID.SyncProjectile, number: wave);
                }
            }

            if (!Main.dedServ)
            {
                for (int i = 0; i < 24; i++)
                {
                    Dust burst = Dust.NewDustDirect(NPC.Bottom - new Vector2(NPC.width / 2f, 4f), NPC.width, 6,
                        DustID.Sand);
                    burst.velocity = new Vector2(Main.rand.NextFloatDirection() * 5f, -Main.rand.NextFloat(1f, 5f));
                    burst.scale = Main.rand.NextFloat(1.2f, 2f);
                }
            }

            EnterState(PaladinState.March, Main.rand.Next(MarchMinTicks, MarchMaxTicks));
        }

        // --- Служебное ---

        private void EnterState(PaladinState state, float duration)
        {
            State = state;
            Timer = duration;
            SubState = 0f;
            NPC.netUpdate = true;
        }

        private void EnterSubState(float subState, float duration)
        {
            SubState = subState;
            Timer = duration;
            NPC.netUpdate = true;
        }

        private void GuardDust(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Dust spark = Dust.NewDustDirect(NPC.Center + new Vector2(NPC.direction * 18f, -4f),
                    6, 12, DustID.Silver);
                spark.noGravity = true;
                spark.velocity = new Vector2(NPC.direction * Main.rand.NextFloat(0.5f, 2f), Main.rand.NextFloatDirection());
            }
        }

        // --- Урон по паладину ---

        // Щит закрывает ту сторону, куда паладин смотрит. Заходить приходится за спину,
        // а он всё время разворачивается — отсюда и вся возня вокруг него
        private void ApplyGuard(float attackerX, ref NPC.HitModifiers modifiers)
        {
            _blockedLastHit = false;

            if (State == PaladinState.Stagger)
            {
                modifiers.FinalDamage *= StaggerDamageMult;
                return;
            }

            if (!GuardUp)
                return;

            bool fromFront = Math.Sign(attackerX - NPC.Center.X) == NPC.direction;
            if (!fromFront)
                return;

            modifiers.FinalDamage *= GuardDamageMult;
            _blockedLastHit = true;
        }

        public override void ModifyHitByItem(Player player, Item item, ref NPC.HitModifiers modifiers)
            => ApplyGuard(player.Center.X, ref modifiers);

        public override void ModifyHitByProjectile(Projectile projectile, ref NPC.HitModifiers modifiers)
        {
            // Для снаряда «откуда пришёл» честнее считать по скорости, а не по позиции:
            // на момент попадания снаряд уже внутри хитбокса
            float from = projectile.velocity.X != 0f
                ? NPC.Center.X - Math.Sign(projectile.velocity.X)
                : projectile.Center.X;
            ApplyGuard(from, ref modifiers);
        }

        public override void OnHitByItem(Player player, Item item, NPC.HitInfo hit, int damageDone)
            => ReactToHit(damageDone);

        public override void OnHitByProjectile(Projectile projectile, NPC.HitInfo hit, int damageDone)
            => ReactToHit(damageDone);

        private void ReactToHit(int damageDone)
        {
            if (!_blockedLastHit || Main.dedServ)
                return;

            GuardDust(6);
            SoundEngine.PlaySound(SoundID.Dig with { Pitch = 0.8f, Volume = 0.5f }, NPC.Center);

            // Тяжёлый удар в щит всё-таки качает паладина: полностью глухая стена
            // не даёт игроку обратной связи, что он вообще что-то делает
            if (damageDone >= GuardBreakThreshold && State == PaladinState.March)
                NPC.velocity.X -= NPC.direction * 1.5f;
        }

        // Кадр выбирается стейтом, а не скоростью: именно по нему игрок читает,
        // что паладин собрался делать, ещё до того как начал двигаться
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
                NPC.frameCounter = 0;   // новый клип начинается с первого кадра
            }

            // Ход отсчитывается скоростью, всё остальное — тиками: иначе шаг
            // живёт отдельно от ног и краб «плывёт»
            double delta = walking ? Math.Abs(NPC.velocity.X) * 0.55 : 1.0;
            NPC.frame.Y = clip.Advance(ref NPC.frameCounter, delta) * frameHeight;
        }

        private SheetClip ClipForState(out bool walking)
        {
            walking = false;

            switch (State)
            {
                case PaladinState.Stagger:
                    return ClipStagger;

                case PaladinState.ChargeWindup:
                    return ClipChargeWindup;

                case PaladinState.Charge:
                    return ClipCharge;

                case PaladinState.ShieldBash:
                    return SubState == 0f ? ClipBashWindup : ClipBashHit;

                case PaladinState.Slam:
                    if (SubState == 0f)
                        return ClipRise;   // присед перед прыжком берёт первый кадр взлёта
                    return NPC.velocity.Y < 0f ? ClipRise : ClipFall;

                default:
                    if (!OnGround)
                        return NPC.velocity.Y < 0f ? ClipRise : ClipFall;
                    if (Math.Abs(NPC.velocity.X) < 0.15f)
                        return ClipIdle;
                    walking = true;
                    return ClipWalk;
            }
        }

        // Пока лист короткий (6 кадров), работаем по-старому: 0 стойка, 1-5 ход
        private void LegacyFrame(int frameHeight)
        {
            if (State == PaladinState.Stagger || (State == PaladinState.Slam && SubState >= 1f)
                || Math.Abs(NPC.velocity.X) < 0.1f)
            {
                NPC.frameCounter = 0;
                NPC.frame.Y = 0;
                return;
            }

            NPC.frameCounter += Math.Abs(NPC.velocity.X) * 0.3;
            if (NPC.frameCounter > 1000)
                NPC.frameCounter = 0;

            int index = 1 + (int)NPC.frameCounter % (LegacySheetFrames - 1);
            NPC.frame.Y = index * frameHeight;
        }

        public override Color? GetAlpha(Color drawColor)
        {
            // Разгон подсвечивается красным: это единственная атака, от которой
            // нельзя увернуться уже после старта
            if (State == PaladinState.ChargeWindup || State == PaladinState.Charge)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(Main.GameUpdateCount * 0.35f);
                return Color.Lerp(drawColor, new Color(255, 90, 70), 0.45f * pulse);
            }

            // В оглушении бледнеет — видно, что щита нет
            if (State == PaladinState.Stagger)
                return Color.Lerp(drawColor, new Color(190, 200, 230), 0.4f);

            return drawColor;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (Main.netMode == NetmodeID.Server || NPC.life > 0)
                return;

            for (int i = 0; i < 20; i++)
            {
                Dust d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.RedTorch);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 4f, -Main.rand.NextFloat(1f, 4f));
                d.noGravity = true;
            }
        }

        public override void SendExtraAI(System.IO.BinaryWriter writer)
        {
            writer.Write((byte)_lastAction);
        }

        public override void ReceiveExtraAI(System.IO.BinaryReader reader)
        {
            _lastAction = (PaladinState)reader.ReadByte();
        }
    }
}
