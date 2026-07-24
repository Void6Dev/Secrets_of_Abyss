using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.DataStructures;
using SoA.Common.CustomClasses;

namespace SoA.Content.Items.Materials
{
    public class HellStar : ModItem
    {
        private readonly SimpleItemAnimation _anim = new(5, 15);
        private int _evaporateTimer;

        public override void SetStaticDefaults()
        {
            Main.RegisterItemAnimation(Item.type, new DrawAnimationVertical(15, 5));
            ItemID.Sets.ItemIconPulse[Item.type] = false;
        }

        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 16;
            Item.scale = 1;
            Item.value = Item.buyPrice(gold: 10);
            Item.maxStack = 9999;
            Item.rare = ItemRarityID.Orange;
        }

        public override void PostUpdate()
        {
            _anim.Update();

            Lighting.AddLight(Item.Center, 1.2f, 0.4f, 0.0f);

            bool onSurface = Item.Center.Y / 16f < Main.worldSurface;

            if (onSurface)
            {
                _evaporateTimer++;

                // Учащённые частицы во время испарения
                int dustChance = Math.Max(1, 5 - _evaporateTimer / 12);
                if (Main.rand.NextBool(dustChance))
                {
                    Dust d = Dust.NewDustDirect(Item.position, Item.width, Item.height, DustID.Smoke);
                    d.noGravity = true;
                    d.scale    = Main.rand.NextFloat(0.6f, 1.2f);
                    d.velocity = new Vector2(Main.rand.NextFloatDirection() * 1.0f, -Main.rand.NextFloat(2f, 5f));
                    d.color    = new Color(200, 80, 0);
                    d.fadeIn   = 0.8f;
                }

                if (_evaporateTimer >= 60)
                {
                    for (int i = 0; i < 12; i++)
                    {
                        Dust d = Dust.NewDustDirect(Item.position, Item.width, Item.height, DustID.Torch);
                        d.noGravity = true;
                        d.scale    = Main.rand.NextFloat(1f, 2f);
                        d.velocity = new Vector2(Main.rand.NextFloatDirection() * 3f, -Main.rand.NextFloat(3f, 7f));
                        d.fadeIn   = 1f;
                    }
                    Item.TurnToAir();
                    return;
                }
            }
            else
            {
                _evaporateTimer = 0;
            }

            if (Main.rand.NextBool(6))
            {
                Dust d = Dust.NewDustDirect(Item.position, Item.width, Item.height, DustID.Torch);
                d.noGravity = true;
                d.scale    = Main.rand.NextFloat(0.5f, 1.0f);
                d.velocity = new Vector2(Main.rand.NextFloatDirection() * 0.6f, -Main.rand.NextFloat(1.5f, 3.0f));
                d.fadeIn   = 0.5f;
            }
        }

        public override Color? GetAlpha(Color lightColor) => Color.White;

        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
            ref float rotation, ref float scale, int whoAmI)
        {
            Texture2D tex    = ModContent.Request<Texture2D>(Texture).Value;
            Rectangle frame  = _anim.GetFrame(tex); 
            Vector2 origin   = _anim.GetOrigin(tex);
            Vector2 pos      = Item.Center - Main.screenPosition;

            // Аддитивное свечение
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            float pulse = 0.3f + 0.2f * (float)Math.Sin(Main.GameUpdateCount * 0.03f);
            spriteBatch.Draw(tex, pos, frame, new Color(255, 120, 0) * pulse,
                rotation, origin, scale * (1.6f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.07f)), SpriteEffects.None, 0);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null,
                Main.GameViewMatrix.TransformationMatrix);

            // Основной спрайт вручную (с анимацией)
            spriteBatch.Draw(tex, pos, frame, alphaColor, rotation, origin, scale, SpriteEffects.None, 0);

            return false;
        }
    }
}