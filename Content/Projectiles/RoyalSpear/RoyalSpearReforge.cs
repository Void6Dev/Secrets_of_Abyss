using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;
using SoA.Common.Players;

namespace SoA.Content.Projectiles
{
    // Пересборка Королевского копья: пока зажата ПКМ, воткнутое где-то копьё утекает
    // песком, а в руке хозяина то же копьё собирается обратно по зерну. Дорога длинная —
    // прийти за копьём ногами всё равно быстрее, зато достать его можно откуда угодно.
    //
    // Живёт только пока игрок держит кнопку: отпустил — песок осыпается обратно на
    // древко, и копьё остаётся торчать там же.
    public class RoyalSpearReforge : ModProjectile
    {
        public const int ForgeTicks = 150;

        private const float HoldDistance = 22f;

        // Дрожь почти собранного копья: последняя четверть шкалы
        private const float ShakeFrom = 0.75f;

        public override string Texture => "SoA/Content/Projectiles/RoyalSpear/RoyalSpearProjectile";

        public float Progress => MathHelper.Clamp(Projectile.ai[0] / ForgeTicks, 0f, 1f);

        private Vector2 Aim => Projectile.velocity.SafeNormalize(Vector2.UnitX);

        // Насколько копьё этого игрока уже утекло в руку: воткнутый снаряд читает это
        // сам, чтобы рассыпаться ровно на столько же
        public static float ProgressFor(Player owner)
        {
            int type = ModContent.ProjectileType<RoyalSpearReforge>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile proj = Main.projectile[i];
                if (proj.active && proj.type == type && proj.owner == owner.whoAmI
                    && proj.ModProjectile is RoyalSpearReforge forge)
                    return forge.Progress;
            }
            return 0f;
        }

        public override void SetDefaults()
        {
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.aiStyle = -1;
            Projectile.friendly = false;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.scale = 1.2f;
            Projectile.netImportant = true;
            Projectile.timeLeft = 60;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];

            // Оглушили, разоружили, умер — песок осыпается, копьё остаётся в мире
            if (!owner.active || owner.dead || owner.noItems || owner.CCed)
            {
                Projectile.Kill();
                return;
            }

            Projectile.timeLeft = 60;
            owner.heldProj = Projectile.whoAmI;
            owner.SetDummyItemTime(2);

            if (Projectile.owner == Main.myPlayer && !UpdateOwner(owner))
                return;

            // Счётчик крутят все стороны: иначе на чужих экранах копьё собиралось бы
            // рывками — только в тики, когда владелец прислал пакет
            Projectile.ai[0]++;

            float shake = Progress >= ShakeFrom ? Main.rand.NextFloat(-1.4f, 1.4f) : 0f;
            RoyalSpearPlayer.HoldSpearInHand(owner, Projectile, Aim, HoldDistance + shake);

            Lighting.AddLight(Projectile.Center, 0.26f * Progress, 0.22f * Progress, 0.12f * Progress);
        }

        // Решения владельца: куда целиться, не отпустил ли кнопку, не собралось ли копьё.
        // Возвращает false, если снаряд в этом тике уже мёртв
        private bool UpdateOwner(Player owner)
        {
            RoyalSpearPlayer.AimAtMouse(owner, Projectile);

            // Копьё вытащили ногами или оно сорвалось в полёт — собирать нечего
            RoyalSpearThrown thrown = RoyalSpearThrown.FindOwned(owner, stuckOnly: true);
            if (thrown == null)
            {
                Projectile.Kill();
                return false;
            }

            if (!RoyalSpearPlayer.HoldingAltFire(owner))
            {
                SoundEngine.PlaySound(SoundID.Dig with { Pitch = -0.4f, Volume = 0.6f }, owner.Center);
                Projectile.Kill();
                return false;
            }

            if (Projectile.ai[0] < ForgeTicks)
                return true;

            // Последнее зерно легло — копьё в руке, а от воткнутого остаётся холмик песка
            thrown.CrumbleAway();
            SoundEngine.PlaySound(SoundID.Item37, owner.Center);
            Projectile.Kill();
            return false;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Vector2 origin = new(tex.Width / 2f, tex.Height - RoyalSpearProjectile.GripOffset);
            Vector2 drawPos = Projectile.Center - Main.screenPosition;

            // Весь песок живёт в системе сборки: отдельные зёрна слетаются на свои места,
            // ванильная пыль тут только замусорила бы кадр. identity как seed —
            // на всех клиентах россыпь получается одинаковой
            SoAVfx.DrawSandForged(Main.spriteBatch, tex, drawPos, lightColor,
                Projectile.rotation, origin, Projectile.scale, Progress,
                Projectile.whoAmI, Projectile.identity);
            return false;
        }
    }
}
