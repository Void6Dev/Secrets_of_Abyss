using System;

namespace SoA.Common.Graphics.Animation
{
    // Клип на листе NPC: непрерывный диапазон кадров одной анимации.
    // Лист у NPC в Terraria всегда одностолбцовый, поэтому анимация — это просто
    // «с какого кадра, сколько штук и как быстро».
    //
    // Счётчиком служит NPC.frameCounter: свой заводить незачем, ваниль его не трогает
    // при aiStyle = -1, а по сети он не нужен — анимация чисто косметическая.
    public readonly struct SheetClip
    {
        public readonly int Start;
        public readonly int Count;
        public readonly int TicksPerFrame;
        public readonly bool Loop;

        public SheetClip(int start, int count, int ticksPerFrame, bool loop = true)
        {
            Start = start;
            Count = Math.Max(1, count);
            TicksPerFrame = Math.Max(1, ticksPerFrame);
            Loop = loop;
        }

        // Последний кадр диапазона — по нему проверяют, влезает ли клип в лист
        public int End => Start + Count - 1;

        // delta — сколько «тиков» прошло. Для ходьбы туда удобно передавать скорость,
        // тогда шаг ускоряется вместе с крабом, а не живёт своей жизнью
        public int Advance(ref double counter, double delta = 1.0)
        {
            counter += delta;
            int step = (int)(counter / TicksPerFrame);

            // Одноразовый клип замирает на последнем кадре: приземление или удар
            // не должны уходить в петлю
            step = Loop ? step % Count : Math.Min(step, Count - 1);
            return Start + step;
        }
    }
}
