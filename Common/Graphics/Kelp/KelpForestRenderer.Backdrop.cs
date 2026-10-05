using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using SoA.Common.Graphics.Atmosphere;
using SoA.Content.Walls;
using SoA.Content.Biomes;
using Strand = SoA.Common.Systems.TideKelpPhysics.Strand;

namespace SoA.Common.Graphics.Kelp
{
    // Дальний лес вместо стен II зоны (Чёрные леса). Вся вода зала заложена стенами биома;
    // там, где они стоят, вместо камня видна толща воды и в ней два слоя силуэтов водорослей
    // на параллаксе (атлас-дымка KelpAtlas_Haze) — зал читается огромным лесом, а не стеной.
    // Ведёт себя как стена: освещается светом мира (но не темнее своего тления), лежит под блоками,
    // в клетках с блоками и без стен его нет, на границе зоны плавно переходит в обычную стену.
    // Стены игрока и построек не трогает — только стены биома
    public sealed partial class KelpForestRenderer
    {
        private readonly struct BackdropLayer
        {
            public readonly float Parallax;    // доля хода камеры по горизонтали: 0 — у горизонта
            public readonly float ParallaxY;   // то же по вертикали, слабее: верхушки уезжают медленно
            public readonly float Spacing;     // px между стеблями слоя
            public readonly float Density;     // доля занятых мест
            public readonly float Brightness;
            public readonly float Glow;        // яркость далёких плодов
            public readonly int Seed;

            public BackdropLayer(float parallax, float parallaxY, float spacing, float density, float brightness,
                float glow, int seed)
            {
                Parallax = parallax;
                ParallaxY = parallaxY;
                Spacing = spacing;
                Density = density;
                Brightness = brightness;
                Glow = glow;
                Seed = seed;
            }
        }

        // Дальний слой первым: ближний ложится поверх. Яркость выше, чем у прямого рисунка:
        // слой ещё умножается на свет мира, как стена
        private static readonly BackdropLayer[] BackdropLayers =
        {
            new(0.30f, 0.12f, 30f, 1.0f, 1.0f, 0.45f, 101),
            new(0.55f, 0.25f, 40f, 0.45f, 1.30f, 0.60f, 202),
        };

        private const int BackdropZone = 2;                // Чёрные леса
        private const int BackdropSegments = 100;          // стебли длиннее экрана: комель всегда ниже кадра
        private const float BackdropTintShare = 0.45f;     // насколько силуэты уходят в оттенок зоны
        private const float BackdropWaterShade = 0.14f;    // толща воды между силуэтами, до света
        // Свет не ниже этого: в Чёрных лесах мир почти не освещён, и честная стена была бы
        // чёрной — лес за ней тлеет сам, как и живой лес (SelfLight)
        private const float BackdropLightFloor = 0.4f;
        private const float BackdropEdgeFade = 0.12f;      // доля зоны по глубине на переход в стены

        private static RenderTarget2D _backTarget;
        private static bool _hasBackdrop;
        private static readonly Dictionary<long, Strand> _backdropStrands = new();

        // Ореолы дальних плодов не рисуются: мягкое пятно легло бы и на блоки
        private static readonly List<Bloom> _backBloomsDiscard = new();

        // Клетки экрана: доля дальнего леса (0 — обычная стена или блок) и свет по углам
        private static float[] _cellCover = Array.Empty<float>();
        private static Vector3[] _cornerLight = Array.Empty<Vector3>();
        private static Vector3[] _tileLight = Array.Empty<Vector3>();
        private static VertexPositionColorTexture[] _cells = new VertexPositionColorTexture[1 << 14];
        private static int _cellQuads;

        private static BlendState _multiplyBlend;
        private static BlendState _eraseBlend;

