using System;
using Terraria;
using Terraria.Utilities;
using SoA.Content.Tiles.Nature;

namespace SoA.Common.Graphics.Kelp
{
    // Лист на стебле: где растёт (S — px вдоль стебля от комля), в какую сторону, какой
    // картинкой и как держится в воде. Back — за стеблем
    internal struct KelpLeaf
    {
        public float S;
        public int Side;
        public int Variant;
        public float Angle;     // отклонение основания от стебля, рад
        public float Curl;      // доворот к кончику: + наружу, - к стеблю
        public float Wave;      // амплитуда волны, бегущей по листу
        public float Phase;
        public bool Back;
    }

    internal struct KelpFruit
    {
        public float S;
        public int Side;
        public int Size;        // 0..2 — область KelpAtlas.Fruits
        public float Stalk;     // длина хвостика, px мира
        public float Phase;
    }

    // Облик одного стебля: узлы листьев, гроздья плодов, план глубины. Строится один раз
    // из координат стебля — у каждого растения свой рисунок, и он не меняется между кадрами.
    // Числа держать в согласии с Docs/art_refs/tidekelp/preview_forest.py: по нему подбирались
    internal sealed class KelpLayout
    {
        // План глубины: стебли леса стоят в каждой колонке, и одинаковые слились бы в стену.
        // Дальние — тонкая полоса, темнее и тонут в цвете воды; ближние — в полном цвете
        public const int PlanBack = 0;
        public const int PlanMiddle = 1;
        public const int PlanFront = 2;

        private readonly struct SpeciesStyle
        {
            public readonly float NodeSpacing;     // px между узлами листьев
            public readonly int[] NodeLeaves;      // сколько листьев в узле (выбор из списка)
            public readonly float AngleMin, AngleMax;
            public readonly float FruitSpacing;    // px между гроздьями
            public readonly int[] FruitSizes;

            public SpeciesStyle(float nodeSpacing, int[] nodeLeaves, float angleMin, float angleMax,
                float fruitSpacing, int[] fruitSizes)
            {
                NodeSpacing = nodeSpacing;
                NodeLeaves = nodeLeaves;
                AngleMin = angleMin;
                AngleMax = angleMax;
                FruitSpacing = fruitSpacing;
                FruitSizes = fruitSizes;
            }
        }

        // Плоды — акцент, а не россыпь: на экране их должно быть мало, иначе лес — гирлянда
        private static readonly SpeciesStyle[] Styles =
        {
            new(52f, new[] { 2, 2, 3 }, 0.10f, 0.95f, 260f, new[] { 1, 1, 2 }),  // лоза
            new(34f, new[] { 1, 2 }, 0.15f, 0.85f, 400f, new[] { 0 }),           // папоротник
            new(14f, new[] { 2, 3 }, 0.30f, 1.10f, 90f, new[] { 2, 1 }),         // куст
        };

        // Герой: крупное растение переднего плана — точка фокуса рощи, как главное растение
        // концепта. Гуще листья, длиннее листья, большие гроздья, чуть ярче
        private const float HeroChance = 0.2f;

        // Редкость листьев по плану: у дальних их меньше — силуэт, а не масса
        private static readonly float[] PlanSparsity = { 2.4f, 1.4f, 1f };

        private static readonly int[] LeafVariants = { 0, 1, 2, 3, 0, 1, 3 };
        private const int BaseLeafVariant = 4;

        public int Height;
        public int Species;
        public int Plan;
        public bool Hero;
        public Microsoft.Xna.Framework.Vector3 Tint;   // свой оттенок стебля: роща не клон
        public float BraidOffset;   // сдвиг косички, арт-пиксели: соседние стебли не в фазе
        public bool Flip;
        public int Rock;
        public float PulseOffset;   // сдвиг волны света по стеблю
        public KelpLeaf[] Leaves;
        public KelpFruit[] Fruits;
        public uint LastUsed;

        public static int PlanOf(int x)
        {
            float r = Hash01(x, 7);
            return r < 0.4f ? PlanBack : r < 0.75f ? PlanMiddle : PlanFront;
        }

