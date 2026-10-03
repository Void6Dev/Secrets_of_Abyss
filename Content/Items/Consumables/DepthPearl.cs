using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics.Particles;
using SoA.Common.Players;

namespace SoA.Content.Items.Consumables
{
    // Жемчужина глубины: расходник как плод жизни. Каждая навсегда ослабляет давление
    // Прилива (TidePressurePlayer), но полностью его не снимает. Лежит в святилищах
    // под снятыми печатями, по одной на ступень
    public class DepthPearl : ModItem
    {
        // Временная подмена: своего спрайта нет
        public override string Texture => "Terraria/Images/Item_" + ItemID.BlackPearl;

        private static readonly Color AbsorbColor = new(140, 220, 255);

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.useTime = 30;
            Item.useAnimation = 30;
            Item.UseSound = SoundID.Item4;
            Item.consumable = true;
            Item.maxStack = Item.CommonMaxStack;
            Item.rare = ItemRarityID.LightRed;
            Item.value = Item.sellPrice(gold: 3);
        }

        public override bool CanUseItem(Player player)
            => player.GetModPlayer<TidePressurePlayer>().Pearls < TidePressurePlayer.MaxPearls;

        public override bool? UseItem(Player player)
        {
            // Счётчик меняет только владелец, остальным он уйдёт через SendClientChanges
            if (player.whoAmI == Main.myPlayer)
                player.GetModPlayer<TidePressurePlayer>().Pearls++;

            if (!Main.dedServ)
            {
                for (int i = 0; i < 20; i++)
                {
                    Vector2 offset = Main.rand.NextVector2CircularEdge(40f, 40f);
                    SoAParticles.SpawnStreak(player.Center + offset, -offset * 0.08f, AbsorbColor, 1.6f, 0f, 22);
                }
                SoAParticles.AddLight(player.Center, AbsorbColor, 1.5f, 30);
            }
            return true;
        }
    }
}
