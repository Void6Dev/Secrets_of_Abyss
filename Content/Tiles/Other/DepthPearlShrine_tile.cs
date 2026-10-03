using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Common.Graphics;
using SoA.Common.Players;
using SoA.Common.Systems;
using SoA.Content.Items.Consumables;
using SoA.Content.Worldgen;

namespace SoA.Content.Tiles.Other
{
    // Святилище жемчужины глубины: пьедестал 3x2 в шахте под снятой печатью. Появляется
    // вместе со снятием (TideSealSystem), поэтому есть и в старых мирах. Стоит уже под
    // давлением следующей ступени — дотянуться до жемчужины значит нырнуть туда без неё.
    //
    // Каждый игрок берёт свою жемчужину один раз (TidePressurePlayer.ShrinesTaken),
    // пьедестал общий и не пустеет — в мультиплеере никто не уводит её у остальных.
    // Ступень святилища — старшая печать над ним, отдельно её хранить не нужно
    public class DepthPearlShrine_tile : ModTile
    {
        private const int Width = 3;
        private const int Height = 2;
        private const int FrameStep = 18;

        // Где искать пол под святилище: глубже входа в зону, но не у самого её дна
        private const int MinDepthBelowSeal = 25;
        private const int MaxDepthBelowSeal = 60;
        private const int FallbackDepthBelowSeal = 120;
        private const int SearchHalfWidth = 20;

        private const float PearlHover = 22f;      // жемчужина висит над пьедесталом, px
        private const float PearlBob = 3f;
        private static readonly Vector3 ShrineLight = new(0.18f, 0.42f, 0.55f);
        private static readonly Color PearlGlow = new(140, 220, 255);

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = false;

            TileID.Sets.DisableSmartCursor[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style3x2);
            TileObjectData.newTile.CoordinateHeights = new[] { 16, 16 };
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaPlacement = LiquidPlacement.NotAllowed;
            TileObjectData.addTile(Type);

