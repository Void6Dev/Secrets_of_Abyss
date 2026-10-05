using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ДОЖИМ СЦЕНЫ СМЕРТИ.
    //   • Трещины — ломаные, с ответвлениями; пока туша лежит, их жар копится и нарастает.
    //   • Кульминация: в миг начала рассыпания панцирь лопается — вспышка, звон, осколки.
    //   • Рассыпание — шейдером (CrabRegalia DissolvePass): рваная раскалённая кромка ползёт
    //     по панцирю сверху вниз, ноги и клешни крошатся по пикселям. Кромка светит на мир.
    //     Раньше спрайт срезался ровной «шторкой», а ноги и клешни таяли призраками.
    public partial class King_crab
    {
        private const int DeathCrackBranches = 6;
        private const float DeathCrackCoreWidth = 2.5f;
        private const float DeathCrackGlowWidth = 8f;
        private const int DeathEdgeLights = 3;         // огоньков вдоль кромки рассыпания

        private bool _deathBurstDone;
        private bool _dissolveActive;
        private MiscShaderData _dissolveShader;

        private bool Dissolving => State == CrabState.Dying && DeathWipe() > 0f;

        // Ноги и клешни рассыпаются чуть быстрее панциря — как раньше гасли
        private float DissolvePartProgress => MathHelper.Clamp(DeathWipe() * 1.4f, 0f, 1f);

        // Линия рассыпания в долях высоты спрайта — та же формула, что в DissolvePS
        private static float DissolveLine(float wipe) => wipe * (1f + 2f * 0.05f + 0.14f) - 0.05f - 0.07f;

        // Насколько трещины накалены: копят жар, пока туша лежит, к кульминации — вдвое ярче
        private float DeathCrackCharge()
        {
            float t = MathHelper.Clamp((DeathElapsed - DeathClipEndTick) / (float)(DeathDissolveStart - DeathClipEndTick), 0f, 1f);
            return 0.75f + 0.95f * t * t;
        }

        #region Рассыпание

        // Всё, что нарисовано между Begin и End, рассыпается. Вне рассыпания — ничего не делает
        private void BeginDissolve(SpriteBatch sb, float progress, bool crumble)
        {
            if (!Dissolving)
                return;
            SoAVfx.BeginPixelImmediate(sb);
            _dissolveShader ??= GameShaders.Misc["SoA:CrabDissolve"];
            _dissolveShader.Shader.Parameters["uProgress"]?.SetValue(progress);
            _dissolveShader.Shader.Parameters["uMode"]?.SetValue(crumble ? 1f : 0f);
            _dissolveActive = true;
        }

        // Перед каждым Draw части: шейдеру нужен размер именно её текстуры (пиксели 2x2)
        private void PrepareDissolve(Texture2D tex)
        {
            if (!_dissolveActive || tex == null)
                return;
            _dissolveShader.Shader.Parameters["uTexSize"]?.SetValue(new Vector2(tex.Width, tex.Height));
            _dissolveShader.Apply();
        }

        private void EndDissolve(SpriteBatch sb)
        {
            if (!_dissolveActive)
                return;
            _dissolveActive = false;
            SoAVfx.EndPixelBatch(sb);
        }

        #endregion

        #region Трещины

        // Последний удар: трещины разбегаются по панцирю ломаными и ветвятся
        private void GenerateDeathCracks()
        {
            _deathCracks.Clear();
            for (int i = 0; i < DeathCrackCount; i++)
            {
                Vector2 start = new(Main.rand.NextFloat(-95f, 95f), Main.rand.NextFloat(-45f, 30f));
                float length = Main.rand.NextFloat(28f, 70f);
                Vector2[] points = CrackPolyline(start, Main.rand.NextFloat(MathHelper.TwoPi), length, 4);
                _deathCracks.Add(new DeathCrack { Points = points, Length = length, Delay = i * DeathCrackStagger, Width = 1f });

                // Ответвление от середины — позже и тоньше: трещина «ползёт» дальше
                if (i < DeathCrackBranches)
                {
                    float angle = (points[3] - points[1]).ToRotation() + Main.rand.NextFloatDirection() * 1.1f;
                    float branchLength = length * Main.rand.NextFloat(0.35f, 0.55f);
                    _deathCracks.Add(new DeathCrack
                    {
                        Points = CrackPolyline(points[2], angle, branchLength, 2),
                        Length = branchLength,
                        Delay = i * DeathCrackStagger + DeathCrackGrowTicks / 2,
                        Width = 0.6f,
                    });
                }
            }
        }

        private static Vector2[] CrackPolyline(Vector2 start, float angle, float length, int segments)
        {
            Vector2[] points = new Vector2[segments + 1];
            points[0] = start;
            for (int k = 1; k <= segments; k++)
            {
                angle += Main.rand.NextFloatDirection() * 0.55f;
                points[k] = points[k - 1] + angle.ToRotationVector2() * (length / segments) * Main.rand.NextFloat(0.75f, 1.2f);
            }
            return points;
        }

        // Рисовать внутри BeginAdditive: трещины светятся изнутри панциря
        private void DrawDeathCracks(SpriteBatch sb)
        {
            if (State != CrabState.Dying || _deathCracks.Count == 0)
                return;

            Texture2D tex = TextureAssets.Npc[Type].Value;
            float halfH = tex == null ? 0f : tex.Height / Math.Max(1, Main.npcFrameCount[Type]) / 2f;
            float wipe = DeathWipe();
            float wipeLocalY = -halfH + DissolveLine(wipe) * halfH * 2f; // выше — панцирь уже рассыпался
            float pulse = 0.8f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.3f);
            float heat = pulse * DeathCrackCharge();
            Color core = Color.Lerp(DeathCrackColor, Color.White, 0.55f) * Math.Min(heat, 1.4f);
            Color glow = DeathCrackColor * (0.45f * heat);

            foreach (DeathCrack crack in _deathCracks)
            {
                float grow = MathHelper.Clamp((DeathElapsed - crack.Delay) / DeathCrackGrowTicks, 0f, 1f);
                if (grow <= 0f)
                    continue;

                // Идём по ломаной на длину grow * Length — трещина растёт от начала
                float left = crack.Length * grow;
                for (int k = 1; k < crack.Points.Length && left > 0f; k++)
                {
                    Vector2 a = crack.Points[k - 1];
                    Vector2 b = crack.Points[k];
                    if (wipe > 0f && (a.Y < wipeLocalY || b.Y < wipeLocalY))
                        break;
                    float segment = Vector2.Distance(a, b);
                    if (left < segment)
                        b = a + (b - a) * (left / segment);
                    left -= segment;

                    Vector2 wa = BodyAnchorToWorld(a);
                    Vector2 wb = BodyAnchorToWorld(b);
                    Vector2 mid = (wa + wb) * 0.5f;
                    float len = Vector2.Distance(wa, wb);
                    float rot = (wb - wa).ToRotation();
                    SoAVfx.DrawTintedQuad(sb, mid, new Vector2(len + DeathCrackGlowWidth * 0.5f, DeathCrackGlowWidth * crack.Width), rot, glow);
                    SoAVfx.DrawTintedQuad(sb, mid, new Vector2(len, DeathCrackCoreWidth * crack.Width), rot, core);
                }
                SoAVfx.DrawTintedGlow(sb, BodyAnchorToWorld(crack.Points[0]), new Vector2(22f * grow * crack.Width), glow);
            }
        }

        // Трещины светят на мир — сильнее по мере накала
        private void LightDeathCracks()
        {
            if (_deathCracks.Count == 0 || !EveryTicks(3))
                return;
            float heat = DeathCrackCharge();
            for (int i = 0; i < _deathCracks.Count; i += 3)
            {
                DeathCrack crack = _deathCracks[i];
                if (DeathElapsed - crack.Delay < DeathCrackGrowTicks * 0.3f)
                    continue;
                SoAParticles.AddLight(BodyAnchorToWorld(crack.Points[0]), DeathCrackColor, 0.35f * heat, 4);
            }
        }

        #endregion

        // Кульминация: накалившийся панцирь лопается — с этого мига он сгорает в песок
        private void OnShellBurst()
        {
            Vector2 at = NPC.Center - new Vector2(0f, BodyLift);
            HitStop(4);
            ScreenPunch(6f, 22);
            SpawnFlash(at, 460f, DeathCrackColor, 5);
            ImpactLight(at, DeathCrackColor, 2.6f, 30);
            SpawnImpactDebris(at, 18, 1.3f);
            SpawnSparks(at, 18, 9f);
            SoundEngine.PlaySound(SoundID.Item27 with { Pitch = -0.7f, Volume = 0.9f }, at);
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.9f, Volume = 0.7f }, at);
        }

        // Кромка рассыпания светит на мир, пока ползёт вниз
        private void LightDissolveEdge(float wipe)
        {
            Texture2D tex = TextureAssets.Npc[Type].Value;
            if (tex == null || !EveryTicks(2))
                return;
            float halfH = tex.Height / Math.Max(1, Main.npcFrameCount[Type]) / 2f;
            float y = -halfH + DissolveLine(wipe) * halfH * 2f;
            for (int i = 0; i < DeathEdgeLights; i++)
            {
                float x = MathHelper.Lerp(-90f, 90f, i / (float)(DeathEdgeLights - 1));
                SoAParticles.AddLight(BodyAnchorToWorld(new Vector2(x, y)), DeathCrackColor, 0.7f, 3);
            }
        }
    }
}
