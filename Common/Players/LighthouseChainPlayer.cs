using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using SoA.Common.Systems;
using SoA.Content.Items.Placeables;
using SoA.Content.Tiles.Furniture;
using SoA.Content.Tiles.Lighthouse;
using SoA.Content.Tiles.Shrines;

namespace SoA.Common.Players
{
    // Игрок держит маячную цепь: пока зажата ПКМ, цепь тянется за курсором
    // (LighthouseChainPhysics), а луч поворачивается туда, куда её тянут, по кратчайшей
    // дуге. По горизонтали это наклон цепи: вправо — к горизонту справа, влево — слева.
    // По вертикали — натяжение вниз: цепь не растягивается, но курсор под звеном
    // в руке значит «тяну вниз», и луч опускается. Скорость — от силы натяжения,
    // так что механизм ощущается рычагом, а не прицелом. Всё это только у владельца персонажа: другим игрокам
    // приходит готовый угол через лампу
    public class LighthouseChainPlayer : ModPlayer
    {
        private const float MaxTurnPerTick = 0.05f;
        // Отклонение в эту долю от вылета звена даёт полную скорость поворота
        private const float FullTurnDeflectionShare = 0.6f;
        // Мелкое покачивание цепи в руке луч не крутит
        private const float DeadZonePx = 6f;
        // Ближе этого к развороту на 180° дуги считаются равными
        private const float TieArc = 0.05f;
        private const float GrabRangePx = 12f * 16f;   // отошёл дальше — цепь выпала из рук
        private const int SyncIntervalTicks = 6;

        private int _grabbedLampId = -1;
        private Point _chainTile;
        private int _chainTopY;     // верхнее звено той же колонки: по нему цепь ищет физика
        private int _syncCooldown;
        private bool _unsentTurn;

        public bool IsHoldingChain => _grabbedLampId != -1;

        // За какое звено цепи с верхом (x, topY) держится игрок; -1 — не за эту
        public int HeldLinkIn(int x, int topY)
            => IsHoldingChain && _chainTile.X == x && _chainTopY == topY ? _chainTile.Y - topY : -1;

        public static void ShowGrabCursor(Player player)
        {
            player.noThrow = 2;
            player.cursorItemIconEnabled = true;
            player.cursorItemIconID = ModContent.ItemType<LighthouseChain>();
        }

        public void Grab(LighthouseLampEntity lamp, Point chainTile, int chainTopY)
        {
            _grabbedLampId = lamp.ID;
            _chainTile = chainTile;
            _chainTopY = chainTopY;
            _syncCooldown = 0;
        }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer || !IsHoldingChain)
                return;

            if (!TileEntity.ByID.TryGetValue(_grabbedLampId, out TileEntity entity)
                || entity is not LighthouseLampEntity lamp)
            {
                _grabbedLampId = -1;
                return;
            }

            bool stillHolding = Main.mouseRight && !Player.dead && !Main.mapFullscreen
                && Vector2.Distance(Player.Center, _chainTile.ToWorldCoordinates()) <= GrabRangePx;
            if (!stillHolding)
            {
                Release(lamp);
                return;
            }

            ShowGrabCursor(Player);

            float turn = TurnFromChainPull();
            if (turn != 0f)
            {
                lamp.Angle = MathHelper.WrapAngle(lamp.BeamAngle + turn);
                lamp.DisplayAngle = lamp.Angle;
                _unsentTurn = true;
            }

            if (--_syncCooldown <= 0 && _unsentTurn)
            {
                lamp.SendState();
                _unsentTurn = false;
                _syncCooldown = SyncIntervalTicks;
            }
        }

        // Поворот за тик по отклонению звена под рукой. Вылет звена — его расстояние
        // от крепления по цепи: длинная цепь отводится дальше, и полная скорость
        // у неё набирается на большем размахе
        private float TurnFromChainPull()
        {
            int heldPoint = _chainTile.Y - _chainTopY + 1;
            if (!LighthouseChainPhysics.TryGetPoint(_chainTile.X, _chainTopY, heldPoint, out Vector2 held, out Vector2 anchor))
                return 0f;

            // Вбок — насколько отведена сама цепь; вниз — насколько курсор тянет
            // ниже звена в руке, куда цепь уже не пускает
            var pull = new Vector2(held.X - anchor.X, Main.MouseWorld.Y - held.Y);
            if (pull.Length() < DeadZonePx)
                return 0f;

            float reach = heldPoint * LighthouseChainPhysics.LinkLength * FullTurnDeflectionShare;
            float strength = MathHelper.Clamp(pull.Length() / reach, 0f, 1f);

            float current = CurrentBeamAngle(_grabbedLampId);
            float arc = MathHelper.WrapAngle(pull.ToRotation() - current);

            // Луч смотрит ровно в другую сторону — обе дуги равны, и знак дуги
            // дрожал бы от тика к тику. Такой разворот идёт через верх, над морем
            if (System.Math.Abs(arc) > MathHelper.Pi - TieArc)
                arc = System.Math.Sign(MathHelper.WrapAngle(-MathHelper.PiOver2 - current)) * System.Math.Abs(arc);

            float step = strength * MaxTurnPerTick;
            return System.Math.Abs(arc) <= step ? arc : System.Math.Sign(arc) * step;
        }

        private static float CurrentBeamAngle(int lampId)
            => TileEntity.ByID.TryGetValue(lampId, out TileEntity entity) && entity is LighthouseLampEntity lamp
                ? lamp.BeamAngle
                : 0f;

        // Последний угол уходит всегда: иначе у остальных луч застынет
        // там, где его застал предыдущий пакет
        private void Release(LighthouseLampEntity lamp)
        {
            if (_unsentTurn)
                lamp.SendState();

            _unsentTurn = false;
            _grabbedLampId = -1;
        }
    }
}
