using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;


namespace SoA.Content.Items.Materials
{
    public class LavaShard : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 64;
            Item.scale = 1;
            Item.value = Item.buyPrice(gold: 3);
            Item.maxStack = 9999;
            Item.rare = ItemRarityID.Orange;
        }
        public override void SetStaticDefaults()    
        {
            ItemID.Sets.ItemIconPulse[Item.type] = true;
        }    
    }

}
        