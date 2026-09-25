using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace SoA.Common.Graphics.SandFormation
{
    // Сборка предмета из отдельных песчинок. Зёрна лежат россыпью, просыпаются
    // вразнобой, дугами слетаются на свои места в силуэте спрайта и садятся —
    // предмет не «проявляется», а действительно складывается из песка.
    //
    // Вся случайность детерминирована (SandFormationRandom), физика тикает
    // фиксированным шагом, поэтому по сети достаточно синхронизировать seed,
    // предмет и FormationProgress: картинка на клиентах совпадёт сама.
    //
    // Два способа гонять шкалу:
    //   effect.Play(1.5f);                 — сама доедет до конца за 1.5 с
    //   effect.FormationProgress = value;  — шкалу крутит игровая логика
    public sealed class SandFormationEffect
    {
        public const float Dt = 1f / 60f;

        // Фазы шкалы. Ниже по коду используются именно эти границы
        private const float AwakenFrom = 0.15f;
        private const float GatherFrom = 0.40f;
        private const float FormationFrom = 0.75f;
        private const float CompletionFrom = 0.95f;

        // Последнее зерно должно проснуться сильно раньше конца шкалы,
        // иначе на 1.0 часть песка окажется ещё в полёте
        private const float LatestDelay = 0.55f;

        // К этой отметке зерно уже полностью «своё»: дальше идёт только посадка
        private const float LocalTarget = 0.92f;

        private const float BurstDuration = 0.14f;
        private const int UntouchedTicksToDrop = 30;

        // Отладочная отрисовка целей/стартов/радиусов. Дев-инструмент: в игре не
        // включается ниоткуда, только руками из отладчика или временной строкой
        public static bool Debug;

        private static readonly Dictionary<int, SandFormationEffect> Attached = new();
        private static readonly List<SandFormationEffect> Standalone = new();
        private static readonly List<int> ExpiredKeys = new();

        internal SandFormationParticle[] Particles = Array.Empty<SandFormationParticle>();
        internal SandFormationSilhouette Silhouette = null;
        internal Texture2D Texture;
        internal SandFormationSettings Settings;
        internal int Seed;

        internal float Time;
        internal float BurstTimer;

        // Значения фаз текущего кадра — считаются раз в тик, читает рендерер
        internal float Formation;
        internal float Completion;
        internal float Glow;

        private bool _ready;
        private float _autoDuration;
        private float _lastProgress;
        private int _untouchedTicks;
        private Vector2 _worldPosition;
        private bool _drawnByOwner;

        public float FormationProgress;

        public bool IsFinished => FormationProgress >= 1f && BurstTimer <= 0f;

        public int ParticleCount => Particles.Length;

        public Vector2 WorldPosition
        {
            get => _worldPosition;
            set => _worldPosition = value;
        }

        // --- Публичный API ---

        // Самостоятельный эффект: система сама тикает его и сама рисует в мире.
        // Живёт до конца сборки, дальше снимается
        public static SandFormationEffect Start(Vector2 worldPosition, Texture2D targetTexture, int seed)
            => Start(worldPosition, targetTexture, seed, SandFormationSettings.ForTexture(targetTexture));

        public static SandFormationEffect Start(Vector2 worldPosition, Texture2D targetTexture, int seed,
            int particleCount, float formationRadius)
        {
            SandFormationSettings settings = SandFormationSettings.ForTexture(targetTexture);
            settings.ParticleCount = (int)MathHelper.Clamp(particleCount, 32f, 256f);
            settings.FormationRadius = formationRadius;
            return Start(worldPosition, targetTexture, seed, settings);
        }

        public static SandFormationEffect Start(Vector2 worldPosition, Texture2D targetTexture, int seed,
            in SandFormationSettings settings)
        {
            var effect = new SandFormationEffect
            {
                Texture = targetTexture,
                Seed = seed,
                Settings = settings,
                _worldPosition = worldPosition,
            };

            if (!Main.dedServ)
                Standalone.Add(effect);
            return effect;
        }

        // Сборка предмета по его типу — текстуру берём из ванильного реестра
        public static SandFormationEffect StartForItem(Vector2 worldPosition, int itemType, int seed)
        {
            Main.instance.LoadItem(itemType);
            return Start(worldPosition, TextureAssets.Item[itemType].Value, seed);
        }

        // Эффект, которым управляет владелец (снаряд, NPC, UI): тикает система,
        // рисует владелец из своего PreDraw. key — стабильный локальный идентификатор
        // владельца, seed — синхронное по сети число (например Projectile.identity).
        public static SandFormationEffect Attach(int key, Texture2D targetTexture, int seed)
        {
            if (Attached.TryGetValue(key, out SandFormationEffect existing))
            {
                // Тот же слот занял другой предмет — пересобираем с нуля
                if (existing.Texture == targetTexture && existing.Seed == seed)
                {
                    existing._untouchedTicks = 0;
                    existing._drawnByOwner = true;
                    return existing;
                }
                Attached.Remove(key);
            }

            var effect = new SandFormationEffect
            {
                Texture = targetTexture,
                Seed = seed,
                Settings = SandFormationSettings.ForTexture(targetTexture),
                _drawnByOwner = true,
            };
            Attached[key] = effect;
            return effect;
        }

        public static void Release(int key) => Attached.Remove(key);

        public static void ClearAll()
        {
            Attached.Clear();
            Standalone.Clear();
        }

        // Автопрогон шкалы за durationSeconds
        public void Play(float durationSeconds)
        {
            _autoDuration = MathHelper.Max(durationSeconds, Dt);
            FormationProgress = 0f;
        }

        // --- Тик ---

        public static void UpdateAll()
        {
            if (Main.dedServ)
                return;

            foreach (KeyValuePair<int, SandFormationEffect> pair in Attached)
            {
                SandFormationEffect effect = pair.Value;
                effect.Update();
                if (++effect._untouchedTicks > UntouchedTicksToDrop)
                    ExpiredKeys.Add(pair.Key);
            }

            foreach (int key in ExpiredKeys)
                Attached.Remove(key);
            ExpiredKeys.Clear();

            for (int i = Standalone.Count - 1; i >= 0; i--)
            {
                SandFormationEffect effect = Standalone[i];
                effect.Update();
                if (effect.IsFinished)
                    Standalone.RemoveAt(i);
            }
        }

        public void Update()
        {
            EnsureReady();
            if (!_ready)
                return;

            Time += Dt;

            if (_autoDuration > 0f)
                FormationProgress += Dt / _autoDuration;
            FormationProgress = Saturate(FormationProgress);

            // Момент, когда последнее зерно легло: короткая вспышка песка
            if (FormationProgress >= 1f && _lastProgress < 1f)
                BurstTimer = BurstDuration;
            else if (BurstTimer > 0f)
                BurstTimer = MathHelper.Max(BurstTimer - Dt, 0f);
            _lastProgress = FormationProgress;

            Formation = Saturate((FormationProgress - FormationFrom) / (CompletionFrom - FormationFrom));
            Completion = SmoothStep(Saturate((FormationProgress - CompletionFrom) / (1f - CompletionFrom)));
            Glow = GlowCurve(FormationProgress) * Settings.GlowIntensity;

            for (int i = 0; i < Particles.Length; i++)
                UpdateParticle(ref Particles[i], i);
        }

        private void UpdateParticle(ref SandFormationParticle particle, int index)
        {
            particle.Life += Dt;

            // Своё время у каждого зерна: пока прогресс не дошёл до его порога,
            // оно просто лежит в россыпи. Отсюда «просыпаются вразнобой»
            float local = SmoothStep(Remap(FormationProgress, particle.Delay, LocalTarget));
            particle.IsGathering = local > 0f;

            Vector2 destination = Destination(in particle, local);

            Vector2 toTarget = destination - particle.Position;
            float distance = toTarget.Length();
            Vector2 direction = distance > 0.001f ? toTarget / distance : Vector2.Zero;

            // Прицел уводится вбок по перпендикуляру — зерно идёт дугой, а не по прямой.
            // Снос гаснет и с приближением, и по мере сборки
            var tangent = new Vector2(-direction.Y, direction.X);
            float swirlFade = (1f - local) * Saturate(distance / MathHelper.Max(Settings.SpreadRadius * 0.5f, 1f));
            float curve = (float)Math.Sin(Time * Settings.SwirlSpeed + particle.SwirlPhase)
                * Settings.SwirlStrength * particle.SwirlSign * swirlFade;
            Vector2 aim = destination + tangent * curve;

            Vector2 velocity = particle.Velocity;

            if (local < 1f)
                velocity += LooseForces(in particle) * (1f - local) * Dt;

            if (local > 0f)
            {
                // Чем ближе зерно, тем жёстче его ведут: дальние ещё «сами по себе»
                float proximity = 1f - Saturate(distance / MathHelper.Max(Settings.FormationRadius, 1f));

                Vector2 desiredVelocity = (aim - particle.Position).SafeNormalize(Vector2.Zero)
                    * Settings.AttractionStrength * (0.4f + 0.6f * local);
                velocity = Vector2.Lerp(velocity, desiredVelocity,
                    Saturate(Settings.AttractionAcceleration * local * Dt));

                // Пружина доводит зерно на последних пикселях и даёт лёгкий перелёт
                velocity += (aim - particle.Position) * Settings.SpringStrength * local
                    * (0.3f + 0.7f * proximity) * Dt;
            }

            velocity *= MathHelper.Lerp(Settings.LooseDamping, Settings.Damping, local);

            float speed = velocity.Length();
            if (speed > Settings.MaxSpeed)
                velocity *= Settings.MaxSpeed / speed;

            particle.Velocity = velocity;

            // Хвост: три предыдущих положения, по одному на тик
            particle.Trail2 = particle.Trail1;
            particle.Trail1 = particle.Trail0;
            particle.Trail0 = particle.Position;

            particle.Position += velocity * Dt;

            // Ещё не проснувшийся песок лежит на дне облака, а не сыплется вниз бесконечно
            if (local < 0.35f)
            {
                float floorY = Settings.SpreadRadius * 0.85f;
                if (particle.Position.Y > floorY)
                {
                    particle.Position.Y = floorY;
                    particle.Velocity.Y = -Math.Abs(particle.Velocity.Y) * 0.35f;
                }
            }

            // Финальная посадка: не телепорт, а короткое доведение точно в силуэт
            if (Completion > 0f)
            {
                particle.Position = Vector2.Lerp(particle.Position, particle.TargetPosition, Completion);
                particle.Velocity *= 1f - Completion;
            }
        }

        // Куда именно летит зерно на этом кадре
        private Vector2 Destination(in SandFormationParticle particle, float local)
        {
            if (particle.Kind != SandFormationGrainKind.Orbit)
                return particle.TargetPosition;

            // Орбита стягивается к сборке по мере готовности — к концу зерно
            // само оказывается в силуэте, а не гаснет посреди воздуха
            float radius = particle.OrbitRadius * (1f - Formation * 0.75f) * (1f - Completion);
            float angle = particle.OrbitPhase + Time * particle.OrbitSpeed;
            var orbit = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius * 0.72f);
            return Vector2.Lerp(orbit, particle.TargetPosition, Completion);
        }

        // Поведение свободного песка: слабая гравитация, боковой ветер и мелкая
        // болтанка. Специально сдержанное — это песчинки, а не дым
        private Vector2 LooseForces(in SandFormationParticle particle)
        {
            float wind = (float)Math.Sin(Time * 0.8f + particle.NoisePhaseX) * Settings.WindStrength;

            var noise = new Vector2(
                (float)Math.Sin(Time * 2.7f + particle.NoisePhaseX),
                (float)Math.Cos(Time * 3.1f + particle.NoisePhaseY));

            return new Vector2(wind, Settings.Gravity) + noise * (Settings.Randomness * 40f);
        }

        // --- Инициализация ---

        private void EnsureReady()
        {
            if (_ready || Texture == null)
                return;

            Silhouette = SandFormationSilhouette.Get(Texture);
            if (Silhouette.IsEmpty)
                return;

            int count = (int)MathHelper.Clamp(Settings.ParticleCount, 32f, 256f);
            Particles = new SandFormationParticle[count];
            for (int i = 0; i < count; i++)
                InitParticle(ref Particles[i], i);

            _ready = true;
        }

        private void InitParticle(ref SandFormationParticle particle, int index)
        {
            particle.Seed = Seed + index;

            float kindRoll = SandFormationRandom.Value(Seed, index, 0);
            particle.Kind = kindRoll < Settings.OrbitShare ? SandFormationGrainKind.Orbit
                : kindRoll < Settings.OrbitShare + Settings.EdgeShare ? SandFormationGrainKind.Edge
                : SandFormationGrainKind.Core;

            // Точка посадки: кайма садится на контур и чуть наружу, остальные — внутрь силуэта
            if (particle.Kind == SandFormationGrainKind.Edge && Silhouette.EdgePoints.Length > 0)
            {
                int edge = SandFormationRandom.Index(Seed, index, 1, Silhouette.EdgePoints.Length);
                particle.TargetPosition = Silhouette.EdgePoints[edge]
                    + Silhouette.EdgeNormals[edge] * SandFormationRandom.Range(Seed, index, 2, 1.5f, 4.5f);
                particle.TargetColor = SandFormationPalette.Ramp[SandFormationPalette.TrailIndex];
            }
            else
            {
                int point = SandFormationRandom.Index(Seed, index, 1, Silhouette.Points.Length);
                particle.TargetPosition = Silhouette.Points[point];
                particle.TargetColor = Silhouette.Colors[point];
            }

            // Старт: рыхлое облако, а не аккуратное кольцо — угол, радиус и смещение
            // берутся из независимых каналов хеша
            float angle = SandFormationRandom.Value(Seed, index, 3) * MathHelper.TwoPi;
            float radius = Settings.SpreadRadius * SandFormationRandom.Range(Seed, index, 4, 0.35f, 1f);
            var scatter = new Vector2(
                SandFormationRandom.Signed(Seed, index, 5),
                SandFormationRandom.Signed(Seed, index, 6)) * Settings.SpreadRadius * 0.28f;

            particle.StartPosition = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius + scatter;
            particle.Position = particle.StartPosition;
            particle.Trail0 = particle.Position;
            particle.Trail1 = particle.Position;
            particle.Trail2 = particle.Position;

            // Ни одно зерно не стоит мёртво: у россыпи сразу есть свой дрейф
            particle.Velocity = new Vector2(
                SandFormationRandom.Signed(Seed, index, 7),
                SandFormationRandom.Signed(Seed, index, 8)) * 14f;

            particle.Delay = SandFormationRandom.Value(Seed, index, 9) * LatestDelay;

            float sizeRoll = SandFormationRandom.Value(Seed, index, 10);
            particle.Size = Settings.ParticleSize * (sizeRoll < 0.6f ? 0.75f : sizeRoll < 0.92f ? 1.15f : 1.6f);

            particle.Brightness = SandFormationRandom.Range(Seed, index, 11, 0.55f, 1f);
            particle.Color = SandFormationPalette.Pick(
                SandFormationRandom.Value(Seed, index, 12),
                SandFormationRandom.Value(Seed, index, 13));

            particle.SwirlSign = SandFormationRandom.Sign(Seed, index, 14);
            particle.SwirlPhase = SandFormationRandom.Value(Seed, index, 15) * MathHelper.TwoPi;

            particle.OrbitRadius = Settings.SpreadRadius * SandFormationRandom.Range(Seed, index, 16, 0.45f, 0.8f);
            particle.OrbitSpeed = SandFormationRandom.Range(Seed, index, 17, 1.1f, 2.3f) * particle.SwirlSign;
            particle.OrbitPhase = SandFormationRandom.Value(Seed, index, 18) * MathHelper.TwoPi;

            particle.NoisePhaseX = SandFormationRandom.Value(Seed, index, 19) * MathHelper.TwoPi;
            particle.NoisePhaseY = SandFormationRandom.Value(Seed, index, 20) * MathHelper.TwoPi;

            particle.Life = 0f;
        }

        // --- Отрисовка ---

        // Для эффектов, которыми управляет владелец: вызывать прямо из PreDraw.
        // origin/rotation/scale — те же, с какими рисовался бы обычный спрайт
        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos, Color light,
            float rotation, Vector2 origin, float scale)
        {
            _untouchedTicks = 0;
            _drawnByOwner = true;
            SandFormationRenderer.Draw(spriteBatch, this, drawPos, light, rotation, origin, scale);
        }

        internal static void DrawStandalone(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < Standalone.Count; i++)
            {
                SandFormationEffect effect = Standalone[i];
                if (effect._drawnByOwner || effect.Texture == null)
                    continue;

                Vector2 drawPos = effect._worldPosition - Main.screenPosition;
                SandFormationRenderer.Draw(spriteBatch, effect, drawPos, Color.White, 0f,
                    effect.Texture.Size() * 0.5f, 1f);
            }
        }

        internal bool IsReady => _ready;

        // --- Кривые ---

        internal static float Saturate(float value) => MathHelper.Clamp(value, 0f, 1f);

        internal static float SmoothStep(float value)
        {
            value = Saturate(value);
            return value * value * (3f - 2f * value);
        }

        internal static float Remap(float value, float from, float to)
        {
            if (to - from <= 0.0001f)
                return value >= to ? 1f : 0f;
            return Saturate((value - from) / (to - from));
        }

        // Свечение по фазам: россыпь почти не светится, к посадке разгорается
        internal static float GlowCurve(float progress)
        {
            if (progress < AwakenFrom)
                return 0.1f;
            if (progress < GatherFrom)
                return MathHelper.Lerp(0.1f, 0.3f, (progress - AwakenFrom) / (GatherFrom - AwakenFrom));
            if (progress < FormationFrom)
                return MathHelper.Lerp(0.3f, 0.8f, (progress - GatherFrom) / (FormationFrom - GatherFrom));
            if (progress < CompletionFrom)
                return MathHelper.Lerp(0.8f, 1.2f, (progress - FormationFrom) / (CompletionFrom - FormationFrom));
            return MathHelper.Lerp(1.2f, 1.8f, (progress - CompletionFrom) / (1f - CompletionFrom));
        }

        internal float BurstFade => BurstTimer <= 0f ? 0f : BurstTimer / BurstDuration;
    }
}