        // Свет умножает цвет и не трогает прозрачность — как свет на стене
        private static BlendState MultiplyBlend => _multiplyBlend ??= new BlendState
        {
            ColorSourceBlend = Blend.Zero,
            ColorDestinationBlend = Blend.SourceColor,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        // Стирает на долю альфы источника: там, где дальнего леса нет, видна настоящая стена
        private static BlendState EraseBlend => _eraseBlend ??= new BlendState
        {
            ColorSourceBlend = Blend.Zero,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.InverseSourceAlpha,
        };

        private enum CellPass
        {
            Water,
            Light,
            Erase,
        }

        // Дешёвая проверка до обхода клеток: камера в биоме у II зоны
        private static float BackdropAmount()
        {
            if (!TideAtmosphere.IsActive || !TideOfShadowsWorldData.HasBounds)
                return 0f;
            float depth = TideAtmosphere.DepthLevel;
            return depth > BackdropZone - 1.6f && depth < BackdropZone + 0.6f ? TideAtmosphere.Presence : 0f;
        }

        // Цель уже выбрана и очищена. false — на экране нет ни одной клетки дальнего леса
        private static bool RenderBackdrop(GraphicsDevice device)
        {
            int left = (int)MathF.Floor(_origin.X / 16f);
            int top = (int)MathF.Floor(_origin.Y / 16f);
            int columns = TargetWidth * Pixel / 16 + 2;
            int rows = TargetHeight * Pixel / 16 + 2;
            if (!MeasureCells(left, top, columns, rows))
                return false;

            Texture2D white = TextureAssets.MagicPixel.Value;
            Vector3 tone = BackdropTone();
            Vector2 shift = -_origin / Pixel;

            // 1. Толща воды на месте стен
            BuildCells(left, top, columns, rows, CellPass.Water, tone * BackdropWaterShade, white);
            DrawPass(device, white, _cells, _cellQuads, BlendState.AlphaBlend, shift);

            // 2. Силуэты двух слоёв: меши в пространстве слоя, слой сдвигается матрицей
            for (int index = 0; index < BackdropLayers.Length; index++)
            {
                CollectBackdropLayer(index, tone, out Vector2 layerShift);
                _layerShift[index] = shift + layerShift / Pixel;
                DrawMeshes(device, _layerMeshes[index], _atlasHaze.Value, BlendState.AlphaBlend, _layerShift[index], false);
            }

            // 3. Свет мира — как на стене
            BuildCells(left, top, columns, rows, CellPass.Light, Vector3.One, white);
            DrawPass(device, white, _cells, _cellQuads, MultiplyBlend, shift);

            // 4. Далёкие плоды тлеют сами, свет им не нужен
            for (int index = 0; index < BackdropLayers.Length; index++)
                DrawMeshes(device, _layerMeshes[index], _atlasGlow.Value, EmissiveBlend, _layerShift[index], true);

            // 5. Где дальнего леса нет — стираем: там видна настоящая стена или блок
            BuildCells(left, top, columns, rows, CellPass.Erase, Vector3.Zero, white);
            DrawPass(device, white, _cells, _cellQuads, EraseBlend, shift);
            return true;
        }

        private static readonly List<StrandMesh>[] _layerMeshes = { new(), new() };
        private static readonly Vector2[] _layerShift = new Vector2[2];

        // Доля дальнего леса по клеткам и свет по углам клеток (среднее четырёх соседних
        // тайлов — плавно, как у стен). true — есть хоть одна клетка
        private static bool MeasureCells(int left, int top, int columns, int rows)
        {
            if (_cellCover.Length < columns * rows)
                _cellCover = new float[columns * rows];
            if (_cornerLight.Length < (columns + 1) * (rows + 1))
                _cornerLight = new Vector3[(columns + 1) * (rows + 1)];

            ushort stoneWall = (ushort)ModContent.WallType<Tidestone_wall>();
            ushort sandWall = (ushort)ModContent.WallType<Tidesand_wall>();
            bool any = false;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    float cover = CellCover(left + x, top + y, stoneWall, sandWall);
                    _cellCover[y * columns + x] = cover;
                    any |= cover > 0f;
                }
            }
            if (!any)
                return false;

            // Свет тайлов — по разу на тайл (с рамкой в тайл), углы — среднее четырёх соседей
            int stride = columns + 1;
            if (_tileLight.Length < stride * (rows + 1))
                _tileLight = new Vector3[stride * (rows + 1)];
            for (int y = 0; y <= rows; y++)
            {
                for (int x = 0; x <= columns; x++)
                    _tileLight[y * stride + x] = Lighting.GetColor(left + x - 1, top + y - 1).ToVector3();
            }

            var floor = new Vector3(BackdropLightFloor);
            for (int y = 0; y <= rows; y++)
            {
                for (int x = 0; x <= columns; x++)
                {
                    int x0 = Math.Max(x - 1, 0), y0 = Math.Max(y - 1, 0);
                    Vector3 sum = _tileLight[y0 * stride + x0] + _tileLight[y0 * stride + x]
                        + _tileLight[y * stride + x0] + _tileLight[y * stride + x];
                    _cornerLight[y * stride + x] = Vector3.Max(sum * 0.25f, floor);
                }
            }
            return true;
        }

