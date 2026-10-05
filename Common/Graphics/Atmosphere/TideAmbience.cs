using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using SoA.Common.Graphics.Particles;
using SoA.Common.Systems;
using SoA.Common.Utils;
using SoA.Content.Biomes;

namespace SoA.Common.Graphics.Atmosphere
{
    // То, что плавает в толще воды Прилива Теней вокруг камеры:
    //   • Snow     — морской снег: медленно тонет, покачивается, освещён миром;
    //   • Plankton — биолюминесценция: едва тлеет, вспыхивает и расступается, когда сквозь
    //                неё плывут игрок или существа. Сильно вспыхнувший чуть светит на мир;
    //   • Eye      — пара красных точек в темноте Разлома: моргает, гаснет, если подплыть;
    //   • Wake     — светящийся след за пловцами: ночью и в тёмной глубине всё, что движется
    //                в воде, оставляет за собой искры, которые разгораются и медленно гаснут;
    //                удар по существу в воде вспыхивает облаком таких искр (Flash);
    //   • источники пузырей на дне — струйки, которые бьют какое-то время и затихают.
    // Плотность каждого вида берётся из профиля TideAtmosphere по глубине игрока.
    //
    // Отдельный пул, а не SoAParticles: фоновых частиц много и живут они долго —
    // в общем пуле они съели бы место под вспышки боя. Только клиент
    public class TideAmbience : ModSystem
    {
        private const int MaxMotes = 900;
        private const int MaxVents = 5;
        private const int MaxDisturbers = 32;
        private const int MaxPlanktonLights = 40;
        private const float ReferenceScreenArea = 1920f * 1080f;
        private const float SpawnMarginPx = 48f;        // спавн чуть за краем видимого
        private const float DespawnMarginPx = 220f;     // дальше этого за краем — удаляется сразу
        private const int FadeInTicks = 90;
        private const int FadeOutTicks = 40;
        private const int QuickFadeTicks = 12;          // упёрся в грунт или вышел из воды

        private const int SnowSpawnPerTick = 6;
        private const float SnowSway = 0.22f;           // размах покачивания, px за тик
        private const float SnowMinBrightness = 0.10f;  // в полной темноте снег едва виден
        private static readonly Color SnowColor = new(196, 214, 232);

        private const int PlanktonSpawnPerTick = 5;
        private const float PlanktonIdleGlow = 0.16f;
        private const float PlanktonExciteDecay = 0.965f;
        private const float PlanktonLightThreshold = 0.35f;
        private const float PlanktonLightStrength = 0.14f;
        private static readonly Color[] PlanktonColors =
        {
            new(90, 210, 255), new(120, 150, 255), new(170, 115, 255), new(80, 245, 210),
        };

        private const int EyeSpawnPerTick = 1;
        private const float EyeMinPlayerDistancePx = 20f * 16f;  // ближе не появляются
        private const float EyeStartleDistancePx = 13f * 16f;    // подплыл — погасли
        private const float EyeMaxSpawnBrightness = 0.08f;       // только в темноте
        private const float EyeStartleBrightness = 0.3f;         // осветили — погасли
        private const int EyeBlinkTicks = 7;
        private static readonly Color EyeColor = new(255, 42, 36);

        // Возмущение: кто плывёт сквозь планктон и снег
        private const float DisturbMinSpeed = 1f;
        private const float DisturbFullSpeed = 7f;
        private const float DisturbRadiusPx = 72f;
        private const float WakeDrag = 0.018f;          // доля скорости пловца, которую перенимает частица
        private const float PlanktonScatter = 0.12f;    // отталкивание планктона от пловца
        private const float DriftReturnRate = 0.02f;    // возврат к своему дрейфу после толчка
        private const float CurrentDrift = 0.6f;        // доля скорости течения, с которой его несёт

        // След: сколько искр за тик на 1 px/тик скорости пловца при полном свечении
        private const int MaxWakes = 360;
        private const float WakePerSpeed = 0.32f;
        private const float WakeMaxPerTick = 3f;
        private const int WakeFlareTicks = 7;           // искра разгорается, потом долго гаснет
        private const float WakeSparkDrag = 0.94f;
        private const float WakeInherit = 0.08f;        // доля скорости пловца, которую уносит искра
        private const float WakeLightThreshold = 0.45f;
        private const float WakeLightStrength = 0.22f;
        private const int FlashSparks = 26;
        private static readonly Color[] WakeColors =
        {
            new(110, 230, 255), new(90, 200, 255), new(150, 255, 235), new(130, 160, 255),
        };

