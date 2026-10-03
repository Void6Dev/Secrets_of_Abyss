using System.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace SoA.Common.Systems.JungleLake
{
    // Обмеры озера в джунглях, снятые при генерации. Границы у каждого мира свои,
    // а нужны они в рантайме: по ним спавнится живность и по ним же деревня
    // на сваях находит своё место
    public class JungleLakeWorldData : ModSystem
    {
        public static int LeftX;
        public static int RightX;
        public static int WaterTopY;
        public static int BedY;      // самое глубокое место чаши
        // Дно жерла родника. По лору через него озеро «дышит»: сюда привяжутся
        // тонущие пузыри и Ночь Вдоха. В мирах до родника тут 0
        public static int SpringX;
        public static int SpringY;

        public static bool Exists => RightX > LeftX && WaterTopY > 0;

        public static int CenterX => (LeftX + RightX) / 2;

        public static bool HasSpring => Exists && SpringY > WaterTopY;

        // Рядом с озером — это и берег тоже: жабы и стрекозы сидят не в воде,
        // а вокруг, поэтому запас по умолчанию щедрый
        public static bool IsNear(int tileX, int tileY, int margin = 40)
        {
            if (!Exists)
                return false;

            return tileX >= LeftX - margin && tileX <= RightX + margin
                && tileY >= WaterTopY - margin && tileY <= BedY + margin;
        }

        public override void ClearWorld()
        {
            LeftX = 0;
            RightX = 0;
            WaterTopY = 0;
            BedY = 0;
            SpringX = 0;
            SpringY = 0;
        }

        public override void SaveWorldData(TagCompound tag)
        {
            if (!Exists)
                return;

            tag["jungleLakeLeftX"] = LeftX;
            tag["jungleLakeRightX"] = RightX;
            tag["jungleLakeWaterTopY"] = WaterTopY;
            tag["jungleLakeBedY"] = BedY;
            tag["jungleLakeSpringX"] = SpringX;
            tag["jungleLakeSpringY"] = SpringY;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            LeftX = tag.GetInt("jungleLakeLeftX");
            RightX = tag.GetInt("jungleLakeRightX");
            WaterTopY = tag.GetInt("jungleLakeWaterTopY");
            BedY = tag.GetInt("jungleLakeBedY");
            SpringX = tag.GetInt("jungleLakeSpringX");
            SpringY = tag.GetInt("jungleLakeSpringY");
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(LeftX);
            writer.Write(RightX);
            writer.Write(WaterTopY);
            writer.Write(BedY);
            writer.Write(SpringX);
            writer.Write(SpringY);
        }

        public override void NetReceive(BinaryReader reader)
        {
            LeftX = reader.ReadInt32();
            RightX = reader.ReadInt32();
            WaterTopY = reader.ReadInt32();
            BedY = reader.ReadInt32();
            SpringX = reader.ReadInt32();
            SpringY = reader.ReadInt32();
        }
    }
}