        private static float CellCover(int x, int y, ushort stoneWall, ushort sandWall)
        {
            if (!WorldGen.InWorld(x, y, 1))
                return 0f;
            Tile tile = Main.tile[x, y];
            if (tile.WallType != stoneWall && tile.WallType != sandWall)
                return 0f;
            if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                return 0f;

            float depth = TideOfShadowsWorldData.DepthLevelAt(x, y);
            if (depth < BackdropZone - 1 || depth > BackdropZone)
                return 0f;
            float fraction = depth - (BackdropZone - 1);
            return MathHelper.SmoothStep(0f, 1f, Math.Min(fraction / BackdropEdgeFade, 1f))
                * MathHelper.SmoothStep(0f, 1f, Math.Min((1f - fraction) / BackdropEdgeFade, 1f));
        }

        private static void BuildCells(int left, int top, int columns, int rows, CellPass pass, Vector3 color,
            Texture2D white)
        {
            _cellQuads = 0;
            var uv = new Vector2(0.5f / white.Width, 0.5f / white.Height);
            if (pass != CellPass.Light)
            {
                BuildCellRuns(left, top, columns, rows, pass, new Color(color), uv);
                return;
            }

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    // Свет — по углам клетки, плавно, как на стене
                    if (_cellCover[y * columns + x] <= 0f)
                        continue;
                    int stride = columns + 1;
                    var c0 = new Color(_cornerLight[y * stride + x]);
                    var c1 = new Color(_cornerLight[y * stride + x + 1]);
                    var c2 = new Color(_cornerLight[(y + 1) * stride + x + 1]);
                    var c3 = new Color(_cornerLight[(y + 1) * stride + x]);

                    var world = new Vector2((left + x) * 16f, (top + y) * 16f);
                    AddCell(ToTarget(world), ToTarget(world + new Vector2(16f, 0f)),
                        ToTarget(world + new Vector2(16f, 16f)), ToTarget(world + new Vector2(0f, 16f)), uv,
                        c0, c1, c2, c3);
                }
            }
        }

        // Заливка и стирание ровные — подряд идущие клетки с одной долей склеиваются в полосу:
        // квадов в десятки раз меньше, чем клеток
        private static void BuildCellRuns(int left, int top, int columns, int rows, CellPass pass, Color fill,
            Vector2 uv)
        {
            for (int y = 0; y < rows; y++)
            {
                int x = 0;
                while (x < columns)
                {
                    float cover = _cellCover[y * columns + x];
                    bool wanted = pass == CellPass.Water ? cover > 0f : cover < 1f;
                    if (!wanted)
                    {
                        x++;
                        continue;
                    }

                    int start = x;
                    while (x < columns && _cellCover[y * columns + x] == cover)
                        x++;

                    Color c = pass == CellPass.Water ? fill : new Color(0, 0, 0, (int)((1f - cover) * 255f));
                    var world = new Vector2((left + start) * 16f, (top + y) * 16f);
                    float width = (x - start) * 16f;
                    AddCell(ToTarget(world), ToTarget(world + new Vector2(width, 0f)),
                        ToTarget(world + new Vector2(width, 16f)), ToTarget(world + new Vector2(0f, 16f)), uv,
                        c, c, c, c);
                }
            }
        }

        private static void AddCell(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 uv,
            Color ca, Color cb, Color cc, Color cd)
        {
            int v = _cellQuads * 4;
            if (v + 4 > _cells.Length)
                Array.Resize(ref _cells, _cells.Length * 2);
            _cells[v] = new VertexPositionColorTexture(new Vector3(a, 0f), ca, uv);
            _cells[v + 1] = new VertexPositionColorTexture(new Vector3(b, 0f), cb, uv);
            _cells[v + 2] = new VertexPositionColorTexture(new Vector3(c, 0f), cc, uv);
            _cells[v + 3] = new VertexPositionColorTexture(new Vector3(d, 0f), cd, uv);
            _cellQuads++;
        }

        // Оттенок зоны: силуэты и толща уходят в цвет воды
        private static Vector3 BackdropTone()
        {
            Vector3 tint = TideAtmosphere.Profile.Tint;
            float strongest = Math.Max(tint.X, Math.Max(tint.Y, tint.Z));
            return Vector3.Lerp(Vector3.One, strongest > 0f ? tint / strongest : Vector3.One, BackdropTintShare);
        }

        // Слой стоит дальше камеры: его пространство отстаёт от мира на долю хода камеры.
        // layerShift — мировой сдвиг слоя в этом кадре (меши строятся без него и кэшируются)
        private static void CollectBackdropLayer(int index, Vector3 tone, out Vector2 layerShift)
        {
            BackdropLayer layer = BackdropLayers[index];
            List<StrandMesh> meshes = _layerMeshes[index];
            meshes.Clear();

            layerShift = new Vector2(Main.screenPosition.X * (1f - layer.Parallax),
                Main.screenPosition.Y * (1f - layer.ParallaxY));
            Rectangle view = ViewRect();
            view.Offset((int)-layerShift.X, (int)-layerShift.Y);
            float surfaceY = TideOfShadowsWorldData.WaterTopY * 16f;

            int first = (int)MathF.Floor(view.Left / layer.Spacing);
            int last = (int)MathF.Ceiling(view.Right / layer.Spacing);
            for (int slot = first; slot <= last; slot++)
            {
                if (KelpLayout.Hash01(slot, layer.Seed) > layer.Density)
                    continue;

                long key = long.MinValue | ((long)index << 40) | (uint)slot;
                StrandMesh mesh = MeshOf(key);
                if (mesh.Layout == null || mesh.Due(BackdropInterval))
                {
                    _flat = true;
                    _flatColor = tone * layer.Brightness;
                    _flatAlpha = 1f;
                    _glowScale = layer.Glow;
                    BeginMesh(mesh);
                    _bloomSink = _backBloomsDiscard;
                    try
                    {
                        KelpLayout layout = BuildBackdropStrand(layer, index, slot, surfaceY, view);
                        EndMesh(mesh, layout ?? mesh.Layout ?? EmptyLayout);
                    }
                    finally
                    {
                        _flat = false;
                        _glowScale = 1f;
                        _bloomSink = _blooms;
                        _backBloomsDiscard.Clear();
                    }
                }
                mesh.UsedFrame = _frame;
                if (mesh.ColorQuads > 0)
                    meshes.Add(mesh);
            }
        }

        // Пустой облик для мест слоя, где стебля в кадре нет: меш пустой, но помечен построенным
        private static readonly KelpLayout EmptyLayout = new() { Leaves = Array.Empty<KelpLeaf>(), Fruits = Array.Empty<KelpFruit>() };

        // Стебель слоя в пространстве слоя. null — в кадре его нет
        private static KelpLayout BuildBackdropStrand(in BackdropLayer layer, int layerIndex, int slot,
            float surfaceY, Rectangle view)
        {
            float x = slot * layer.Spacing + (KelpLayout.Hash01(slot, layer.Seed + 1) - 0.5f) * layer.Spacing * 0.6f;

            // Верхушка: своё место на экране; по вертикали слой отстаёт от камеры (сдвиг слоя),
            // так что в его пространстве верхушка стоит на месте
            float topOnScreen = MathHelper.Lerp(0.05f, 0.6f, KelpLayout.Hash01(slot, layer.Seed + 2)) * Main.screenHeight;
            float top = topOnScreen + layer.ParallaxY * surfaceY;
            if (top > view.Bottom)
                return null;

            long key = long.MinValue | ((long)layerIndex << 40) | (uint)slot;
            if (!_backdropStrands.TryGetValue(key, out Strand strand))
            {
                strand = new Strand { X = slot, BaseY = layerIndex };
                strand.ResetToRest(BackdropSegments);
                _backdropStrands[key] = strand;
            }

            // Цепь от комля (далеко под кадром) к верхушке; качание медленное и общее для слоя
            float length = BackdropSegments * SegmentLength;
            float phase = KelpLayout.Hash01(slot, layer.Seed + 3) * MathF.Tau;
            float lean = 0.012f * MathF.Sin(_time * 0.2f + phase);
            Vector2[] points = strand.Points;
            points[0] = new Vector2(x, top + length);
            for (int k = 0; k < BackdropSegments; k++)
            {
                float angle = lean + 0.025f * MathF.Sin(_time * 0.45f + phase + k * 0.11f);
                strand.Angle[k] = angle;
                points[k + 1] = points[k] + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * SegmentLength;
            }

            int species = KelpLayout.Hash01(slot, layer.Seed + 4) < 0.8f ? 0 : 1;
            if (!_layouts.TryGetValue(key, out KelpLayout layout))
            {
                layout = KelpLayout.Build(slot * 7919 + layer.Seed, layer.Seed, BackdropSegments, species,
                    KelpLayout.PlanBack);
                _layouts[key] = layout;
            }
            layout.LastUsed = _frame;
            BuildStrand(strand, layout, view);
            return layout;
        }

        // Дальний лес ложится поверх стен (на их месте) и под блоки
        private static void DrawBackdropBehindWalls(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self)
        {
            orig(self);
            if (!_hasBackdrop || _backTarget == null)
                return;

            SpriteBatch sb = Main.spriteBatch;
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            sb.Draw(_backTarget, _origin - Main.screenPosition, null, Color.White, 0f, Vector2.Zero, Pixel,
                SpriteEffects.None, 0f);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        }
    }
}
