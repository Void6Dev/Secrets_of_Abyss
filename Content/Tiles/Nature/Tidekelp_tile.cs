using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using SoA.Common.Systems;

namespace SoA.Content.Tiles.Nature
{
    // Приливная флора: стебель из 1x1 сегментов, растущий со дна вверх сквозь толщу
    // воды. Один тайл держит все три вида — ель, папоротник и рыжий куст: логика
    // роста, обрыва и качания у них общая, различаются только рисунок, свечение,
    // высота и жёсткость.
    //
    // TileObjectData намеренно нет: сегменты стоят друг на друге, и проверка якоря
    // «под собой твёрдый тайл» убила бы всё выше первого. Рамки ставит генератор
    // (PlantKelpForests / PlantBlackForest), обрыв стебля разбирается цепочкой
    // в KillTile — так же устроены ванильные лианы.
    //
    // Раскладка листа: клетка 48x16 при шаге 50x18. Клетка шире тайла втрое —
    // лапы свисают на соседние тайлы, и заросли читаются зарослями, а не палками
    // по колонкам. На проходимость это не влияет: тайл не твёрдый, свисает только
    // рисунок. Столбец (TileFrameX) — форма сегмента, ряд (TileFrameY) — вид.
    public class Tidekelp_tile : ModTile
    {
        public const int CellWidth = 48;
        public const int CellHeight = 16;
        public const int FrameStepX = CellWidth + 2;
        public const int FrameStepY = CellHeight + 2;

        // Столбцы листа — форма сегмента
        public const int VariantBase = 0;         // комель: утолщение на грунте
        public const int VariantStem = 1;         // голый стебель, просвет в кроне
        public const int VariantBranchLeftA = 2;
        public const int VariantBranchLeftB = 3;
        public const int VariantBranchRightA = 4;
        public const int VariantBranchRightB = 5;
        public const int VariantBranchBoth = 6;
        public const int VariantCrown = 7;        // верхушка, только верхний сегмент
        public const int VariantCount = 8;

        // Ряды листа — виды
        public const int SpeciesSpruce = 0;   // тёмная «ель» с красными вкраплениями
        public const int SpeciesFern = 1;     // светлый сине-зелёный папоротник
        public const int SpeciesBush = 2;     // рыжий низкий куст у дна
        public const int SpeciesCount = 3;

        private const float GlowPulseSpeed = 0.02f;
        private const float GlowPulseDepth = 0.18f;

        // Биолюминесценция: стебель тлеет ровно, верхушка светит заметно ярче.
        // Свет держится на верхушках намеренно — в Чёрных лесах стебли уходят
        // за экран вверх, и цепочка огоньков по кронам показывает высоту зала,
        // которую иначе не видно
        public readonly struct SpeciesTraits
        {
            public readonly int MinHeight;
            public readonly int MaxHeight;
            // Поведение в воде (TideKelpPhysics)
            public readonly float Buoyancy;        // тяга вверх: выше — быстрее и твёрже встаёт
            public readonly float BendStiffness;   // 0..1, как сильно сегмент выравнивается по соседям: выше — плавнее дуга
            public readonly float Pliancy;         // 0..1, насколько стебель увлекает проплывающий
            public readonly float CurrentResponse; // насколько его колышет течение
            public readonly Vector3 StemGlow;
            public readonly Vector3 CrownGlow;
            public readonly Color MapColor;

            public SpeciesTraits(int minHeight, int maxHeight, float buoyancy, float bendStiffness,
                float pliancy, float currentResponse, Vector3 stemGlow, Vector3 crownGlow, Color mapColor)
            {
                MinHeight = minHeight;
                MaxHeight = maxHeight;
                Buoyancy = buoyancy;
                BendStiffness = bendStiffness;
                Pliancy = pliancy;
                CurrentResponse = currentResponse;
                StemGlow = stemGlow;
                CrownGlow = crownGlow;
                MapColor = mapColor;
            }
        }

        public static readonly SpeciesTraits[] Traits =
        {
            // Ель: несущий вид Чёрных лесов, тянется от пола до свода. Ствол толстый:
            // пловец отводит его сдержанно, и он неторопливо встаёт обратно
            new(12, 110, 0.004f, 0.50f, 0.45f, 1.0f, new Vector3(0.030f, 0.070f, 0.095f),
                new Vector3(0.150f, 0.360f, 0.430f), new Color(30, 62, 70)),
            // Папоротник: подлесок по пояс, гибкий — легко увлекается следом и сильнее колышется
            new(6, 26, 0.006f, 0.30f, 0.85f, 1.5f, new Vector3(0.045f, 0.105f, 0.100f),
                new Vector3(0.120f, 0.330f, 0.300f), new Color(46, 104, 92)),
            // Рыжий куст: акцент у самого дна, жёсткий, почти не шевелится
            new(3, 7, 0.020f, 0.60f, 0.40f, 0.3f, new Vector3(0.090f, 0.045f, 0.015f),
                new Vector3(0.280f, 0.150f, 0.045f), new Color(122, 74, 30)),
        };

        public static int MinHeightOf(int species) => Traits[species].MinHeight;

        public static int MaxHeightOf(int species) => Traits[species].MaxHeight;

