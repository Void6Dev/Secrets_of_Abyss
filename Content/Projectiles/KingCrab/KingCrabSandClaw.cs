using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Content.NPCs.Bosses.KingCrab;

namespace SoA.Content.Projectiles
{
    // Клешня из песка: король под землёй бьёт снизу в точку, где стоял игрок.
    //   1. Предупреждение (ai[0] тиков): на грунте разгорается трещина, над ней встаёт
    //      бледный столб — ровно та полоса, куда придёт удар; песок бьёт фонтанчиками.
    //   2. Удар: клешня выстреливает из песка раскрытой, захлопывается, уходит обратно.
    // Ранит только та часть, что над грунтом, и только пока клешня поднята.
    // Рисуется позади тайлов и обрезается по линии грунта: полностью тёмные тайлы игра
    // не рисует, и одни тайлы нижнюю часть не прятали — в темноте она висела на чёрном.
    public class KingCrabSandClaw : ModProjectile
    {
        public override string Texture => "SoA/Content/NPCs/Bosses/KingCrab/KingCrabClawBase";

        public const int StrikeTicks = 40;      // подъём + захлоп + уход после предупреждения
        public const int HitboxHeight = 96;     // на сколько клешня возвышается над грунтом

        private const int RiseTicks = 8;
        private const int SnapTick = RiseTicks + 3;
        private const int HoldTicks = 16;
        private const int SinkTicks = StrikeTicks - RiseTicks - HoldTicks;
        private const float ClawScale = 1.15f;
        private const float BuriedDepth = 150f;  // запястье в начале подъёма — глубоко под грунтом
        private const float RaisedDepth = 20f;   // и на пике: основание пинцера остаётся в песке
        private const float ClawRotation = -2.36f; // пинцером вверх (−135°)

        // Анкеры текстур клешни — те же, что у рига короля (King_crab.Rig.cs)
        private static readonly Vector2 ClawBaseShoulder = new(57f, 15f);
        private static readonly Vector2 ClawBaseHinge = new(66f, 53f);
        private static readonly Vector2 ClawTipHinge = new(10f, 17f);
        private static readonly Vector2 WristFromCenter = new(-40f, 0f); // визуальный центр пинцера правее запястья

        private static readonly Color WarnColor = new(255, 120, 50);

        private static Asset<Texture2D> _tip;

        private int WarnTicks => Math.Max(1, (int)Projectile.ai[0]);
        private int Age => (int)Projectile.localAI[0];
        private int StrikeAge => Age - WarnTicks;  // < 0 — ещё предупреждение
        private bool Flip => Projectile.identity % 2 == 0; // зеркалим через раз — не все удары одинаковые
        private float Ground => Projectile.Bottom.Y;

        public override void SetDefaults()
        {
            Projectile.width = 64;
            Projectile.height = HitboxHeight;
            Projectile.hostile = true;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 600; // реальную жизнь задаёт AI: предупреждение + удар
            Projectile.hide = true;    // рисуется в DrawBehind, позади тайлов
        }

        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;
            Projectile.localAI[0]++;

            if (StrikeAge >= StrikeTicks)
            {
                Projectile.Kill();
                return;
            }

            if (Main.dedServ)
                return;

            Vector2 surface = new(Projectile.Center.X, Ground);
            if (StrikeAge < 0)
                WarnFx(surface, Age / (float)WarnTicks);
            else if (StrikeAge == 0)
                BurstFx(surface);
            else if (StrikeAge == SnapTick)
                SoundEngine.PlaySound(SoundID.Item23 with { Pitch = -0.6f, Volume = 0.9f }, surface);
        }

