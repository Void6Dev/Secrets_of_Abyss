using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Common.Systems;

namespace SoA.Content.Tiles.Other
{
    // Замок печати 3x3 — та самая штука, по которой игрок понимает, ЧТО именно
    // держит проход. Сам по себе ничего не перекрывает: перекрывает мембрана
    // TideSealBarrier_tile вокруг, а замок только показывает состояние и по ПКМ
    // называет условие.
    //
    // Лист 106x52: клетки 0-2 запечатана, клетки 3-5 открыта. Состояние хранится
    // в frameX самого тайла, отдельного поля не нужно
    public class TideSeal_tile : ModTile
    {
        public const int SizeInTiles = 3;
        public const int FrameStep = 18;                      // клетка 16 + разбежка 2
        public const int StyleWidth = SizeInTiles * FrameStep; // сдвиг frameX между состояниями

        private static readonly Vector3 SealedLight = new(0.32f, 0.10f, 0.42f);
        private static readonly Vector3 OpenLight = new(0.16f, 0.55f, 0.68f);

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = false;

            TileID.Sets.DisableSmartCursor[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style3x3);
            TileObjectData.newTile.CoordinateWidth = 16;
            TileObjectData.newTile.CoordinatePadding = 2;
            TileObjectData.newTile.CoordinateHeights = new[] { 16, 16, 16 };
            TileObjectData.newTile.Origin = new Point16(1, 1);
            // Печать висит в шахте, опоры под ней нет — все якоря сняты,
            // иначе генератор не сможет её поставить в воде посреди прохода
            TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
            TileObjectData.newTile.AnchorTop = AnchorData.Empty;
            TileObjectData.newTile.AnchorLeft = AnchorData.Empty;
            TileObjectData.newTile.AnchorRight = AnchorData.Empty;
            TileObjectData.newTile.StyleHorizontal = true;
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaPlacement = LiquidPlacement.NotAllowed;
            TileObjectData.addTile(Type);

            DustType = DustID.BlueTorch;
            AddMapEntry(new Color(128, 96, 190), CreateMapEntryName());
        }

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            blockDamaged = false;
            return false;
        }

        public override bool CanExplode(int i, int j) => false;

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Vector3 light = IsOpen(i, j) ? OpenLight : SealedLight;
            r = light.X;
            g = light.Y;
            b = light.Z;
        }

        public override bool RightClick(int i, int j)
        {
            int step = TideSealSystem.StepAt(i, j);
            if (step == 0)
                return false;

            string key = IsOpen(i, j) ? "SealOpened" : "SealLocked" + step;
            Main.NewText(Language.GetTextValue("Mods.SoA.Misc." + key), 150, 200, 255);
            return true;
        }

        public static bool IsOpen(int i, int j) => Main.tile[i, j].TileFrameX >= StyleWidth;
    }
}
