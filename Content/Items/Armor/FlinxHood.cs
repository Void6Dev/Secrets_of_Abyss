using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using SoA.Common.Systems;

namespace SoA.Content.Items.Armor
{
	[AutoloadEquip(EquipType.Head)]
	public class FlinxHood : ModItem
	{
		public static readonly int ManaCostReductionPercent = 10;

		public static LocalizedText SetBonusText { get; private set;  }

		public override void SetStaticDefaults() {
			SetBonusText = this.GetLocalization("SetBonus").WithFormatArgs("{0}", ManaCostReductionPercent);
		}

		public override void SetDefaults() {
			Item.width = 18; 
			Item.height = 18; 
			Item.value = Item.sellPrice(gold: 1); 
			Item.rare = ItemRarityID.Green; 
			Item.defense = 2;
		}

		public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ItemID.FlinxFurCoat;
        }

		public override void UpdateArmorSet(Player player) {
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
				.AddIngredient(ItemID.Amethyst, 2)
				.AddTile(TileID.Anvils)
				.Register();
		}
	}
}