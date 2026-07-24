using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace SoA.Content.Tiles.Other
{
    // Прибрежный факел: горит под водой (в лаве гаснет)
    public class Ttorch_tile : ModTile
    {
        private const int UnlitFrameStart = 66; // колонки 66+ — незажжённые варианты
        private static readonly Vector3 LightColor = new(0.15f, 0.65f, 0.95f);

        private Asset<Texture2D> _flameTexture;

        public override void SetStaticDefaults()
        {
            if (!Main.dedServ)
                _flameTexture = ModContent.Request<Texture2D>(Texture + "_Flame");

            Main.tileLighted[Type] = true;
            Main.tileSolid[Type] = false;
            Main.tileNoAttach[Type] = true;
            Main.tileFrameImportant[Type] = true;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = true;

            TileID.Sets.FramesOnKillWall[Type] = true;
            TileID.Sets.Torch[Type] = true;

            DustType = DustID.BlueTorch;
            AdjTiles = new int[] { TileID.Torches };

            AddMapEntry(new Color(45, 170, 200), CreateMapEntryName());

            TileObjectData.newTile.CopyFrom(TileObjectData.StyleTorch);
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.LavaDeath = true;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaPlacement = LiquidPlacement.NotAllowed;
            TileObjectData.addTile(Type);

            // Лист в ванильном формате торча (132x20): колонки 0/22/44 — пол и стены,
            // 66/88/110 — те же варианты незажжёнными. Анимации у тайла нет,
            // пламя нарисовано прямо в спрайте.
        }

        public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, ref TileDrawInfo drawData)
        {
            if (!Main.rand.NextBool(18))
                return;

            Tile tile = Main.tile[i, j];
            Vector2 flamePos = new(i * 16 + 8, j * 16 + 2);
            if (tile.TileFrameX == 22)
                flamePos.X -= 4; // крепление на стене
            else if (tile.TileFrameX == 44)
                flamePos.X += 4;

            // Под водой — пузырьки, на воздухе — искры пламени
            bool underwater = tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water;
            Dust d = Dust.NewDustPerfect(flamePos, underwater ? DustID.Water : DustID.BlueTorch);
            d.noGravity = true;
            d.velocity = new Vector2(Main.rand.NextFloatDirection() * 0.3f, -Main.rand.NextFloat(0.5f, 1.2f));
            d.scale = Main.rand.NextFloat(0.8f, 1.3f);
        }

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            // Лёгкое несинхронное мерцание между факелами
            float flicker = 0.9f + 0.1f * (float)Math.Sin(Main.GameUpdateCount * 0.08f + i * 1.3f + j * 0.7f);
            r = LightColor.X * flicker;
            g = LightColor.Y * flicker;
            b = LightColor.Z * flicker;
        }

        public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (tile.TileFrameX >= UnlitFrameStart)
                return;

            // Семь полупрозрачных копий пламени со случайным дрожанием — как у ванильных
            // факелов; сид от координат, чтобы дрожание было стабильным между кадрами отрисовки
            Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
            ulong randSeed = Main.TileFrameSeed ^ (ulong)((long)j << 32 | (uint)i);
            Color flameColor = new(100, 100, 100, 0);

            for (int c = 0; c < 7; c++)
            {
                float shakeX = Utils.RandomInt(ref randSeed, -10, 11) * 0.15f;
                float shakeY = Utils.RandomInt(ref randSeed, -10, 1) * 0.35f;
                spriteBatch.Draw(_flameTexture.Value,
                    new Vector2(i * 16 - (int)Main.screenPosition.X - 2 + shakeX, j * 16 - (int)Main.screenPosition.Y + shakeY) + zero,
                    new Rectangle(tile.TileFrameX, tile.TileFrameY, 20, 20),
                    flameColor, 0f, default, 1f, SpriteEffects.None, 0f);
            }
        }
    }
}
