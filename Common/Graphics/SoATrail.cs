using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Graphics
{
    // Внешний вид ленты: всё, что не зависит от конкретного пути. Цвет тела и затухание
    // задаёт вызывающий функцией цвета, здесь — характер самой ленты
    public readonly struct TrailStyle
    {
        public Color CoreColor { get; init; }     // жила по центру, у головы
        public float CoreWidth { get; init; }     // доля полуширины, 0..1
        public float NoiseScale { get; init; }    // повторов шума вдоль ленты
        public float ScrollSpeed { get; init; }   // бег струй к хвосту, текстур/с
        public float EdgeTear { get; init; }      // насколько шум рвёт край, 0..1
        public float Opacity { get; init; }

        // Приливная вода: белая жила, рваная пена по краю, струи бегут к хвосту
        public static readonly TrailStyle Tide = new()
        {
            CoreColor = new Color(225, 245, 255),
            CoreWidth = 0.35f,
            NoiseScale = 1.6f,
            ScrollSpeed = 1.8f,
            EdgeTear = 0.55f,
            Opacity = 1f,
        };
    }

    // Лента-трейл по произвольному пути — для росчерков оружия, хвостов снарядов,
    // следов рывка. Путь сглаживается сплайном, так что хватает точки на тик даже
    // у быстрых ударов. Рисует сразу в GraphicsDevice (SoATrail.fx), поэтому звать
    // можно из любого PreDraw, в том числе внутри прохода игрока (heldProj): лента
    // ляжет под спрайты текущего батча — под само оружие, а не поверх него.
    public static class SoATrail
    {
        // progress: 0 — голова, 1 — хвост
        public delegate float WidthFunction(float progress);   // полуширина в пикселях
        public delegate Color ColorFunction(float progress);

        private const int SubdivisionsPerSegment = 4;
        private const int MaxPoints = 64;

        private static Asset<Effect> effect;
        private static readonly Vector2[] smoothed = new Vector2[(MaxPoints - 1) * SubdivisionsPerSegment + 1];
        private static readonly VertexPositionColorTexture[] vertices =
            new VertexPositionColorTexture[smoothed.Length * 2];

        private static Effect Effect =>
            (effect ??= ModContent.Request<Effect>("SoA/Assets/Effects/SoATrail", AssetRequestMode.ImmediateLoad)).Value;

        // points — путь в мировых координатах, от головы к хвосту
        public static void Draw(ReadOnlySpan<Vector2> points, WidthFunction width, ColorFunction color,
            in TrailStyle style)
        {
            if (Main.dedServ || points.Length < 2)
                return;

            int count = Smooth(points.Length > MaxPoints ? points[..MaxPoints] : points);
            if (count < 2)
                return;

            int vertexCount = BuildStrip(count, width, color);
            Render(vertexCount, style);
        }

        // Catmull-Rom через все точки: удар за тик уходит на десятки пикселей, и без
        // сглаживания лента ломалась бы углами
        private static int Smooth(ReadOnlySpan<Vector2> points)
        {
            int count = 0;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 p0 = points[Math.Max(i - 1, 0)];
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = points[Math.Min(i + 2, points.Length - 1)];

                for (int s = 0; s < SubdivisionsPerSegment; s++)
                    smoothed[count++] = Vector2.CatmullRom(p0, p1, p2, p3, s / (float)SubdivisionsPerSegment);
            }
            smoothed[count++] = points[^1];
            return count;
        }

        private static int BuildStrip(int count, WidthFunction width, ColorFunction color)
        {
            int v = 0;
            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)(count - 1);

                // Касательная по соседям: на изгибе лента не пережимается
                Vector2 tangent = smoothed[Math.Min(i + 1, count - 1)] - smoothed[Math.Max(i - 1, 0)];
                Vector2 normal = tangent.LengthSquared() > 0.0001f
                    ? Vector2.Normalize(new Vector2(-tangent.Y, tangent.X))
                    : Vector2.UnitY;

                Vector2 center = smoothed[i] - Main.screenPosition;
                Vector2 offset = normal * width(progress);
                Color tint = color(progress);

                vertices[v++] = new VertexPositionColorTexture(new Vector3(center + offset, 0f), tint,
                    new Vector2(progress, 0f));
                vertices[v++] = new VertexPositionColorTexture(new Vector3(center - offset, 0f), tint,
                    new Vector2(progress, 1f));
            }
            return v;
        }

        private static void Render(int vertexCount, in TrailStyle style)
        {
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            Effect shader = Effect;

            Matrix projection = Matrix.CreateOrthographicOffCenter(0, Main.screenWidth, Main.screenHeight, 0, -1, 1);
            shader.Parameters["uWorldViewProjection"].SetValue(Main.GameViewMatrix.TransformationMatrix * projection);
            shader.Parameters["uTime"].SetValue(Main.GlobalTimeWrappedHourly);
            shader.Parameters["uOpacity"].SetValue(style.Opacity);
            shader.Parameters["uCoreColor"].SetValue(style.CoreColor.ToVector3());
            shader.Parameters["uCoreWidth"].SetValue(style.CoreWidth);
            shader.Parameters["uNoiseScale"].SetValue(style.NoiseScale);
            shader.Parameters["uScrollSpeed"].SetValue(style.ScrollSpeed);
            shader.Parameters["uEdgeTear"].SetValue(style.EdgeTear);

            // Состояние устройства возвращаем как было: если нас позвали внутри
            // Immediate-батча, следующий спрайт не должен унаследовать наш бленд
            BlendState previousBlend = device.BlendState;
            RasterizerState previousRasterizer = device.RasterizerState;

            device.BlendState = BlendState.Additive;
            device.RasterizerState = RasterizerState.CullNone;
            device.Textures[1] = SoAVfx.TrailNoise;
            device.SamplerStates[1] = SamplerState.LinearWrap;

            shader.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, vertices, 0, vertexCount - 2);

            device.BlendState = previousBlend;
            device.RasterizerState = previousRasterizer;
            Main.pixelShader.CurrentTechnique.Passes[0].Apply();
        }
    }
}
