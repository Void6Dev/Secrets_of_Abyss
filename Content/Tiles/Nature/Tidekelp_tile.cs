using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Kelp;
using SoA.Common.Systems;

namespace SoA.Content.Tiles.Nature
{
    // Приливная флора: стебель из 1x1 сегментов, растущий со дна вверх сквозь толщу
    // воды. Один тайл держит все три вида — лозу, папоротник и пузырчатый куст: логика
    // роста, обрыва и качания у них общая, различаются только рисунок, свечение,
    // высота и жёсткость.
    //
    // TileObjectData намеренно нет: сегменты стоят друг на друге, и проверка якоря
    // «под собой твёрдый тайл» убила бы всё выше первого. Рамки ставит генератор
    // (PlantKelpForests / PlantBlackForest), обрыв стебля разбирается цепочкой
    // в KillTile — так же устроены ванильные лианы.
    //
    // Рамка тайла (TileFrameX/Y) — логическая: форма * 50 и вид * 18, так её пишет
    // генератор и хранят миры. Лист нарисован крупнее и шагом не совпадает: клетка
    // 64x40 при шаге 66x42 — в четыре тайла шириной (листья расходятся на соседей, и
    // заросли читаются зарослями, а не палками по колонкам) и с запасом сверху и снизу:
    // листья поднимаются над сегментом, плоды свисают на сегменты ниже. На проходимость
    // это не влияет — тайл не твёрдый, свисает только рисунок.
    //
    // Стебель — косичка из лент с периодом в два сегмента, поэтому у каждого вида два
    // ряда листа (две фазы косички): ряд = вид * 2 + (j & 1). У соседних по высоте тайлов
    // чётность j всегда разная — косичка идёт без шва. Лист, маску свечения и таблицу
    // ореолов (Tidekelp_tile.Blooms.cs) собирает Docs/art_refs/tidekelp/make_tidekelp.py,
    // концепт — concept.png рядом с ним.
    public partial class Tidekelp_tile : ModTile
    {
        public const int FrameStepX = 50;
        public const int FrameStepY = 18;

        private const int DrawCellWidth = 64;
        private const int DrawCellHeight = 40;
        private const int DrawStepX = DrawCellWidth + 2;
        private const int DrawStepY = DrawCellHeight + 2;
        private const int DrawAnchorY = 24; // низ сегмента в клетке: 8 px над ним — поднятые листья, 16 под — свисающие

        // Столбцы листа — форма сегмента
        public const int VariantBase = 0;         // комель: камни у грунта
        public const int VariantStem = 1;         // голая косичка, просвет в кроне
        public const int VariantBranchLeftA = 2;
        public const int VariantBranchLeftB = 3;
        public const int VariantBranchRightA = 4;
        public const int VariantBranchRightB = 5;
        public const int VariantBranchBoth = 6;
        public const int VariantCrown = 7;        // верхушка, только верхний сегмент
        public const int VariantCluster = 8;      // два плода по бокам косички (у лозы)
        public const int VariantCount = 9;

        // Ряды листа — виды. Имена исторические: «ель» теперь лоза
        public const int SpeciesSpruce = 0;   // лоза: косичка из двух лент, листья-языки, циановые плоды
        public const int SpeciesFern = 1;     // папоротник: одна волнистая лента, светлее и тоньше
        public const int SpeciesBush = 2;     // куст у дна: тёмный жгут из трёх лент, крупные плоды
        public const int SpeciesCount = 3;

        private const float GlowPulseSpeed = 0.02f;
        private const float GlowPulseDepth = 0.18f;
        private const float GlowMaskStrength = 0.85f;
        private const float BloomStrength = 0.4f;
        private const int ClusterChance = 6;      // 1 из N сегментов лозы — пара плодов

        // Маска свечения: плоды и слабый контур по светлым кромкам лент и листьев.
        // Рисуется поверх аддитивно и не зависит от света — в тёмной воде светится сам рисунок
        private static Asset<Texture2D> _glowMask;

        // Ореол вокруг плода: позиции плодов — в Tidekelp_tile.Blooms.cs (пишет генератор листа)
        private readonly record struct Bloom(int Species, int Variant, Vector2 Offset, float Size);

        private const float BloomScale = 1.8f;    // ореол шире самой точки свечения
        private static readonly Color FruitColor = new(90, 210, 255);
        private static readonly Vector3 FruitGlow = new(0.10f, 0.30f, 0.42f); // свет одного плода
        private static int[,] _fruitCount;

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
            // Нижний предел освещённости рисунка: ткань сама тлеет, и в полной темноте
            // глубины заросли читаются тусклым силуэтом своего цвета, а не исчезают
            public readonly Vector3 SelfLight;
            public readonly Color MapColor;

            public SpeciesTraits(int minHeight, int maxHeight, float buoyancy, float bendStiffness,
                float pliancy, float currentResponse, Vector3 stemGlow, Vector3 crownGlow, Vector3 selfLight,
                Color mapColor)
            {
                MinHeight = minHeight;
                MaxHeight = maxHeight;
                Buoyancy = buoyancy;
                BendStiffness = bendStiffness;
                Pliancy = pliancy;
                CurrentResponse = currentResponse;
                StemGlow = stemGlow;
                CrownGlow = crownGlow;
                SelfLight = selfLight;
                MapColor = mapColor;
            }
        }

