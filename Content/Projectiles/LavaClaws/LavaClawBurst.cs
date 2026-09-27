using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace SoA.Content.Projectiles
{
    // Взрыв лавовой метки Когтей: снаряд игрока, поставившего метку (см. FireBurstProjectile).
    //   damage — накопленный меткой урон, ai[0] — радиус взрыва
    public class LavaClawBurst : FireBurstProjectile
    {
        private const float MinRadius = 90f;
        private const float MaxRadius = 180f;
        public const float FullIntensityDamage = 350f; // с какого накопленного урона взрыв максимален

        protected override DamageClass BurstDamageClass => DamageClass.Melee;
        protected override float DefaultRadius => MinRadius;
        protected override float Intensity => MathHelper.Clamp((Radius - MinRadius) / (MaxRadius - MinRadius), 0f, 1f);

        public static float RadiusFor(int accumulatedDamage)
            => MathHelper.Lerp(MinRadius, MaxRadius, MathHelper.Clamp(accumulatedDamage / FullIntensityDamage, 0f, 1f));
    }
}
