using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;
using SoA.Common.Graphics.Atmosphere;
using SoA.Common.Systems;
using Strand = SoA.Common.Systems.TideKelpPhysics.Strand;

namespace SoA.Common.Graphics.Kelp
{
    // Подводный лес целиком: каждый стебель приливной флоры — сплошная лента-меш вдоль его
    // физической цепи (TideKelpPhysics), без стыков между тайлами. На ленту натянута
    // повторяющаяся полоса косички, на изгибающиеся ленты листьев — картинки листьев;
    // плоды на хвостиках покачиваются, светятся и светят в мир.
    //
    // Пиксель-арт при любом изгибе: всё рисуется в цель половинного разрешения (пиксель
    // цели = 2x2 пикселя мира, сетка привязана к миру) с точечной выборкой атласа в
    // арт-разрешении, потом цель растягивается вдвое. Повёрнутые и изогнутые формы
    // остаются честными арт-пикселями, а не мылом и не пикселями разного размера.
    //
    // Порядок кадра: цель рисуется в On_Main.CheckMonoliths — камера уже обновлена, а кадр
    // ещё не начат, переключать цели безопасно. На экран она ложится после нетвёрдых тайлов:
    // за твёрдыми тайлами, игроком и существами, под водой — как и прежний рисунок тайла.
    //
    // Чисто клиентское. Атлас и его области — Docs/art_refs/tidekelp/make_kelp_atlas.py,
    // превью этого же рендера на Python — preview_forest.py рядом
    public sealed partial class KelpForestRenderer : ModSystem
    {
        // Работает — тайл сам себя не рисует (иначе старый лист тайла как запасной путь)
        public static bool Active => !_failed && _atlas is { IsLoaded: true } && _atlasGlow is { IsLoaded: true }
            && _atlasHaze is { IsLoaded: true };

        private const int Pixel = 2;                // пикселей мира в пикселе цели
        private const int TargetPadding = 40;       // пикселей цели за краем экрана
        private const float CullMarginPx = 90f;     // листья и плоды вылезают за стебель
        private const float SegmentLength = 16f;
        private const float StemStep = 8f;          // шаг сэмплов стебля вдоль цепи, px
        private const float TaperLength = 40f;      // верхушка сужается на столько px
        private const int LeafSections = 6;
        private const float LeafReach = 72f;         // самый длинный лист, px: отсев по отрезку стебля
        private const float FruitReach = 40f;
        private const int MaxQuadsPerCall = 16383;  // 4 вершины на квад — в 16-битные индексы

        // Свет: ткань сама слегка тлеет, и в темноте глубины заросли читаются силуэтом
        private static readonly Vector3[] SelfLight =
        {
            new(0.46f, 0.56f, 0.54f),
            new(0.44f, 0.56f, 0.58f),
            new(0.46f, 0.54f, 0.48f),
        };

        // По плану глубины: яркость, примесь цвета воды, тлеет ли свечение стебля и листьев
        private static readonly float[] PlanBrightness = { 0.42f, 0.72f, 1f };
        private static readonly float[] PlanFog = { 0.62f, 0.30f, 0f };
        // Крапины на листьях — только у ближнего плана: дальше их не видно, а квадов тысячи
        private static readonly float[] PlanLeafGlow = { 0f, 0f, 0.35f };
        private static readonly float[] PlanFruitGlow = { 0.55f, 1f, 1f };
        private static readonly Vector3 DefaultFog = new(0.05f, 0.11f, 0.20f);
        private const float BackLeafShade = 0.7f;

        // Свет сквозь крону: низ стебля тонет в сумраке, верх ловит свет сверху
        private const float CanopyBottom = 0.78f;
        private const float CanopyTop = 1.12f;

        // Волна света бежит по стеблю вверх
        private const float PulseSpeed = 60f;       // px/с
        private const float PulseWidth = 26f;
        private const float PulseStrength = 1.4f;
        private const float StemGlowBase = 0.35f;

        // Плоды
        private static readonly Color BloomColor = new(90, 210, 255);
        private static readonly Vector3 FruitLight = new(0.10f, 0.30f, 0.42f);
        private const float BloomScale = 4.7f;      // поперечник ореола / ширина плода
        private const float BloomStrength = 0.5f;

        private static Asset<Texture2D> _atlas;
        private static Asset<Texture2D> _atlasGlow;
        private static Asset<Texture2D> _atlasHaze;
        private static RenderTarget2D _target;
        private static BasicEffect _effect;
        private static BlendState _emissiveBlend;
        private static short[] _indices;
        private static bool _failed;
        private static bool _hasFrame;

        // Меши стеблей кэшируются в мировых координатах (сдвиг камеры — матрицей на видеокарте),
        // а перестраиваются по очереди: в густой роще каждый стебель раз в 2-3 кадра, но разные
        // стебли в разные кадры — работа ровная, без пиков. Покачивание медленное, 20-30 Гц
        // на одном стебле не видно
        private const int BusyQuads = 12000;        // больше — перестройка раз в 2 кадра
        private const int HeavyQuads = 30000;       // больше — раз в 3
        private const int BackdropInterval = 3;     // дальний лес качается совсем медленно

