using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;

public class SoARecipeGroups : ModSystem
{
    public static RecipeGroup SilverTungstenBar;

    public override void AddRecipeGroups()
    {
        // Название группы в тултипе рецепта: «Any Silver Bar»
        SilverTungstenBar = new RecipeGroup(
            () => $"{Language.GetTextValue("LegacyMisc.37")} {Lang.GetItemNameValue(ItemID.SilverBar)}",
            ItemID.SilverBar, ItemID.TungstenBar);
        RecipeGroup.RegisterGroup("SoA:SilverTungstenBar", SilverTungstenBar);
    }
}