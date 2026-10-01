using Terraria;

namespace SoA.Common.Weapons
{
    // Общее для оружия, которое держит в руке снаряд (дубины, косы, сюрикен в замахе)
    public static class HeldProjectiles
    {
        // Повороты рук задаются для взгляда вправо; при взгляде влево — зеркально
        public static float Mirror(float rightFacing, int direction) => direction == -1 ? -rightFacing : rightFacing;

        // Владелец держит снаряд: смотрит в его сторону, рисует его в руке (heldProj)
        // и не может сменить предмет, пока снаряд жив
        public static void Hold(Projectile projectile, Player owner, int direction)
        {
            owner.ChangeDir(direction);
            owner.heldProj = projectile.whoAmI;
            owner.itemTime = owner.itemAnimation = 2;
        }

        // Обе руки по поворотам для взгляда вправо, с зеркалом под направление
        public static void SetArms(Player owner, int direction, float frontRightFacing, float backRightFacing)
        {
            owner.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, Mirror(frontRightFacing, direction));
            owner.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Full, Mirror(backRightFacing, direction));
        }
    }
}
