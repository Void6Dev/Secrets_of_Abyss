using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Common.Players;

namespace SoA.Content.Items.Armor
{
	[AutoloadEquip(EquipType.Head)]
	public class FlinxHood : ModItem
	{
		// {0} в тексте — клавиша двойного нажатия, подставляется в UpdateArmorSet
		public static LocalizedText SetBonusText { get; private set;  }

		public override void SetStaticDefaults() {
			SetBonusText = this.GetLocalization("SetBonus");
		}

		public override void SetDefaults() {
			Item.width = 18; 
			Item.height = 18; 
			Item.value = Item.sellPrice(gold: 1); 
			Item.rare = ItemRarityID.Green; 
			Item.defense = 2;
		}

		// Бонус самого капюшона — действует и без сета
		public override void UpdateEquip(Player player) {
			player.GetDamage(DamageClass.Summon) += 0.05f;
		}

		public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ItemID.FlinxFurCoat;
        }

		public override void UpdateArmorSet(Player player) {
			player.whipRangeMultiplier += 0.3f;
			player.setBonus = SetBonusText.Format(Language.GetTextValue(Main.ReversedUpDownArmorSetBonuses ? "Key.UP" : "Key.DOWN"));
			// Снежную ауру ведут FlinxArmorSetBonusPlayer (частицы) и FlinxSnowAuraLayer (снежинки на орбите)
			player.GetModPlayer<FlinxArmorSetBonusPlayer>().FlinxSet = true;
		}

		public override void AddRecipes() {
			CreateRecipe()
				.AddIngredient(ItemID.FlinxFur, 6)
				.AddIngredient(ItemID.Silk, 10)
				.AddIngredient(ItemID.Amethyst, 2)
				.AddTile(TileID.Anvils)
				.Register();
		}
	}
}