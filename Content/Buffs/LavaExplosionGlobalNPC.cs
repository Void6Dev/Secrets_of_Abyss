using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.GameContent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Buffs
{
    public class LavaExplosionGlobalNPC : GlobalNPC
    {
        public int cumulativeDamage = 0;

        public override bool InstancePerEntity => true;

        // Tint the NPC redder the more damage has accumulated
        public override void DrawEffects(NPC npc, ref Color drawColor)
        {
            if (cumulativeDamage <= 0 || !npc.HasBuff(ModContent.BuffType<LavaExplosionDebuff>()))
                return;

            float intensity = MathHelper.Clamp(cumulativeDamage / 350f, 0f, 1f);
            drawColor = Color.Lerp(drawColor, new Color(255, 30, 30), intensity * 0.72f);
        }

        // Draw the damage counter above the NPC's head
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (cumulativeDamage <= 0)
                return;

            int buffIndex = npc.FindBuffIndex(ModContent.BuffType<LavaExplosionDebuff>());
            if (buffIndex < 0)
                return;

            bool aboutToExplode = npc.buffTime[buffIndex] <= 15;
            string text = cumulativeDamage.ToString();
            var font = FontAssets.MouseText.Value;
            Vector2 textSize = font.MeasureString(text);
            // Center above the NPC's top
            Vector2 pos = npc.Top - screenPos - new Vector2(textSize.X * 0.5f, textSize.Y + 4f);

            if (aboutToExplode)
            {
                float pulse = (float)Math.Sin(Main.GameUpdateCount * 0.5f) * 0.5f + 0.5f;
                float scale = 1.1f + pulse * 0.25f;


                Color glow = new Color(255, 55, 0) * (0.38f + pulse * 0.38f);
                for (int ox = -2; ox <= 2; ox += 2)
                for (int oy = -2; oy <= 2; oy += 2)
                {
                    if (ox == 0 && oy == 0) continue;
                    Utils.DrawBorderString(spriteBatch, text, pos + new Vector2(ox, oy), glow, scale);
                }

                Color core = Color.Lerp(Color.OrangeRed, Color.White, pulse);
                Utils.DrawBorderString(spriteBatch, text, pos, core, scale);
            }
            else
            {
                float intensity = MathHelper.Clamp(cumulativeDamage / 350f, 0f, 1f);
                Color textColor = Color.Lerp(new Color(255, 210, 80), Color.OrangeRed, intensity);
                Utils.DrawBorderString(spriteBatch, text, pos, textColor, 1f);
            }
        }
    }
}
