using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Systems
{
	// This ModPlayer facilitates a set bonus effect. This example shows how either ArmorSetBonusActivated or ArmorSetBonusHeld can be used depending on how you want the player to interact with the set bonus effect. 
	public class FlinxArmorSetBonusPlayer : ModPlayer
	{
		public bool FlinxSetHood; // Indicates if the ExampleSet with ExampleHood is the active armor set.
		public int ShadowStyle = 1; // This is the shadow to use. Note that ExampleHood.ArmorSetShadows will only be called if the full armor set is visible.

		public override void ResetEffects() {
			FlinxSetHood = false;
		}

		public override void ArmorSetBonusActivated() {
			if (!FlinxSetHood) {
				return;
			}

			if (ShadowStyle == 2) {
				ShadowStyle = 0;
			}
			ShadowStyle = (ShadowStyle + 1) % 2;
			ShowMessageForShadowStyle();
		}

		public override void ArmorSetBonusHeld(int holdTime) {
			if (!FlinxSetHood) {
				return;
			}

			if (holdTime == 60) {
				ShadowStyle = ShadowStyle == 2 ? 0 : 2;
				ShowMessageForShadowStyle();
			}
		}

		private void ShowMessageForShadowStyle() {
			string styleName = ShadowStyle switch {
				1 => "armorEffectDrawShadow",
				_ => "None",
			};
			Main.NewText("Current shadow style: " + styleName);
		}
	}
}