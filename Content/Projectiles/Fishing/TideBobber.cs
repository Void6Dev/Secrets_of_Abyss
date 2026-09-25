using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Projectiles.Fishing
{
    // Поплавок удочки прилива: сам светит под водой, иначе в тёмной воде биома его не видно.
    // Спрайт — ванильный деревянный поплавок (placeholder до собственного арта)
    public class TideBobber : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.BobberWooden;

        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.BobberWooden);
            AIType = ProjectileID.BobberWooden;
        }

        public override void AI()
        {
            if (!Main.dedServ)
                Lighting.AddLight(Projectile.Center, 0.2f, 0.5f, 0.6f);
        }
    }
}
