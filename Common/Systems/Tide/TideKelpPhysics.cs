using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using SoA.Content.Tiles.Nature;

namespace SoA.Common.Systems
{
    // Физика стеблей приливной флоры в воде. Стебель — цепь сегментов, у каждого свой
    // угол от вертикали; сегмент стоит на верхушке нижнего, так что поворот любого
    // уносит всё, что выше. Растение ведёт себя как водоросль, а не как пружина:
    //   • плавучесть тянет каждый сегмент вверх, вода гасит вращение сильнее
    //     критического — стебель отводится и плавно встаёт обратно, без отскока;
    //   • гладкость держит выравнивание по соседям без инерции: изгиб расходится
    //     по стеблю дугой, а изломам неоткуда взять энергию и затрястись;
    //   • течение колышет лес волной, общей для соседних стеблей;
    //   • пловец поворачивает сегменты, которые задевает, — по их настоящему положению,
    //     а не по тайлу, так что отогнутый стебель задевается там, где нарисован.
    //     Соседние сегменты тянутся следом — гнётся всё растение.
    // Подбиралось прогоном модели: касание в середине ели уводит верхушку ~50 px,
    // возврат за 3-4 секунды, перелёта на другую сторону нет.
    //
    // Чисто клиентское: считается только для стеблей в кадре, по сети ничего не шлётся.
    // Цепи стеблей читает KelpForestRenderer: по ним он натягивает меши стеблей и листьев
    public sealed class TideKelpPhysics : ModSystem
    {
        private const float SegmentLength = 16f;
        private const int ScanPaddingTiles = 8;         // стебли за краем экрана тоже живут (лист длиннее тайла)
        private const int MaxStrandSegments = 160;      // предел обхода вверх и вниз по колонке
        private const int ForgetAfterTicks = 120;       // ушёл с экрана — состояние выбрасывается

        private const float WaterDrag = 0.3f;           // гашение вращения за тик: выше критического
        private const float MaxSpin = 0.2f;             // рад за тик
        private const float MaxAngle = 1.3f;            // сегмент не ложится и не проворачивается
        private const int SmoothingPasses = 3;

        // Течение: две волны по миру, соседние стебли колышутся согласованно
        private const float CurrentStrength = 0.00015f;
        private const float CurrentSlowSpeed = 0.011f;
        private const float CurrentFastSpeed = 0.027f;

        // Пловец: зона влияния — хитбокс с запасом. Сегмент внутри неё разворачивается
        // так, чтобы его середина шла за пловцом
        private const float InfluencePaddingPx = 10f;
        private const float EntrainRate = 0.25f;        // доля разницы скоростей за тик
        private const float MinDisturberSpeed = 0.3f;

        public sealed class Strand
        {
            public int X;
            public int BaseY;
            public int Height;
            public int Species;
            public int LastSeen;
            public float[] Angle = Array.Empty<float>();          // угол сегмента k от вертикали
            public float[] Spin = Array.Empty<float>();           // его угловая скорость
            public Vector2[] Points = Array.Empty<Vector2>();     // Height + 1 точек, 0 — комель
            public Rectangle Bounds;                               // охват точек, для отсева пловцов

            public long Key => KeyOf(X, BaseY);

            public void ResetToRest(int height)
            {
                Height = height;
                Angle = new float[height];
                Spin = new float[height];
                Points = new Vector2[height + 1];
            }
        }

        private static readonly Dictionary<long, Strand> _strands = new();
        private static readonly Dictionary<int, List<Strand>> _byColumn = new();
        private static readonly List<long> _forgotten = new();
        private static int _tick;
        private static bool _swaying;

        public static long KeyOf(int x, int baseY) => ((long)x << 32) | (uint)baseY;

        // Стебли, найденные в последнем обходе кадра (с запасом за краем экрана)
        public static void CollectVisible(List<Strand> into)
        {
            into.Clear();
            foreach (Strand strand in _strands.Values)
            {
                if (strand.LastSeen == _tick)
                    into.Add(strand);
            }
        }

        public override void OnWorldUnload() => Clear();

        public override void Unload() => Clear();

        private static void Clear()
        {
            _strands.Clear();
            _byColumn.Clear();
            _tick = 0;
        }

        // Середина нижней грани комля
        private static Vector2 RootOf(int x, int baseY) => new(x * 16 + 8, baseY * 16 + 16);