        private void WarnFx(Vector2 surface, float t)
        {
            Lighting.AddLight(surface - new Vector2(0f, 8f), 0.6f * t, 0.25f * t, 0.08f * t);

            if (Age == 1)
                SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.7f }, surface);
            if (Age == WarnTicks - 12)
                SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.9f, Volume = 0.5f }, surface);

            // Песок фонтанчиками — чем ближе удар, тем чаще и выше
            if (Main.rand.NextFloat() < 0.35f + 0.6f * t)
            {
                Dust sand = Dust.NewDustPerfect(surface + new Vector2(Main.rand.NextFloatDirection() * 36f, -2f),
                    King_crab.GroundDustType(surface), new Vector2(Main.rand.NextFloatDirection() * 0.8f,
                        -Main.rand.NextFloat(1.5f, 3f + 5f * t)));
                sand.scale = Main.rand.NextFloat(0.9f, 1.4f);
            }
        }

        private void BurstFx(Vector2 surface)
        {
            SoundEngine.PlaySound(SoundID.Item14 with { Pitch = -0.4f }, surface);
            int dust = King_crab.GroundDustType(surface);
            for (int i = 0; i < 26; i++)
            {
                Dust d = Dust.NewDustPerfect(surface + new Vector2(Main.rand.NextFloatDirection() * 40f, -4f), dust,
                    new Vector2(Main.rand.NextFloatDirection() * 4f, -Main.rand.NextFloat(3f, 9f)));
                d.scale = Main.rand.NextFloat(1.1f, 1.8f);
            }
        }

        // 0 — в песке, 1 — на пике
        private float Emerge()
        {
            int s = StrikeAge;
            if (s < 0)
                return 0f;
            if (s < RiseTicks)
            {
                float t = s / (float)RiseTicks;
                return 1f - (1f - t) * (1f - t) * (1f - t); // выстреливает и тормозит на пике
            }
            if (s < RiseTicks + HoldTicks)
                return 1f;
            float sink = (s - RiseTicks - HoldTicks) / (float)SinkTicks;
            return 1f - sink * sink;
        }

        // Ранит только поднятая часть и только пока клешня идёт вверх и держится
        public override bool CanHitPlayer(Player target) => StrikeAge >= 0 && StrikeAge < RiseTicks + HoldTicks;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            int raised = (int)(HitboxHeight * Emerge());
            if (raised <= 4)
                return false;
            Rectangle live = new(projHitbox.X, (int)Ground - raised, projHitbox.Width, raised);
            return live.Intersects(targetHitbox);
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info) => King_crab.ReportAttackLanded();

        public override void DrawBehind(int index, List<int> behindNPCsAndTiles, List<int> behindNPCs,
            List<int> behindProjectiles, List<int> overPlayers, List<int> overWiresUI)
            => behindNPCsAndTiles.Add(index);

        public override bool PreDraw(ref Color lightColor)
        {
            if (StrikeAge < 0)
                DrawWarning(Age / (float)WarnTicks);
            else
                DrawClaw(lightColor);
            return false;
        }

        // Зона удара ровно по хитбоксу клешни (заливка-таймер: залилась — клешня вылетает)
        // и светящаяся трещина на грунте — та, из которой она покажется
        private void DrawWarning(float t)
        {
            Vector2 surface = new(Projectile.Center.X, Ground);

            SoAVfx.BeginAlphaImmediate(Main.spriteBatch);
            SoAVfx.DrawDangerZone(Main.spriteBatch, surface, new Vector2(Projectile.width, HitboxHeight), t);
            SoAVfx.EndAdditive(Main.spriteBatch);

            float pulse = 0.6f + 0.4f * (float)Math.Sin(Age * (0.25f + 0.55f * t));
            SoAVfx.BeginAdditive(Main.spriteBatch);
            Color crack = WarnColor * (MathHelper.Lerp(0.35f, 1f, t) * pulse);
            crack.A = 0;
            SoAVfx.DrawTintedGlow(Main.spriteBatch, surface, new Vector2(MathHelper.Lerp(70f, 100f, t), 18f), crack);
            SoAVfx.EndAdditive(Main.spriteBatch);
        }

        private void DrawClaw(Color lightColor)
        {
            Texture2D baseTex = ModContent.Request<Texture2D>(Texture).Value;
            _tip ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabClawTip");
            Texture2D tipTex = _tip.Value;

            bool flip = Flip;
            float side = flip ? 1f : -1f;
            float wristDepth = MathHelper.Lerp(BuriedDepth, RaisedDepth, Emerge());
            Vector2 wrist = new Vector2(Projectile.Center.X + (flip ? -WristFromCenter.X : WristFromCenter.X) * ClawScale,
                Ground + wristDepth);
            float rot = flip ? -ClawRotation : ClawRotation;

            // Пинцер раскрыт на подъёме, на SnapTick — клац
            float open = StrikeAge < SnapTick ? 0.9f : -0.15f;
            Vector2 armTex = ClawBaseHinge - ClawBaseShoulder;
            Vector2 hinge = wrist + (new Vector2(flip ? -armTex.X : armTex.X, armTex.Y) * ClawScale).RotatedBy(rot);

            SpriteEffects fx = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 baseOrigin = flip ? new Vector2(baseTex.Width - ClawBaseShoulder.X, ClawBaseShoulder.Y) : ClawBaseShoulder;
            Vector2 tipOrigin = flip ? new Vector2(tipTex.Width - ClawTipHinge.X, ClawTipHinge.Y) : ClawTipHinge;

            SoAVfx.BeginClippedAbove(Main.spriteBatch, Ground);
            Main.EntitySpriteDraw(tipTex, hinge - Main.screenPosition, null, lightColor, rot + side * open * 0.5f,
                tipOrigin, ClawScale, fx, 0);
            Main.EntitySpriteDraw(baseTex, wrist - Main.screenPosition, null, lightColor, rot, baseOrigin,
                ClawScale, fx, 0);
            SoAVfx.EndClipped(Main.spriteBatch);
        }
    }
}
