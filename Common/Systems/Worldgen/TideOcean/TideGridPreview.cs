using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SoA.Common.Systems.TideOcean
{
    // Отладочный снимок биома: весь растр целиком выгружается в PNG один тайл = один пиксель.
    // Биом не влезает ни на экран, ни на карту мира, а посмотреть на него целиком надо.
    // Пишем свой минимальный энкодер: System.Drawing в .NET 8 недоступен,
    // а Texture2D.SaveAsPng требует GraphicsDevice, которого в потоке генерации нет.
    public static class TideGridPreview
    {
        // Цвета подобраны так, чтобы формы читались с одного взгляда,
        // а не так, как биом выглядит в игре
        private static readonly byte[] ColourUntouched = { 26, 24, 30 };    // ванильная порода вокруг
        private static readonly byte[] ColourStone = { 138, 92, 186 };      // Tidestone
        private static readonly byte[] ColourSand = { 74, 86, 170 };        // Tidesand
        private static readonly byte[] ColourWater = { 32, 104, 156 };      // вода
        private static readonly byte[] ColourAir = { 240, 228, 150 };       // воздушные карманы и секреты
        private static readonly byte[] ColourSky = { 176, 196, 214 };       // небо над зеркалом воды
        private static readonly byte[] ColourSeal = { 236, 64, 64 };        // места печатей

        public static string Save(TideGrid grid, IEnumerable<(int Gx, int Gy, int Width, int Height)> sealRects,
            int waterLineGy, string directory, string fileName)
        {
            int width = grid.Width;
            int height = grid.Height;

            // Для правого океана gx растёт на запад — переворачиваем, чтобы
            // картинка читалась как мир: запад слева
            bool flip = grid.Dir < 0;

            byte[] raw = new byte[height * (1 + width * 3)];
            int cursor = 0;

            for (int py = 0; py < height; py++)
            {
                raw[cursor++] = 0;   // фильтр строки: None

                for (int px = 0; px < width; px++)
                {
                    int gx = flip ? width - 1 - px : px;
                    byte cell = grid.Get(gx, py);
                    // Небо и воздушный карман — обе пустоты, но путать их на снимке нельзя
                    byte[] colour = cell == TideGrid.Air && py < waterLineGy ? ColourSky : ColourFor(cell);
                    raw[cursor++] = colour[0];
                    raw[cursor++] = colour[1];
                    raw[cursor++] = colour[2];
                }
            }

            foreach ((int rx, int ry, int rw, int rh) in sealRects)
                MarkSeal(raw, width, height, flip, rx, ry, rw, rh);

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            using (var file = File.Create(path))
                WritePng(file, width, height, raw);
            return path;
        }

        private static byte[] ColourFor(byte cell) => cell switch
        {
            TideGrid.Stone => ColourStone,
            TideGrid.Sand => ColourSand,
            TideGrid.Water => ColourWater,
            TideGrid.Air => ColourAir,
            _ => ColourUntouched
        };

        // Печати рисуем рамкой, а не заливкой: под ними должно быть видно,
        // действительно ли там узкий проход, а не сплошная порода
        private static void MarkSeal(byte[] raw, int width, int height, bool flip,
            int rx, int ry, int rw, int rh)
        {
            for (int dy = 0; dy < rh; dy++)
            {
                for (int dx = 0; dx < rw; dx++)
                {
                    bool onBorder = dx == 0 || dy == 0 || dx == rw - 1 || dy == rh - 1;
                    if (!onBorder)
                        continue;

                    int gx = rx + dx;
                    int gy = ry + dy;
                    if (gx < 0 || gy < 0 || gx >= width || gy >= height)
                        continue;

                    int px = flip ? width - 1 - gx : gx;
                    int offset = gy * (1 + width * 3) + 1 + px * 3;
                    raw[offset] = ColourSeal[0];
                    raw[offset + 1] = ColourSeal[1];
                    raw[offset + 2] = ColourSeal[2];
                }
            }
        }

        private static void WritePng(Stream output, int width, int height, byte[] raw)
        {
            output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;    // бит на канал
            header[9] = 2;    // цветовой тип: truecolour RGB
            WriteChunk(output, "IHDR", header);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(raw, 0, raw.Length);
            WriteChunk(output, "IDAT", compressed.ToArray());

            WriteChunk(output, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            output.Write(length);

            var payload = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type).CopyTo(payload, 0);
            data.CopyTo(payload, 4);
            output.Write(payload);

            var crc = new byte[4];
            WriteBigEndian(crc, 0, Crc32(payload));
            output.Write(crc);
        }

        private static void WriteBigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