        public static int SpeciesAt(Tile tile) =>
            Math.Clamp(tile.TileFrameY / FrameStepY, 0, SpeciesCount - 1);

        // Форма сегмента по его месту в стебле. Живёт в тайле, а не в генераторе:
        // раскладку листа знает тайл, и оба места посадки обязаны выбирать лапы
        // одинаково. branchedLeft переносит сторону предыдущей лапы — лапы идут
        // вразбежку, иначе куст заваливается на один бок
        public static int VariantAt(int indexFromBottom, int height, int species,
            UnifiedRandom rand, ref bool branchedLeft)
        {
            if (indexFromBottom <= 0)
                return VariantBase;
            if (indexFromBottom >= height - 1)
                return VariantCrown;

            // Просветы: сквозь заросли должно быть видно, сплошная стена лап
            // читается как текстура, а не как лес
            if (rand.NextBool(species == SpeciesBush ? 6 : 5))
                return VariantStem;

            // Низ гуще верха: лапы в обе стороны только в нижней половине стебля
            float reach = indexFromBottom / (float)(height - 1);
            if (reach < 0.45f && rand.NextBool(3))
                return VariantBranchBoth;

            branchedLeft = !branchedLeft;
            int variant = branchedLeft ? VariantBranchLeftA : VariantBranchRightA;
            return rand.NextBool(2) ? variant : variant + 1;
        }

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileCut[Type] = true;
            Main.tileNoFail[Type] = true;
            Main.tileLighted[Type] = true;

            TileID.Sets.IgnoredByGrowingSaplings[Type] = true;
            TileID.Sets.ReplaceTileBreakDown[Type] = true;

            DustType = DustID.JungleGrass;
            HitSound = SoundID.Grass;

            // Один ключ названия на все три вида: на карте они различаются цветом,
            // а не подписью
            foreach (SpeciesTraits traits in Traits)
                AddMapEntry(traits.MapColor, CreateMapEntryName());
        }

        public override ushort GetMapOption(int i, int j) => (ushort)SpeciesAt(Main.tile[i, j]);

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Tile tile = Main.tile[i, j];
            SpeciesTraits traits = Traits[SpeciesAt(tile)];
            bool crown = tile.TileFrameX == VariantCrown * FrameStepX;
            Vector3 glow = crown ? traits.CrownGlow : traits.StemGlow;

            // Пульс идёт волной по стеблю: фаза сдвинута высотой, соседние
            // верхушки не мигают в такт
            float pulse = 1f + GlowPulseDepth *
                MathF.Sin(Main.GameUpdateCount * GlowPulseSpeed + i * 0.7f + j * 0.23f);

            r += glow.X * pulse;
            g += glow.Y * pulse;
            b += glow.Z * pulse;
        }

        // Верхние сегменты держатся на нижнем: срезал стебель — уплыла вся макушка
        public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
        {
            noItem = true;
            if (fail || effectOnly || j <= 1)
                return;

            Tile above = Main.tile[i, j - 1];
            if (above.HasTile && above.TileType == Type)
                WorldGen.KillTile(i, j - 1);
        }

        // Флора живёт только в воде и только на грунте либо на своём же стебле.
        // Осушил бухту или выбил опору — стебель распадается
        public override void RandomUpdate(int i, int j)
        {
            Tile tile = Main.tile[i, j];
            if (tile.LiquidAmount < 100 || !HasSupport(i, j))
                WorldGen.KillTile(i, j);
        }

        private bool HasSupport(int i, int j)
        {
            if (j + 1 >= Main.maxTilesY)
                return false;

            Tile below = Main.tile[i, j + 1];
            if (!below.HasTile)
                return false;
            return below.TileType == Type || Main.tileSolid[below.TileType];
        }

        // Тайлы движок рисует в кэш-текстуру раз в несколько кадров — нарисованное здесь
        // двигалось бы рывками. Поэтому здесь сегмент только регистрируется особой точкой
        // (как ванильные лианы и трава), а рисуется в SpecialDraw — каждый кадр
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Main.instance.TilesRenderer.AddSpecialPoint(i, j, TileDrawing.TileCounterType.CustomNonSolid);
            return false;
        }

        // Клетка шире тайла (рисунок центрируется по стеблю и свисает на соседей),
        // поза — из физики стебля: сегмент стоит на верхушке нижнего и повёрнут вдоль
        // своего отрезка цепи
        public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (!tile.HasTile || tile.TileType != Type)
                return;

            // Стебель, который физика ещё не видела (посадили в этот кадр), стоит прямо
            if (!TideKelpPhysics.TryGetPose(i, j, out Vector2 anchor, out float rotation))
            {
                anchor = new Vector2(i * 16 + 8, j * 16 + 16);
                rotation = 0f;
            }

            Texture2D texture = TextureAssets.Tile[Type].Value;
            Vector2 origin = new(CellWidth / 2f, CellHeight);
            var frame = new Rectangle(tile.TileFrameX, tile.TileFrameY, CellWidth, CellHeight);
            Color light = Lighting.GetColor(anchor.ToTileCoordinates());

            spriteBatch.Draw(texture, anchor - Main.screenPosition, frame, light,
                rotation, origin, 1f, SpriteEffects.None, 0f);
        }
    }
}
