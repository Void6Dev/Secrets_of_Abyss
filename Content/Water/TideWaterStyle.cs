using Microsoft.Xna.Framework;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.Water
{
    // Тёмная вода Прилива Теней. Текстуры: TideWaterStyle.png (поверхность),
    // _Block (заливка), _Slope (склоны) — размеры совпадают с ванильным форматом
    public class TideWaterStyle : ModWaterStyle
    {
        public override int ChooseWaterfallStyle()
            => ModContent.GetInstance<TideWaterfallStyle>().Slot;

        public override int GetSplashDust() => DustID.Water;

        public override int GetDropletGore() => GoreID.WaterDrip;


        public override Color BiomeHairColor() => new(88, 58, 156);
    }

    public class TideWaterfallStyle : ModWaterfallStyle
    {
    }
}
