using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Graphics.Particles
{
    // Собственные частицы мода — то, чего не умеет ванильная пыль (8 px, одинаковая, живёт миг):
    //   • Debris — обломки грунта: гравитация, вращение, отскок от тайлов, освещены миром;
    //   • Smoke  — клубы пыли: растут, тормозят, оседают, освещены миром, НЕ светятся;
    //   • Streak — штрихи вдоль скорости: брызги воды, искры; светятся (аддитив);
    //   • Glow   — мягкое светящееся пятно, гаснет и сжимается.
    // Плюс световые импульсы: настоящий свет на мир (Lighting.AddLight) на несколько тиков —
    // вспышка удара освещает грунт и стены вокруг, а не только рисуется поверх.
    //
    // Чистая косметика на клиенте. Тикается раз в игровой тик (PostUpdateDusts),
    // рисуется поверх сущностей, сразу после ванильной пыли.
    public class SoAParticles : ModSystem
    {
        private const int MaxParticles = 1500;
        private const int MaxLights = 48;
        private const int DebrisFadeTicks = 14;     // обломок гаснет в последние тики жизни
        private const float DebrisBounce = 0.35f;   // сколько скорости сохраняет отскок
        private const float DebrisGroundFriction = 0.7f;

        public enum Kind : byte { Debris, Smoke, Streak, Glow }

        private struct Particle
        {
            public Kind Kind;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Rotation;
            public float Spin;
            public Vector2 Size;       // Debris — стороны обломка; Streak — X длина, Y толщина
            public float EndSize;      // Smoke/Glow — размер к концу жизни (старт в Size.X)
            public Color Color;
            public float Opacity;
            public float Gravity;
            public float Drag;         // множитель скорости за тик
            public int Life;
            public int MaxLife;
        }

        private struct LightPulse
        {
            public Vector2 Position;
            public Vector3 Color;
            public int Life;
            public int MaxLife;
        }

        private static readonly Particle[] _particles = new Particle[MaxParticles];
        private static int _count;
        private static readonly LightPulse[] _lights = new LightPulse[MaxLights];
        private static int _lightCount;

        public override void Load()
        {
            if (Main.dedServ)
                return;
            On_Main.DrawDust += DrawAfterDust;
        }

        public override void Unload()
        {
            _count = 0;
            _lightCount = 0;
        }

        public override void OnWorldUnload()
        {
            _count = 0;
            _lightCount = 0;
        }

        #region Спавн

        // Обломок грунта. size — сторона в px мира, color — цвет грунта (свет мира накладывается сам)
        public static void SpawnDebris(Vector2 position, Vector2 velocity, Color color, float size, int life = 70)
        {
            if (!TryAllocate(out int index))
                return;
            ref Particle p = ref _particles[index];
            p.Kind = Kind.Debris;
            p.Position = position;
            p.Velocity = velocity;
            p.Rotation = Main.rand.NextFloat(MathHelper.TwoPi);
            p.Spin = Main.rand.NextFloatDirection() * 0.25f;
            p.Size = new Vector2(size, size * Main.rand.NextFloat(0.6f, 1f)); // не идеальные квадратики
            p.Color = color;
            p.Opacity = 1f;
            p.Gravity = 0.32f;
            p.Drag = 0.985f;
            p.Life = p.MaxLife = life;
        }

        // Клуб пыли: растёт от startSize до endSize по easeOut, тормозит, гаснет к концу
        public static void SpawnSmoke(Vector2 position, Vector2 velocity, Color color, float startSize, float endSize,
            float opacity = 0.55f, int life = 60)
        {
            if (!TryAllocate(out int index))
                return;
            ref Particle p = ref _particles[index];
            p.Kind = Kind.Smoke;
            p.Position = position;
            p.Velocity = velocity;
            p.Rotation = Main.rand.NextFloat(MathHelper.TwoPi);
            p.Spin = Main.rand.NextFloatDirection() * 0.01f;
            p.Size = new Vector2(startSize);
            p.EndSize = endSize;
            p.Color = color;
            p.Opacity = opacity;
            p.Gravity = -0.015f; // пыль чуть поднимается, пока не осядет
            p.Drag = 0.93f;
            p.Life = p.MaxLife = life;
        }

        // Светящийся штрих вдоль скорости: брызги (gravity > 0) и искры (gravity ≈ 0)
        public static void SpawnStreak(Vector2 position, Vector2 velocity, Color color, float thickness,
            float gravity = 0.25f, int life = 28, float lengthPerSpeed = 3.2f)
        {
            if (!TryAllocate(out int index))
                return;
            ref Particle p = ref _particles[index];
            p.Kind = Kind.Streak;
            p.Position = position;
            p.Velocity = velocity;
            p.Size = new Vector2(lengthPerSpeed, thickness);
            p.Color = color;
            p.Opacity = 1f;
            p.Gravity = gravity;
            p.Drag = 0.97f;
            p.Life = p.MaxLife = life;
        }

        // Мягкое светящееся пятно
        public static void SpawnGlow(Vector2 position, Vector2 velocity, Color color, float startSize, float endSize,
            int life = 20)
        {
            if (!TryAllocate(out int index))
                return;
            ref Particle p = ref _particles[index];
            p.Kind = Kind.Glow;
            p.Position = position;
            p.Velocity = velocity;
            p.Size = new Vector2(startSize);
            p.EndSize = endSize;
            p.Color = color;
            p.Opacity = 1f;
            p.Drag = 0.9f;
            p.Life = p.MaxLife = life;
        }

        // Свет на мир: intensity ~1 — факел, 2–3 — вспышка удара. Гаснет линейно за life тиков
        public static void AddLight(Vector2 position, Color color, float intensity, int life = 12)
        {
            if (Main.dedServ || _lightCount >= MaxLights)
                return;
            _lights[_lightCount++] = new LightPulse
            {
                Position = position,
                Color = color.ToVector3() * intensity,
                Life = life,
                MaxLife = life,
            };
        }

        // Слот под новую частицу; переполнение — частица просто не рождается
        private static bool TryAllocate(out int index)
        {
            index = _count;
            if (Main.dedServ || _count >= MaxParticles)
                return false;
            _particles[_count++] = default;
            return true;
        }

        #endregion

        #region Тик

        public override void PostUpdateDusts()
        {
            if (Main.dedServ)
                return;

            for (int i = _count - 1; i >= 0; i--)
            {
                ref Particle p = ref _particles[i];
                if (--p.Life <= 0)
                {
                    _particles[i] = _particles[--_count]; // swap-remove: порядок не важен
                    continue;
                }

                p.Velocity.Y += p.Gravity;
                p.Velocity *= p.Drag;
                p.Rotation += p.Spin;

                if (p.Kind == Kind.Debris)
                    MoveDebris(ref p);
                else
                    p.Position += p.Velocity;
            }

            for (int i = _lightCount - 1; i >= 0; i--)
            {
                ref LightPulse l = ref _lights[i];
                if (--l.Life <= 0)
                {
                    _lights[i] = _lights[--_lightCount];
                    continue;
                }
                Vector3 c = l.Color * (l.Life / (float)l.MaxLife);
                Lighting.AddLight(l.Position, c.X, c.Y, c.Z);
            }
        }

        // Обломок отскакивает от тайлов по осям отдельно: удар о стену гасит X, о пол — Y
        private static void MoveDebris(ref Particle p)
        {
            Vector2 half = new Vector2(2f);
            Vector2 next = p.Position + p.Velocity;
            if (!Collision.SolidCollision(next - half, 4, 4))
            {
                p.Position = next;
                return;
            }

            if (Collision.SolidCollision(new Vector2(next.X, p.Position.Y) - half, 4, 4))
                p.Velocity.X *= -DebrisBounce;
            if (Collision.SolidCollision(new Vector2(p.Position.X, next.Y) - half, 4, 4))
            {
                p.Velocity.Y *= -DebrisBounce;
                p.Velocity.X *= DebrisGroundFriction;
                p.Spin *= 0.5f;
            }

            next = p.Position + p.Velocity;
            if (!Collision.SolidCollision(next - half, 4, 4))
                p.Position = next;
        }

        #endregion

        #region Отрисовка

        private static void DrawAfterDust(On_Main.orig_DrawDust orig, Main self)
        {
            orig(self);
            if (_count == 0)
                return;

            SpriteBatch sb = Main.spriteBatch;

            // Обломки и пыль — непрозрачные и освещённые миром: в темноте темнеют вместе с грунтом
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                Main.GameViewMatrix.TransformationMatrix);
            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Kind == Kind.Smoke)
                    DrawSmoke(sb, ref p);
                else if (p.Kind == Kind.Debris)
                    DrawDebris(sb, ref p);
            }
            sb.End();

            // Брызги, искры, свечения — светятся сами
            sb.Begin(SpriteSortMode.Deferred, SoAVfx.GlowBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                Main.GameViewMatrix.TransformationMatrix);
            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Kind == Kind.Streak)
                    DrawStreak(sb, ref p);
                else if (p.Kind == Kind.Glow)
                    DrawGlow(sb, ref p);
            }
            sb.End();
        }

        private static float Age(in Particle p) => 1f - p.Life / (float)p.MaxLife; // 0 → 1

        private static Color Lit(Vector2 worldPos, Color color)
            => color.MultiplyRGB(Lighting.GetColor(worldPos.ToTileCoordinates()));

        private static void DrawDebris(SpriteBatch sb, ref Particle p)
        {
            float fade = MathHelper.Clamp(p.Life / (float)DebrisFadeTicks, 0f, 1f);
            Texture2D tex = SoAVfx.Quad;
            sb.Draw(tex, p.Position - Main.screenPosition, null, Lit(p.Position, p.Color) * (p.Opacity * fade),
                p.Rotation, tex.Size() / 2f, p.Size / tex.Width, SpriteEffects.None, 0f);
        }

        private static void DrawSmoke(SpriteBatch sb, ref Particle p)
        {
            float age = Age(p);
            float grow = 1f - (1f - age) * (1f - age); // easeOut: клуб вспухает сразу, потом медленно
            float size = MathHelper.Lerp(p.Size.X, p.EndSize, grow);
            // Короткий вход (иначе клуб «выскакивает»), затем долгий спад
            float alpha = Math.Min(age / 0.08f, 1f) * (float)Math.Pow(1f - age, 1.4f);

            Texture2D tex = SoAVfx.SoftGlow;
            sb.Draw(tex, p.Position - Main.screenPosition, null, Lit(p.Position, p.Color) * (p.Opacity * alpha),
                p.Rotation, tex.Size() / 2f, size / tex.Width, SpriteEffects.None, 0f);
        }

        private static void DrawStreak(SpriteBatch sb, ref Particle p)
        {
            float age = Age(p);
            float speed = p.Velocity.Length();
            float length = Math.Max(p.Size.Y * 2f, speed * p.Size.X);
            Color c = p.Color * (p.Opacity * (1f - age));

            Texture2D tex = SoAVfx.SoftStreak;
            Vector2 scale = new Vector2(length / tex.Width, p.Size.Y / tex.Height);
            sb.Draw(tex, p.Position - Main.screenPosition, null, c, p.Velocity.ToRotation(),
                tex.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        private static void DrawGlow(SpriteBatch sb, ref Particle p)
        {
            float age = Age(p);
            float size = MathHelper.Lerp(p.Size.X, p.EndSize, age);
            Color c = p.Color * (p.Opacity * (1f - age) * (1f - age));

            Texture2D tex = SoAVfx.SoftGlow;
            sb.Draw(tex, p.Position - Main.screenPosition, null, c, 0f, tex.Size() / 2f,
                size / tex.Width, SpriteEffects.None, 0f);
        }

        #endregion
    }
}