        // Когда вода светится: ночью у поверхности, а глубже первой зоны — всегда,
        // туда солнце уже не достаёт. Ночь разгорается и гаснет плавно у краёв
        private const double NightLength = 32400.0;
        private const double NightRampTicks = 2400.0;
        private const float DarkDepthStart = 0.75f;     // DepthLevel, где глубина начинает светиться сама
        private const float DarkDepthRange = 0.5f;

        private const int VentScanTiles = 40;
        private static readonly Color VentBubbleColor = new(170, 215, 255);

        private enum MoteKind : byte { Snow, Plankton, Eye, Wake }

        private struct Mote
        {
            public MoteKind Kind;
            public Vector2 Position;
            public Vector2 Velocity;
            public Vector2 Drift;       // собственный дрейф, к нему частица возвращается
            public float Phase;         // фаза покачивания/мерцания/моргания
            public float Size;          // Eye — размер точки; расстояние между глазами в Spread
            public float Spread;
            public float Excite;        // Plankton — вспышка 0..1
            public Color Color;
            public int Life;
            public int MaxLife;
        }

        private struct Vent
        {
            public Vector2 Position;
            public int Life;
            public int Cooldown;
        }

        private struct Disturber
        {
            public Vector2 Center;
            public Vector2 Velocity;
            public float Radius;
            public float Strength;
        }

        private static readonly Mote[] _motes = new Mote[MaxMotes];
        private static int _moteCount;
        private static readonly int[] _kindCounts = new int[4];
        private static readonly Vent[] _vents = new Vent[MaxVents];
        private static int _ventCount;
        private static readonly List<Disturber> _disturbers = new(MaxDisturbers);

        private static Vector2 _pullSink;
        private static float _pullStrength;
        private static float _pullRadius;
        private static uint _pullTick;

        public override void Load()
        {
            if (Main.dedServ)
                return;
            On_Main.DrawDust += DrawAfterDust;
        }

        public override void Unload() => Clear();

        public override void OnWorldUnload() => Clear();

        private static void Clear()
        {
            _moteCount = 0;
            _ventCount = 0;
            _pullStrength = 0f;
            Array.Clear(_kindCounts);
            _disturbers.Clear();
        }

        #region Тик

        public override void PostUpdateDusts()
        {
            if (Main.dedServ)
                return;

            if (!TideAtmosphere.IsActive && _moteCount == 0 && _ventCount == 0)
                return;

            Rectangle view = VisibleWorldRect();
            CollectDisturbers(view);
            UpdateMotes(view);
            UpdateVents(view);

            if (TideAtmosphere.IsActive)
            {
                SpawnAmbient(view);
                SpawnWakes();
            }

            if (_pullTick != Main.GameUpdateCount)
                _pullStrength = 0f;
        }

        // 0..1: насколько светится вода в этой точке. Вне биома — 0
        public static float BioGlowAt(Vector2 worldPosition)
        {
            Point tile = worldPosition.ToTileCoordinates();
            float depth = TideOfShadowsWorldData.DepthLevelAt(tile.X, tile.Y);
            if (depth < 0f)
                return 0f;

            float dark = MathHelper.Clamp((depth - DarkDepthStart) / DarkDepthRange, 0f, 1f);
            return Math.Max(NightFactor(), dark);
        }

        private static float NightFactor()
        {
            if (Main.dayTime)
                return 0f;
            double edge = Math.Min(Main.time, NightLength - Main.time);
            return (float)Math.Clamp(edge / NightRampTicks, 0.0, 1.0);
        }

        // Течение к точке: всё, что плавает вокруг, тянет туда. Действует, пока его
        // выставляют каждый тик (сцену печати ведёт TideSealCinematic)
        public static void SetPull(Vector2 sink, float strength, float radius)
        {
            _pullSink = sink;
            _pullStrength = strength;
            _pullRadius = radius;
            _pullTick = Main.GameUpdateCount;
        }