            DustType = DustID.BlueTorch;
            AddMapEntry(new Color(110, 190, 230), CreateMapEntryName());
        }

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            blockDamaged = false;
            return false;
        }

        public override bool CanExplode(int i, int j) => false;

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            r = ShrineLight.X;
            g = ShrineLight.Y;
            b = ShrineLight.Z;
        }

        public override void MouseOver(int i, int j)
        {
            Player player = Main.LocalPlayer;
            if (HasPearlFor(player, i, j))
            {
                player.noThrow = 2;
                player.cursorItemIconEnabled = true;
                player.cursorItemIconID = ModContent.ItemType<DepthPearl>();
            }
        }

        public override bool RightClick(int i, int j)
        {
            Player player = Main.LocalPlayer;
            int step = TideSealSystem.SealsAbove(j);
            if (step == 0)
                return false;

            var pressure = player.GetModPlayer<TidePressurePlayer>();
            if (pressure.HasTakenShrine(step))
            {
                Main.NewText(Language.GetTextValue("Mods.SoA.Misc.ShrineEmpty"), 150, 200, 255);
                return true;
            }

            pressure.MarkShrineTaken(step);
            player.QuickSpawnItem(new EntitySource_TileInteraction(player, i, j), ModContent.ItemType<DepthPearl>());
            SoundEngine.PlaySound(SoundID.Item4 with { Pitch = -0.3f }, new Vector2(i, j) * 16f);
            return true;
        }

        private static bool HasPearlFor(Player player, int i, int j)
        {
            int step = TideSealSystem.SealsAbove(j);
            return step != 0 && !player.GetModPlayer<TidePressurePlayer>().HasTakenShrine(step);
        }

        #region Отрисовка

        // Жемчужина над пьедесталом качается — рисуется каждый кадр, а не в кэше тайлов
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (tile.TileFrameX == 0 && tile.TileFrameY == 0)
                Main.instance.TilesRenderer.AddSpecialPoint(i, j, TileDrawing.TileCounterType.CustomNonSolid);
            return true;
        }

        public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (!tile.HasTile || tile.TileType != Type || !HasPearlFor(Main.LocalPlayer, i, j))
                return;

            float time = Main.GlobalTimeWrappedHourly;
            Vector2 at = new Vector2((i + Width / 2f) * 16f, j * 16f - PearlHover + (float)Math.Sin(time * 2f) * PearlBob)
                - Main.screenPosition;

            Texture2D glow = SoAVfx.SoftGlow;
            float pulse = 0.75f + 0.25f * (float)Math.Sin(time * 3f);
            spriteBatch.Draw(glow, at, null, SoAVfx.Additive(PearlGlow) * (0.55f * pulse), 0f, glow.Size() / 2f,
                40f / glow.Width, SpriteEffects.None, 0f);

            Main.instance.LoadItem(ModContent.ItemType<DepthPearl>());
            Texture2D pearl = TextureAssets.Item[ModContent.ItemType<DepthPearl>()].Value;
            spriteBatch.Draw(pearl, at, null, Color.White, 0f, pearl.Size() / 2f, 1f, SpriteEffects.None, 0f);
        }

        #endregion

        #region Установка

        // Ставит святилище в шахте под печатью. Только сервер / одиночная игра.
        // Ищет ровный пол в окне глубин, при неудаче — глубже, в крайнем случае ставит
        // в любой свободный проём: печать ведь уже снята, и жемчужина должна быть
        public static void TryRaise(TideSealSite site)
        {
            int centerX = site.X + site.Width / 2;
            int bottom = site.Y + site.Height;

            if (!TryFindSpot(centerX, bottom + MinDepthBelowSeal, bottom + MaxDepthBelowSeal, true, out Point origin) &&
                !TryFindSpot(centerX, bottom + 2, bottom + FallbackDepthBelowSeal, true, out origin) &&
                !TryFindSpot(centerX, bottom + 2, bottom + FallbackDepthBelowSeal, false, out origin))
            {
                ModContent.GetInstance<SoA>().Logger.Warn($"No room for the pearl shrine under seal {site.Step}");
                return;
            }

            ushort type = (ushort)ModContent.TileType<DepthPearlShrine_tile>();
            for (int dx = 0; dx < Width; dx++)
            {
                for (int dy = 0; dy < Height; dy++)
                {
                    Tile tile = Main.tile[origin.X + dx, origin.Y + dy];
                    tile.ResetToType(type);
                    // Кадр руками, как у замка печати: святилище может стоять в воде без опоры
                    tile.TileFrameX = (short)(dx * FrameStep);
                    tile.TileFrameY = (short)(dy * FrameStep);
                }
            }

            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendTileSquare(-1, origin.X, origin.Y, Width, Height);
        }

        // Ближе к оси шахты и выше — лучше. needFloor — под пьедесталом сплошной грунт
        private static bool TryFindSpot(int centerX, int fromY, int toY, bool needFloor, out Point origin)
        {
            for (int y = fromY; y <= toY; y++)
            {
                for (int offset = 0; offset <= SearchHalfWidth; offset++)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        int left = centerX - 1 + offset * sign;
                        if (IsSpotFree(left, y, needFloor))
                        {
                            origin = new Point(left, y);
                            return true;
                        }
                        if (offset == 0)
                            break;
                    }
                }
            }
            origin = default;
            return false;
        }

        private static bool IsSpotFree(int left, int top, bool needFloor)
        {
            if (!WorldGen.InWorld(left, top, 12) || !WorldGen.InWorld(left + Width, top + Height + 1, 12))
                return false;

            for (int dx = 0; dx < Width; dx++)
            {
                for (int dy = 0; dy < Height; dy++)
                {
                    if (Main.tile[left + dx, top + dy].HasTile)
                        return false;
                }

                if (needFloor)
                {
                    Tile floor = Main.tile[left + dx, top + Height];
                    if (!floor.HasTile || !Main.tileSolid[floor.TileType] || Main.tileSolidTop[floor.TileType])
                        return false;
                }
            }
            return true;
        }

        #endregion
    }
}