        public static readonly SpeciesTraits[] Traits =
        {
            // Лоза: несущий вид Чёрных лесов, тянется от пола до свода — косичка из двух лент.
            // Пловец отводит её сдержанно, и она неторопливо встаёт обратно
            new(12, 110, 0.004f, 0.50f, 0.45f, 1.0f, new Vector3(0.015f, 0.045f, 0.035f),
                new Vector3(0.100f, 0.300f, 0.420f), new Vector3(0.40f, 0.48f, 0.46f), new Color(26, 80, 50)),
            // Папоротник: подлесок по пояс, одна волнистая лента, гибкий — легко увлекается
            // следом и сильнее колышется
            new(6, 26, 0.006f, 0.30f, 0.85f, 1.5f, new Vector3(0.020f, 0.060f, 0.055f),
                new Vector3(0.060f, 0.180f, 0.220f), new Vector3(0.38f, 0.52f, 0.52f), new Color(28, 96, 82)),
            // Куст: акцент у самого дна, плотный жгут из трёх лент, жёсткий, почти не шевелится
            new(3, 7, 0.020f, 0.60f, 0.40f, 0.3f, new Vector3(0.015f, 0.040f, 0.030f),
                new Vector3(0.100f, 0.300f, 0.420f), new Vector3(0.40f, 0.46f, 0.42f), new Color(32, 70, 40)),
        };

        public static int MinHeightOf(int species) => Traits[species].MinHeight;

        public static int MaxHeightOf(int species) => Traits[species].MaxHeight;

        public static int SpeciesAt(Tile tile) =>
            Math.Clamp(tile.TileFrameY / FrameStepY, 0, SpeciesCount - 1);

        public static int VariantOf(Tile tile) =>
            Math.Clamp(tile.TileFrameX / FrameStepX, 0, VariantCount - 1);

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

            // Плоды — по всей лозе, кроме самого низа: огоньки по высоте
            // показывают размер зала, как гирлянда
            if (species == SpeciesSpruce && indexFromBottom > 2 && rand.NextBool(ClusterChance))
                return VariantCluster;

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

        // Пульс идёт волной по стеблю: фаза сдвинута высотой, соседние
        // верхушки не мигают в такт
        private static float GlowPulse(int i, int j) => 1f + GlowPulseDepth *
            MathF.Sin(Main.GameUpdateCount * GlowPulseSpeed + i * 0.7f + j * 0.23f);

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Tile tile = Main.tile[i, j];
            SpeciesTraits traits = Traits[SpeciesAt(tile)];
            int variant = VariantOf(tile);
            // Плоды светят сами из KelpForestRenderer — там они и висят; здесь только запасной путь
            Vector3 glow = variant == VariantCrown ? traits.CrownGlow : traits.StemGlow;
            if (!KelpForestRenderer.Active)
                glow += FruitGlow * FruitCount(SpeciesAt(tile), variant);
            float pulse = GlowPulse(i, j);

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

        // Весь лес рисует KelpForestRenderer: стебель целиком одной лентой вдоль физической
        // цепи, с листьями и плодами. Лист тайла ниже — запасной путь, если рендер не готов
        // (атлас ещё грузится) или сломался.
        // Тайлы движок рисует в кэш-текстуру раз в несколько кадров — нарисованное здесь
        // двигалось бы рывками. Поэтому здесь сегмент только регистрируется особой точкой
        // (как ванильные лианы и трава), а рисуется в SpecialDraw — каждый кадр
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            if (!KelpForestRenderer.Active)
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

            int species = SpeciesAt(tile);
            int variant = VariantOf(tile);
            Texture2D texture = TextureAssets.Tile[Type].Value;
            Vector2 origin = new(DrawCellWidth / 2f, DrawAnchorY);
            int row = species * 2 + (j & 1); // фаза косички: у соседей по высоте всегда разная
            var frame = new Rectangle(variant * DrawStepX, row * DrawStepY, DrawCellWidth, DrawCellHeight);
            Vector3 floor = Traits[species].SelfLight;
            Color light = new(Vector3.Max(Lighting.GetColor(anchor.ToTileCoordinates()).ToVector3(), floor));
            Vector2 at = anchor - Main.screenPosition;

            spriteBatch.Draw(texture, at, frame, light, rotation, origin, 1f, SpriteEffects.None, 0f);

            // A = 0 под premultiplied-смешением — чистое сложение поверх рисунка
            float pulse = GlowPulse(i, j);
            _glowMask ??= ModContent.Request<Texture2D>(Texture + "_Glow");
            Color glow = Color.White * (GlowMaskStrength * pulse);
            glow.A = 0;
            spriteBatch.Draw(_glowMask.Value, at, frame, glow, rotation, origin, 1f, SpriteEffects.None, 0f);

            DrawBloom(spriteBatch, species, variant, at, rotation, pulse);
        }

        private static void DrawBloom(SpriteBatch spriteBatch, int species, int variant, Vector2 at,
            float rotation, float pulse)
        {
            Texture2D soft = SoAVfx.SoftGlow;
            if (soft == null)
                return;
            foreach (Bloom bloom in Blooms)
            {
                if (bloom.Species != species || bloom.Variant != variant)
                    continue;
                Color color = FruitColor * (BloomStrength * pulse);
                color.A = 0;
                spriteBatch.Draw(soft, at + bloom.Offset.RotatedBy(rotation), null, color, 0f, soft.Size() / 2f,
                    bloom.Size * BloomScale / soft.Width, SpriteEffects.None, 0f);
            }
        }

        // Сколько плодов на форме — от него свет сегмента
        private static int FruitCount(int species, int variant)
        {
            if (_fruitCount == null)
            {
                var count = new int[SpeciesCount, VariantCount];
                foreach (Bloom bloom in Blooms)
                    count[bloom.Species, bloom.Variant]++;
                _fruitCount = count;
            }
            return _fruitCount[species, variant];
        }

        public override void Unload()
        {
            _glowMask = null;
            _fruitCount = null;
        }
    }
}
