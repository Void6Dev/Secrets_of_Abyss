using Terraria;
using Terraria.ID;

namespace SoA.Common.Utils
{
    // Категории тайлов для галочек «игнорируемые тайлы» в диалоге сохранения.
    // Категории считаются по ванильным наборам, а не по спискам ID, поэтому
    // модовые тайлы попадают в них автоматически.
    public static class StructureFilters
    {
        public static bool Skips(int tileType, StructureFilter filters)
        {
            if (Has(filters, StructureFilter.SkipCobwebs) && tileType == TileID.Cobweb)
                return true;
            if (Has(filters, StructureFilter.SkipVines) && IsVine(tileType))
                return true;
            if (Has(filters, StructureFilter.SkipGrass) && IsGrassOrPlant(tileType))
                return true;
            if (Has(filters, StructureFilter.SkipLightSources) && Main.tileLighted[tileType])
                return true;
            return false;
        }

        private static bool Has(StructureFilter filters, StructureFilter flag) => (filters & flag) != 0;

        private static bool IsVine(int tileType) => TileID.Sets.IsVine[tileType];

        // Трава — это и сами блоки травы, и всё срезаемое, кроме лиан и паутины:
        // у тех свои галочки
        private static bool IsGrassOrPlant(int tileType)
            => TileID.Sets.Conversion.Grass[tileType]
            || (Main.tileCut[tileType] && !IsVine(tileType) && tileType != TileID.Cobweb);
    }
}
