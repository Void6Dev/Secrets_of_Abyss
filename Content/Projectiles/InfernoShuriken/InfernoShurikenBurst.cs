using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using SoA.Content.Buffs;

namespace SoA.Content.Projectiles
{
    // Взрыв Инферно-сюрикена (см. FireBurstProjectile): от маленького хлопка лёгкого броска
    // до широкого взрыва идеального броска на полном Жаре.
    //   ai[0] — радиус, ai[1] — сила эффектов 0..1
    public class InfernoShurikenBurst : FireBurstProjectile
    {
        private const float PunchIntensity = 0.3f;   // тряска камеры — только у идеального броска
        private const int HellFireTicks = 300;

        protected override DamageClass BurstDamageClass => DamageClass.Ranged;
        protected override float ParticleSpread => MathHelper.Clamp(Radius / DefaultRadius, 0.5f, 1.4f);
        protected override int SparkCount => 14 + (int)(30 * Intensity);
        protected override float CameraPunch => Intensity >= PunchIntensity ? 6f * Intensity : 0f;

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
            => target.AddBuff(ModContent.BuffType<HellFireDebuff>(), HellFireTicks);
    }
}
