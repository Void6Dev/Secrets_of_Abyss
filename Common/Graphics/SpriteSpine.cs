using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace SoA.Common.Graphics
{
    // Кадр спрайта, натянутый на ломаную «позвоночника»: длинные существа (угорь, черви)
    // гнутся целиком, без стыков между сегментами. Спрайт должен смотреть влево —
    // голова у левого края кадра, позвоночник идёт от головы к хвосту.
    // Звать между SoAVfx.BeginPixelImmediate и SoAVfx.EndPixelBatch
    public static class SpriteSpine
    {
        private const int MaxPoints = 64;

        private static BasicEffect effect;
        private static readonly VertexPositionColorTexture[] vertices = new VertexPositionColorTexture[MaxPoints * 2];

        // spine — точки в мире; frameX — координата каждой точки вдоль кадра в пикселях текстуры.
        // offsets — сдвиг точки поперёк тела в пикселях (изгиб поверх пути), может быть пустым.
        // color с A = 0 даёт аддитивное свечение (для масок свечения)
        public static void Draw(Texture2D texture, Rectangle frame, ReadOnlySpan<Vector2> spine,
            ReadOnlySpan<float> frameX, ReadOnlySpan<float> offsets, bool flipVertical, Func<int, Color> color)
        {
            if (Main.dedServ || spine.Length < 2)
                return;

            int count = Math.Min(spine.Length, MaxPoints);
            float halfHeight = frame.Height / 2f;
            float vTop = frame.Top / (float)texture.Height;
            float vBottom = frame.Bottom / (float)texture.Height;

            for (int i = 0; i < count; i++)
            {
                Vector2 towardTail = (spine[Math.Min(i + 1, count - 1)] - spine[Math.Max(i - 1, 0)]).SafeNormalize(Vector2.UnitX);
                // Верх кадра при хвосте справа — это -Y; поворачиваем вместе с телом
                Vector2 up = new(towardTail.Y, -towardTail.X);
                if (flipVertical)
                    up = -up;

                float lateral = offsets.Length > i ? offsets[i] : 0f;
                Vector2 center = spine[i] + up * lateral - Main.screenPosition;
                float u = (frame.X + frameX[i]) / texture.Width;
                Color vertexColor = color(i);

                vertices[2 * i] = new VertexPositionColorTexture(new Vector3(center + up * halfHeight, 0f), vertexColor, new Vector2(u, vTop));
                vertices[2 * i + 1] = new VertexPositionColorTexture(new Vector3(center - up * halfHeight, 0f), vertexColor, new Vector2(u, vBottom));
            }

            Render(texture, count * 2);
        }

        private static void Render(Texture2D texture, int vertexCount)
        {
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            effect ??= new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true };

            effect.World = Matrix.Identity;
            effect.View = Main.GameViewMatrix.TransformationMatrix;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0, Main.screenWidth, Main.screenHeight, 0, -1, 1);
            effect.Texture = texture;

            RasterizerState previousRasterizer = device.RasterizerState;
            device.RasterizerState = RasterizerState.CullNone;
            device.SamplerStates[0] = SamplerState.PointClamp;

            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, vertices, 0, vertexCount - 2);

            device.RasterizerState = previousRasterizer;
            Main.pixelShader.CurrentTechnique.Passes[0].Apply();
        }
    }
}