        private sealed class StrandMesh
        {
            public VertexPositionColorTexture[] Color = Array.Empty<VertexPositionColorTexture>();
            public int ColorQuads;
            public VertexPositionColorTexture[] Glow = Array.Empty<VertexPositionColorTexture>();
            public int GlowQuads;
            public readonly List<Bloom> Blooms = new();
            public KelpLayout Layout;
            public uint BuiltFrame;
            public uint UsedFrame;
            public uint Stagger;

            public bool Due(int interval)
            {
                if (Layout == null || BuiltFrame == 0)
                    return true;
                uint age = _frame - BuiltFrame;
                return age >= interval && ((_frame + Stagger) % (uint)interval == 0 || age > interval);
            }
        }

        private static readonly Dictionary<long, StrandMesh> _meshes = new();
        private static readonly List<StrandMesh> _drawList = new();
        private static int _lastQuads;
        private static int _rebuilt;
        private static Vector2 _uvScale;

        // Замер: /kelpperf — сколько мс кадра уходит на лес
        private static bool _perf;
        private static double _perfMs;
        private static int _perfFrames;
        private static readonly System.Diagnostics.Stopwatch _watch = new();

        public static void TogglePerf()
        {
            _perf = !_perf;
            _perfMs = 0;
            _perfFrames = 0;
            Main.NewText(_perf ? "Замер подводного леса: включён" : "Замер подводного леса: выключен", 150, 220, 255);
        }
        private static Vector2 _origin;             // мировая точка пикселя (0, 0) цели
        private static Vector3 _fog;
        private static float _time;
        private static uint _frame;

        private static VertexPositionColorTexture[] _verts = new VertexPositionColorTexture[1 << 14];
        private static int _quads;
        private static VertexPositionColorTexture[] _glow = new VertexPositionColorTexture[1 << 14];
        private static int _glowQuads;

        private readonly struct Bloom
        {
            public readonly Vector2 Position;
            public readonly float Size;
            public readonly float Strength;

            public Bloom(Vector2 position, float size, float strength)
            {
                Position = position;
                Size = size;
                Strength = strength;
            }
        }

        private static readonly List<Bloom> _blooms = new();
        private static List<Bloom> _bloomSink = _blooms;

        // Рисунок без света мира — для дальнего леса: ровный цвет толщи и общая прозрачность
        private static bool _flat;
        private static Vector3 _flatColor;
        private static float _flatAlpha = 1f;
        private static float _glowScale = 1f;
        private static readonly List<Strand> _visible = new();
        private static readonly Dictionary<long, KelpLayout> _layouts = new();
        private static readonly List<long> _staleLayouts = new();

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _atlas = ModContent.Request<Texture2D>("SoA/Assets/Textures/Kelp/KelpAtlas");
            _atlasGlow = ModContent.Request<Texture2D>("SoA/Assets/Textures/Kelp/KelpAtlas_Glow");
            _atlasHaze = ModContent.Request<Texture2D>("SoA/Assets/Textures/Kelp/KelpAtlas_Haze");
            On_Main.CheckMonoliths += RenderBeforeFrame;
            On_Main.DoDraw_Tiles_NonSolid += DrawAfterNonSolidTiles;
            On_Main.DoDraw_WallsAndBlacks += DrawBackdropBehindWalls;
        }

        public override void Unload()
        {
            RenderTarget2D target = _target, backTarget = _backTarget;
            BasicEffect effect = _effect;
            if (target != null || backTarget != null || effect != null)
            {
                // Ресурсы видеокарты освобождаем в главном потоке
                Main.QueueMainThreadAction(() =>
                {
                    target?.Dispose();
                    backTarget?.Dispose();
                    effect?.Dispose();
                });
            }
            _target = null;
            _backTarget = null;
            _effect = null;
            _atlas = null;
            _atlasGlow = null;
            _atlasHaze = null;
            _backdropStrands.Clear();
            _layouts.Clear();
            _meshes.Clear();
            _blooms.Clear();
            _visible.Clear();
        }

        public override void OnWorldUnload()
        {
            _meshes.Clear();
            _layouts.Clear();
            _backdropStrands.Clear();
            _hasFrame = false;
            _hasBackdrop = false;
        }

        // Плоды светят в мир: соседние листья подсвечиваются их циановым светом
        public override void PostUpdateEverything()
        {
            if (Main.dedServ || !Active)
                return;

            TideKelpPhysics.CollectVisible(_visible);
            // Свет за краем экрана никто не увидит — плоды там пропускаем
            float top = Main.screenPosition.Y - 64f, bottom = Main.screenPosition.Y + Main.screenHeight + 64f;
            float leftEdge = Main.screenPosition.X - 64f, rightEdge = Main.screenPosition.X + Main.screenWidth + 64f;
            foreach (Strand strand in _visible)
            {
                if (strand.Bounds.Right < leftEdge || strand.Bounds.Left > rightEdge)
                    continue;
                KelpLayout layout = LayoutOf(strand);
                float strength = layout.Plan == KelpLayout.PlanBack ? 0.45f : 1f;
                float length = strand.Height * SegmentLength;
                foreach (KelpFruit fruit in layout.Fruits)
                {
                    if (fruit.S > length)
                        continue;
                    float roughY = strand.Points[Math.Min((int)(fruit.S / SegmentLength), strand.Height)].Y;
                    if (roughY < top || roughY > bottom)
                        continue;
                    Curve(strand.Points, fruit.S, out Vector2 position, out _);
                    float size = 0.7f + 0.3f * fruit.Size;
                    Lighting.AddLight(position + new Vector2(fruit.Side * 6f, fruit.Stalk + 6f),
                        FruitLight * (strength * size));
                }
            }
        }

