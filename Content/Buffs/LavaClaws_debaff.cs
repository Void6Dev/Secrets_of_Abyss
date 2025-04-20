using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;

namespace SoA.Content.Buffs
{
    public class LavaExplosionDebuff : ModBuff
    {
        public override void SetStaticDefaults() {
            Main.debuff[Type] = true; // This makes the debuff not removable by the nurse
        }

        public override void Update(NPC npc, ref int buffIndex) {
            // Countdown happens automatically; explosion occurs when debuff expires
            if (npc.buffTime[buffIndex] == 0) {
                // Trigger explosion when the debuff ends
                Explode(npc);
            }
        }

        private void Explode(NPC npc) {
            // Get the accumulated damage from the ModNPC instance
            LavaExplosionGlobalNPC modNPC = npc.GetGlobalNPC<LavaExplosionGlobalNPC>();

            // Create an explosion effect
            for (int i = 0; i < 30; i++) {
                Dust.NewDust(npc.position, npc.width, npc.height, DustID.InfernoFork);
            }

            // Deal the accumulated explosion damage to the main NPC (if it's not friendly or a town NPC)
            if (!npc.friendly && !npc.townNPC) {
                NPC.HitInfo hitInfo = new NPC.HitInfo() {
                    Damage = modNPC.cumulativeDamage, // Explosion damage is the accumulated damage
                    HitDirection = npc.Center.X < Main.LocalPlayer.Center.X ? -1 : 1, // Direction based on player position
                };
                npc.StrikeNPC(hitInfo);
            }

            // Optional: You can also damage nearby NPCs
            int explosionRadius = 300; // Set the explosion radius (in pixels)
            foreach (NPC nearbyNPC in Main.npc) {
                // Exclude the main NPC, friendly NPCs, town NPCs, and check the distance
                if (!nearbyNPC.friendly && !nearbyNPC.townNPC && nearbyNPC != npc && nearbyNPC.Distance(npc.Center) < explosionRadius) {
                    // Create hit info for nearby NPCs
                    NPC.HitInfo nearbyHitInfo = new NPC.HitInfo() {
                        Damage = modNPC.cumulativeDamage, // Same explosion damage
                        Knockback = 0f,
                        HitDirection = nearbyNPC.Center.X < Main.LocalPlayer.Center.X ? -1 : 1,
                    };
                    nearbyNPC.StrikeNPC(nearbyHitInfo);
                }
            }

            // Reset cumulative damage after explosion
            modNPC.cumulativeDamage = 0;

            // Play explosion sound
            SoundEngine.PlaySound(SoundID.Item14, npc.position);
        }
    }
}