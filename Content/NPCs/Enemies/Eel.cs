using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SoA.Common.Graphics;
using SoA.Content.Items.Materials;
using SoA.Content.Biomes;

namespace SoA.Content.NPCs.Enemies
{
    // Угорь: кружит вокруг жертвы змейкой, заряжается и бьёт рывком, после разряда
    // на мгновение беззащитен. Хитбокс только на голове — тело рисуется мешем по следу головы
    internal class Eel : ModNPC
    {
        private enum EelState
        {
            Swim,
            Windup,
            Dash,
            Recover
        }

        private const int FrameCount = 8;
        private const int HitboxSize = 28;

        private const float SwimSpeed = 6.5f;
        private const float SwimInertia = 20f;
        private const float OrbitRadius = 240f;
        private const float WeaveFrequency = 0.07f;
        private const float WeaveAngle = 0.55f;
        private const float DashSpeed = 26f;
        private const float DashRange = 520f;
        private const int WindupTicks = 45;
        private const int DashTicks = 30;
        private const int RecoverTicks = 45;
        private const int CooldownTicks = 150;
        private const int DashDamageBonus = 14;
        private const float SpawnChanceInTideWater = 0.3f;
        private const float FlopDetectVelocityY = -3f;

        // Тело: точки позвоночника от центра головы к хвосту. Спрайт 192 px в масштабе 2x,
        // центр головы на 16 px от кончика морды
        private const int SpinePoints = 23;
        private const float SpineSpacing = 8f;
        private const float HeadCenterFrameX = 16f;
        private const float SpineResetDistance = 400f;
        private const float FacingThreshold = 0.25f;
        private const float CoilAmplitude = 9f;
        private const float CoilWaves = 1.25f;
        private const float DashTrailWidth = 16f;
        private const int LightEveryNthPoint = 4;

        private static readonly Color GlowColor = new(110, 215, 255);
        private static readonly string[] GoreNames = ["EelGoreHead", "EelGoreBody", "EelGoreTail"];
        private static Texture2D glowMask;

        // Состояние, таймер и сторона облёта в NPC.ai — ваниль сама синхронизирует их в мультиплеере
        private EelState State
        {
            get => (EelState)NPC.ai[0];
            set => NPC.ai[0] = (float)value;
        }

        private ref float Timer => ref NPC.ai[1];
        private ref float OrbitDirection => ref NPC.ai[2];

        // Чисто клиентская косметика, по сети не ходит. Массив создаётся лениво:
        // ModNPC клонируется поверхностно, инициализатор поля разделил бы его между угрями
        private Vector2[] spine;
        private bool spineReady;
        private int bodyFacing = -1;
        private float lastFlopVelocityY;