        #region Кадр в цель

        private static void RenderBeforeFrame(On_Main.orig_CheckMonoliths orig)
        {
            orig();
            _hasFrame = false;
            _hasBackdrop = false;
            if (Main.dedServ || Main.gameMenu || !Active)
                return;

            TideKelpPhysics.CollectVisible(_visible);
            float backdrop = BackdropAmount();
            if (_visible.Count == 0 && backdrop <= 0f)
                return;

            if (_perf)
                _watch.Restart();

            GraphicsDevice device = Main.graphics.GraphicsDevice;
            RenderTargetBinding[] previousTargets = null;
            try
            {
                _frame++;
                _time = Main.GlobalTimeWrappedHourly;
                _fog = TideAtmosphere.IsActive ? TideAtmosphere.Profile.Tint * 0.4f : DefaultFog;

                Vector2 topLeft = Main.screenPosition - new Vector2(TargetPadding * Pixel);
                _origin = new Vector2(MathF.Floor(topLeft.X / Pixel) * Pixel, MathF.Floor(topLeft.Y / Pixel) * Pixel);
                Texture2D atlas = _atlas.Value;
                _uvScale = new Vector2(1f / atlas.Width, 1f / atlas.Height);

                BlendState blend = device.BlendState;
                DepthStencilState depth = device.DepthStencilState;
                RasterizerState rasterizer = device.RasterizerState;
                SamplerState sampler = device.SamplerStates[0];
                previousTargets = device.GetRenderTargets();

                if (backdrop > 0f)
                {
                    EnsureTarget(device, ref _backTarget);
                    device.SetRenderTarget(_backTarget);
                    device.Clear(Color.Transparent);
                    _hasBackdrop = RenderBackdrop(device);
                }

                if (_visible.Count > 0)
                {
                    EnsureTarget(device, ref _target);
                    RenderFront(device);
                    _hasFrame = true;
                }
                PurgeLayouts();

                device.BlendState = blend;
                device.DepthStencilState = depth;
                device.RasterizerState = rasterizer;
                device.SamplerStates[0] = sampler;
            }
            catch (Exception exception)
            {
                // Рендер сломался — возвращаем тайлу его старый рисунок и пишем в лог один раз
                _failed = true;
                ModContent.GetInstance<KelpForestRenderer>().Mod.Logger.Warn("Kelp forest render failed: " + exception);
            }
            finally
            {
                if (previousTargets != null)
                {
                    if (previousTargets.Length > 0)
                        device.SetRenderTargets(previousTargets);
                    else
                        device.SetRenderTarget(null);
                }
            }

            if (_perf)
                ReportPerf();
        }

        private static void ReportPerf()
        {
            _perfMs += _watch.Elapsed.TotalMilliseconds;
            if (++_perfFrames < 120)
                return;
            Main.NewText($"Лес: {_perfMs / _perfFrames:F2} мс/кадр, квадов {_lastQuads}, перестроено за кадр {_rebuilt}, " +
                $"стеблей {_drawList.Count}", 150, 220, 255);
            _perfMs = 0;
            _perfFrames = 0;
        }