        // plan < 0 — план по колонке (живой лес); дальний лес на параллаксе задаёт свой
        public static KelpLayout Build(int x, int baseY, int height, int species, int plan = -1)
        {
            species = Math.Clamp(species, 0, Tidekelp_tile.SpeciesCount - 1);
            var rand = new UnifiedRandom(unchecked(x * 73856093 ^ baseY * 19349663));
            SpeciesStyle style = Styles[species];
            if (plan < 0)
                plan = PlanOf(x);
            bool hero = plan == PlanFront && species == Tidekelp_tile.SpeciesSpruce && Hash01(x, 29) < HeroChance;
            float length = height * 16f;

            var leaves = new System.Collections.Generic.List<KelpLeaf>(height * 2);
            var fruits = new System.Collections.Generic.List<KelpFruit>(height / 2 + 2);

            // Узлы: 1-3 листа веером в разные стороны, между узлами видна голая косичка
            float spacing = style.NodeSpacing * PlanSparsity[plan] * (hero ? 0.8f : 1f);
            float s = 22f + rand.NextFloat(12f);
            while (s < length - 30f)
            {
                int count = style.NodeLeaves[rand.Next(style.NodeLeaves.Length)] + (hero ? 1 : 0);
                int side = rand.NextBool() ? -1 : 1;
                for (int n = 0; n < count; n++)
                {
                    leaves.Add(new KelpLeaf
                    {
                        S = s + rand.NextFloat(-3f, 3f),
                        Side = side,
                        Variant = hero ? rand.Next(2) : LeafVariants[rand.Next(LeafVariants.Length)],
                        Angle = rand.NextFloat(style.AngleMin, style.AngleMax) * (n == 0 ? 1f : 0.7f),
                        Back = n % 2 == 1 || rand.NextFloat() < 0.3f,
                        Phase = rand.NextFloat(MathF.Tau),
                        Curl = rand.NextFloat(-0.5f, 0.6f),
                        Wave = rand.NextFloat(0.12f, 0.3f),
                    });
                    side = -side;
                }
                s += spacing * rand.NextFloat(0.45f, 1.7f);
            }

            // Верхушка — веер из трёх длинных листьев, комель — два коротких
            float top = length - 6f;
            AddFixed(leaves, rand, top, -1, 1, 0.30f, true, 0.3f, 0.2f);
            AddFixed(leaves, rand, top, 1, 0, 0.38f, false, 0.4f, 0.2f);
            AddFixed(leaves, rand, top, -1, 0, 0.06f, false, -0.2f, 0.2f);
            AddFixed(leaves, rand, 6f, -1, BaseLeafVariant, 0.9f, false, 0.3f, 0.1f);
            AddFixed(leaves, rand, 6f, 1, BaseLeafVariant, 0.9f, false, 0.3f, 0.1f);

            // Плоды гроздьями: на узле 1-3 плода в разные стороны
            float fruitSpacing = style.FruitSpacing * (plan == PlanBack ? 2.5f : 1f) * (hero ? 0.45f : 1f);
            s = 40f + rand.NextFloat(fruitSpacing);
            while (s < length - 20f)
            {
                int count = hero ? 2 + rand.Next(2) : rand.Next(4) switch { 0 => 1, 3 => 3, _ => 2 };
                for (int n = 0; n < count; n++)
                {
                    fruits.Add(new KelpFruit
                    {
                        S = s + rand.NextFloat(-5f, 5f),
                        Side = rand.NextBool() ? -1 : 1,
                        Size = hero ? 2 : style.FruitSizes[rand.Next(style.FruitSizes.Length)],
                        Stalk = rand.NextFloat(4f, 14f),
                        Phase = rand.NextFloat(MathF.Tau),
                    });
                }
                s += fruitSpacing * rand.NextFloat(0.6f, 1.4f);
            }

            return new KelpLayout
            {
                Height = height,
                Species = species,
                Plan = plan,
                Hero = hero,
                Tint = new Microsoft.Xna.Framework.Vector3(1f + (Hash01(x, 31) - 0.5f) * 0.16f,
                    1f + (Hash01(x, 37) - 0.5f) * 0.10f, 1f + (Hash01(x, 41) - 0.5f) * 0.22f)
                    * (hero ? 1.08f : 1f),
                BraidOffset = rand.NextFloat(64f),
                Flip = rand.NextBool(),
                Rock = rand.Next(KelpAtlas.Rocks.Length),
                PulseOffset = Hash01(x, 13) * 900f,
                Leaves = leaves.ToArray(),
                Fruits = fruits.ToArray(),
            };
        }

        private static void AddFixed(System.Collections.Generic.List<KelpLeaf> leaves, UnifiedRandom rand,
            float s, int side, int variant, float angle, bool back, float curl, float wave)
        {
            leaves.Add(new KelpLeaf
            {
                S = s,
                Side = side,
                Variant = variant,
                Angle = angle,
                Back = back,
                Phase = rand.NextFloat(MathF.Tau),
                Curl = curl,
                Wave = wave,
            });
        }

        // Хеш колонки 0..1: план глубины и сдвиги, одинаковые при каждом построении
        public static float Hash01(int x, int salt)
        {
            uint h = unchecked((uint)x * 2654435761u ^ (uint)salt * 2246822519u);
            h ^= h >> 15;
            h = unchecked(h * 2246822519u);
            h ^= h >> 13;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