        // Поза сегмента (i, j): точка его опоры в мире и поворот
        public static bool TryGetPose(int i, int j, out Vector2 anchor, out float rotation)
        {
            anchor = Vector2.Zero;
            rotation = 0f;
            if (!_byColumn.TryGetValue(i, out List<Strand> column))
                return false;

            foreach (Strand strand in column)
            {
                int index = strand.BaseY - j;
                if (index < 0 || index >= strand.Height)
                    continue;

                anchor = strand.Points[index];
                rotation = strand.Angle[index];
                return true;
            }
            return false;
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            // Настройка «растения качаются» выключена — стебли встают прямо и не качаются,
            // но обход идёт: по цепям стеблей их рисует KelpForestRenderer
            bool sway = Main.SettingsEnabled_TilesSwayInWind;
            if (_swaying && !sway)
                Clear();
            _swaying = sway;

            _tick++;
            ScanVisibleStrands();

            if (sway)
            {
                IReadOnlyList<WaterDisturber> disturbers = WaterDisturbers.Get();
                foreach (Strand strand in _strands.Values)
                {
                    if (strand.LastSeen == _tick)
                        Simulate(strand, disturbers);
                }
            }

            ForgetOffscreen();
        }

        #region Поиск стеблей

        // Колонки экрана сверху вниз: первый сегмент стебля в кадре — от него вниз до комля
        // и вверх до верхушки, даже если они за краем. Стебель целиком нужен физике:
        // верхушка за кадром тоже колышется и тянет видимую часть
        private static void ScanVisibleStrands()
        {
            foreach (List<Strand> column in _byColumn.Values)
                column.Clear();

            int kelpType = ModContent.TileType<Tidekelp_tile>();
            int left = Math.Max((int)(Main.screenPosition.X / 16f) - ScanPaddingTiles, 1);
            int top = Math.Max((int)(Main.screenPosition.Y / 16f) - ScanPaddingTiles, 1);
            int right = Math.Min(left + Main.screenWidth / 16 + ScanPaddingTiles * 2, Main.maxTilesX - 2);
            int bottom = Math.Min(top + Main.screenHeight / 16 + ScanPaddingTiles * 2, Main.maxTilesY - 2);

            for (int x = left; x <= right; x++)
            {
                for (int y = top; y <= bottom; y++)
                {
                    if (!IsKelp(x, y, kelpType))
                        continue;

                    int baseY = y;
                    while (baseY - y < MaxStrandSegments && baseY + 1 < Main.maxTilesY - 1 && IsKelp(x, baseY + 1, kelpType))
                        baseY++;

                    int crownY = y;
                    while (baseY - crownY < MaxStrandSegments && crownY - 1 > 0 && IsKelp(x, crownY - 1, kelpType))
                        crownY--;

                    RegisterStrand(x, baseY, baseY - crownY + 1);
                    y = baseY + 1; // ниже комля — грунт, следующий стебель начнётся после него
                }
            }
        }

        private static bool IsKelp(int x, int y, int kelpType)
        {
            Tile tile = Main.tile[x, y];
            return tile.HasTile && tile.TileType == kelpType;
        }

        private static void RegisterStrand(int x, int baseY, int height)
        {
            long key = KeyOf(x, baseY);
            if (!_strands.TryGetValue(key, out Strand strand))
            {
                strand = new Strand { X = x, BaseY = baseY };
                _strands[key] = strand;
            }

            // Стебель срезали или он дорос — встаёт заново из покоя
            if (strand.Height != height)
            {
                strand.ResetToRest(height);
                FinishPose(strand);
            }

            strand.Species = Tidekelp_tile.SpeciesAt(Main.tile[x, baseY]);
            strand.LastSeen = _tick;

            if (!_byColumn.TryGetValue(x, out List<Strand> column))
            {
                column = new List<Strand>(2);
                _byColumn[x] = column;
            }
            column.Add(strand);
        }

        private static void ForgetOffscreen()
        {
            _forgotten.Clear();
            foreach (KeyValuePair<long, Strand> pair in _strands)
            {
                if (_tick - pair.Value.LastSeen > ForgetAfterTicks)
                    _forgotten.Add(pair.Key);
            }
            foreach (long key in _forgotten)
                _strands.Remove(key);
        }

        #endregion

        #region Физика

