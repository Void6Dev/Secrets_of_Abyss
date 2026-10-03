using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using SoA.Common.Players;

namespace SoA.Common.Systems
{
    // Физика маячной цепи: верлет-верёвка из звеньев по 16 px, верх закреплён под
    // потолком. Пока игрок держит цепь, звено под рукой тянется к курсору, а всё,
    // что ниже, висит на нём и раскачивается. Чем длиннее цепь, тем сильнее она
    // отстаёт и тем шире размах — тяжёлый механизм, а не жёсткий рычаг.
    // Отпустил — цепь качается под своим весом и успокаивается.
    //
    // Для ламинарии верлет не годился (изломы бегали по стеблю секундами), но там
    // стебель держится снизу и всплывает. Цепь висит, и маятник — это ровно то,
    // как она должна себя вести.
    //
    // Сквозь блоки цепь не проходит: точка, попавшая в твёрдый тайл, возвращается
    // назад, сначала по одной оси — так цепь скользит по стене, а не залипает в ней.
    // Платформы пропускают: на них цепь не ложится, как и ванильные верёвки.
    //
    // Чисто клиентское: живут только цепи, которые рисовались недавно
    public sealed class LighthouseChainPhysics : ModSystem
    {
        public const float LinkLength = 16f;

        private const float Gravity = 0.35f;
        private const float Damping = 0.985f;          // доля скорости, что остаётся за тик
        private const float PullStiffness = 0.3f;      // доля пути к курсору за тик для звена под рукой
        // Рука не вытягивает цепь в струну: звено под рукой не уходит от крепления
        // дальше этой доли длины цепи до него. Остаток провисает дугой под весом —
        // при сильном рывке цепь гнётся, а не превращается в палку
        private const float MaxTautShare = 0.93f;
        private const int ConstraintPasses = 14;
        private const int ForgetAfterTicks = 120;
        private const int PointRadius = 3;             // точка — квадрат 6x6 px: тоньше звено не бывает
        private const float WallFriction = 0.6f;       // доля скорости вдоль стены, что остаётся после касания

        public sealed class Rope
        {
            public Vector2[] Points;     // LinkCount + 1 точек, 0 — крепление
            public Vector2[] Previous;
            public int LastSeen;
        }

        private static readonly Dictionary<long, Rope> _ropes = new();
        private static readonly List<long> _forgotten = new();
        private static int _tick;

        public override void OnWorldUnload() => _ropes.Clear();

        public override void Unload() => _ropes.Clear();

        private static long Key(int x, int topY) => ((long)x << 32) | (uint)topY;

        // Верёвка для цепи с верхним звеном (x, topY). Новая или сменившая длину
        // встаёт в покой — отвесно вниз
        public static Rope Get(int x, int topY, int linkCount)
        {
            long key = Key(x, topY);
            if (!_ropes.TryGetValue(key, out Rope rope) || rope.Points.Length != linkCount + 1)
            {
                rope = new Rope
                {
                    Points = new Vector2[linkCount + 1],
                    Previous = new Vector2[linkCount + 1]
                };
                Vector2 anchor = Anchor(x, topY);
                for (int k = 0; k <= linkCount; k++)
                {
                    rope.Points[k] = anchor + new Vector2(0f, k * LinkLength);
                    rope.Previous[k] = rope.Points[k];
                }
                _ropes[key] = rope;
            }

            rope.LastSeen = _tick;
            return rope;
        }

        // Где сейчас точка point цепи с верхом (x, topY) и её крепление.
        // false — этой цепи физика сейчас не ведёт
        public static bool TryGetPoint(int x, int topY, int point, out Vector2 position, out Vector2 anchor)
        {
            position = anchor = Vector2.Zero;
            if (!_ropes.TryGetValue(Key(x, topY), out Rope rope) || point <= 0 || point >= rope.Points.Length)
                return false;

            position = rope.Points[point];
            anchor = rope.Points[0];
            return true;
        }

        private static Vector2 Anchor(int x, int topY) => new(x * 16f + 8f, topY * 16f);

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            _tick++;
            var holder = Main.LocalPlayer.GetModPlayer<LighthouseChainPlayer>();

            _forgotten.Clear();
            foreach ((long key, Rope rope) in _ropes)
            {
                if (_tick - rope.LastSeen > ForgetAfterTicks)
                {
                    _forgotten.Add(key);
                    continue;
                }

                int x = (int)(key >> 32);
                int topY = (int)(key & 0xFFFFFFFF);
                int heldLink = holder.HeldLinkIn(x, topY);
                Step(rope, Anchor(x, topY), heldLink);
            }

            foreach (long key in _forgotten)
                _ropes.Remove(key);
        }

        private static void Step(Rope rope, Vector2 anchor, int heldLink)
        {
            Vector2[] points = rope.Points;
            Vector2[] previous = rope.Previous;

            for (int k = 1; k < points.Length; k++)
            {
                Vector2 velocity = (points[k] - previous[k]) * Damping;
                previous[k] = points[k];
                points[k] += velocity + new Vector2(0f, Gravity);
            }

            // Звено под рукой — конец этого звена, то есть точка heldLink + 1
            int heldPoint = heldLink + 1;
            if (heldLink >= 0 && heldPoint < points.Length)
                points[heldPoint] = Vector2.Lerp(points[heldPoint], PullTarget(anchor, heldPoint), PullStiffness);

            for (int pass = 0; pass < ConstraintPasses; pass++)
            {
                points[0] = anchor;
                for (int k = 0; k < points.Length - 1; k++)
                {
                    Vector2 delta = points[k + 1] - points[k];
                    float distance = delta.Length();
                    if (distance < 0.001f)
                        continue;

                    Vector2 correction = delta * ((distance - LinkLength) / distance);
                    if (k == 0)
                    {
                        points[k + 1] -= correction;
                    }
                    else
                    {
                        points[k] += correction * 0.5f;
                        points[k + 1] -= correction * 0.5f;
                    }
                }

                // Внутри каждого прохода: иначе следующий проход длин снова
                // протолкнул бы звено в стену
                for (int k = 1; k < points.Length; k++)
                    PushOutOfTiles(ref points[k], ref previous[k]);
            }
            points[0] = anchor;
        }

        // Курсор, но не дальше, чем позволяет запас на провис
        private static Vector2 PullTarget(Vector2 anchor, int heldPoint)
        {
            Vector2 offset = Main.MouseWorld - anchor;
            float maxReach = heldPoint * LinkLength * MaxTautShare;
            float distance = offset.Length();
            return distance <= maxReach ? Main.MouseWorld : anchor + offset * (maxReach / distance);
        }

        // previous — где точка была в начале тика, там стены точно не было
        private static void PushOutOfTiles(ref Vector2 point, ref Vector2 previous)
        {
            if (!InsideTiles(point))
                return;

            // Упёрлась в пол или потолок — скользит по горизонтали, в стену — по вертикали;
            // трение гасит скорость вдоль поверхности
            var keepHorizontal = new Vector2(point.X, previous.Y);
            var keepVertical = new Vector2(previous.X, point.Y);

            if (!InsideTiles(keepHorizontal))
            {
                point = keepHorizontal;
                previous.X = point.X - (point.X - previous.X) * WallFriction;
            }
            else if (!InsideTiles(keepVertical))
            {
                point = keepVertical;
                previous.Y = point.Y - (point.Y - previous.Y) * WallFriction;
            }
            else
            {
                point = previous;
            }
        }

        private static bool InsideTiles(Vector2 point)
            => Collision.SolidCollision(point - new Vector2(PointRadius), PointRadius * 2, PointRadius * 2);
    }
}
