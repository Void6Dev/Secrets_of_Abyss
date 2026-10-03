using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Content.Items.Placebles;

namespace SoA.Content.Tiles.Other
{
    // Лампа маяка 2x2. Сама только светит и хранит состояние (LighthouseLampEntity);
    // луч рисует LighthouseBeamRenderer, поворачивают его маячной цепью.
    // ПКМ по лампе — зажечь или погасить.
    // Текстура — ванильный хрустальный шар (placeholder до собственного арта)
    public class LighthouseLamp_tile : ModTile
    {
        private static readonly Vector3 LampLight = new(1.15f, 0.95f, 0.6f);

        public override string Texture => "Terraria/Images/Tiles_" + TileID.CrystalBall;

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.HasOutlines[Type] = false;
            TileID.Sets.DisableSmartCursor[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
            TileObjectData.newTile.CoordinateHeights = new[] { 16, 16 };
            TileObjectData.newTile.LavaDeath = true;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.HookPostPlaceMyPlayer =
                ModContent.GetInstance<LighthouseLampEntity>().Generic_HookPostPlaceMyPlayer;
            TileObjectData.addTile(Type);

            DustType = DustID.Glass;
            AddMapEntry(new Color(255, 225, 150), CreateMapEntryName());
        }

        public override void KillMultiTile(int i, int j, int frameX, int frameY)
            => ModContent.GetInstance<LighthouseLampEntity>().Kill(i, j);

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            if (!LighthouseLampEntity.TryGetAt(i, j, out LighthouseLampEntity lamp) || !lamp.Lit)
                return;

            r = LampLight.X;
            g = LampLight.Y;
            b = LampLight.Z;
        }

        public override void MouseOver(int i, int j)
        {
            Player player = Main.LocalPlayer;
            player.noThrow = 2;
            player.cursorItemIconEnabled = true;
            player.cursorItemIconID = ModContent.ItemType<LighthouseLamp>();
        }

        public override bool RightClick(int i, int j)
        {
            if (!LighthouseLampEntity.TryGetAt(i, j, out LighthouseLampEntity lamp))
                return false;

            lamp.Lit = !lamp.Lit;
            lamp.SendState();
            SoundEngine.PlaySound(SoundID.MenuTick, lamp.Center);
            return true;
        }
    }
}