        private static void EnsureTarget(GraphicsDevice device, ref RenderTarget2D target)
        {
            int width = TargetWidth, height = TargetHeight;
            if (target != null && target.Width == width && target.Height == height && !target.IsDisposed)
                return;

            target?.Dispose();
            target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0,
                RenderTargetUsage.PreserveContents);
        }

        private static int TargetWidth => Main.screenWidth / Pixel + TargetPadding * 2 + 2;

        private static int TargetHeight => Main.screenHeight / Pixel + TargetPadding * 2 + 2;

        // Вершины — в пикселях цели от начала мира (мир / 2): кэш не зависит от камеры.
        // shift — сдвиг в те же пиксели: минус начало цели, для дальнего леса ещё и слой
        private static void ApplyEffect(GraphicsDevice device, Texture2D texture, BlendState blend, Vector2 shift)
        {
            _effect ??= new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true };
            _effect.World = Matrix.CreateTranslation(shift.X, shift.Y, 0f);
            _effect.View = Matrix.Identity;
            _effect.Projection = Matrix.CreateOrthographicOffCenter(0, TargetWidth, TargetHeight, 0, -1, 1);
            _effect.Texture = texture;

            device.BlendState = blend;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            device.SamplerStates[0] = SamplerState.PointClamp;
            _effect.CurrentTechnique.Passes[0].Apply();
            EnsureIndices();
        }

        private static void DrawVertices(GraphicsDevice device, VertexPositionColorTexture[] vertices, int quads)
        {
            for (int start = 0; start < quads; start += MaxQuadsPerCall)
            {
                int count = Math.Min(MaxQuadsPerCall, quads - start);
                device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, start * 4, count * 4,
                    _indices, 0, count * 2);
            }
        }

        private static void DrawMeshes(GraphicsDevice device, List<StrandMesh> meshes, Texture2D texture,
            BlendState blend, Vector2 shift, bool glow)
        {
            ApplyEffect(device, texture, blend, shift);
            foreach (StrandMesh mesh in meshes)
            {
                if (glow)
                    DrawVertices(device, mesh.Glow, mesh.GlowQuads);
                else
                    DrawVertices(device, mesh.Color, mesh.ColorQuads);
            }
        }

        // Свечение прибавляется к цвету и не трогает прозрачность: светящиеся пиксели
        // ложатся на экран сложением
        private static BlendState EmissiveBlend => _emissiveBlend ??= new BlendState
        {
            ColorSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        private static void DrawPass(GraphicsDevice device, Texture2D texture, VertexPositionColorTexture[] vertices,
            int quads, BlendState blend, Vector2 shift)
        {
            if (quads <= 0)
                return;
            ApplyEffect(device, texture, blend, shift);
            DrawVertices(device, vertices, quads);
        }

        private static void EnsureIndices()
        {
            if (_indices != null)
                return;
            _indices = new short[MaxQuadsPerCall * 6];
            for (int q = 0; q < MaxQuadsPerCall; q++)
            {
                int v = q * 4;
                _indices[q * 6] = unchecked((short)v);
                _indices[q * 6 + 1] = unchecked((short)(v + 1));
                _indices[q * 6 + 2] = unchecked((short)(v + 2));
                _indices[q * 6 + 3] = unchecked((short)v);
                _indices[q * 6 + 4] = unchecked((short)(v + 2));
                _indices[q * 6 + 5] = unchecked((short)(v + 3));
            }
        }

        #endregion

        #region На экран

        private static void DrawAfterNonSolidTiles(On_Main.orig_DoDraw_Tiles_NonSolid orig, Main self)
        {
            orig(self);
            if (!_hasFrame || _target == null)
                return;

            // Батч открыт ванилью (DoDraw_Tiles_NonSolid заканчивается Begin) — закрываем,
            // рисуем своё и открываем таким же
            SpriteBatch sb = Main.spriteBatch;
            sb.End();

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            sb.Draw(_target, _origin - Main.screenPosition, null, Color.White, 0f, Vector2.Zero, Pixel,
                SpriteEffects.None, 0f);
            sb.End();

            DrawBlooms(sb, _blooms);

            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
        }

        // Мягкие ореолы плодов — уже не пиксельные, поверх цели сложением
        private static void DrawBlooms(SpriteBatch sb, List<Bloom> blooms)
        {
            Texture2D soft = SoAVfx.SoftGlow;
            if (soft == null || blooms.Count == 0)
                return;

            sb.Begin(SpriteSortMode.Deferred, SoAVfx.GlowBlend, SamplerState.LinearClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            Vector2 origin = soft.Size() / 2f;
            foreach (Bloom bloom in blooms)
            {
                sb.Draw(soft, bloom.Position - Main.screenPosition, null, BloomColor * (BloomStrength * bloom.Strength),
                    0f, origin, bloom.Size / soft.Width, SpriteEffects.None, 0f);
            }
            sb.End();
        }

        #endregion

        #region Меши

        private static int RebuildInterval => _lastQuads > HeavyQuads ? 3 : _lastQuads > BusyQuads ? 2 : 1;

        private static void RenderFront(GraphicsDevice device)
        {
            _blooms.Clear();
            _drawList.Clear();
            _rebuilt = 0;

            // Дальние первыми: ближние ложатся поверх
            _visible.Sort(CompareByPlan);

            Rectangle view = ViewRect();
            int interval = RebuildInterval;
            int total = 0;
            foreach (Strand strand in _visible)
            {
                if (strand.Height <= 0 || !strand.Bounds.Intersects(view))
                    continue;

                KelpLayout layout = LayoutOf(strand);
                StrandMesh mesh = MeshOf(strand.Key);
                if (mesh.Layout != layout || mesh.Due(interval) || IsDisturbed(strand))
                {
                    BeginMesh(mesh);
                    BuildStrand(strand, layout, view);
                    EndMesh(mesh, layout);
                }
                mesh.UsedFrame = _frame;
                _drawList.Add(mesh);
                _blooms.AddRange(mesh.Blooms);
                total += mesh.ColorQuads + mesh.GlowQuads;
            }
            _lastQuads = total;

            device.SetRenderTarget(_target);
            device.Clear(Color.Transparent);
            Vector2 shift = -_origin / Pixel;
            DrawMeshes(device, _drawList, _atlas.Value, BlendState.AlphaBlend, shift, false);
            DrawMeshes(device, _drawList, _atlasGlow.Value, EmissiveBlend, shift, true);
        }

        // Стебель прямо сейчас раскачивает пловец — его перестраиваем каждый кадр, вне очереди:
        // таких единицы, а отклик на касание должен быть плавным
        private const float DisturbedSpin = 0.01f;

        private static bool IsDisturbed(Strand strand)
        {
            foreach (float spin in strand.Spin)
            {
                if (Math.Abs(spin) > DisturbedSpin)
                    return true;
            }
            return false;
        }

        private static StrandMesh MeshOf(long key)
        {
            if (!_meshes.TryGetValue(key, out StrandMesh mesh))
            {
                mesh = new StrandMesh { Stagger = (uint)(key ^ (key >> 32)) & 0xFFFF };
                _meshes[key] = mesh;
            }
            return mesh;
        }

        // Меш строится в общие буферы, потом копируется в свой кэш
        private static void BeginMesh(StrandMesh mesh)
        {
            _quads = 0;
            _glowQuads = 0;
            mesh.Blooms.Clear();
            _bloomSink = mesh.Blooms;
        }

        private static void EndMesh(StrandMesh mesh, KelpLayout layout)
        {
            _bloomSink = _blooms;
            CopyQuads(_verts, _quads, ref mesh.Color);
            mesh.ColorQuads = _quads;
            CopyQuads(_glow, _glowQuads, ref mesh.Glow);
            mesh.GlowQuads = _glowQuads;
            mesh.Layout = layout;
            mesh.BuiltFrame = _frame;
            _rebuilt++;
        }

        private static void CopyQuads(VertexPositionColorTexture[] from, int quads, ref VertexPositionColorTexture[] to)
        {
            int count = quads * 4;
            if (to.Length < count)
                to = new VertexPositionColorTexture[Math.Max(count, to.Length * 2)];
            Array.Copy(from, to, count);
        }

        private static int CompareByPlan(Strand a, Strand b)
        {
            int plan = LayoutOf(a).Plan.CompareTo(LayoutOf(b).Plan);
            return plan != 0 ? plan : a.X.CompareTo(b.X);
        }

        private static Rectangle ViewRect() => new((int)(_origin.X - CullMarginPx), (int)(_origin.Y - CullMarginPx),
            TargetWidth * Pixel + (int)(CullMarginPx * 2), TargetHeight * Pixel + (int)(CullMarginPx * 2));

        private static KelpLayout LayoutOf(Strand strand)
        {
            if (!_layouts.TryGetValue(strand.Key, out KelpLayout layout) || layout.Height != strand.Height
                || layout.Species != strand.Species)
            {
                layout = KelpLayout.Build(strand.X, strand.BaseY, strand.Height, strand.Species);
                _layouts[strand.Key] = layout;
            }
            layout.LastUsed = _frame;
            return layout;
        }

        // Облики и меши стеблей, давно не бывавших в кадре, выбрасываются
        private static void PurgeLayouts()
        {
            if (_frame % 600 != 0)
                return;
            _staleLayouts.Clear();
            foreach (KeyValuePair<long, KelpLayout> pair in _layouts)
            {
                if (_frame - pair.Value.LastUsed > 600)
                    _staleLayouts.Add(pair.Key);
            }
            foreach (long key in _staleLayouts)
                _layouts.Remove(key);

            _staleLayouts.Clear();
            foreach (KeyValuePair<long, StrandMesh> pair in _meshes)
            {
                if (_frame - pair.Value.UsedFrame > 600)
                    _staleLayouts.Add(pair.Key);
            }
            foreach (long key in _staleLayouts)
                _meshes.Remove(key);
        }

        private static void BuildStrand(Strand strand, KelpLayout layout, Rectangle view)
        {
            // Видимый отрезок стебля (px от комля): высокие стебли уходят за кадр, и строить
            // их целиком — лишняя работа. Отрезок — по точкам цепи, с запасом в сегмент
            float viewTop = view.Top, viewBottom = view.Bottom;
            Vector2[] points = strand.Points;
            int firstVisible = -1, lastVisible = -1;
            for (int k = 0; k < points.Length; k++)
            {
                float y = points[k].Y;
                if (y < viewTop || y > viewBottom)
                    continue;
                if (firstVisible < 0)
                    firstVisible = k;
                lastVisible = k;
            }
            if (firstVisible < 0)
                return;

            float length = strand.Height * SegmentLength;
            float visibleFrom = Math.Max(0f, (firstVisible - 1) * SegmentLength);
            float visibleTo = Math.Min(length, (lastVisible + 1) * SegmentLength);

            int species = layout.Species;
            bool far = layout.Plan == KelpLayout.PlanBack;
            Rectangle stemRect = KelpAtlas.Stems[species * 2 + (far ? 1 : 0)];
            float stemHalfWidth = stemRect.Width * Pixel / 2f;
            float sway = 0f;
            int spinFrom = Math.Max(0, firstVisible - 1), spinTo = Math.Min(strand.Spin.Length, lastVisible + 1);
            for (int k = spinFrom; k < spinTo; k++)
                sway = Math.Max(sway, Math.Abs(strand.Spin[k]));

            // Лист длиннее сегмента: свисает в кадр и из-за его края
            float leafFrom = visibleFrom - LeafReach, leafTo = visibleTo + LeafReach;
            foreach (KelpLeaf leaf in layout.Leaves)
            {
                if (leaf.Back && leaf.S >= leafFrom && leaf.S <= leafTo)
                    BuildLeaf(strand, layout, leaf, stemHalfWidth, sway, viewTop, viewBottom);
            }

            BuildStem(strand, layout, stemRect, stemHalfWidth, length, visibleFrom, visibleTo);

            foreach (KelpLeaf leaf in layout.Leaves)
            {
                if (!leaf.Back && leaf.S >= leafFrom && leaf.S <= leafTo)
                    BuildLeaf(strand, layout, leaf, stemHalfWidth, sway, viewTop, viewBottom);
            }

            foreach (KelpFruit fruit in layout.Fruits)
            {
                if (fruit.S >= visibleFrom - FruitReach && fruit.S <= visibleTo + FruitReach)
                    BuildFruit(strand, layout, fruit, stemHalfWidth, length, viewTop, viewBottom);
            }

            if (visibleFrom <= 0f)
                BuildRocks(strand, layout);
        }

        private static void BuildStem(Strand strand, KelpLayout layout, Rectangle rect, float halfWidth,
            float length, float from, float to)
        {
            int period = rect.Height;
            float offset = layout.BraidOffset;
            float pulseAt = (_time * PulseSpeed + layout.PulseOffset) % (length + 400f) - 200f;
            float leafGlow = PlanLeafGlow[layout.Plan];
            bool glows = layout.Plan != KelpLayout.PlanBack;
            float uLeft = layout.Flip ? rect.Right : rect.Left;
            float uRight = layout.Flip ? rect.Left : rect.Right;
            float end = Math.Min(to, length);

            // Сэмплы на общей сетке шага от комля: косичка не плывёт, когда отрезок сдвигается
            float a = MathF.Floor(from / StemStep) * StemStep;
            Sample(strand, layout, a, halfWidth, length, out Vector2 la, out Vector2 ra, out Color ca);
            while (a < end)
            {
                float b = Math.Min(a + StemStep, length);
                Sample(strand, layout, b, halfWidth, length, out Vector2 lb, out Vector2 rb, out Color cb);

                // Режем квад на границах периода полосы: атлас не повторяется сам
                float phaseA = a / Pixel + offset;
                float cycles = MathF.Floor(phaseA / period);
                float phaseStart = phaseA - cycles * period;
                float next = (cycles + 1f) * period;
                float cutStart = a;
                Vector2 lc = la, rc = ra;
                Color cc = ca;
                while (true)
                {
                    float cutEnd = Math.Min(b, (next - offset) * Pixel);
                    float t = (cutEnd - a) / (b - a);
                    Vector2 le = Vector2.Lerp(la, lb, t), re = Vector2.Lerp(ra, rb, t);
                    Color ce = Color.Lerp(ca, cb, t);

                    // Фаза в пределах полосы: выборка не уходит в соседнюю область атласа
                    float phaseEnd = Math.Min(phaseStart + (cutEnd - cutStart) / Pixel, period);
                    float vStart = rect.Y + period - phaseStart;
                    float vEnd = rect.Y + period - phaseEnd;

                    AddQuad(ref _verts, ref _quads, lc, rc, re, le,
                        new Vector2(uLeft, vStart), new Vector2(uRight, vStart),
                        new Vector2(uRight, vEnd), new Vector2(uLeft, vEnd), cc, cc, ce, ce);
                    if (glows)
                    {
                        Color g0 = Emission(cutStart, pulseAt, leafGlow);
                        Color g1 = Emission(cutEnd, pulseAt, leafGlow);
                        AddQuad(ref _glow, ref _glowQuads, lc, rc, re, le,
                            new Vector2(uLeft, vStart), new Vector2(uRight, vStart),
                            new Vector2(uRight, vEnd), new Vector2(uLeft, vEnd), g0, g0, g1, g1);
                    }

                    if (cutEnd >= b - 0.001f)
                        break;
                    cutStart = cutEnd;
                    phaseStart = 0f;
                    next += period;
                    lc = le;
                    rc = re;
                    cc = ce;
                }

                a = b;
                la = lb;
                ra = rb;
                ca = cb;
            }
        }

        // Точка стебля: левая и правая кромка в пикселях цели и свет
        private static void Sample(Strand strand, KelpLayout layout, float s, float halfWidth, float length,
            out Vector2 left, out Vector2 right, out Color color)
        {
            Curve(strand.Points, s, out Vector2 position, out Vector2 tangent);
            var normal = new Vector2(-tangent.Y, tangent.X);
            float taper = s < length - TaperLength ? 1f : Math.Max(0.3f, (length - s) / TaperLength);
            float half = halfWidth * taper;
            left = ToTarget(position - normal * half);
            right = ToTarget(position + normal * half);
            color = Shade(position, layout, Canopy(s, length));
        }

        private static float Canopy(float s, float length)
            => MathHelper.Lerp(CanopyBottom, CanopyTop, Math.Clamp(s / Math.Max(length, 1f), 0f, 1f));

        // Подъём листа к вертикали вдоль длины: u^0.8 по секциям, считается один раз
        private static readonly float[] LeafBend = BuildLeafBend();

        private static float[] BuildLeafBend()
        {
            var table = new float[LeafSections + 1];
            for (int i = 0; i <= LeafSections; i++)
                table[i] = MathF.Pow(i / (float)LeafSections, 0.8f);
            return table;
        }

        private static Color Emission(float s, float pulseAt, float baseGlow)
        {
            float d = (s - pulseAt) / PulseWidth;
            float e = StemGlowBase + PulseStrength * MathF.Exp(-d * d);
            e *= (baseGlow > 0.2f ? 1f : 0.6f) * _glowScale;
            return new Color(new Vector3(e));
        }

        private static void BuildLeaf(Strand strand, KelpLayout layout, KelpLeaf leaf, float stemHalfWidth, float sway,
            float viewTop, float viewBottom)
        {
            Rectangle rect = KelpAtlas.Leaves[layout.Species][leaf.Variant];
            float total = rect.Height * Pixel;
            float s = Math.Min(leaf.S, strand.Height * SegmentLength);
            Curve(strand.Points, s, out Vector2 position, out Vector2 tangent);
            if (position.Y + total < viewTop || position.Y - total > viewBottom)
                return;

            float alpha = MathF.Atan2(tangent.X, -tangent.Y);
            float halfWidth = rect.Width * Pixel / 2f;
            int side = leaf.Side;
            float baseAngle = alpha + side * leaf.Angle;
            float upright = alpha * 0.3f + side * 0.12f;
            // Пловец рядом — лист трепещет сильнее
            float wave = leaf.Wave + Math.Min(sway * 6f, 0.5f);

            var normal = new Vector2(-tangent.Y, tangent.X);
            Vector2 point = position + normal * (side * stemHalfWidth * 0.6f);
            float step = total / LeafSections;

            Color color = Shade(position, layout, (leaf.Back ? BackLeafShade : 1f) * Canopy(s, strand.Height * SegmentLength));
            float glowLevel = leaf.Back ? 0f : PlanLeafGlow[layout.Plan] * _glowScale;
            Color glow = new(new Vector3(glowLevel));
            float uOuter = rect.Left, uInner = rect.Right;

            Vector2 prevOuter = default, prevInner = default;
            float prevV = 0f;
            for (int i = 0; i <= LeafSections; i++)
            {
                float u = i / (float)LeafSections;
                float angle = baseAngle + (upright - baseAngle) * 0.55f * LeafBend[i]
                    + wave * MathF.Sin(_time * 1.6f - u * 4.5f + leaf.Phase) * u
                    + side * leaf.Curl * u * u;
                var dir = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
                if (i > 0)
                    point += dir * step;

                var leftSide = new Vector2(dir.Y, -dir.X);
                Vector2 outerDir = side < 0 ? leftSide : -leftSide;
                Vector2 outer = ToTarget(point + outerDir * halfWidth);
                Vector2 inner = ToTarget(point - outerDir * halfWidth);
                float v = rect.Bottom - u * rect.Height;

                if (i > 0)
                {
                    AddQuad(ref _verts, ref _quads, prevOuter, prevInner, inner, outer,
                        new Vector2(uOuter, prevV), new Vector2(uInner, prevV),
                        new Vector2(uInner, v), new Vector2(uOuter, v), color, color, color, color);
                    if (glowLevel > 0f)
                    {
                        AddQuad(ref _glow, ref _glowQuads, prevOuter, prevInner, inner, outer,
                            new Vector2(uOuter, prevV), new Vector2(uInner, prevV),
                            new Vector2(uInner, v), new Vector2(uOuter, v), glow, glow, glow, glow);
                    }
                }
                prevOuter = outer;
                prevInner = inner;
                prevV = v;
            }
        }

        private static void BuildFruit(Strand strand, KelpLayout layout, KelpFruit fruit, float stemHalfWidth,
            float length, float viewTop, float viewBottom)
        {
            if (fruit.S > length)
                return;
            Curve(strand.Points, fruit.S, out Vector2 position, out Vector2 tangent);
            if (position.Y + 40f < viewTop || position.Y - 40f > viewBottom)
                return;

            var normal = new Vector2(-tangent.Y, tangent.X);
            int side = fruit.Side;
            Vector2 attach = position + normal * (side * stemHalfWidth * 0.7f);
            float swing = side * 0.45f + 0.22f * MathF.Sin(_time * 1.3f + fruit.Phase);
            var down = new Vector2(-MathF.Sin(swing), MathF.Cos(swing));
            var across = new Vector2(down.Y, -down.X);
            Vector2 end = attach + down * fruit.Stalk;

            Color color = Shade(position, layout, 1f);

            // Хвостик
            Rectangle stalk = KelpAtlas.Stalk;
            float stalkHalf = stalk.Width * Pixel / 2f;
            AddQuad(ref _verts, ref _quads,
                ToTarget(attach - across * stalkHalf), ToTarget(attach + across * stalkHalf),
                ToTarget(end + across * stalkHalf), ToTarget(end - across * stalkHalf),
                new Vector2(stalk.Left, stalk.Top), new Vector2(stalk.Right, stalk.Top),
                new Vector2(stalk.Right, stalk.Bottom), new Vector2(stalk.Left, stalk.Bottom),
                color, color, color, color);

            // Сам плод: светится, в темноте почти не зависит от света мира
            Rectangle rect = KelpAtlas.Fruits[fruit.Size];
            float halfWidth = rect.Width * Pixel / 2f;
            float height = rect.Height * Pixel;
            Vector2 topLeft = end - across * halfWidth, topRight = end + across * halfWidth;
            Vector2 bottomLeft = topLeft + down * height, bottomRight = topRight + down * height;
            Color body = _flat ? color : new Color(Vector3.Min(color.ToVector3() * 1.4f, Vector3.One));
            AddQuad(ref _verts, ref _quads, ToTarget(topLeft), ToTarget(topRight), ToTarget(bottomRight),
                ToTarget(bottomLeft), new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top),
                new Vector2(rect.Right, rect.Bottom), new Vector2(rect.Left, rect.Bottom), body, body, body, body);

            float pulse = 0.8f + 0.2f * MathF.Sin(_time * 2.1f + fruit.Phase);
            float brightness = PlanFruitGlow[layout.Plan] * pulse * _glowScale;
            Color glow = new(new Vector3(brightness));
            AddQuad(ref _glow, ref _glowQuads, ToTarget(topLeft), ToTarget(topRight), ToTarget(bottomRight),
                ToTarget(bottomLeft), new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top),
                new Vector2(rect.Right, rect.Bottom), new Vector2(rect.Left, rect.Bottom), glow, glow, glow, glow);

            _bloomSink.Add(new Bloom(end + down * (height * 0.55f), rect.Width * Pixel * BloomScale, brightness));
        }

        private static void BuildRocks(Strand strand, KelpLayout layout)
        {
            Rectangle rect = KelpAtlas.Rocks[layout.Rock];
            Vector2 root = strand.Points[0];
            float width = rect.Width * Pixel, height = rect.Height * Pixel;
            var topLeft = new Vector2(root.X - width / 2f, root.Y - height + 2f);
            Color color = Shade(root - new Vector2(0f, 8f), layout, 1f);
            AddQuad(ref _verts, ref _quads, ToTarget(topLeft), ToTarget(topLeft + new Vector2(width, 0f)),
                ToTarget(topLeft + new Vector2(width, height)), ToTarget(topLeft + new Vector2(0f, height)),
                new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top),
                new Vector2(rect.Right, rect.Bottom), new Vector2(rect.Left, rect.Bottom), color, color, color, color);
        }

        // Свет точки: мир, но не темнее собственного тления ткани; дальние планы тонут в воде
        private static Color Shade(Vector2 world, KelpLayout layout, float shade)
        {
            if (_flat)
                return new Color(Vector3.Min(_flatColor * shade, Vector3.One)) * _flatAlpha;

            Vector3 light = Lighting.GetColor((int)(world.X / 16f), (int)(world.Y / 16f)).ToVector3();
            light = Vector3.Max(light, SelfLight[layout.Species]) * layout.Tint;
            int plan = layout.Plan;
            float fog = PlanFog[plan];
            Vector3 color = light * (PlanBrightness[plan] * shade * (1f - fog)) + _fog * (fog * 2.2f);
            return new Color(Vector3.Min(color, Vector3.One));
        }

        #endregion

        #region Геометрия

        // В пиксели цели от начала мира: начало цели вычитает матрица (ApplyEffect)
        private static Vector2 ToTarget(Vector2 world) => world / Pixel;

        // Точка и касательная на гладкой кривой через точки цепи (Катмулл-Ром).
        // s — px вдоль стебля от комля
        private static void Curve(Vector2[] points, float s, out Vector2 position, out Vector2 tangent)
        {
            int last = points.Length - 1;
            float f = Math.Clamp(s / SegmentLength, 0f, last - 0.0001f);
            int i = (int)f;
            float t = f - i;
            Vector2 p0 = points[Math.Max(i - 1, 0)];
            Vector2 p1 = points[i];
            Vector2 p2 = points[Math.Min(i + 1, last)];
            Vector2 p3 = points[Math.Min(i + 2, last)];

            float t2 = t * t, t3 = t2 * t;
            position = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (3f * p1 - p0 - 3f * p2 + p3) * t3);
            Vector2 derivative = 0.5f * ((p2 - p0) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t
                + 3f * (3f * p1 - p0 - 3f * p2 + p3) * t2);
            tangent = derivative.LengthSquared() > 1e-6f ? Vector2.Normalize(derivative) : -Vector2.UnitY;
        }

        private static void AddQuad(ref VertexPositionColorTexture[] buffer, ref int quads,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td,
            Color ca, Color cb, Color cc, Color cd)
        {
            int v = quads * 4;
            if (v + 4 > buffer.Length)
                Array.Resize(ref buffer, buffer.Length * 2);

            Vector2 scale = _uvScale;
            buffer[v] = new VertexPositionColorTexture(new Vector3(a, 0f), ca, ta * scale);
            buffer[v + 1] = new VertexPositionColorTexture(new Vector3(b, 0f), cb, tb * scale);
            buffer[v + 2] = new VertexPositionColorTexture(new Vector3(c, 0f), cc, tc * scale);
            buffer[v + 3] = new VertexPositionColorTexture(new Vector3(d, 0f), cd, td * scale);
            quads++;
        }

        #endregion
    }
}
