using Terraria;

namespace SoA.Common.Utils
{
    // Послойное наполнение сундука. Вынесено отдельно, потому что один и тот же
    // приём нужен и затонувшему галеону, и деревне на сваях: молча пропускать
    // лишнее, когда слоты кончились, и не падать на несуществующем типе предмета
    public struct ChestFill
    {
        private readonly Chest _chest;
        private int _slot;

        public ChestFill(Chest chest)
        {
            _chest = chest;
            _slot = 0;
        }

        public void Add(int type, int stack = 1)
        {
            if (_chest == null || type <= 0 || stack <= 0 || _slot >= Chest.maxItems)
                return;

            _chest.item[_slot].SetDefaults(type);
            _chest.item[_slot].stack = stack;
            _slot++;
        }
    }
}
