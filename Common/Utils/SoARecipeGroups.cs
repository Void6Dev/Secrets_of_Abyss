using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;

public class SoARecipeGroups : ModSystem
{
    public static RecipeGroup SilverBar;
    public static RecipeGroup GoldBar;

    public override void AddRecipeGroups()
    {
        // Название группы в тултипе рецепта: «Any Silver Bar»
        SilverBar = new RecipeGroup(
            () => $"{Language.GetTextValue("LegacyMisc.37")} {Lang.GetItemNameValue(ItemID.SilverBar)}",
            ItemID.SilverBar, ItemID.TungstenBar);
        RecipeGroup.RegisterGroup("SoA:SilverBar", SilverBar);

        GoldBar = new RecipeGroup(
            () => $"{Language.GetTextValue("LegacyMisc.37")} {Lang.GetItemNameValue(ItemID.GoldBar)}",
            ItemID.GoldBar, ItemID.PlatinumBar);
        RecipeGroup.RegisterGroup("SoA:GoldBar", GoldBar);
    }
}