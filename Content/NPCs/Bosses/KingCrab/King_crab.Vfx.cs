using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Визуальный слой Короля-краба: свечение/аура, афтеримиджи на рывке-прыжке, кольца ударов.
    // Чистая косметика — считается локально на клиентах из синхронизированных позиции/стадии,
    // по сети ничего не шлём. Вызывается из PreDraw (King_crab.Legs.cs).
    public partial class King_crab
    {
        private const int AfterimageMax = 4;       // длина шлейфа: 3–4 копии силуэта, не рой панцирей
        private const int MaxRings = 6;            // одновременных колец удара
        private const int MaxBursts = 4;           // одновременных пылевых всплесков
        private const int MaxFlashes = 6;
        private const int MaxCracks = 8;
        private const int CrackLifeTicks = 40;
        private const float CrackProbeDepth = 64f; // как глубоко под точкой удара искать грунт
        private const int SandyFeetTicks = 60;     // сколько с лап сыплется песок после подкопа
        private const int WetTicks = 180;          // сколько с панциря капает вода
        private const int ShellGrainLife = 200;    // сколько песчинки держатся на панцире

        // Снимок позы для шлейфа: тело + мировые точки запястий. Раньше писались только
        // NPC.Center и NPC.rotation, а рисовался один панцирь — за крабом летел рой
        // одиноких панцирей, оторванных от анимированного тела.
        private struct Ghost
        {
            public Vector2 Center;
            public float Rot;
            public Vector2 Scale;
            public Vector2 Wrist0;
            public Vector2 Wrist1;
        }

        private readonly List<Ghost> _afterimages = new();
        private readonly List<ImpactRing> _rings = new();
        private readonly List<BurrowBurst> _bursts = new();

        private struct ImpactRing
        {
            public Vector2 Pos;
            public float Age;
            public float Duration;
            public float MaxSize;
            public float Blend; // 0 — голубое, 1 — фиолетовое
        }

        private struct BurrowBurst
        {
            public Vector2 Base;   // точка на поверхности грунта (низ квада)
            public float Age;
            public float Duration;
            public float Width;    // размеры квада в px
            public float Height;
            public Vector3 Color;  // цвет грунта в точке всплеска
        }

        // Быстрые стадии, на которых тянем шлейф: рывок, прыжок в воздухе, вылет из-под земли
        private bool IsFastState()
        {
            // Под землёй шлейфа быть не может. Проверка обязательна первой: подземный ход идёт
            // на 13 px/тик, и нижнее условие по скорости само его подхватывало
            if (BurrowBuried)
                return false;
            if (State == CrabState.ClawSweep && SubState != 0f)
                return true;
            if (State == CrabState.JumpCrush && SubState >= 1f)
                return true;
            if (State == CrabState.Burrow && SubState >= 2f)
                return true;
            if (State == CrabState.KnightCourt && SubState >= 2f)
                return true;
            return Math.Abs(NPC.velocity.X) > 9f;
        }

        // --- Кольца ударов (публичный триггер зовётся из AI на импактах) ---

        private void TriggerImpactRing(Vector2 pos, float maxSize, float duration, float blend = 0f)
        {
            if (Main.dedServ)
                return;
            if (_rings.Count >= MaxRings)
                _rings.RemoveAt(0);
            _rings.Add(new ImpactRing { Pos = pos, Age = 0f, Duration = duration, MaxSize = maxSize, Blend = blend });
        }

        private void UpdateRings()
        {
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                ImpactRing r = _rings[i];
                r.Age += 1f;
                if (r.Age >= r.Duration)
                {
                    _rings.RemoveAt(i);
                    continue;
                }
                _rings[i] = r;
            }
        }

        private void DrawRings(SpriteBatch sb)
        {
            foreach (ImpactRing r in _rings)
            {
                float p = r.Age / r.Duration; // 0..1 = радиус кольца
                float opacity = MathHelper.Clamp(1f - p, 0f, 1f) * 0.9f;
                SoAVfx.DrawRing(sb, r.Pos, r.MaxSize, p, opacity, r.Blend);
            }
        }

        // --- Пылевые всплески закапывания/выныривания (шейдер SoA:BurrowBurst) ---

        // basePos — точка на поверхности грунта; цвет пыли берём из тайла под ней
        private void TriggerBurrowBurst(Vector2 basePos, float widthPx, float heightPx, float duration)
        {
            if (Main.dedServ)
                return;
            if (_bursts.Count >= MaxBursts)
                _bursts.RemoveAt(0);
            _bursts.Add(new BurrowBurst
            {
                Base = basePos,
                Age = 0f,
                Duration = duration,
                Width = widthPx,
                Height = heightPx,
                Color = GetGroundTint(basePos)
            });
        }

        // Тайл грунта под точкой — общий источник и для цвета пыли, и для типа частиц
        private static ushort GroundTileType(Vector2 worldPos)
        {
            int tx = (int)(worldPos.X / 16f);
            int ty = (int)(worldPos.Y / 16f) + 1; // на тайл ниже поверхности — сам грунт
            Tile tile = Framing.GetTileSafely(tx, ty);
            if (!tile.HasTile)
                tile = Framing.GetTileSafely(tx, ty + 1);
            return tile.TileType;
        }

        // Тип пылинки по грунту: песок шага должен быть песком, а снег — снегом
        private static int GroundDustType(Vector2 worldPos) => GroundTileType(worldPos) switch
        {
            TileID.Pearlsand => DustID.Pearlsand,
            // Порченый и багровый песок отдельной пылинки не имеют — берём песок,
            // а цвет ему выставит GetGroundTint
            TileID.SnowBlock or TileID.IceBlock => DustID.Snow,
            TileID.Stone or TileID.Sandstone => DustID.Stone,
            TileID.Dirt or TileID.Grass or TileID.Mud or TileID.JungleGrass => DustID.Dirt,
            _ => DustID.Sand,
        };

        // Цвет пыли по типу грунта в точке: песок/снег/земля/камень и т.д.
        private static Vector3 GetGroundTint(Vector2 worldPos)
        {
            switch (GroundTileType(worldPos))
            {
                case TileID.Sand:
                case TileID.HardenedSand:
                case TileID.Sandstone:
                    return new Vector3(0.82f, 0.72f, 0.45f);
                case TileID.Pearlsand:
                    return new Vector3(0.85f, 0.80f, 0.75f);
                case TileID.Ebonsand:
                    return new Vector3(0.45f, 0.40f, 0.50f);
                case TileID.Crimsand:
                    return new Vector3(0.60f, 0.35f, 0.28f);
                case TileID.SnowBlock:
                case TileID.IceBlock:
                    return new Vector3(0.85f, 0.92f, 1.0f);
                case TileID.Dirt:
                case TileID.Grass:
                case TileID.Mud:
                case TileID.JungleGrass:
                    return new Vector3(0.55f, 0.42f, 0.30f);
                case TileID.Stone:
                    return new Vector3(0.55f, 0.55f, 0.58f);
                default:
                    return new Vector3(0.65f, 0.58f, 0.45f); // нейтральный пыльный
            }
        }

        private void UpdateBursts()
        {
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                BurrowBurst b = _bursts[i];
                b.Age += 1f;
                if (b.Age >= b.Duration)
                {
                    _bursts.RemoveAt(i);
                    continue;
                }
                _bursts[i] = b;
            }
        }

        // Рисовать внутри BeginAlphaImmediate/EndAdditive: пыль непрозрачная, не светится
        private void DrawBursts(SpriteBatch sb)
        {
            MiscShaderData shader = GameShaders.Misc["SoA:BurrowBurst"];
            Texture2D noise = SoAVfx.Noise;
            Vector2 origin = new Vector2(noise.Width / 2f, noise.Height); // низ-центр = поверхность

            foreach (BurrowBurst b in _bursts)
            {
                shader.UseOpacity(0.85f);
                shader.UseColor(b.Color); // uColor ставится в Apply из внутреннего поля
                shader.Shader.Parameters["uProgress"]?.SetValue(b.Age / b.Duration);
                shader.Apply();
                Main.EntitySpriteDraw(noise, b.Base - Main.screenPosition, null, Color.White, 0f,
                    origin, new Vector2(b.Width / noise.Width, b.Height / noise.Height), SpriteEffects.None, 0);
            }
        }

        // Вертикальный водяной столб — драматический акцент на смене фаз/финале
        private void SpawnWaterColumn(int count)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = NPC.Bottom + new Vector2(Main.rand.NextFloat(-45f, 45f), 0f);
                Dust d = Dust.NewDustPerfect(p, Main.rand.NextBool(3) ? DustID.RedTorch : DustID.Water,
                    new Vector2(Main.rand.NextFloatDirection() * 1.5f, -Main.rand.NextFloat(6f, 14f)));
                d.noGravity = Main.rand.NextBool();
                d.scale = Main.rand.NextFloat(1.2f, 2.2f);
            }
        }

        // Кик-ап песка из-под краба — общий эмиттер для всех наземных ударов/закапываний
        private void SpawnSandBurst(Vector2 rectPos, int width, int height, int count,
            float spread, float upMin, float upMax, float scaleMin = 1.2f, float scaleMax = 2f,
            int dustType = DustID.Sand)
        {
            if (Main.netMode == NetmodeID.Server)
                return;
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustDirect(rectPos, width, height, dustType);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * spread, -Main.rand.NextFloat(upMin, upMax));
                d.scale = Main.rand.NextFloat(scaleMin, scaleMax);
            }
        }

        // --- Афтеримиджи (шлейф тела) ---

        private void RecordAfterimage()
        {
            // Скрылся под грунтом — шлейф гасим ЦЕЛИКОМ и сразу. Иначе уже записанные копии
            // продолжают рисоваться: тело — сквозь землю, а клешни — по последним точкам
            // запястий, оставшимся на поверхности, и в воздухе висят две оторванные клешни
            if (BurrowBuried)
            {
                _afterimages.Clear();
                return;
            }

            if (IsFastState())
            {
                float sx = 1f + _bodySquash * BodySquashAmount;
                float sy = 1f - _bodySquash * BodySquashAmount;

                // На рывке копии РАСТЯГИВАЮТСЯ по ходу движения. Настоящий smear-кадр в
                // пиксель-арте требует отдельного спрайта, растянутая копия даёт 80% эффекта.
                float smear = State == CrabState.ClawSweep && SubState != 0f ? 1.25f : 1f;

                _afterimages.Insert(0, new Ghost
                {
                    Center = AnimatedBodyCenter(),
                    Rot = AnimatedBodyRotation(),
                    Scale = new Vector2(NPC.scale * sx * smear, NPC.scale * sy),
                    Wrist0 = _clawWristWorld[0],
                    Wrist1 = _clawWristWorld[1],
                });
                if (_afterimages.Count > AfterimageMax)
                    _afterimages.RemoveAt(_afterimages.Count - 1);
            }
            else if (_afterimages.Count > 0)
            {
                _afterimages.RemoveAt(_afterimages.Count - 1); // плавно тают, когда затормозил
            }
        }

        // Цвет шлейфа — ОТ СОСТОЯНИЯ: холодный синий на красном боссе в ярости был прямым
        // конфликтом, а из-под земли краб обязан вылетать в песке, а не в воде.
        private Color AfterimageTint()
        {
            if (InOceanRage)
                return new Color(255, 45, 25);
            if ((State == CrabState.Burrow || State == CrabState.KnightCourt) && SubState >= 2f)
                return new Color(210, 180, 120);
            return new Color(120, 190, 255);
        }

        private void DrawAfterimages(SpriteBatch sb, Vector2 screenPos)
        {
            if (_afterimages.Count == 0 || BurrowBuried)
                return;

            Color tint = AfterimageTint();
            // Хвост рисуем первым (тусклее), голова ложится поверх
            for (int i = _afterimages.Count - 1; i >= 0; i--)
            {
                Ghost g = _afterimages[i];
                float t = 1f - i / (float)AfterimageMax; // ближе к голове — ярче
                Color c = tint * (t * 0.32f);
                DrawBodySprite(g.Center, g.Rot, g.Scale, c, screenPos);
                DrawClawGhost(sb, 1, g.Wrist1, g.Rot, screenPos, c);
                DrawClawGhost(sb, 0, g.Wrist0, g.Rot, screenPos, c);
            }
        }

        // --- Ауры ---

        // Приливная аура через шейдер SoA:CrabAura (рисовать в отдельном аддитив-батче)
        private void DrawWaterAura(SpriteBatch sb)
        {
            Vector2 c = NPC.Center - new Vector2(0f, BodyLift * 0.4f);
            float breathe = 0.85f + 0.15f * (float)Math.Sin(Main.GameUpdateCount * 0.06f);
            float opacity = (Phase2 ? 0.5f : 0.32f) * breathe;
            SoAVfx.DrawGlow(sb, c, NPC.width * 1.15f, opacity);
        }

        // Плоские цветные ауры: ярость фазы 2 и красный пульс «треснувшего панциря»
        // (без шейдера — рисовать в отдельном аддитив-батче, не смешивая с DrawWaterAura)
        private void DrawTintAuras(SpriteBatch sb)
        {
            Vector2 c = NPC.Center - new Vector2(0f, BodyLift * 0.4f);
            float breathe = 0.85f + 0.15f * (float)Math.Sin(Main.GameUpdateCount * 0.06f);
            float baseSize = NPC.width * 1.15f;

            // Разгорается 40 тиков (_phase2Aura), а не включается скачком по HP
            if (_phase2Aura > 0.01f)
            {
                Color rage = new Color(255, 90, 40) * ((Desperate ? 0.5f : 0.28f) * breathe * _phase2Aura);
                rage.A = 0;
                SoAVfx.DrawTintedGlow(sb, c, new Vector2(baseSize * 0.9f), rage);
            }

            if (_vulnerableTimer > 0)
            {
                float pulse = 0.55f + 0.45f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
                float fade = _vulnerableTimer / (float)ShellCrackTicks;
                Color crack = new Color(255, 70, 30) * (pulse * (0.4f + 0.6f * fade));
                crack.A = 0;
                SoAVfx.DrawTintedGlow(sb, c, new Vector2(baseSize * 1.1f * (0.9f + 0.2f * pulse)), crack);

                // Контурное свечение по силуэту панциря: блоб один читается «подсветкой пола»,
                // а трещина в панцире должна обводить сам панцирь
                float sx = 1f + _bodySquash * BodySquashAmount;
                float sy = 1f - _bodySquash * BodySquashAmount;
                Color outline = new Color(255, 60, 30) * (0.3f * fade * pulse);
                outline.A = 0;
                DrawBodySprite(AnimatedBodyCenter(), AnimatedBodyRotation(),
                    new Vector2(NPC.scale * sx, NPC.scale * sy) * 1.05f, outline, Main.screenPosition);
            }
        }

        // ==================================================================================
        //  ОТЛОЖЕННЫЕ ЭФФЕКТЫ
        // ==================================================================================

        // Второе кольцо через 3 тика, второй слой звука, оседающая пыль, рыцари вразнобой —
        // всем этим нужна задержка. Один общий планировщик вместо россыпи таймеров.
        private readonly List<(int Delay, Action Act)> _delayedFx = new();

        private void ScheduleFx(int delay, Action act)
        {
            if (Main.dedServ)
                return;
            _delayedFx.Add((Math.Max(1, delay), act));
        }

        private void UpdateDelayedFx()
        {
            for (int i = _delayedFx.Count - 1; i >= 0; i--)
            {
                (int delay, Action act) = _delayedFx[i];
                if (--delay > 0)
                {
                    _delayedFx[i] = (delay, act);
                    continue;
                }
                _delayedFx.RemoveAt(i);
                act();
            }
        }

        // ==================================================================================
        //  ВСПЫШКИ И ТРЕЩИНЫ
        // ==================================================================================

        private struct Flash
        {
            public Vector2 Pos;
            public float Size;
            public Color Color;
            public int Life;
            public int Max;
        }

        private struct Crack
        {
            public Vector2 Pos;
            public float Rot;
            public float Length;
            public int Life;
        }

        private readonly List<Flash> _flashes = new();
        private readonly List<Crack> _cracks = new();

        // «Искра от столкновения»: белая вспышка на 2–3 кадра в точке любого импакта
        private void SpawnFlash(Vector2 pos, float size, Color color, int life = 3)
        {
            if (Main.dedServ)
                return;
            if (_flashes.Count >= MaxFlashes)
                _flashes.RemoveAt(0);
            _flashes.Add(new Flash { Pos = pos, Size = size, Color = color, Life = life, Max = life });
        }

        // Трескаться может только ТВЁРДЫЙ грунт прямо под точкой удара. Ни воздух, ни настил
        // платформы трещину держать не могут: в воздухе она висела бы сама по себе, а на
        // платформе — рисовалась поверх сквозной доски, под которой пустота.
        private bool CanCrackGround(float worldX, float fromY, out float groundY)
        {
            groundY = FindGroundY(worldX, fromY - 8f, CrackProbeDepth, false); // только сплошные блоки
            if (float.IsNaN(groundY))
                return false;

            // Первое, что попалось сверху, — платформа? Значит стоим на настиле, а не на грунте
            float floorY = FindGroundY(worldX, fromY - 8f, CrackProbeDepth, true);
            return float.IsNaN(floorY) || floorY >= groundY - 0.5f;
        }

        // Трещины на грунте после слэма и приземления: вытянутые тёмные квады, 40 тиков.
        // Каждая проверяет грунт под СВОЕЙ точкой — у кромки уступа половина не появится.
        private void SpawnCracks(Vector2 center, int count, float spread)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < count; i++)
            {
                float x = center.X + Main.rand.NextFloat(-spread, spread);
                if (!CanCrackGround(x, center.Y, out float groundY))
                    continue;

                if (_cracks.Count >= MaxCracks)
                    _cracks.RemoveAt(0);
                _cracks.Add(new Crack
                {
                    Pos = new Vector2(x, groundY + 2f), // ложатся ровно на поверхность
                    Rot = Main.rand.NextFloat(-0.5f, 0.5f),
                    Length = Main.rand.NextFloat(60f, 140f),
                    Life = CrackLifeTicks,
                });
            }
        }

        private void UpdateFlashesAndCracks()
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                Flash f = _flashes[i];
                if (--f.Life <= 0)
                    _flashes.RemoveAt(i);
                else
                    _flashes[i] = f;
            }

            for (int i = _cracks.Count - 1; i >= 0; i--)
            {
                Crack c = _cracks[i];
                if (--c.Life <= 0)
                    _cracks.RemoveAt(i);
                else
                    _cracks[i] = c;
            }
        }

        // Аддитивно, поверх всего
        private void DrawFlashes(SpriteBatch sb)
        {
            foreach (Flash f in _flashes)
            {
                float t = f.Life / (float)f.Max;
                Color c = f.Color * t;
                c.A = 0;
                SoAVfx.DrawTintedGlow(sb, f.Pos, new Vector2(f.Size * (1.4f - 0.4f * t)), c);
            }
        }

        // В обычном AlphaBlend-батче: трещины ТЁМНЫЕ, аддитивом их не нарисовать
        private void DrawCracks(SpriteBatch sb)
        {
            foreach (Crack c in _cracks)
            {
                float fade = c.Life / (float)CrackLifeTicks;
                SoAVfx.DrawTintedQuad(sb, c.Pos, new Vector2(c.Length, 7f), c.Rot,
                    new Color(20, 14, 10) * (fade * 0.75f));
            }
        }

        // ==================================================================================
        //  ТЕНИ И ТЕЛЕГРАФЫ НА ГРУНТЕ
        // ==================================================================================

        // Метка зоны приземления — САМЫЙ ОПАСНЫЙ момент боя был вообще не размечен.
        // Точку считаем баллистикой от текущей скорости, а не «под крабом».
        // Отметка СВЕТЯЩАЯСЯ, а не тень: затемнять картинку в бою нельзя.
        private void DrawLandingMarker(SpriteBatch sb)
        {
            bool jumping = (State == CrabState.JumpCrush && SubState == 1f)
                        || (State == CrabState.OceanRage && SubState == RageSubFlank)
                        || (State == CrabState.Burrow && SubState >= BurrowSubErupt && NPC.velocity.Y > 0f);
            if (!jumping)
                return;

            // Время до земли при текущем ускорении: t = (-v + sqrt(v² + 2*g*h)) / g
            float ground = FindGroundY(NPC.Center.X, NPC.Bottom.Y, 1200f, true);
            if (float.IsNaN(ground))
                return;

            float h = ground - NPC.Bottom.Y;
            float v = NPC.velocity.Y;
            float disc = v * v + 2f * Gravity * Math.Max(0f, h);
            float ticks = disc <= 0f ? 0f : (-v + (float)Math.Sqrt(disc)) / Gravity;
            float landX = NPC.Center.X + NPC.velocity.X * ticks;

            float groundAtLand = FindGroundY(landX, NPC.Bottom.Y, 1400f, true);
            if (float.IsNaN(groundAtLand))
                groundAtLand = ground;

            // Чем ближе к земле, тем ярче и меньше пятно
            float closeness = 1f - MathHelper.Clamp(h / 600f, 0f, 1f);
            Color c = new Color(255, 90, 50) * (0.2f + 0.45f * closeness);
            c.A = 0;
            SoAVfx.DrawTintedQuad(sb, new Vector2(landX, groundAtLand + 2f),
                new Vector2(NPC.width * (1.3f - 0.4f * closeness), 54f), 0f, c);
        }

        // Линия рывка: лучший телеграф из всех — игрок видит зону ещё до старта.
        // Разгорается к концу взвода.
        private void DrawSweepTelegraph(SpriteBatch sb)
        {
            bool coiling = State == CrabState.ClawSweep && SubState == 0f;
            bool ramming = State == CrabState.OceanRage && SubState == RageSubCharge;
            if (!coiling && !ramming)
                return;

            float total = coiling ? ClawSweepWindupTicks : RageChargeWindupTicks;
            float progress = MathHelper.Clamp(1f - Timer / total, 0f, 1f);
            float reach = (coiling ? SweepDashSpeed * ClawSweepDashTicks : RageRamSpeed * RageRamTicks) * 0.6f;

            float ground = FindGroundY(NPC.Center.X, NPC.Bottom.Y - 20f, 300f, true);
            if (float.IsNaN(ground))
                return;

            int dir = NPC.spriteDirection;
            Vector2 center = new Vector2(NPC.Center.X + dir * reach * 0.5f, ground - 6f);
            Color c = new Color(255, 120, 60) * (progress * progress * 0.55f);
            c.A = 0;
            SoAVfx.DrawTintedQuad(sb, center, new Vector2(reach, 16f + 10f * progress), 0f, c);
        }

        // Растущая отметка под точкой удара слэма: игрок видит, КУДА придёт клешня
        private void DrawSlamTelegraph(SpriteBatch sb)
        {
            if (State != CrabState.ClawSlam || SubState != 0f)
                return;

            float progress = MathHelper.Clamp(1f - Timer / ClawWindupTicks, 0f, 1f);
            Vector2 impact = SlamImpactPoint();
            float ground = FindGroundY(impact.X, impact.Y - 30f, 300f, true);
            if (float.IsNaN(ground))
                return;

            Color c = new Color(255, 100, 55) * (0.5f * progress);
            c.A = 0;
            SoAVfx.DrawTintedQuad(sb, new Vector2(impact.X, ground + 2f),
                new Vector2(170f * progress, 40f * progress), 0f, c);
        }

        // Подсветка бреши в приливной стене: единственный проход обязан читаться ЗАРАНЕЕ
        private void DrawTideGapLight(SpriteBatch sb)
        {
            if (State != CrabState.TideCall)
                return;

            float elapsed = TideTelegraphTicks - Timer;
            if (elapsed < 30f)
                return;

            float progress = MathHelper.Clamp((elapsed - 30f) / 20f, 0f, 1f);
            float baseY = NPC.Bottom.Y;
            float gapY = baseY - (TideGapIndex() + 0.5f) * TideWallSpacing;
            int dir = Math.Sign(NPC.Center.X - Main.player[NPC.target].Center.X);
            if (dir == 0)
                dir = -NPC.spriteDirection;

            Color c = new Color(120, 220, 255) * (progress * 0.6f);
            c.A = 0;
            SoAVfx.DrawTintedQuad(sb, new Vector2(NPC.Center.X - dir * TideWallStartDist * 0.5f, gapY),
                new Vector2(TideWallStartDist, TideWallSpacing * 0.8f), 0f, c);
        }

        // Внутренняя подсветка пинцера на изготовке захлопа: распахнутый пинцер — хороший
        // телеграф, но ему не хватало красного акцента между половинками
        private void DrawGripGlow(SpriteBatch sb)
        {
            bool windup = State == CrabState.CrushingGrip && SubState == 0f;
            bool grabbing = State == CrabState.OceanRage && SubState == RageSubGrab;
            if (!windup && !grabbing)
                return;

            float progress = windup ? MathHelper.Clamp(1f - Timer / GripWindupTicks, 0f, 1f) : 1f;
            Color c = new Color(255, 70, 40) * (progress * progress * 0.7f);
            c.A = 0;
            SoAVfx.DrawTintedGlow(sb, _clawWristWorld[0], new Vector2(70f * NPC.scale * (0.6f + 0.5f * progress)), c);
        }

        // Свет от короны на грунт: эллипс под боссом, пульсирующий В ТАКТ свечению самой
        // короны. Королевский приказ должен освещать арену, а не только светиться на голове.
        private void DrawCrownGroundLight(SpriteBatch sb)
        {
            if (BurrowBuried || _crownMode != CrownMode.Seated)
                return;

            float glow = AnimPose(LayerCrown).Aux;
            if (State == CrabState.CrownCommand)
                glow += 0.4f;
            else if (Desperate)
                glow += 0.25f;
            if (glow <= 0.05f)
                return;

            float ground = FindGroundY(NPC.Center.X, NPC.Bottom.Y - 20f, 300f, true);
            if (float.IsNaN(ground))
                return;

            Color c = new Color(255, 205, 110) * (MathHelper.Clamp(glow, 0f, 1f) * 0.45f);
            c.A = 0;
            SoAVfx.DrawTintedGlow(sb, new Vector2(NPC.Center.X, ground),
                new Vector2(NPC.width * 1.5f, 70f) * (0.8f + 0.2f * glow), c);
        }

        // Блик на панцире: мокрый хитин обязан бликовать, а положение блика — зависеть
        // от угла тела, иначе он выглядит наклейкой
        private void DrawShellGlint(SpriteBatch sb)
        {
            if (BurrowBuried)
                return;

            float rot = AnimatedBodyRotation();
            float slide = (float)Math.Sin(rot * 3f) * 30f;
            Vector2 pos = AnimatedFacingToWorld(new Vector2(slide, -46f) * NPC.scale);
            Color c = new Color(180, 225, 255) * (0.18f + 0.12f * _calmness);
            c.A = 0;
            SoAVfx.DrawTintedQuad(sb, pos, new Vector2(96f * NPC.scale, 12f * NPC.scale), rot, c);
        }

        // ==================================================================================
        //  «ЖИВОСТЬ»: ШАГИ, ПЕСОК НА ЛАПАХ, ВОДА, ПУЗЫРЬКИ
        // ==================================================================================

        private int _sandyFeet;   // тиков, пока с лап сыплется песок после выхода из земли
        private int _wetTimer;    // тиков, пока с панциря стекает вода
        private readonly List<(Vector2 Local, int Life)> _shellGrains = new();

        // Постановка стопы: восемь ног, которые СЛЫШНО, — половина ощущения массы.
        // Раньше краб весом с дом ходил абсолютно бесшумно.
        private void OnFootPlanted(int legIndex, Vector2 foot, float speedT)
        {
            if (Main.dedServ)
                return;

            Vector3 tint = GetGroundTint(foot);
            Color dustColor = new Color(tint.X, tint.Y, tint.Z);
            int type = GroundDustType(foot);

            int count = 2 + (int)(2f * speedT);
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(foot + new Vector2(Main.rand.NextFloatDirection() * 6f, -2f), type,
                    new Vector2(Main.rand.NextFloatDirection() * 1.2f, -Main.rand.NextFloat(0.4f, 1.6f)));
                d.scale = Main.rand.NextFloat(0.9f, 1.5f);
                d.color = dustColor;
            }

            // В агонии из-под лап выбивает уже камешки, а не песок
            if (Desperate && Main.rand.NextBool(3))
            {
                Dust rock = Dust.NewDustPerfect(foot + new Vector2(0f, -3f), DustID.Stone,
                    new Vector2(Main.rand.NextFloatDirection() * 1.6f, -Main.rand.NextFloat(1f, 2.6f)));
                rock.scale = Main.rand.NextFloat(0.8f, 1.2f);
            }

            // Песок сыплется с лап ещё 60 тиков после выхода из земли
            if (_sandyFeet > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    Dust grain = Dust.NewDustPerfect(foot + new Vector2(Main.rand.NextFloatDirection() * 8f, -10f),
                        DustID.Sand, new Vector2(0f, Main.rand.NextFloat(0.5f, 1.8f)));
                    grain.scale = Main.rand.NextFloat(0.8f, 1.3f);
                }
            }

            // Питч от индекса ноги: восемь ног дают ритм, а не один повторяющийся щелчок
            SoundEngine.PlaySound(SoundID.Item14 with
            {
                Volume = MathHelper.Lerp(0.12f, 0.25f, speedT),
                Pitch = -0.55f + legIndex * 0.13f,
                PitchVariance = 0.08f,
                MaxInstances = 0,
            }, foot);

            // Микро-тряска — только на полной скорости и только от двух задних ног
            if (speedT > 0.85f && legIndex >= 2)
                ScreenRumble(0.35f);
        }

        // Разворот: 260-пиксельная туша разворачивалась беззвучно и бесследно
        private void OnTurnAround()
        {
            _anim.PlayOnce("turn");

            if (Main.dedServ || _legs == null)
                return;

            // Принудительная волна перестановки от хвоста к голове: иначе лапы
            // переставляются хаотично
            for (int i = HipLocal.Length - 1; i >= 0; i--)
            {
                for (int s = 0; s < 2; s++)
                {
                    Leg leg = _legs[i * 2 + s];
                    if (leg.StepTimer <= 0)
                        leg.StepTimer = leg.StepSpan = 10 + (HipLocal.Length - 1 - i) * 2;
                }
            }
        }

        // Раз в N тиков: капли с панциря, пузыри в воде, редкие пузырьки изо рта,
        // осыпающиеся песчинки на панцире
        private void UpdateLiveliness()
        {
            if (Main.dedServ)
                return;

            if (_sandyFeet > 0) _sandyFeet--;
            if (_wetTimer > 0) _wetTimer--;

            // Вышел из воды или его окатило приливом — с панциря стекает вода
            if (Collision.WetCollision(NPC.position, NPC.width, NPC.height))
                _wetTimer = WetTicks;

            if (_wetTimer > 0 && Main.GameUpdateCount % 5 == 0 && !BurrowBuried)
            {
                Vector2 from = AnimatedFacingToWorld(new Vector2(Main.rand.NextFloat(-90f, 90f), -60f) * NPC.scale);
                Dust drop = Dust.NewDustPerfect(from, DustID.Water, new Vector2(0f, 1f));
                drop.scale = Main.rand.NextFloat(0.9f, 1.4f);
            }

            // Пузыри при движении в воде — пропорционально скорости
            if (Collision.WetCollision(NPC.position, NPC.width, NPC.height))
            {
                int bubbles = (int)(Math.Abs(NPC.velocity.X) / 3f);
                for (int i = 0; i < bubbles; i++)
                {
                    Dust b = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.BubbleBurst_Blue);
                    b.velocity = new Vector2(0f, -Main.rand.NextFloat(1f, 2.5f));
                    b.noGravity = true;
                }
            }

            // Краб — водное существо на суше: редкие одиночные пузырьки от морды
            if (!BurrowBuried && _calmness > 0.5f && Main.rand.NextBool(200))
            {
                Vector2 mouth = AnimatedFacingToWorld(new Vector2(70f, -10f) * NPC.scale);
                Dust b = Dust.NewDustPerfect(mouth, DustID.BubbleBurst_Blue, new Vector2(0f, -1.2f));
                b.noGravity = true;
                b.scale = Main.rand.NextFloat(0.8f, 1.2f);
            }

            UpdateShellGrains();
        }

        // Песчинки, налипшие на панцирь после подкопа: осыпаются по одной, а не разом
        private void AddShellGrains(int count)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < count; i++)
            {
                _shellGrains.Add((new Vector2(Main.rand.NextFloat(-95f, 95f), Main.rand.NextFloat(-58f, 10f)),
                    ShellGrainLife + Main.rand.Next(60)));
            }
        }

        private void UpdateShellGrains()
        {
            for (int i = _shellGrains.Count - 1; i >= 0; i--)
            {
                (Vector2 local, int life) = _shellGrains[i];
                if (--life <= 0)
                {
                    _shellGrains.RemoveAt(i);
                    Dust d = Dust.NewDustPerfect(AnimatedFacingToWorld(local * NPC.scale), DustID.Sand,
                        new Vector2(0f, 1.2f));
                    d.scale = 1.1f;
                    continue;
                }
                _shellGrains[i] = (local, life);
            }
        }

        private void DrawShellGrains(SpriteBatch sb)
        {
            if (_shellGrains.Count == 0 || BurrowBuried)
                return;

            Color sand = new Color(226, 200, 140) * 0.75f;
            foreach ((Vector2 local, int _) in _shellGrains)
            {
                SoAVfx.DrawTintedQuad(sb, AnimatedFacingToWorld(local * NPC.scale),
                    new Vector2(5f * NPC.scale), 0f, sand);
            }
        }

        // Общий примитив отрисовки панциря — переиспользуют DrawBody и афтеримиджи
        private void DrawBodySprite(Vector2 worldCenter, float rotation, Vector2 scale, Color color, Vector2 screenPos)
        {
            Texture2D tex = TextureAssets.Npc[Type].Value;
            if (tex == null)
                return;
            int frameCount = Math.Max(1, Main.npcFrameCount[Type]);
            int frameHeight = tex.Height / frameCount;
            Rectangle src = new Rectangle(0, NPC.frame.Y, tex.Width, frameHeight);
            Vector2 origin = new Vector2(tex.Width / 2f, frameHeight / 2f);
            SpriteEffects fx = NPC.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(tex, worldCenter - screenPos, src, color, rotation, origin, scale, fx, 0);
        }
    }
}
