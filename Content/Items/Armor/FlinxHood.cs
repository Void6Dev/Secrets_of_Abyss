using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Common.Systems;

namespace SoA.Content.Items.Armor
{
	// The AutoloadEquip attribute automatically attaches an equip texture to this item.
	// Providing the EquipType.Head value here will result in TML expecting a X_Head.png file to be placed next to the item's main texture.
	[AutoloadEquip(EquipType.Head)]
	public class FlinxHood : ModItem
	{
		public static readonly int ManaCostReductionPercent = 10;

		public static LocalizedText SetBonusText { get; private set;  }

		public override void SetStaticDefaults() {
			SetBonusText = this.GetLocalization("SetBonus").WithFormatArgs("{0}", ManaCostReductionPercent);
		}

		public override void SetDefaults() {
			Item.width = 18; // Width of the item
			Item.height = 18; // Height of the item
			Item.value = Item.sellPrice(gold: 1); // How many coins the item is worth
			Item.rare = ItemRarityID.Green; // The rarity of the item
			Item.defense = 2; // The amount of defense the item will give when equipped
		}

		// IsArmorSet determines what armor pieces are needed for the setbonus to take effect
		public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ItemID.FlinxFurCoat;
        }

		// UpdateArmorSet allows you to give set bonuses to the armor.
		public override void UpdateArmorSet(Player player) {
			// This is the setbonus tooltip:
			//   Double tap or hold DOWN/UP to toggle various armor shadow effects
			//   10% reduced mana cost
			player.whipRangeMultiplier += 0.3f;
			player.GetDamage(DamageClass.Summon) += 0.05f;
			player.setBonus = SetBonusText.Format(Language.GetTextValue(Main.ReversedUpDownArmorSetBonuses ? "Key.UP" : "Key.DOWN"));
			player.GetModPlayer<FlinxArmorSetBonusPlayer>().FlinxSetHood = true;
		}

		public override void ArmorSetShadows(Player player) {
			var exampleArmorSetBonusPlayer = player.GetModPlayer<FlinxArmorSetBonusPlayer>();
			if(exampleArmorSetBonusPlayer.ShadowStyle == 1) {
				player.armorEffectDrawShadow = true;
			}
			}

		public override void AddRecipes() {
			CreateRecipe()
				.AddIngredient(ItemID.FlinxFur, 6)
				.AddIngredient(ItemID.Silk, 10)
				.AddIngredient(ItemID.Sapphire, 2)
				.AddTile(TileID.Anvils)
				.Register();
		}
	}
}