        private static void ApplyPull(ref Mote m)
        {
            if (_pullStrength <= 0f)
                return;

            Vector2 toSink = _pullSink - m.Position;
            float distance = toSink.Length();
            if (distance < 1f || distance > _pullRadius)
                return;

            // Ближе к воронке тянет сильнее и закручивает — вода уходит не прямо, а спиралью
            float closeness = 1f - distance / _pullRadius;
            Vector2 inward = toSink / distance;
            Vector2 swirl = inward.RotatedBy(MathHelper.PiOver2);
            m.Velocity += (inward + swirl * 0.35f * closeness) * (_pullStrength * (0.3f + closeness));
            if (distance < 24f)
                m.Life = Math.Min(m.Life, QuickFadeTicks);
        }

        // Вспышка от удара: облако искр, разлетающихся из точки
        public static void Flash(Vector2 position, float strength)
        {
            if (Main.dedServ || !IsOpenWater(position))
                return;
            float glow = BioGlowAt(position);
            if (glow <= 0f)
                return;

            int sparks = (int)(FlashSparks * strength * glow);
            for (int i = 0; i < sparks; i++)
            {
                Vector2 velocity = Main.rand.NextVector2Circular(3.2f, 3.2f) * strength;
                SpawnWake(position + Main.rand.NextVector2Circular(6f, 6f), velocity, glow * Main.rand.NextFloat(0.7f, 1.2f));
            }
            SoAParticles.AddLight(position, WakeColors[0], 1.4f * strength * glow, 20);
        }

        // Каждый пловец в светящейся воде сыплет искрами из-под себя, тем гуще, чем быстрее плывёт
        private static void SpawnWakes()
        {
            foreach (Disturber d in _disturbers)
            {
                if (_kindCounts[(int)MoteKind.Wake] >= MaxWakes)
                    return;
                if (!IsOpenWater(d.Center))
                    continue;

                float glow = BioGlowAt(d.Center);
                if (glow <= 0f)
                    continue;

                float speed = d.Velocity.Length();
                float rate = Math.Min(speed * WakePerSpeed, WakeMaxPerTick) * glow;
                int count = (int)rate + (Main.rand.NextFloat() < rate % 1f ? 1 : 0);
                float bodyRadius = d.Radius - DisturbRadiusPx;

                for (int i = 0; i < count; i++)
                {
                    // Из-под тела, чуть позади: след тянется за пловцом, а не вокруг него
                    Vector2 position = d.Center - d.Velocity * Main.rand.NextFloat(0.2f, 1f)
                        + Main.rand.NextVector2Circular(bodyRadius * 0.7f, bodyRadius * 0.7f);
                    Vector2 velocity = d.Velocity * WakeInherit + Main.rand.NextVector2Circular(0.35f, 0.35f);
                    SpawnWake(position, velocity, glow * d.Strength);
                    _kindCounts[(int)MoteKind.Wake]++;
                }
            }
        }

        private static void SpawnWake(Vector2 position, Vector2 velocity, float intensity)
        {
            if (_moteCount >= MaxMotes)
                return;

            AddMote(new Mote
            {
                Kind = MoteKind.Wake,
                Position = position,
                Velocity = velocity,
                Drift = Main.rand.NextVector2Circular(0.05f, 0.05f) - new Vector2(0f, 0.04f),
                Phase = Main.rand.NextFloat(MathHelper.TwoPi),
                Size = Main.rand.NextFloat(2.5f, 5f),
                Spread = MathHelper.Clamp(intensity, 0.35f, 1.2f),
                Color = WakeColors[Main.rand.Next(WakeColors.Length)],
                MaxLife = Main.rand.Next(80, 170),
            });
        }

        // Яркость искры следа: короткий разгар, затем долгое угасание
        private static float WakeBrightness(in Mote m)
        {
            int age = m.MaxLife - m.Life;
            float flare = Math.Min(age / (float)WakeFlareTicks, 1f);
            float remaining = m.Life / (float)m.MaxLife;
            return flare * remaining * remaining * m.Spread;
        }

