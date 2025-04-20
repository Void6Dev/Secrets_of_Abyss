using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SoA.Content.Items.Placebles;
using Terraria.Localization;
using System;
namespace SoA.Content.Tiles.Other

{
    public class Ttorch_tile : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileLighted[Type] = true;
            Main.tileSolid[Type] = false;
            Main.tileLavaDeath[Type] = false;
            Main.tileFrameImportant[Type] = true;

            AddMapEntry(new Color(50, 168, 82), Language.GetText("TideTorch"));

            // Настройки плитки факела
            TileObjectData.newTile.CopyFrom(TileObjectData.StyleTorch);
            TileObjectData.newTile.LavaDeath = false;
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.addTile(Type);
        }


        public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
        {
            // Рисуем светящийся эффект
            Texture2D texture = ModContent.Request<Texture2D>("SoA/Content/Effects/CustomTorchGlow").Value;
            Vector2 drawPosition = new Vector2(i * 16, j * 16);
            spriteBatch.Draw(texture, drawPosition, Color.White);
        }
        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            // Цвет свечения факела (например, синий свет)
            r = 0.2f;
            g = 0.4f;
            b = 1f;
        }
        
    }
}