        private float WindupProgress => State == EelState.Windup ? 1f - Timer / WindupTicks : 0f;
        private float DashStrength => State == EelState.Dash ? Timer / DashTicks : 0f;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = FrameCount;
            NPCID.Sets.NPCBestiaryDrawModifiers value = new()
            {
                Velocity = 1f
            };
            NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, value);
        }

        public override void SetDefaults()
        {
            NPC.width = HitboxSize;
            NPC.height = HitboxSize;
            // Уровень до Короля Краба: контакт не выше, чем у самого босса (26)
            NPC.damage = 26;
            NPC.defense = 10;
            NPC.lifeMax = 200;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = 150f;
            NPC.knockBackResist = 0.8f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;

            SpawnModBiomes = [ModContent.GetInstance<TideOfShadowsBiome>().Type];
        }

        public override void Unload()
        {
            Texture2D mask = glowMask;
            glowMask = null;
            if (mask != null)
                Main.QueueMainThreadAction(mask.Dispose);
        }

        public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(
            [
                BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Ocean,
                new FlavorTextBestiaryInfoElement("Mods.SoA.NPCs.Eel.Bestiary")
            ]);
        }

        public override void FindFrame(int frameHeight)
        {
            // Волна по телу бежит быстрее, когда угорь плывёт быстрее
            NPC.frameCounter += 1.0 + NPC.velocity.Length() * 0.12;
            if (NPC.frameCounter >= 8.0)
            {
                NPC.frameCounter = 0.0;
                NPC.frame.Y = (NPC.frame.Y + frameHeight) % (frameHeight * FrameCount);
            }
        }

        public override float SpawnChance(NPCSpawnInfo spawnInfo)
        {
            // SpawnModBiomes влияет только на бестиарий, биом проверяем сами
            if (!spawnInfo.Player.InModBiome<TideOfShadowsBiome>() || !spawnInfo.Water)
                return 0f;
            return SpawnChanceInTideWater;
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<Ichthyofang>(), 4, 1, 3));
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            target.AddBuff(BuffID.Electrified, 150);
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.netMode == NetmodeID.Server)
                return;

            // Куски — голова, середина и хвост там, где их видно на изогнутом теле
            for (int i = 0; i < GoreNames.Length; i++)
            {
                Vector2 piecePosition = BodyPoint(i / (float)(GoreNames.Length - 1));
                Gore.NewGore(NPC.GetSource_Death(), piecePosition, NPC.velocity * 0.5f,
                    Mod.Find<ModGore>(GoreNames[i]).Type);
            }

            for (int i = 0; i < 14; i++)
            {
                Vector2 sparkPosition = BodyPoint(Main.rand.NextFloat());
                Dust spark = Dust.NewDustDirect(sparkPosition - new Vector2(4f), 8, 8, DustID.Electric,
                    2.5f * Main.rand.NextFloatDirection(), -2.5f, 0, default, 0.7f);
                spark.noGravity = true;
            }
        }

        public override void AI()
        {
            NPC.defense = State == EelState.Recover ? 0 : NPC.defDefense;

            if (!NPC.wet)
            {
                UpdateOutOfWater();
                return;
            }

            NPC.TargetClosest(faceTarget: false);
            Player target = Main.player[NPC.target];

            if (target.dead || !target.active)
            {
                // Цели нет — медленно уплывает и деспавнится
                NPC.EncourageDespawn(120);
                Vector2 drift = new(NPC.direction * SwimSpeed * 0.6f, -1.5f);
                NPC.velocity = (NPC.velocity * (SwimInertia - 1f) + drift) / SwimInertia;
                return;
            }

            // Сторону облёта выбирает сервер, иначе клиенты кружили бы угря в разные стороны
            if (OrbitDirection == 0f && Main.netMode != NetmodeID.MultiplayerClient)
            {
                OrbitDirection = Main.rand.NextBool() ? 1f : -1f;
                NPC.netUpdate = true;
            }

            switch (State)
            {
                case EelState.Swim:
                    UpdateSwim(target);
                    break;
                case EelState.Windup:
                    UpdateWindup(target);
                    break;
                case EelState.Dash:
                    UpdateDash();
                    break;
                case EelState.Recover:
                    UpdateRecover();
                    break;
            }
        }

        private void UpdateSwim(Player target)
        {
            NPC.damage = NPC.defDamage;
            OrbitTowards(target.Center);
            FaceTowards(NPC.Center + NPC.velocity);

            if (Timer > 0f)
            {
                Timer--;
                return;
            }

            if (Vector2.Distance(NPC.Center, target.Center) < DashRange
                && Collision.CanHitLine(NPC.position, NPC.width, NPC.height,
                    target.position, target.width, target.height))
            {
                State = EelState.Windup;
                Timer = WindupTicks;
                SoundEngine.PlaySound(SoundID.DD2_LightningBugZap with { Volume = 0.7f, Pitch = -0.3f }, NPC.Center);
                NPC.netUpdate = true;
            }
        }

        // Телеграф: отплывает назад, сворачиваясь в S, по телу от хвоста к голове бежит заряд
        private void UpdateWindup(Player target)
        {
            Timer--;
            NPC.damage = NPC.defDamage;

            Vector2 away = (NPC.Center - target.Center).SafeNormalize(Vector2.UnitX);
            if (away.Y < 0f && HeadOutOfWater())
                away.Y = 0.5f;
            NPC.velocity = (NPC.velocity * 9f + away * 2f) / 10f;
            FaceTowards(target.Center);

            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                Vector2 sparkPosition = BodyPoint(1f - WindupProgress);
                Dust spark = Dust.NewDustDirect(sparkPosition - new Vector2(6f), 12, 12, DustID.Electric);
                spark.velocity *= 0.3f;
                spark.scale = 0.8f;
                spark.noGravity = true;
            }

            if (Timer > 0f)
                return;

            State = EelState.Dash;
            Timer = DashTicks;
            // Упреждение: целимся туда, где игрок окажется через треть секунды
            Vector2 lead = target.Center + target.velocity * 20f;
            NPC.velocity = (lead - NPC.Center).SafeNormalize(Vector2.UnitX) * DashSpeed;
            NPC.damage = NPC.defDamage + DashDamageBonus;
            SoundEngine.PlaySound(SoundID.Item96 with { Volume = 0.6f, Pitch = 0.4f }, NPC.Center);
            NPC.netUpdate = true;
        }

        // Скорость не перезаписываем — рывок плавно затухает, нокбэк и тайлы работают честно
        private void UpdateDash()
        {
            Timer--;
            NPC.velocity *= 0.98f;
            FaceTowards(NPC.Center + NPC.velocity);
            SpawnDashSpark();

            if (Timer <= 0f)
                EnterRecover();
        }

        // Разряжен: защита 0 (см. начало AI), свечение гаснет, дрейфует по инерции
        private void UpdateRecover()
        {
            Timer--;
            NPC.damage = NPC.defDamage;
            NPC.velocity *= 0.92f;

            if (Timer > 0f)
                return;

            State = EelState.Swim;
            Timer = CooldownTicks;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                OrbitDirection = Main.rand.NextBool() ? 1f : -1f;
                NPC.netUpdate = true;
            }
        }

        private void EnterRecover()
        {
            State = EelState.Recover;
            Timer = RecoverTicks;
        }

        private void UpdateOutOfWater()
        {
            // Рывок продолжается и вне воды: выпрыгивает по дуге и падает обратно
            if (State == EelState.Dash && Timer > 0f)
            {
                Timer--;
                NPC.velocity.Y += 0.25f;
                FaceTowards(NPC.Center + NPC.velocity);
                SpawnDashSpark();

                if (Timer <= 0f)
                    EnterRecover();
                return;
            }

            // Иначе бьётся на суше, как рыба: подпрыгивает в случайные стороны
            State = EelState.Swim;
            NPC.damage = NPC.defDamage;
            NPC.velocity.Y = Math.Min(NPC.velocity.Y + 0.3f, 10f);

            // Прыжок пришёл от сервера пакетом — клиент узнаёт его по резкой смене скорости
            if (Main.netMode == NetmodeID.MultiplayerClient && lastFlopVelocityY >= 0f && NPC.velocity.Y < FlopDetectVelocityY)
                PlayFlopEffects();

            // Случайный прыжок решает только сервер, клиенты получают его через netUpdate
            bool grounded = NPC.velocity.Y == 0f || NPC.collideY;
            if (grounded && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.velocity.Y = Main.rand.NextFloat(-6.5f, -3.5f);
                NPC.velocity.X = Main.rand.NextFloat(2f, 5f) * (Main.rand.NextBool() ? 1 : -1);
                FaceTowards(NPC.Center + NPC.velocity);
                NPC.netUpdate = true;
                PlayFlopEffects();
            }

            lastFlopVelocityY = NPC.velocity.Y;
        }

        // Облёт по кругу радиуса OrbitRadius: касательная плюс поправка к кругу,
        // поверх — змеиное виляние. Издалека поправка перевешивает, и угорь подходит по спирали
        private void OrbitTowards(Vector2 center)
        {
            Vector2 fromCenter = NPC.Center - center;
            Vector2 radial = fromCenter.SafeNormalize(Vector2.UnitX);
            Vector2 tangent = radial.RotatedBy(MathHelper.PiOver2 * OrbitDirection);
            float radialError = (fromCenter.Length() - OrbitRadius) / OrbitRadius;

            Vector2 direction = (tangent - radial * MathHelper.Clamp(radialError * 2f, -1f, 3f)).SafeNormalize(tangent);
            float weave = (float)Math.Sin((Main.GameUpdateCount + NPC.whoAmI * 37) * WeaveFrequency) * WeaveAngle;
            Vector2 desired = direction.RotatedBy(weave) * SwimSpeed;

            // Обычным плаванием из воды не выталкиваемся: у поверхности прижимаемся вниз
            if (desired.Y < 0f && HeadOutOfWater())
                desired.Y = 1f;

            NPC.velocity = (NPC.velocity * (SwimInertia - 1f) + desired) / SwimInertia;
        }

        private void SpawnDashSpark()
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            Dust trail = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Electric);
            trail.velocity = NPC.velocity * 0.2f;
            trail.scale = 0.9f;
            trail.noGravity = true;
        }

        private void PlayFlopEffects()
        {
            if (Main.netMode == NetmodeID.Server)
                return;

            SoundEngine.PlaySound(SoundID.NPCHit1 with { Volume = 0.3f, Pitch = 0.6f }, NPC.Center);
            for (int i = 0; i < 4; i++)
            {
                Dust splash = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Water);
                splash.velocity = new Vector2(Main.rand.NextFloatDirection() * 2f, -Main.rand.NextFloat(1f, 3f));
            }
        }

        private bool HeadOutOfWater()
        {
            return !Collision.WetCollision(NPC.position - new Vector2(0f, 16f), NPC.width, 8);
        }

        private void FaceTowards(Vector2 point)
        {
            NPC.direction = point.X >= NPC.Center.X ? 1 : -1;
            NPC.spriteDirection = NPC.direction;
        }

        public override void PostAI()
        {
            if (Main.dedServ || !spineReady)
                return;

            for (int i = 0; i < SpinePoints; i += LightEveryNthPoint)
                Lighting.AddLight(spine[i], GlowColor.ToVector3() * (GlowIntensity(i / (float)(SpinePoints - 1)) * 0.6f));
        }

        // --- Тело ---

        // Точка на теле: 0 — голова, 1 — кончик хвоста. До первой отрисовки тела нет — берём центр
        private Vector2 BodyPoint(float progress)
        {
            if (!spineReady)
                return NPC.Center;
            return spine[(int)Math.Round(MathHelper.Clamp(progress, 0f, 1f) * (SpinePoints - 1))];
        }

        // Тело тянется за головой как верёвка постоянной длины. Считается в отрисовке, а не в AI:
        // к этому моменту голова уже сдвинута на скорость тика, и тело не отстаёт от хитбокса
        private void UpdateSpine()
        {
            spine ??= new Vector2[SpinePoints];

            if (!spineReady || Vector2.Distance(spine[0], NPC.Center) > SpineResetDistance)
            {
                Vector2 tailward = new(-(NPC.direction == 0 ? 1 : NPC.direction), 0f);
                for (int i = 0; i < SpinePoints; i++)
                    spine[i] = NPC.Center + tailward * (SpineSpacing * i);
                spineReady = true;
            }

            spine[0] = NPC.Center;
            Vector2 fallback = new(-bodyFacing, 0f);
            for (int i = 1; i < SpinePoints; i++)
                spine[i] = spine[i - 1] + (spine[i] - spine[i - 1]).SafeNormalize(fallback) * SpineSpacing;

            // Сторона, куда смотрит спрайт, меняется с запасом, чтобы на вертикали тело не мигало
            Vector2 headDirection = (spine[0] - spine[1]).SafeNormalize(Vector2.Zero);
            if (headDirection.X > FacingThreshold)
                bodyFacing = 1;
            else if (headDirection.X < -FacingThreshold)
                bodyFacing = -1;
        }

        // Яркость пятен: 0 — голова, 1 — хвост. Спокойное мерцание, волна заряда
        // от хвоста к голове на замахе, полный накал в рывке, почти ноль после разряда
        private float GlowIntensity(float progress)
        {
            float time = Main.GlobalTimeWrappedHourly;
            switch (State)
            {
                case EelState.Windup:
                    float front = 1f - WindupProgress;
                    float wave = (float)Math.Exp(-(progress - front) * (progress - front) / 0.01f);
                    return 0.5f + WindupProgress * 0.5f + wave;
                case EelState.Dash:
                    return 1.1f;
                case EelState.Recover:
                    return 0.08f + 0.12f * Math.Max(0f, (float)Math.Sin(time * 40f + progress * 9f));
                default:
                    return 0.45f + 0.2f * (float)Math.Sin(time * 2.5f + progress * 8f);
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D texture = TextureAssets.Npc[Type].Value;

            // В бестиарии — обычный кадр: позвоночника у иконки нет, и её батч не мировой
            if (NPC.IsABestiaryIconDummy)
            {
                SpriteEffects effects = NPC.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(texture, NPC.Center - screenPos, NPC.frame, drawColor, 0f,
                    NPC.frame.Size() / 2f, NPC.scale, effects, 0f);
                return false;
            }

            UpdateSpine();

            // Перед позвоночником — кончик морды, он на HeadCenterFrameX впереди центра головы
            Span<Vector2> points = stackalloc Vector2[SpinePoints + 1];
            Span<float> frameX = stackalloc float[SpinePoints + 1];
            Span<float> offsets = stackalloc float[SpinePoints + 1];
            float[] glow = new float[SpinePoints + 1];

            Vector2 headDirection = (spine[0] - spine[1]).SafeNormalize(new Vector2(bodyFacing, 0f));
            points[0] = spine[0] + headDirection * HeadCenterFrameX;
            frameX[0] = 0f;
            float coil = WindupProgress;
            for (int i = 0; i <= SpinePoints; i++)
            {
                if (i > 0)
                {
                    points[i] = spine[i - 1];
                    frameX[i] = Math.Min(HeadCenterFrameX + (i - 1) * SpineSpacing, NPC.frame.Width);
                }

                float progress = frameX[i] / NPC.frame.Width;
                offsets[i] = CoilAmplitude * coil * (float)Math.Sin(progress * MathHelper.TwoPi * CoilWaves) * progress;
                glow[i] = GlowIntensity(progress);
            }

            Vector2[] lightPoints = points.ToArray();
            bool flip = bodyFacing == 1;

            SoAVfx.BeginPixelImmediate(spriteBatch);

            float dash = DashStrength;
            if (dash > 0f)
            {
                SoATrail.Draw(points,
                    progress => DashTrailWidth * (1f - progress),
                    progress => GlowColor * (dash * (1f - progress)),
                    TrailStyle.Tide);
            }

            SpriteSpine.Draw(texture, NPC.frame, points, frameX, offsets, flip, i =>
            {
                Color light = Lighting.GetColor(lightPoints[i].ToTileCoordinates());
                return NPC.GetAlpha(NPC.GetNPCColorTintedByBuffs(light));
            });

            SpriteSpine.Draw(GetGlowMask(texture), NPC.frame, points, frameX, offsets, flip,
                i => SoAVfx.Additive(GlowColor * MathHelper.Clamp(glow[i], 0f, 1f)));

            SoAVfx.EndPixelBatch(spriteBatch);

            // Ореол у головы нарастает к концу замаха — последнее предупреждение перед рывком
            if (coil > 0f)
            {
                Texture2D halo = SoAVfx.SoftGlow;
                float size = 70f * coil;
                Main.EntitySpriteDraw(halo, points[0] - screenPos, null, SoAVfx.Additive(GlowColor * (0.55f * coil)), 0f,
                    halo.Size() / 2f, size / halo.Width, SpriteEffects.None, 0);
            }

            return false;
        }

        // Маска свечения собирается из самого спрайта: голубые пятна — светящиеся, остальное прозрачно.
        // Отдельный файл маски не нужен, и она не разойдётся со спрайтом при перерисовке
        private static Texture2D GetGlowMask(Texture2D source)
        {
            if (glowMask != null && !glowMask.IsDisposed)
                return glowMask;

            Color[] pixels = new Color[source.Width * source.Height];
            source.GetData(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                bool glowing = pixel.A > 200 && pixel.G >= 150 && pixel.B >= 150 && pixel.G > pixel.R + 30;
                if (!glowing)
                    pixels[i] = Color.Transparent;
            }

            glowMask = new Texture2D(Main.graphics.GraphicsDevice, source.Width, source.Height);
            glowMask.SetData(pixels);
            return glowMask;
        }
    }
}
