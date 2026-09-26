using System;
using Terraria;
using Terraria.ModLoader;
using Terraria.GameContent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Particles;
using SoA.Content.Projectiles;

namespace SoA.Content.Buffs
{
    // Лавовая метка Когтей на враге: копит урон ударов и взрывается, когда удары прекратились
    // (метка истекла) или враг погиб. Урон копится на машине игрока, который бьёт, — там же
    // и взрыв: он спавнит LavaClawBurst от своего имени, и сеть синхронизирует его как любой снаряд
    public class LavaExplosionGlobalNPC : GlobalNPC
    {
        private static readonly Color EmberColor = new(255, 120, 30);
        private static readonly Color HeatColor = new(255, 70, 20);

        public int cumulativeDamage = 0;
        private int _markOwner = -1;
        private bool _wasMarked;

        public override bool InstancePerEntity => true;

        private static bool HasMark(NPC npc) => npc.HasBuff(ModContent.BuffType<LavaExplosionDebuff>());
        private float Intensity => MathHelper.Clamp(cumulativeDamage / LavaClawBurst.FullIntensityDamage, 0f, 1f);

        // Зовётся на машине атакующего (OnHitNPC снаряда удара)
        public void AddMark(int owner, int damage)
        {
            if (_markOwner != owner)
                cumulativeDamage = 0; // чужая метка не наследуется
            _markOwner = owner;
            cumulativeDamage += damage;
        }

        public override void PostAI(NPC npc)
        {
            bool marked = HasMark(npc);
            if (_wasMarked && !marked)
                Detonate(npc);
            _wasMarked = marked;

            if (marked && !Main.dedServ)
                SpawnMarkEmbers(npc);
        }

        // Добитый помеченный враг взрывается сразу, не дожидаясь конца метки
        public override void HitEffect(NPC npc, NPC.HitInfo hit)
        {
            if (npc.life <= 0 && cumulativeDamage > 0)
                Detonate(npc);
        }

        private void Detonate(NPC npc)
        {
            int damage = cumulativeDamage;
            int owner = _markOwner;
            cumulativeDamage = 0;
            _markOwner = -1;
            if (damage <= 0 || owner != Main.myPlayer)
                return;

            Projectile.NewProjectile(npc.GetSource_FromThis(), npc.Center, Vector2.Zero,
                ModContent.ProjectileType<LavaClawBurst>(), damage, 4f, owner, LavaClawBurst.RadiusFor(damage));
        }

        // Раскалённый враг «дымится»: угольки поднимаются тем гуще, чем больше накоплено
        private void SpawnMarkEmbers(NPC npc)
        {
            if (Main.rand.NextFloat() > 0.25f + 0.6f * Intensity)
                return;
            Vector2 at = new Vector2(Main.rand.NextFloat(npc.position.X, npc.position.X + npc.width),
                Main.rand.NextFloat(npc.position.Y, npc.position.Y + npc.height));
            SoAParticles.SpawnStreak(at, new Vector2(Main.rand.NextFloatDirection() * 0.6f, -Main.rand.NextFloat(1.2f, 2.8f)),
                EmberColor, Main.rand.NextFloat(1.6f, 2.6f), gravity: -0.02f, life: Main.rand.Next(18, 32), lengthPerSpeed: 2f);
        }

        // Чем больше накоплено, тем краснее враг
        public override void DrawEffects(NPC npc, ref Color drawColor)
        {
            if (cumulativeDamage <= 0 || !npc.HasBuff(ModContent.BuffType<LavaExplosionDebuff>()))
                return;

            float intensity = MathHelper.Clamp(cumulativeDamage / 350f, 0f, 1f);
            drawColor = Color.Lerp(drawColor, new Color(255, 30, 30), intensity * 0.72f);
        }

        // Накопленный урон над головой врага
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (cumulativeDamage <= 0)
                return;

            int buffIndex = npc.FindBuffIndex(ModContent.BuffType<LavaExplosionDebuff>());
            if (buffIndex < 0)
                return;

            bool aboutToExplode = npc.buffTime[buffIndex] <= 15;

            // Жар вокруг раскалённого врага; перед взрывом — частый яркий пульс
            float heatPulse = aboutToExplode
                ? 0.6f + 0.4f * (float)Math.Sin(Main.GameUpdateCount * 0.6f)
                : 0.85f + 0.15f * (float)Math.Sin(Main.GameUpdateCount * 0.15f);
            float heatSize = Math.Max(npc.width, npc.height) * (1.6f + 0.4f * Intensity);
            SoAVfx.BeginAdditive(spriteBatch);
            SoAVfx.DrawTintedGlow(spriteBatch, npc.Center, new Vector2(heatSize),
                HeatColor * ((0.25f + 0.4f * Intensity) * heatPulse));
            SoAVfx.EndAdditive(spriteBatch);
            string text = cumulativeDamage.ToString();
            var font = FontAssets.MouseText.Value;
            Vector2 textSize = font.MeasureString(text);
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