        // Видимая часть мира с учётом зума камеры
        private static Rectangle VisibleWorldRect()
        {
            Vector2 zoom = Main.GameViewMatrix.Zoom;
            Vector2 size = new Vector2(Main.screenWidth / zoom.X, Main.screenHeight / zoom.Y);
            Vector2 topLeft = Main.screenPosition + new Vector2(Main.screenWidth, Main.screenHeight) / 2f - size / 2f;
            return new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)size.X, (int)size.Y);
        }

        // Из общего списка пловцов — только те, кто в кадре и движется заметно
        private static void CollectDisturbers(Rectangle view)
        {
            _disturbers.Clear();
            Rectangle area = view;
            area.Inflate((int)DisturbRadiusPx, (int)DisturbRadiusPx);

            foreach (WaterDisturber source in WaterDisturbers.Get())
            {
                float speed = source.Velocity.Length();
                if (_disturbers.Count >= MaxDisturbers || speed < DisturbMinSpeed || !area.Contains(source.Hitbox.Center))
                    continue;

                _disturbers.Add(new Disturber
                {
                    Center = source.Center,
                    Velocity = source.Velocity,
                    Radius = DisturbRadiusPx + Math.Max(source.Hitbox.Width, source.Hitbox.Height) * 0.5f,
                    Strength = MathHelper.Clamp((speed - DisturbMinSpeed) / (DisturbFullSpeed - DisturbMinSpeed), 0.15f, 1f),
                });
            }
        }

        private static void UpdateMotes(Rectangle view)
        {
            Rectangle keep = view;
            keep.Inflate((int)DespawnMarginPx, (int)DespawnMarginPx);
            Vector2 localPlayer = Main.LocalPlayer.Center;
            int lightsLeft = MaxPlanktonLights;
            bool currentsOn = TideCurrents.Enabled;
            Array.Clear(_kindCounts);

            for (int i = _moteCount - 1; i >= 0; i--)
            {
                ref Mote m = ref _motes[i];
                if (--m.Life <= 0 || !keep.Contains(m.Position.ToPoint()))
                {
                    _motes[i] = _motes[--_moteCount]; // swap-remove: порядок не важен
                    continue;
                }
                _kindCounts[(int)m.Kind]++;

                if (m.Kind == MoteKind.Eye)
                {
                    UpdateEye(ref m, localPlayer);
                    continue;
                }

                ApplyPull(ref m);

                if (m.Kind == MoteKind.Wake)
                {
                    UpdateWake(ref m, ref lightsLeft);
                    continue;
                }

                ApplyDisturbance(ref m);
                m.Velocity = Vector2.Lerp(m.Velocity, m.Drift, DriftReturnRate);
                m.Phase += m.Kind == MoteKind.Snow ? 0.02f : 0.05f;

                Vector2 sway = m.Kind == MoteKind.Snow ? new Vector2((float)Math.Sin(m.Phase) * SnowSway, 0f) : Vector2.Zero;
                m.Position += m.Velocity + sway;
                // Течения (экспериментальная физика) видны по тому, как их несёт
                if (currentsOn)
                    m.Position += TideCurrents.At(m.Position) * CurrentDrift;

                if (!IsOpenWater(m.Position))
                    m.Life = Math.Min(m.Life, QuickFadeTicks);

                if (m.Kind == MoteKind.Plankton)
                {
                    m.Excite *= PlanktonExciteDecay;
                    if (m.Excite > PlanktonLightThreshold && lightsLeft > 0)
                    {
                        lightsLeft--;
                        Vector3 light = m.Color.ToVector3() * (m.Excite * PlanktonLightStrength);
                        Lighting.AddLight(m.Position, light.X, light.Y, light.Z);
                    }
                }
            }
        }

        // Пловец тянет частицы за собой следом, планктон ещё и вспыхивает и расступается
        private static void ApplyDisturbance(ref Mote m)
        {
            foreach (Disturber d in _disturbers)
            {
                Vector2 away = m.Position - d.Center;
                float distanceSquared = away.LengthSquared();
                if (distanceSquared >= d.Radius * d.Radius)
                    continue;

                float distance = (float)Math.Sqrt(distanceSquared);
                float falloff = (1f - distance / d.Radius) * d.Strength;
                m.Velocity += d.Velocity * (WakeDrag * falloff);

                if (m.Kind != MoteKind.Plankton)
                    continue;

                m.Excite = Math.Max(m.Excite, falloff);
                if (distance > 0.01f)
                    m.Velocity += away / distance * (PlanktonScatter * falloff);
            }
        }

        // Искра следа тормозит и зависает в воде, пока не погаснет; самые яркие чуть светят на мир
        private static void UpdateWake(ref Mote m, ref int lightsLeft)
        {
            m.Velocity = (m.Velocity - m.Drift) * WakeSparkDrag + m.Drift;
            m.Position += m.Velocity;
            m.Phase += 0.15f;

            if (!IsOpenWater(m.Position))
                m.Life = Math.Min(m.Life, QuickFadeTicks / 2);

            float brightness = WakeBrightness(m);
            if (brightness > WakeLightThreshold && lightsLeft > 0 && Main.rand.NextBool(3))
            {
                lightsLeft--;
                Vector3 light = m.Color.ToVector3() * (brightness * WakeLightStrength);
                Lighting.AddLight(m.Position, light.X, light.Y, light.Z);
            }
        }

        // Глаза не плывут за течением: висят, моргают и гаснут, если к ним подплыть или осветить
        private static void UpdateEye(ref Mote m, Vector2 localPlayer)
        {
            m.Position += m.Velocity;
            m.Phase += 1f;

            Point tile = m.Position.ToTileCoordinates();
            bool startled = Vector2.DistanceSquared(m.Position, localPlayer) < EyeStartleDistancePx * EyeStartleDistancePx
                || Lighting.Brightness(tile.X, tile.Y) > EyeStartleBrightness;
            if (startled && m.Life > QuickFadeTicks)
            {
                m.Life = QuickFadeTicks;
                m.Velocity = (m.Position - localPlayer).SafeNormalize(Vector2.Zero) * 0.6f;
            }
        }

        private static void UpdateVents(Rectangle view)
        {
            Rectangle keep = view;
            keep.Inflate((int)DespawnMarginPx, (int)DespawnMarginPx);

            for (int i = _ventCount - 1; i >= 0; i--)
            {
                ref Vent vent = ref _vents[i];
                if (--vent.Life <= 0 || !keep.Contains(vent.Position.ToPoint()))
                {
                    _vents[i] = _vents[--_ventCount];
                    continue;
                }

                if (--vent.Cooldown > 0)
                    continue;

                // Струйка идёт очередями: то пузырь, то пауза подлиннее
                vent.Cooldown = Main.rand.NextBool(4) ? Main.rand.Next(30, 70) : Main.rand.Next(5, 14);
                Vector2 position = vent.Position + new Vector2(Main.rand.NextFloat(-4f, 4f), -2f);
                Vector2 velocity = new(Main.rand.NextFloat(-0.3f, 0.3f), Main.rand.NextFloat(-1.2f, -0.5f));
                SoAParticles.SpawnBubble(position, velocity, Main.rand.NextFloat(3f, 8f), VentBubbleColor, 260);
            }
        }

        #endregion

        #region Спавн

        private static void SpawnAmbient(Rectangle view)
        {
            TideAtmosphereProfile profile = TideAtmosphere.Profile;
            float scale = TideAtmosphere.Presence * view.Width * view.Height / ReferenceScreenArea;

            Rectangle spawnArea = view;
            spawnArea.Inflate((int)SpawnMarginPx, (int)SpawnMarginPx);

            SpawnDeficit(MoteKind.Snow, profile.SnowDensity * scale, SnowSpawnPerTick, spawnArea);
            SpawnDeficit(MoteKind.Plankton, profile.PlanktonDensity * scale, PlanktonSpawnPerTick, spawnArea);
            SpawnDeficit(MoteKind.Eye, profile.EyeDensity * scale, EyeSpawnPerTick, spawnArea);

            if (_ventCount < MaxVents && Main.rand.NextFloat() < profile.VentRate * TideAtmosphere.Presence)
                TrySpawnVent(view);
        }

        private static void SpawnDeficit(MoteKind kind, float target, int maxPerTick, Rectangle area)
        {
            int deficit = (int)target - _kindCounts[(int)kind];
            for (int i = 0; i < Math.Min(deficit, maxPerTick); i++)
            {
                if (_moteCount >= MaxMotes)
                    return;

                Vector2 position = new(Main.rand.NextFloat(area.Left, area.Right), Main.rand.NextFloat(area.Top, area.Bottom));
                if (!IsOpenWater(position) || !InBiome(position))
                    continue;

                bool spawned = kind switch
                {
                    MoteKind.Snow => SpawnSnow(position),
                    MoteKind.Plankton => SpawnPlankton(position),
                    _ => TrySpawnEye(position),
                };
                if (spawned)
                    _kindCounts[(int)kind]++;
            }
        }

        private static bool SpawnSnow(Vector2 position)
        {
            Vector2 drift = new(Main.rand.NextFloat(-0.06f, 0.06f), Main.rand.NextFloat(0.1f, 0.32f));
            float shade = Main.rand.NextFloat(0.75f, 1f);
            AddMote(new Mote
            {
                Kind = MoteKind.Snow,
                Position = position,
                Velocity = drift,
                Drift = drift,
                Phase = Main.rand.NextFloat(MathHelper.TwoPi),
                Size = Main.rand.NextFloat(3f, 7f),
                Color = SnowColor * shade,
                MaxLife = Main.rand.Next(480, 900),
            });
            return true;
        }

        private static bool SpawnPlankton(Vector2 position)
        {
            Vector2 drift = Main.rand.NextVector2Circular(0.12f, 0.08f);
            AddMote(new Mote
            {
                Kind = MoteKind.Plankton,
                Position = position,
                Velocity = drift,
                Drift = drift,
                Phase = Main.rand.NextFloat(MathHelper.TwoPi),
                Size = Main.rand.NextFloat(3f, 6f),
                Color = PlanktonColors[Main.rand.Next(PlanktonColors.Length)],
                MaxLife = Main.rand.Next(600, 1200),
            });
            return true;
        }

        private static bool TrySpawnEye(Vector2 position)
        {
            if (Vector2.DistanceSquared(position, Main.LocalPlayer.Center) < EyeMinPlayerDistancePx * EyeMinPlayerDistancePx)
                return false;

            Point tile = position.ToTileCoordinates();
            if (Lighting.Brightness(tile.X, tile.Y) > EyeMaxSpawnBrightness)
                return false;

            AddMote(new Mote
            {
                Kind = MoteKind.Eye,
                Position = position,
                Velocity = Main.rand.NextVector2Circular(0.04f, 0.02f),
                Phase = Main.rand.Next(0, 400),
                Size = Main.rand.NextFloat(2.5f, 4f),
                Spread = Main.rand.NextFloat(7f, 12f),
                Color = EyeColor,
                MaxLife = Main.rand.Next(400, 900),
            });
            return true;
        }

        private static void AddMote(Mote mote)
        {
            mote.Life = mote.MaxLife;
            _motes[_moteCount++] = mote;
        }

        // Случайная точка экрана, от неё вниз до грунта: если под водой лежит дно — там струйка
        private static void TrySpawnVent(Rectangle view)
        {
            int tileX = (int)(Main.rand.NextFloat(view.Left, view.Right) / 16f);
            int tileY = (int)(Main.rand.NextFloat(view.Top, view.Bottom) / 16f);

            for (int y = tileY; y < tileY + VentScanTiles && y < Main.maxTilesY - 1; y++)
            {
                Tile tile = Framing.GetTileSafely(tileX, y);
                if (!IsSolid(tile))
                    continue;

                Vector2 seabed = new(tileX * 16f + 8f, y * 16f);
                if (y > tileY && IsOpenWater(seabed - new Vector2(0f, 8f)) && InBiome(seabed))
                {
                    _vents[_ventCount++] = new Vent
                    {
                        Position = seabed,
                        Life = Main.rand.Next(300, 900),
                        Cooldown = Main.rand.Next(0, 20),
                    };
                }
                return;
            }
        }

        #endregion

        private static bool IsSolid(Tile tile)
            => tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];

        private static bool IsOpenWater(Vector2 worldPosition)
            => SoACombat.IsInWater(worldPosition) && !IsSolid(Framing.GetTileSafely(worldPosition.ToTileCoordinates()));

        private static bool InBiome(Vector2 worldPosition) => TideOfShadowsWorldData.Contains(worldPosition);

        #region Отрисовка

        private static void DrawAfterDust(On_Main.orig_DrawDust orig, Main self)
        {
            orig(self);
            if (_moteCount == 0)
                return;

            SpriteBatch sb = Main.spriteBatch;
            Texture2D glow = SoAVfx.SoftGlow;
            Vector2 origin = glow.Size() / 2f;

            // Снег освещён миром: в темноте темнеет вместе с водой
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                Main.GameViewMatrix.TransformationMatrix);
            for (int i = 0; i < _moteCount; i++)
            {
                ref Mote m = ref _motes[i];
                if (m.Kind == MoteKind.Snow)
                    DrawSnow(sb, glow, origin, ref m);
            }
            sb.End();

            // Планктон и глаза светятся сами
            sb.Begin(SpriteSortMode.Deferred, SoAVfx.GlowBlend, Main.DefaultSamplerState,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
                Main.GameViewMatrix.TransformationMatrix);
            for (int i = 0; i < _moteCount; i++)
            {
                ref Mote m = ref _motes[i];
                if (m.Kind == MoteKind.Plankton)
                    DrawPlankton(sb, glow, origin, ref m);
                else if (m.Kind == MoteKind.Wake)
                    DrawWake(sb, glow, origin, ref m);
                else if (m.Kind == MoteKind.Eye)
                    DrawEyes(sb, glow, origin, ref m);
            }
            sb.End();
        }

        // Плавное появление и угасание по жизни частицы
        private static float LifeFade(in Mote m)
        {
            int age = m.MaxLife - m.Life;
            float fadeIn = Math.Min(age / (float)FadeInTicks, 1f);
            float fadeOut = Math.Min(m.Life / (float)FadeOutTicks, 1f);
            return fadeIn * fadeOut;
        }

        private static void DrawSnow(SpriteBatch sb, Texture2D glow, Vector2 origin, ref Mote m)
        {
            Vector3 light = Lighting.GetColor(m.Position.ToTileCoordinates()).ToVector3();
            light = Vector3.Max(light, new Vector3(SnowMinBrightness));
            Color color = m.Color.MultiplyRGB(new Color(light)) * (LifeFade(m) * 0.8f);
            sb.Draw(glow, m.Position - Main.screenPosition, null, color, 0f, origin, m.Size / glow.Width,
                SpriteEffects.None, 0f);
        }

        private static void DrawPlankton(SpriteBatch sb, Texture2D glow, Vector2 origin, ref Mote m)
        {
            float twinkle = 0.75f + 0.25f * (float)Math.Sin(m.Phase * 1.7f);
            float brightness = (PlanktonIdleGlow * twinkle + m.Excite) * LifeFade(m);
            Vector2 screen = m.Position - Main.screenPosition;

            // Ореол растёт со вспышкой, ядро — яркая точка
            float haloSize = m.Size * (2.2f + m.Excite * 2.5f);
            sb.Draw(glow, screen, null, m.Color * (brightness * 0.45f), 0f, origin, haloSize / glow.Width,
                SpriteEffects.None, 0f);
            sb.Draw(glow, screen, null, Color.Lerp(m.Color, Color.White, 0.5f) * brightness, 0f, origin,
                m.Size / glow.Width, SpriteEffects.None, 0f);
        }

        // Как планктон, только ярче и с мерцанием: в разгаре ядро почти белое
        private static void DrawWake(SpriteBatch sb, Texture2D glow, Vector2 origin, ref Mote m)
        {
            float brightness = WakeBrightness(m) * (0.8f + 0.2f * (float)Math.Sin(m.Phase));
            if (brightness <= 0.01f)
                return;

            Vector2 screen = m.Position - Main.screenPosition;
            float haloSize = m.Size * (2.5f + brightness * 2f);
            sb.Draw(glow, screen, null, m.Color * (brightness * 0.4f), 0f, origin, haloSize / glow.Width,
                SpriteEffects.None, 0f);
            sb.Draw(glow, screen, null, Color.Lerp(m.Color, Color.White, 0.3f + 0.4f * brightness) * brightness, 0f,
                origin, m.Size / glow.Width, SpriteEffects.None, 0f);
        }

        private static void DrawEyes(SpriteBatch sb, Texture2D glow, Vector2 origin, ref Mote m)
        {
            // Моргают раз в несколько секунд, у каждой пары свой ритм
            int blinkPeriod = 180 + (int)(m.Spread * 17f);
            if ((int)m.Phase % blinkPeriod < EyeBlinkTicks)
                return;

            float alpha = LifeFade(m);
            Vector2 center = m.Position - Main.screenPosition;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 eye = center + new Vector2(side * m.Spread / 2f, 0f);
                sb.Draw(glow, eye, null, m.Color * (alpha * 0.35f), 0f, origin, m.Size * 3f / glow.Width,
                    SpriteEffects.None, 0f);
                sb.Draw(glow, eye, null, m.Color * alpha, 0f, origin, m.Size / glow.Width,
                    SpriteEffects.None, 0f);
            }
        }

        #endregion
    }
}
