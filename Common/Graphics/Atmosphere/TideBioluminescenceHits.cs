using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Graphics.Atmosphere
{
    // Удар по существу в светящейся воде Прилива вспыхивает облаком искр.
    // HitEffect вызывается у каждого клиента, так что вспышку видят все
    public class TideBioluminescenceHits : GlobalNPC
    {
        private const float NormalFlash = 0.75f;
        private const float CritFlash = 1.2f;
        private const float DeathFlash = 1.6f;

        public override void HitEffect(NPC npc, NPC.HitInfo hit)
        {
            if (Main.dedServ || !TideAtmosphere.IsActive)
                return;

            float strength = npc.life <= 0 ? DeathFlash : hit.Crit ? CritFlash : NormalFlash;
            TideAmbience.Flash(npc.Center, strength);
        }
    }
}
