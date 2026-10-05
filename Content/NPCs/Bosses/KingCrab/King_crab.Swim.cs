using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ПЛАВАНИЕ — как Latcher из Barotrauma. Раньше в воде на короля действовала полная
    // гравитация: он тонул и бродил по дну. Теперь, когда вода доходит выше середины туши,
    // он плывёт гребками: туша разворачивается короной по ходу движения, лапы широко
    // раскрываются вперёд и мощно загребают назад, смыкаясь за телом, — туша рвётся вперёд
    // и скользит, пока лапы медленно раскрываются для следующего гребка.
    // В воде подводные блоки ему не помеха — проплывает сквозь (но дна держится), над водой
    // всплывает «по шею». Игрок рядом на берегу — выпрыгивает к нему. Бьёт в воде только
    // тем, что там имеет смысл: залп пузырей, захлоп (выпад прямо к игроку), рёв, прилив.
    // «Плывёт» считается из позиции на каждой машине одинаково — по сети не шлём.
    public partial class King_crab
    {
        private const float SwimEnterDepth = 40f;      // вода выше центра на столько — поплыл
        private const float SwimRideDepth = 60f;       // центр туши под поверхностью, выше не всплывает

        // Гребок и скольжение. Тяга не мгновенная, а растянута на загрёб по мягкой кривой —
        // скорость нарастает и спадает плавно. В среднем ~6 px/тик в начале боя и ~10 к концу
        private const float SwimStrokeTicksFullHealth = 40f;
        private const float SwimStrokeTicksNearDeath = 28f;
        private const float SwimThrustFullHealth = 8.4f;  // суммарный прирост скорости за гребок
        private const float SwimThrustNearDeath = 9.8f;
        private const float SwimSteer = 0.12f;            // в загрёбе скорость доворачивает к цели
        private const float SwimGlideDrag = 0.965f;       // вода гасит скольжение медленно — оно длинное
        private const float SwimMaxSpeed = 16f;
        private const float SwimKickStretch = -0.16f;     // туша вытягивается в гребке

        // Разворот туши по ходу: короной вперёд. Головой вниз — не дальше ~137°
        private const float SwimMaxTilt = 2.4f;
        private const float SwimTurnRate = 0.12f;
        private const float SwimUprightRate = 0.06f;   // завис у игрока — выпрямляется
        private const float SwimLeaveUprightRate = 0.2f; // атака или вышел из воды — выпрямляется быстро
        private const float SwimTurnMinSpeed = 1.5f;

        // Гребок лап в осях туши (−Y — к короне), px ноги до LegScale.
        // Загрёб: лапы сметаются назад и смыкаются; медленное раскрытие — вперёд и вширь
        private const float SwimPowerShare = 0.35f;    // доля цикла на загрёб — на нём же идёт тяга
        private const float SwimLegWave = 0.03f;       // задняя пара отстаёт — гребок идёт волной

        private const float SwimSink = 0.06f;          // в атаках и оглушении медленно тонет
        private const float SwimDrag = 0.92f;
        private const float SwimMaxSink = 2.5f;

        private const float SwimLeapRange = 260f;      // игрок на берегу ближе этого — выпрыгивает к нему
        private const float SwimLeapSpeed = 12f;
        private const float SwimLeapForward = 7f;
        private const int SwimUnstickSteps = 12;       // вышел из воды внутри берега — выталкиваем вверх по 8 px

        private bool _swimming;
        private bool _swimLeap;        // выпрыгивает на берег: на подъёме сквозь кромку, ловит грунт на спуске
        private float _swimStrokeTimer;
        private int _swimKickAge = 999; // тиков с последнего толчка — для лап
        private Vector2 _swimKickDir = Vector2.UnitX;

        private float SwimStrokeTicks => MathHelper.Lerp(SwimStrokeTicksFullHealth, SwimStrokeTicksNearDeath, Fury);

        // Туша плывёт короной вперёд — разворот только в стойке, атаки он делает выпрямившись
        private bool SwimOriented => _swimming && State == CrabState.Scuttle;

        // 0 — сразу после толчка, 1 — к следующему
        private float SwimStrokePhase => MathHelper.Clamp(_swimKickAge / SwimStrokeTicks, 0f, 1f);

        // Начало тика AI. Гистерезис: поплыл, когда вода выше середины туши, перестал —
        // когда в воде не осталось ни одной части туши
        private void UpdateSwimming()
        {
            _swimKickAge++;

            UpdateSwimTilt();

            if (_swimLeap)
            {
                SwimLeapTick();
                return;
            }

            // Подкоп, гвардия, свита и сцены сами ведут столкновения — плавание туда не лезет
            bool ownState = State is CrabState.Burrow or CrabState.KnightCourt or CrabState.CourtDuel
                or CrabState.Dying or CrabState.Intro;
            if (ownState || (!_swimming && NPC.noTileCollide))
            {
                _swimming = false;
                return;
            }

            bool was = _swimming;
            _swimming = was ? BodyInWater() : WaterAt(NPC.Center - new Vector2(0f, SwimEnterDepth));

            if (_swimming)
            {
                NPC.noTileCollide = true; // подводные блоки — насквозь
                if (!Main.dedServ && EveryTicks(6))
                {
                    Dust bubble = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.BreatheBubble);
                    bubble.velocity = new Vector2(Main.rand.NextFloatDirection() * 0.4f, -Main.rand.NextFloat(0.5f, 1.5f));
                    bubble.noGravity = true;
                }
            }
            else if (was)
            {
                NPC.noTileCollide = false;
                UnstickFromTiles();
            }
        }

        private static bool WaterAt(Vector2 world)
        {
            Tile tile = Framing.GetTileSafely(world.ToTileCoordinates());
            if (tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                return false;
            return tile.LiquidAmount > 100 && tile.LiquidType == LiquidID.Water;
        }

        // Хоть одна часть туши в воде. Проплывая сквозь блок, центр может оказаться в камне —
        // из-за этого плавание не должно обрываться посреди скалы
        private bool BodyInWater()
        {
            float w = NPC.width * 0.3f;
            return WaterAt(NPC.Center) || WaterAt(NPC.Center + new Vector2(-w, 0f)) || WaterAt(NPC.Center + new Vector2(w, 0f))
                || WaterAt(NPC.Center - new Vector2(0f, SwimEnterDepth)) || WaterAt(NPC.Bottom - new Vector2(0f, 20f));
        }

        // Под ногами дно: тонуть и толкаться вниз дальше некуда
        private bool OnSeabed()
        {
            Tile tile = Framing.GetTileSafely((NPC.Bottom + new Vector2(0f, 4f)).ToTileCoordinates());
            return tile.HasUnactuatedTile && Main.tileSolid[tile.TileType];
        }

        private void UnstickFromTiles()
        {
            for (int i = 0; i < SwimUnstickSteps && Collision.SolidCollision(NPC.position, NPC.width, NPC.height); i++)
                NPC.position.Y -= 8f;
        }

        // Поверхность воды над точкой: идём вверх, пока есть вода
        private static float WaterSurfaceY(Vector2 world)
        {
            Point p = world.ToTileCoordinates();
            for (int i = 0; i < 80 && WaterAt(new Vector2(p.X * 16f + 8f, (p.Y - 1) * 16f + 8f)); i++)
                p.Y--;
            return p.Y * 16f;
        }

        // Вместо гравитации в атаках и оглушении: вязкость и медленное погружение до дна
        private void WaterDrift()
        {
            NPC.velocity.X *= SwimDrag;
            NPC.velocity.Y = MathHelper.Clamp(NPC.velocity.Y * SwimDrag + SwimSink, -SwimMaxSpeed, SwimMaxSink);
            if (OnSeabed() && NPC.velocity.Y > 0f)
                NPC.velocity.Y = 0f;
        }

        // Стойка в воде: толчок к игроку → скольжение → толчок. Не всплывает выше «по шею»
        private void SwimToward(Player target)
        {
            float surface = WaterSurfaceY(NPC.Center);
            Vector2 goal = target.Center;
            goal.Y = Math.Max(goal.Y, surface + SwimRideDepth);
            Vector2 to = goal - NPC.Center;

            NPC.velocity *= SwimGlideDrag;
            SwimThrust(to);
            if (OnSeabed() && NPC.velocity.Y > 0f)
                NPC.velocity.Y = 0f;

            if (--_swimStrokeTimer <= 0f && to.Length() > KeepDistance)
                SwimKick(to);

            // Игрок рядом на берегу, король у поверхности — выпрыгивает к нему
            bool targetAshore = target.Bottom.Y < surface - 8f;
            bool atSurface = NPC.Center.Y < surface + SwimRideDepth + 40f;
            if (targetAshore && atSurface && Math.Abs(to.X) < SwimLeapRange && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int dir = Math.Sign(to.X);
                NPC.velocity = new Vector2((dir == 0 ? NPC.spriteDirection : dir) * SwimLeapForward, -SwimLeapSpeed);
                _swimming = false;
                _swimLeap = true;
                NPC.netUpdate = true;
            }
        }

        // Начало гребка: лапы пошли назад. Сама тяга — в SwimThrust, на всём загрёбе
        private void SwimKick(Vector2 toGoal)
        {
            _swimKickDir = toGoal.SafeNormalize(Vector2.UnitX);
            _swimStrokeTimer = SwimStrokeTicks;
            _swimKickAge = 0;

            if (Main.dedServ)
                return;
            // Туша вытягивается в гребке, из-под лап — облако пузырей назад
            _squashImpact = SwimKickStretch;
            _squashImpactVel = 0f;
            for (int i = 0; i < 8; i++)
            {
                Dust bubble = Dust.NewDustPerfect(NPC.Center - _swimKickDir * NPC.width * 0.4f
                        + Main.rand.NextVector2Circular(40f, 30f), DustID.BreatheBubble,
                    -_swimKickDir * Main.rand.NextFloat(1f, 3f));
                bubble.noGravity = true;
            }
            SoundEngine.PlaySound(SoundID.SplashWeak with { Pitch = -0.5f, Volume = 0.45f }, NPC.Center);
        }

        // Тяга загрёба колоколом (sin): разгон мягкий, пик посередине, к концу затухает.
        // Направление по ходу доворачивает к цели — король плывёт дугой, а не ломаной
        private void SwimThrust(Vector2 toGoal)
        {
            float powerTicks = SwimStrokeTicks * SwimPowerShare;
            if (_swimKickAge >= powerTicks)
                return;

            _swimKickDir = Vector2.Lerp(_swimKickDir, toGoal.SafeNormalize(_swimKickDir), SwimSteer).SafeNormalize(Vector2.UnitX);
            float total = MathHelper.Lerp(SwimThrustFullHealth, SwimThrustNearDeath, Fury);
            float bell = (float)Math.Sin(MathHelper.Pi * (_swimKickAge + 0.5f) / powerTicks);
            NPC.velocity += _swimKickDir * total * bell * MathHelper.Pi / (2f * powerTicks); // интеграл колокола = total
            if (NPC.velocity.Length() > SwimMaxSpeed)
                NPC.velocity = NPC.velocity.SafeNormalize(Vector2.Zero) * SwimMaxSpeed;
        }

        // Прыжок на берег: на подъёме сквозь кромку (иначе застрял бы в ней), ловит грунт на спуске.
        // Гравитацию ведём сами: ApplyGravity при noTileCollide не работает
        private void SwimLeapTick()
        {
            if (NPC.velocity.Y < 0f)
            {
                NPC.noTileCollide = true;
                NPC.velocity.Y += Gravity;
                return;
            }
            _swimLeap = false;
            NPC.noTileCollide = false;
            UnstickFromTiles();
        }

        // Разворот туши короной по ходу движения: верх туши — вдоль скорости
        private void UpdateSwimTilt()
        {
            if (!SwimOriented)
            {
                NPC.rotation = NPC.rotation.AngleLerp(0f, SwimLeaveUprightRate);
                if (Math.Abs(NPC.rotation) < 0.01f)
                    NPC.rotation = 0f;
                return;
            }

            if (NPC.velocity.Length() < SwimTurnMinSpeed)
            {
                NPC.rotation = NPC.rotation.AngleLerp(0f, SwimUprightRate);
                return;
            }
            float target = MathHelper.Clamp(MathHelper.WrapAngle(NPC.velocity.ToRotation() + MathHelper.PiOver2),
                -SwimMaxTilt, SwimMaxTilt);
            NPC.rotation = NPC.rotation.AngleLerp(target, SwimTurnRate);
        }

        // Стопа ноги в гребке: смещение от бедра в осях туши, scale — масштаб ноги
        private Vector2 SwimLegStroke(int i, int sign, float scale)
        {
            Vector2 open = new(sign * (52f + 6f * i), -22f + 12f * i);   // вперёд и вширь
            Vector2 closed = new(sign * (10f + 3f * i), 70f + 4f * i);   // назад, сомкнуты за телом

            float phase = SwimStrokePhase - i * SwimLegWave;
            Vector2 local;
            if (phase < SwimPowerShare)
            {
                // Загрёб плавный: та же мягкая кривая, что и тяга
                float t = MathHelper.SmoothStep(0f, 1f, MathHelper.Clamp(phase / SwimPowerShare, 0f, 1f));
                local = Vector2.Lerp(open, closed, t);
            }
            else
            {
                float t = MathHelper.SmoothStep(0f, 1f, (phase - SwimPowerShare) / (1f - SwimPowerShare));
                local = Vector2.Lerp(closed, open, t)
                    + new Vector2(sign * 26f * (float)Math.Sin(t * MathHelper.Pi), 0f); // раскрывается по дуге, вширь
            }
            return (local * scale).RotatedBy(AnimatedBodyRotation());
        }

        // Что из арсенала годится в воде: удары по грунту и подкоп — нет
        private static bool IsSwimAttack(CrabState attack) => attack
            is CrabState.BubbleVolley or CrabState.CrushingGrip or CrabState.RoyalRoar or CrabState.TideCall;
    }
}
