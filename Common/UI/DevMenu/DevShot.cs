using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace SoA.Common.UI
{
    // Снимок экрана для заметки. Берём кадр из бэкбуфера уже после того, как меню
    // закрылось: в снимке должна быть игра, а не оверлей доски задач
    public static class DevShot
    {
        public static string Capture(out string error)
        {
            error = null;
            try
            {
                GraphicsDevice device = Main.instance.GraphicsDevice;
                int width = device.PresentationParameters.BackBufferWidth;
                int height = device.PresentationParameters.BackBufferHeight;

                var pixels = new Color[width * height];
                device.GetBackBufferData(pixels);

                Directory.CreateDirectory(DevBoard.ShotFolderPath);
                string fileName = $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png";

                using var texture = new Texture2D(device, width, height);
                texture.SetData(pixels);
                using FileStream stream = File.Create(Path.Combine(DevBoard.ShotFolderPath, fileName));
                texture.SaveAsPng(stream, width, height);

                return fileName;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }
    }
}