        private static void Simulate(Strand strand, IReadOnlyList<WaterDisturber> disturbers)
        {
            Tidekelp_tile.SpeciesTraits traits = Tidekelp_tile.Traits[strand.Species];
            float[] angle = strand.Angle;
            float[] spin = strand.Spin;

            // Плавучесть тянет сегмент к вертикали, вода гасит вращение, течение клонит
            for (int k = 0; k < strand.Height; k++)
            {
                Vector2 middle = (strand.Points[k] + strand.Points[k + 1]) / 2f;
                float current = CurrentAt(middle) * traits.CurrentResponse;
                spin[k] += -traits.Buoyancy * MathF.Sin(angle[k]) - WaterDrag * spin[k] + current;
            }

            ApplyDisturbers(strand, traits, disturbers);

            for (int k = 0; k < strand.Height; k++)
            {
                spin[k] = MathHelper.Clamp(spin[k], -MaxSpin, MaxSpin);
                angle[k] = MathHelper.Clamp(angle[k] + spin[k], -MaxAngle, MaxAngle);
            }

            SmoothBend(angle, traits.BendStiffness);
            FinishPose(strand);
        }

        // Течение в точке мира: медленная длинная волна и быстрая короткая поверх
        private static float CurrentAt(Vector2 world)
        {
            float time = Main.GameUpdateCount;
            float slow = MathF.Sin(time * CurrentSlowSpeed + world.X * 0.0021f + world.Y * 0.0013f);
            float fast = MathF.Sin(time * CurrentFastSpeed - world.X * 0.0047f + world.Y * 0.0031f);
            return (slow + 0.4f * fast) * CurrentStrength;
        }

        // Сегмент в зоне пловца разворачивается так, чтобы его середина шла со скоростью
        // пловца поперёк стебля. Меняется угловая скорость, а не угол — без рывков
        private static void ApplyDisturbers(Strand strand, Tidekelp_tile.SpeciesTraits traits,
            IReadOnlyList<WaterDisturber> disturbers)
        {
            const float middleArm = SegmentLength / 2f;

            foreach (WaterDisturber disturber in disturbers)
            {
                if (disturber.Velocity.Length() < MinDisturberSpeed)
                    continue;

                Rectangle zone = disturber.Hitbox;
                zone.Inflate((int)InfluencePaddingPx, (int)InfluencePaddingPx);
                if (!zone.Intersects(strand.Bounds))
                    continue;

                Vector2 center = disturber.Center;
                Vector2 halfSize = new(zone.Width / 2f, zone.Height / 2f);

                for (int k = 0; k < strand.Height; k++)
                {
                    // Эллипс, вписанный в зону: 0 в центре, 1 на краю
                    Vector2 middle = (strand.Points[k] + strand.Points[k + 1]) / 2f;
                    float distance = ((middle - center) / halfSize).Length();
                    if (distance >= 1f)
                        continue;

                    // Поперёк сегмента: вдоль стебля пловец его не поворачивает
                    Vector2 across = new(MathF.Cos(strand.Angle[k]), MathF.Sin(strand.Angle[k]));
                    float targetSpin = Vector2.Dot(disturber.Velocity, across) / middleArm;
                    float influence = (1f - distance) * traits.Pliancy;
                    strand.Spin[k] += (targetSpin - strand.Spin[k]) * EntrainRate * influence;
                }
            }
        }

        // Каждый сегмент подтягивается к среднему соседей (комель — к вертикали грунта,
        // верхушка — к нижнему). Без инерции: изгиб расходится плавной дугой и не звенит
        private static void SmoothBend(float[] angle, float stiffness)
        {
            int last = angle.Length - 1;
            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                for (int k = 0; k <= last; k++)
                {
                    float below = k > 0 ? angle[k - 1] : 0f;
                    float target = k < last ? (below + angle[k + 1]) * 0.5f : below;
                    angle[k] += (target - angle[k]) * stiffness;
                }
            }
        }

        // Точки цепи из углов и охват для отсева пловцов
        private static void FinishPose(Strand strand)
        {
            Vector2[] points = strand.Points;
            points[0] = RootOf(strand.X, strand.BaseY);
            float minX = points[0].X, maxX = minX, minY = points[0].Y, maxY = minY;

            for (int k = 0; k < strand.Height; k++)
            {
                float a = strand.Angle[k];
                Vector2 p = points[k] + new Vector2(MathF.Sin(a), -MathF.Cos(a)) * SegmentLength;
                points[k + 1] = p;

                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            strand.Bounds = new Rectangle((int)minX - 8, (int)minY - 8, (int)(maxX - minX) + 16, (int)(maxY - minY) + 16);
        }

        #endregion
    }
}