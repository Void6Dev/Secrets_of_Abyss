using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Content.Items.Weapons
{
    public class StaffOfTornado : ModItem
    {
        public override void SetDefaults() {
            Item.width = 26;
            Item.height = 26;
            Item.DamageType = DamageClass.Ranged;
            Item.noMelee = true;
        }
    }
}