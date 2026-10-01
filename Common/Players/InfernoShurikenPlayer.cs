using System;
using Terraria.ModLoader;

namespace SoA.Common.Players
{
    // То, что переживает отдельные броски Инферно-сюрикена: стаки Жара (копятся цепочкой
    // пойманных идеальных бросков) и откат. Решает всё владелец; другим игрокам Жар нужен
    // только для угольков на орбите, и его привозит сам сюрикен в руке (InfernoShurikenHeld)
    public class InfernoShurikenPlayer : ModPlayer
    {
        public const int MaxHeat = 3;

        public int heat;
        public int cooldown;   // только у владельца: пока идёт, новый замах не начинается

        public override void PostUpdate()
        {
            if (cooldown > 0)
                cooldown--;
        }

        public void OnShurikenCaught()
        {
            heat = Math.Min(heat + 1, MaxHeat);
            cooldown = 0;
        }

        public void ResetHeat() => heat = 0;
    }
}
