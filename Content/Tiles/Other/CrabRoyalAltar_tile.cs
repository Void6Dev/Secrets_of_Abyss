using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Content.Items.BossSummons;
using SoA.Content.Items.Placebles;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Content.Tiles.Other
{
    // Королевский приливный алтарь 5x7: ПКМ с Королевской приманкой в инвентаре
    // приносит её в жертву и призывает Короля-краба
    public class CrabRoyalAltar_tile : ModTile
    {
        private const int TileWidth = 5;
        private const int TileHeight = 7;

        // Ячейка чаши на листе (col 2, row 4) — источник света и искр
        private const int BowlColumn = 2;
        private const int BowlRow = 4;
        private const int BowlFrameX = BowlColumn * 18;
        private const int BowlFrameY = BowlRow * 18;

        private static readonly Vector3 LightColor = new(0.18f, 0.7f, 0.85f);

        public override void SetStaticDefaults()
        {
            Main.tileLighted[Type] = true;
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = false;

            TileID.Sets.DisableSmartCursor[Type] = true;

            DustType = DustID.Stone;
            AddMapEntry(new Color(210, 170, 60), CreateMapEntryName());

            TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
            TileObjectData.newTile.Width = TileWidth;
            TileObjectData.newTile.Height = TileHeight;
            TileObjectData.newTile.Origin = new Point16(TileWidth / 2, TileHeight - 1);
            TileObjectData.newTile.CoordinateWidth = 16;
            TileObjectData.newTile.CoordinatePadding = 2;
            TileObjectData.newTile.CoordinateHeights = new int[] { 16, 16, 16, 16, 16, 16, 16 };
            TileObjectData.newTile.AnchorBottom = new AnchorData(
                AnchorType.SolidTile | AnchorType.SolidWithTop, TileWidth, 0);
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaPlacement = LiquidPlacement.NotAllowed;
            TileObjectData.addTile(Type);

            RegisterItemDrop(ModContent.ItemType<CrabRoyalAltar>());
        }

        public override bool RightClick(int i, int j)
        {
            Player player = Main.LocalPlayer;
            int bossType = ModContent.NPCType<King_crab>();
            if (NPC.AnyNPCs(bossType))
                return false;

            int baitType = ModContent.ItemType<CrabRoyalBait>();
            if (!player.HasItem(baitType))
            {
                Main.NewText(Language.GetTextValue("Mods.SoA.Misc.AltarNeedsBait"), 140, 210, 255);
                return true;
            }

            player.ConsumeItem(baitType);
            SummonEffects(i, j);
            SoundEngine.PlaySound(SoundID.Roar, player.Center);

            if (Main.netMode != NetmodeID.MultiplayerClient)
                NPC.SpawnOnPlayer(player.whoAmI, bossType);
            else
                NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent,
                    number: player.whoAmI, number2: bossType);
            return true;
        }

        // Вспышка над чашей алтаря в момент жертвы
        private static void SummonEffects(int i, int j)
        {
            Tile tile = Main.tile[i, j];
            Vector2 topLeftTile = new Vector2(i - tile.TileFrameX / 18, j - tile.TileFrameY / 18);
            Vector2 top = (topLeftTile + new Vector2(BowlColumn, BowlRow)) * 16f + new Vector2(8f, -4f);
            for (int k = 0; k < 30; k++)
            {
                Dust d = Dust.NewDustPerfect(top, DustID.TreasureSparkle,
                    Main.rand.NextVector2Circular(3f, 3f) - new Vector2(0f, 2f));
                d.noGravity = true;
                d.scale = Main.rand.NextFloat(1f, 1.6f);
            }
        }

        public override void MouseOver(int i, int j)
        {
            Player player = Main.LocalPlayer;
            player.noThrow = 2;
            player.cursorItemIconEnabled = true;
            player.cursorItemIconID = ModContent.ItemType<CrabRoyalBait>();
        }

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Tile tile = Main.tile[i, j];
            // Светит только ячейка чаши, чтобы свет не дублировался по всем 35 ячейкам
            if (tile.TileFrameX != BowlFrameX || tile.TileFrameY != BowlFrameY)
                return;

            float pulse = 0.85f + 0.15f * (float)Math.Sin(Main.GameUpdateCount * 0.045f + i);
            r = LightColor.X * pulse;
            g = LightColor.Y * pulse;
            b = LightColor.Z * pulse;
        }

        public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, ref TileDrawInfo drawData)
        {
            Tile tile = Main.tile[i, j];
            if (tile.TileFrameX != BowlFrameX || tile.TileFrameY != BowlFrameY || !Main.rand.NextBool(18))
                return;

            Dust d = Dust.NewDustDirect(new Vector2(i * 16, j * 16 - 4), 16, 8, DustID.TreasureSparkle);
            d.noGravity = true;
            d.velocity = new Vector2(0f, -Main.rand.NextFloat(0.2f, 0.7f));
            d.scale = Main.rand.NextFloat(0.7f, 1.1f);
        }
    }
}
