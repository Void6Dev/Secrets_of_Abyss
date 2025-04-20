using Terraria.ModLoader;


namespace SoA.Content.Buffs
{
    public class LavaExplosionGlobalNPC : GlobalNPC
        {
            public int cumulativeDamage; // Store the cumulative damage for the explosion

            public override bool InstancePerEntity => true; // Ensure each NPC has its own instance of this class

        }
}