using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Content.Items.Materials;

namespace SoA.Common.Systems.Loot
{
    // Звезда преисподней в Теневых сундуках Преисподней (так задумано в Feature items.txt).
    // Кладётся при генерации мира в первый свободный слот; в уже созданных мирах её
    // заменяет дроп с демонов (UnderworldMaterialDrops)
    public class ShadowChestLoot : ModSystem
    {
        private const int ShadowChestStyle = 3;        // Теневой сундук
        private const int LockedShadowChestStyle = 4;  // запертый — в таких и генерируется лут Преисподней
        private const int ChestStyleWidth = 36;        // ширина стиля в листе сундуков, px
        private const int HellStarChance = 3;          // один сундук из трёх

        public override void PostWorldGen()
        {
            int star = ModContent.ItemType<HellStar>();

            foreach (Chest chest in Main.chest)
            {
                if (chest == null)
                    continue;

                Tile tile = Main.tile[chest.x, chest.y];
                if (!tile.HasTile || tile.TileType != TileID.Containers)
                    continue;

                int style = tile.TileFrameX / ChestStyleWidth;
                if (style != ShadowChestStyle && style != LockedShadowChestStyle)
                    continue;
                if (!WorldGen.genRand.NextBool(HellStarChance))
                    continue;

                for (int slot = 0; slot < Chest.maxItems; slot++)
                {
                    if (!chest.item[slot].IsAir)
                        continue;
                    chest.item[slot].SetDefaults(star);
                    break;
                }
            }
        }
    }
}
