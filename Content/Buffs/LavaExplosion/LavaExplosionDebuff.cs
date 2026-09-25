using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;
namespace SoA.Content.Buffs
{
    public class LavaExplosionDebuff : ModBuff
    {
        // Спрайт лежит рядом с кодом в папке контента, а не по пути пространства имён
        public override string Texture => "SoA/Content/Buffs/LavaExplosion/LavaExplosionDebuff";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
        }

        public override void Update(NPC npc, ref int buffIndex)
        {
            if (npc.buffTime[buffIndex] == 0)
                Explode(npc);
        }

        private void Explode(NPC npc)
        {
            LavaExplosionGlobalNPC modNPC = npc.GetGlobalNPC<LavaExplosionGlobalNPC>();

            // Dramatic fire burst
            for (int i = 0; i < 55; i++)
            {
                Dust.NewDust(npc.position, npc.width, npc.height, DustID.InfernoFork,
                    Main.rand.NextFloat(-7f, 7f), Main.rand.NextFloat(-7f, 7f),
                    0, default, Main.rand.NextFloat(1f, 2.3f));
            }
            // Smoke plumes rising from center
            for (int i = 0; i < 22; i++)
            {
                Dust.NewDust(npc.position, npc.width, npc.height, DustID.Smoke,
                    Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-5f, -0.5f),
                    100, default, Main.rand.NextFloat(1.2f, 1.8f));
            }

            // Flash of light at the explosion point
            Lighting.AddLight(npc.Center, 2.2f, 0.7f, 0f);

            if (!npc.friendly && !npc.townNPC)
            {
                npc.StrikeNPC(new NPC.HitInfo
                {
                    Damage = modNPC.cumulativeDamage,
                    HitDirection = npc.Center.X < Main.LocalPlayer.Center.X ? -1 : 1,
                });
            }

            int explosionRadius = 300;
            foreach (NPC nearby in Main.npc)
            {
                if (!nearby.active || nearby.friendly || nearby.townNPC || nearby == npc)
                    continue;
                if (nearby.Distance(npc.Center) >= explosionRadius)
                    continue;

                nearby.StrikeNPC(new NPC.HitInfo
                {
                    Damage = modNPC.cumulativeDamage,
                    Knockback = 0f,
                    HitDirection = nearby.Center.X < Main.LocalPlayer.Center.X ? -1 : 1,
                });
            }

            modNPC.cumulativeDamage = 0;
            SoundEngine.PlaySound(SoundID.Item14, npc.position);
        }
    }
}
