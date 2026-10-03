using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;

namespace SoA.Common.Utils
{
    // Анимация предметов по листу кадров, уложенных сверху вниз.
    //
    // Одна строка в SetStaticDefaults — и ванильная отрисовка листает кадры сама везде:
    // в руке, в инвентаре, на земле, в сундуке и магазине. Состояния у экземпляров нет:
    // анимация общая на тип и обновляется игрой раз в тик, поэтому она не ускоряется
    // от числа копий предмета и не замирает там, где никто не вызвал Update.
    //
    // Своя отрисовка (свечение, другой масштаб) берёт кадр и центр отсюда же —
    // так источник правды о скорости и числе кадров один
    public static class ItemAnimations
    {
        // pingPong — кадры идут 0 → N-1 → 0 вместо прыжка с последнего на первый
        public static void Register(int itemType, int frameCount, int ticksPerFrame, bool pingPong = false)
            => Main.RegisterItemAnimation(itemType, new DrawAnimationVertical(ticksPerFrame, frameCount, pingPong));

        // Кадр, который игра показывает сейчас. Без регистрации — вся текстура
        public static Rectangle Frame(int itemType, Texture2D texture)
            => Main.itemAnimations[itemType]?.GetFrame(texture) ?? texture.Frame();

        // Центр кадра — origin для Draw, чтобы предмет вращался и масштабировался вокруг середины
        public static Vector2 Origin(int itemType, Texture2D texture)
            => Frame(itemType, texture).Size() / 2f;
    }
}
