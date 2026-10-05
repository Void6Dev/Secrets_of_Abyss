using System;
using System.Collections.Generic;
using System.IO;
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
using SoA.Content.Items.BossSummons;
using SoA.Content.Items.Placeables;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Content.Tiles.Shrines
{
    // Королевский приливный алтарь 5x7: ПКМ с Королевской приманкой в инвентаре
    // приносит её в жертву и призывает Короля-краба.
    // Лист 90x252: сверху пустой алтарь, снизу с жемчужиной в чаше
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

        // Жемчужина: ячейки, где нижний кадр отличается от верхнего, и её центр в px
        // от левого верхнего угла алтаря
        private const int PearlSheetOffsetY = TileHeight * 18;
        private const int PearlFirstColumn = 1;
        private const int PearlLastColumn = 3;
        private const int PearlFirstRow = 2;
        private const int PearlLastRow = 4;
        private static readonly Vector2 PearlCenter = new(40f, 53f);
        private static readonly Color PearlGlow = new(170, 200, 255);
        private static readonly Vector3 PearlLight = new(0.35f, 0.4f, 0.55f);

        private const float PearlFadeInStep = 1f / 45f;
        private const float PearlFadeOutStep = 1f / 60f;
        // Король появляется не сразу (в мультиплеере — после ответа сервера),
        // столько тиков жемчужина держится и без него
        private const int BossSpawnGrace = 180;
        // Насколько свежей должна быть жемчужина, чтобы король пришёл за ней
        private const uint ClaimWindow = 600;

        private class PearlState
        {
            public float Progress;
            public int SinceOffered;
            public uint OfferedAt;
            public bool Claimed;
        }

        // Жемчужины по левому верхнему углу алтаря. Кадры тайла в мире не меняются:
        // клиенты по ним рисуют жемчужину, сервер — помнит, к какому алтарю вести короля.
        // Лежит, пока король её не съест (TakePearl); ушёл без неё — гаснет
        private static readonly Dictionary<Point16, PearlState> Pearls = new();

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

            // Приманка не расходуется: все призывалки мода многоразовые
            SummonEffects(i, j);
            Point16 topLeft = TopLeft(i, j);
            OfferPearl(topLeft);
            SendPearl(topLeft);
            SoundEngine.PlaySound(SoundID.Roar, player.Center);

            if (Main.netMode != NetmodeID.MultiplayerClient)
                NPC.SpawnOnPlayer(player.whoAmI, bossType);
            else
                NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent,
                    number: player.whoAmI, number2: bossType);
            return true;
        }

        private static Point16 TopLeft(int i, int j)
        {
            Tile tile = Main.tile[i, j];
            return new Point16(i - tile.TileFrameX / 18, j - tile.TileFrameY / 18);
        }

        // Вспышка над чашей алтаря в момент жертвы
        private static void SummonEffects(int i, int j)
        {
            Vector2 top = (TopLeft(i, j).ToVector2() + new Vector2(BowlColumn, BowlRow)) * 16f + new Vector2(8f, -4f);
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
            float pearl = Pearls.TryGetValue(new Point16(i - BowlColumn, j - BowlRow), out PearlState state)
                ? state.Progress : 0f;
            r = LightColor.X * pulse + PearlLight.X * pearl;
            g = LightColor.Y * pulse + PearlLight.Y * pearl;
            b = LightColor.Z * pulse + PearlLight.Z * pearl;
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

        #region Pearl

        private static void OfferPearl(Point16 topLeft)
        {
            if (!Pearls.TryGetValue(topLeft, out PearlState pearl))
                Pearls[topLeft] = pearl = new PearlState();
            pearl.SinceOffered = 0;
            pearl.OfferedAt = Main.GameUpdateCount;
            pearl.Claimed = false;
        }

        private static Vector2 PearlWorld(Point16 topLeft) => topLeft.ToVector2() * 16f + PearlCenter;

        // Король на спавне ищет свежую жемчужину рядом с призвавшим — к ней он и выйдет.
        // Только сервер / одиночная игра. Серверу жемчужина больше не нужна, клиенты
        // держат свою до хвата (TakePearl)
        public static bool TryClaimPearl(Vector2 near, float range, out Vector2 pearlWorld)
        {
            pearlWorld = Vector2.Zero;
            Point16 best = default;
            float bestDist = range;
            foreach (var (topLeft, pearl) in Pearls)
            {
                float dist = Vector2.Distance(PearlWorld(topLeft), near);
                if (pearl.Claimed || Main.GameUpdateCount - pearl.OfferedAt > ClaimWindow || dist >= bestDist)
                    continue;
                best = topLeft;
                bestDist = dist;
                pearlWorld = PearlWorld(topLeft);
            }

            if (pearlWorld == Vector2.Zero)
                return false;
            if (Main.dedServ)
                Pearls.Remove(best);
            else
                Pearls[best].Claimed = true;
            return true;
        }

        // Клешня короля сомкнулась на жемчужине: из чаши она пропадает сразу, без затухания
        public static void TakePearl(Vector2 pearlWorld)
        {
            foreach (var (topLeft, _) in Pearls)
            {
                if (Vector2.DistanceSquared(PearlWorld(topLeft), pearlWorld) > 16f * 16f)
                    continue;
                Pearls.Remove(topLeft);
                for (int k = 0; k < 10; k++)
                {
                    Dust d = Dust.NewDustPerfect(pearlWorld, DustID.TreasureSparkle, Main.rand.NextVector2Circular(2f, 2f));
                    d.noGravity = true;
                    d.scale = Main.rand.NextFloat(0.8f, 1.2f);
                }
                return;
            }
        }

        // Вызывается раз в тик на клиентах — заодно ведём проявление жемчужин
        public override void AnimateTile(ref int frame, ref int frameCounter)
        {
            if (Pearls.Count == 0)
                return;

            bool bossAlive = NPC.AnyNPCs(ModContent.NPCType<King_crab>());
            List<Point16> faded = null;
            foreach (var (topLeft, pearl) in Pearls)
            {
                pearl.SinceOffered++;
                bool held = bossAlive || pearl.SinceOffered < BossSpawnGrace;
                pearl.Progress = MathHelper.Clamp(pearl.Progress + (held ? PearlFadeInStep : -PearlFadeOutStep), 0f, 1f);
                if (!held && pearl.Progress <= 0f)
                    (faded ??= new List<Point16>()).Add(topLeft);
            }
            faded?.ForEach(topLeft => Pearls.Remove(topLeft));
        }

        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (tile.TileFrameX == 0 && tile.TileFrameY == 0 && Pearls.ContainsKey(new Point16(i, j)))
                Main.instance.TilesRenderer.AddSpecialPoint(i, j, TileDrawing.TileCounterType.CustomNonSolid);
            return true;
        }

        // Нижний кадр листа проступает поверх верхнего, в середине перехода — вспышка
        public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
        {
            if (!Pearls.TryGetValue(new Point16(i, j), out PearlState pearl) || pearl.Progress <= 0f)
                return;

            float shown = pearl.Progress * pearl.Progress * (3f - 2f * pearl.Progress);
            Texture2D sheet = TextureAssets.Tile[Type].Value;
            for (int dx = PearlFirstColumn; dx <= PearlLastColumn; dx++)
            {
                for (int dy = PearlFirstRow; dy <= PearlLastRow; dy++)
                {
                    var source = new Rectangle(dx * 18, PearlSheetOffsetY + dy * 18, 16, 16);
                    Vector2 at = new Vector2(i + dx, j + dy) * 16f - Main.screenPosition;
                    spriteBatch.Draw(sheet, at, source, Lighting.GetColor(i + dx, j + dy) * shown);
                }
            }

            Texture2D glow = SoAVfx.SoftGlow;
            float flash = (float)Math.Sin(shown * MathHelper.Pi);
            float shimmer = 0.85f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.5f);
            float strength = flash * 0.6f + shown * 0.25f * shimmer;
            Vector2 center = new Vector2(i, j) * 16f + PearlCenter - Main.screenPosition;
            spriteBatch.Draw(glow, center, null, SoAVfx.Additive(PearlGlow) * strength, 0f, glow.Size() / 2f,
                44f / glow.Width, SpriteEffects.None, 0f);
        }

        // Клиент -> сервер -> остальные клиенты: на каком алтаре положили жемчужину.
        // Пакет уходит раньше запроса на призыв, поэтому сервер знает алтарь к спавну короля
        private static void SendPearl(Point16 topLeft, int ignoreClient = -1)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            ModPacket packet = ModContent.GetInstance<SoA>().GetPacket();
            packet.Write((byte)SoAPacketType.AltarPearl);
            packet.Write(topLeft.X);
            packet.Write(topLeft.Y);
            packet.Send(-1, ignoreClient);
        }

        public static void ReceivePearl(BinaryReader reader, int sender)
        {
            var topLeft = new Point16(reader.ReadInt16(), reader.ReadInt16());
            OfferPearl(topLeft); // серверу — чтобы король знал, к какому алтарю идти
            if (Main.netMode == NetmodeID.Server)
                SendPearl(topLeft, sender);
        }

        #endregion
    }
}
