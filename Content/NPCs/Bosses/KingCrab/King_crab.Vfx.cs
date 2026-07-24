using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
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
        private const int AfterimageMax = 10;      // длина шлейфа тела на скорости
        private const int MaxRings = 6;            // одновременных колец удара
        private const int MaxBursts = 4;           // одновременных пылевых всплесков

        private readonly List<(Vector2 pos, float rot)> _afterimages = new();
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

        // Цвет пыли по типу грунта в точке: песок/снег/земля/камень и т.д.
        private static Vector3 GetGroundTint(Vector2 worldPos)
        {
            int tx = (int)(worldPos.X / 16f);
            int ty = (int)(worldPos.Y / 16f) + 1; // на тайл ниже поверхности — сам грунт
            Tile tile = Framing.GetTileSafely(tx, ty);
            if (!tile.HasTile)
                tile = Framing.GetTileSafely(tx, ty + 1);

            switch (tile.TileType)
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
            float spread, float upMin, float upMax, float scaleMin = 1.2f, float scaleMax = 2f)
        {
            if (Main.netMode == NetmodeID.Server)
                return;
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustDirect(rectPos, width, height, DustID.Sand);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * spread, -Main.rand.NextFloat(upMin, upMax));
                d.scale = Main.rand.NextFloat(scaleMin, scaleMax);
            }
        }

        // --- Афтеримиджи (шлейф тела) ---

        private void RecordAfterimage()
        {
            if (IsFastState())
            {
                _afterimages.Insert(0, (NPC.Center, NPC.rotation));
                if (_afterimages.Count > AfterimageMax)
                    _afterimages.RemoveAt(_afterimages.Count - 1);
            }
            else if (_afterimages.Count > 0)
            {
                _afterimages.RemoveAt(_afterimages.Count - 1); // плавно тают, когда затормозил
            }
        }

        private void DrawAfterimages(SpriteBatch sb, Vector2 screenPos)
        {
            // Хвост рисуем первым (тусклее), голова ложится поверх
            for (int i = _afterimages.Count - 1; i >= 0; i--)
            {
                float t = 1f - i / (float)AfterimageMax; // ближе к голове — ярче
                Color c = new Color(120, 190, 255) * (t * 0.32f);
                Vector2 center = _afterimages[i].pos - new Vector2(0f, BodyLift);
                DrawBodySprite(center, _afterimages[i].rot, new Vector2(NPC.scale), c, screenPos);
            }
        }

        // --- Ауры ---

        // Водяная свирл-аура через шейдер SoA:BeamGlow (рисовать в отдельном аддитив-батче)
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

            if (Phase2)
            {
                Color rage = new Color(255, 90, 40) * ((Desperate ? 0.5f : 0.28f) * breathe);
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
