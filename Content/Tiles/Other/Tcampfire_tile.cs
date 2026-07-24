using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Tiles.Other
{
    // Приливный костёр 3x2: считается костром (бафф регенерации), горит под водой.
    // Лист 56x324: 8 кадров пламени по 36px + нижний ряд — потухший костёр.
    // Потухшее состояние кодируется сдвигом базового TileFrameY на +36;
    // тушится и зажигается правым кликом или сигналом провода.
    public class Tcampfire_tile : ModTile
    {
        private const int AnimationFrames = 8;
        private const int FrameHeight = 36;
        private static readonly Vector3 LightColor = new(0.25f, 0.8f, 1.1f);

        private static bool IsLit(int i, int j) => Main.tile[i, j].TileFrameY < FrameHeight;

        public override void SetStaticDefaults()
        {
            Main.tileLighted[Type] = true;
            Main.tileFrameImportant[Type] = true;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = true;

            TileID.Sets.Campfire[Type] = true;
            TileID.Sets.InteractibleByNPCs[Type] = true;

            DustType = DustID.BlueTorch;
            AdjTiles = new int[] { TileID.Campfire };

            AddMapEntry(new Color(45, 170, 200), CreateMapEntryName());

            TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
            TileObjectData.newTile.Width = 3;
            TileObjectData.newTile.Height = 2;
            TileObjectData.newTile.Origin = new Point16(1, 1);
            TileObjectData.newTile.CoordinateHeights = new int[] { 16, 16 };
            TileObjectData.newTile.AnchorBottom = new AnchorData(
                AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table, 3, 0);
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaPlacement = LiquidPlacement.NotAllowed;
            TileObjectData.addTile(Type);

            AnimationFrameHeight = FrameHeight; // кадр листа: 2 ряда ячеек по 18px
        }

        public override void AnimateTile(ref int frame, ref int frameCounter)
        {
            if (++frameCounter >= 6)
            {
                frameCounter = 0;
                frame = (frame + 1) % AnimationFrames;
            }
        }

        public override void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset)
        {
            // frameYOffset приходит уже равным сдвигу текущего кадра анимации.
            // Потухший костёр не анимируется: заменяем сдвиг целиком, целясь
            // в нижний блок листа (9-й, y = 288; базовый frameY уже содержит +36)
            if (!IsLit(i, j))
                frameYOffset = (AnimationFrames - 1) * FrameHeight;
        }

        public override void NearbyEffects(int i, int j, bool closer)
        {
            // Засчитываем сцене костёр — ваниль сама выдаст бафф Cozy Fire
            if (IsLit(i, j))
                Main.SceneMetrics.HasCampfire = true;
        }

        public override void MouseOver(int i, int j)
        {
            Player player = Main.LocalPlayer;
            player.noThrow = 2;
            player.cursorItemIconEnabled = true;
            player.cursorItemIconID = ModContent.ItemType<Tcampfire>();
        }

        public override bool RightClick(int i, int j)
        {
            ToggleFire(i, j);
            return true;
        }

        public override void HitWire(int i, int j)
        {
            Tile tile = Main.tile[i, j];
            int left = i - tile.TileFrameX / 18;
            int top = j - tile.TileFrameY % FrameHeight / 18;

            // Один сигнал — одно переключение, даже если провод задел несколько ячеек
            for (int x = left; x < left + 3; x++)
                for (int y = top; y < top + 2; y++)
                    Wiring.SkipWire(x, y);

            ToggleFire(i, j);
        }

        private static void ToggleFire(int i, int j)
        {
            Tile tile = Main.tile[i, j];
            int left = i - tile.TileFrameX / 18;
            int top = j - tile.TileFrameY % FrameHeight / 18;
            bool turningOff = IsLit(i, j);

            for (int x = left; x < left + 3; x++)
                for (int y = top; y < top + 2; y++)
                    Main.tile[x, y].TileFrameY += (short)(turningOff ? FrameHeight : -FrameHeight);

            SoundEngine.PlaySound(
                turningOff
                    ? SoundID.SplashWeak with { Volume = 0.7f, Pitch = -0.3f }
                    : SoundID.Item34 with { Volume = 0.5f, Pitch = 0.4f },
                new Vector2(left, top) * 16f);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                NetMessage.SendTileSquare(-1, left, top, 3, 2);
        }

        public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, ref TileDrawInfo drawData)
        {
            Tile tile = Main.tile[i, j];
            // Искры только из верхнего ряда пламени
            if (tile.TileFrameY != 0 || !Main.rand.NextBool(10))
                return;

            bool underwater = tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water;
            Dust d = Dust.NewDustDirect(new Vector2(i * 16, j * 16), 16, 8,
                underwater ? DustID.Water : DustID.BlueTorch);
            d.noGravity = true;
            d.velocity = new Vector2(Main.rand.NextFloatDirection() * 0.4f, -Main.rand.NextFloat(0.8f, 1.8f));
            d.scale = Main.rand.NextFloat(0.9f, 1.5f);
        }

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Tile tile = Main.tile[i, j];
            // Светит только центральная верхняя ячейка, чтобы свет не троился
            if (tile.TileFrameX != 18 || tile.TileFrameY != 0)
                return;

            float flicker = 0.9f + 0.1f * (float)Math.Sin(Main.GameUpdateCount * 0.06f + i);
            r = LightColor.X * flicker;
            g = LightColor.Y * flicker;
            b = LightColor.Z * flicker;
        }
    }
}
