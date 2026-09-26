using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace SoA.Common.Graphics.Animation
{
    // Кейфреймовая анимация процедурных ригов: клип = набор треков (слой → ключи по времени),
    // AnimPlayer крутит базовый луп + один-шот реакции поверх и семплирует позу каждого слоя на тик.
    // Все позы АДДИТИВНЫ к процедурному ригу: Identity (rot 0, offset 0, scale 1, aux 0) = «не трогаю».
    // Чистая косметика: тикается на клиентах из синхронизированного состояния, по сети ничего не шлёт.

    public enum EaseMode
    {
        Linear,
        Smooth,   // сплайн через соседние ключи — дефолт (см. AnimClip.BuildTangents)
        EaseIn,   // разгон: старт с места, к следующему ключу приходит на полной скорости (удар)
        EaseOut,  // торможение: срыв с места на полной скорости, у следующего ключа замирает
        Snap      // держит позу до следующего ключа, затем скачок
    }

    // Трансформ одного слоя. Оси — «лицевые»: +X вперёд по взгляду, +Y вниз, rotation + = наклон вперёд.
    // Aux/Aux2 — свободные каналы слоя (раскрытие пинцера, сила свечения) — владелец решает сам.
    // Scale НЕОДНОРОДЕН (Vector2): squash & stretch доступен не только панцирю, но и клешням с короной.
    // Дефолт Scale = Vector2.One, поэтому клип, который канал не трогает, ничего не деформирует.
    public struct LayerPose
    {
        public float Rotation;
        public Vector2 Offset;
        public Vector2 Scale;
        public float Aux;
        public float Aux2;

        public static readonly LayerPose Identity = new() { Scale = Vector2.One };

        public static LayerPose Lerp(in LayerPose a, in LayerPose b, float t) => new()
        {
            Rotation = MathHelper.Lerp(a.Rotation, b.Rotation, t),
            Offset = Vector2.Lerp(a.Offset, b.Offset, t),
            Scale = Vector2.Lerp(a.Scale, b.Scale, t),
            Aux = MathHelper.Lerp(a.Aux, b.Aux, t),
            Aux2 = MathHelper.Lerp(a.Aux2, b.Aux2, t),
        };

        // Сложение аддитивных поз: один-шот ложится поверх базового клипа
        public static LayerPose Combine(in LayerPose a, in LayerPose b) => new()
        {
            Rotation = a.Rotation + b.Rotation,
            Offset = a.Offset + b.Offset,
            Scale = a.Scale * b.Scale,
            Aux = a.Aux + b.Aux,
            Aux2 = a.Aux2 + b.Aux2,
        };

        // Ослабление к Identity: t=0 — ничего, t=1 — поза целиком (конверт входа/выхода реакций)
        public static LayerPose Faded(in LayerPose p, float t) => Lerp(Identity, p, t);

        // --- Поканальный доступ: сплайн и инерция кроссфейда считают все 7 каналов одинаково ---
        public const int ChannelCount = 7;

        public float Get(int channel) => channel switch
        {
            0 => Rotation,
            1 => Offset.X,
            2 => Offset.Y,
            3 => Scale.X,
            4 => Scale.Y,
            5 => Aux,
            _ => Aux2,
        };

        public void Set(int channel, float value)
        {
            switch (channel)
            {
                case 0: Rotation = value; break;
                case 1: Offset.X = value; break;
                case 2: Offset.Y = value; break;
                case 3: Scale.X = value; break;
                case 4: Scale.Y = value; break;
                case 5: Aux = value; break;
                default: Aux2 = value; break;
            }
        }
    }

    public struct Keyframe
    {
        public float Time;     // тик внутри клипа (60/с)
        public LayerPose Pose;
        public EaseMode Ease;  // сглаживание на пути К СЛЕДУЮЩЕМУ ключу

        // Касательные сплайна, единицы канала за тик. Считает AnimClip.BuildTangents,
        // хранятся в тех же полях LayerPose, но это скорости, а не поза
        public LayerPose InTangent;   // с какой скоростью приходим в ключ
        public LayerPose OutTangent;  // с какой уходим из него
    }

    public class AnimClip
    {
        public const float DefaultOneShotFade = 6f;

        public readonly string Name;
        public readonly float Duration; // тики
        public readonly bool Loop;

        // Конверт один-шота — свойство КЛИПА, а не игрока: короткой реакции (hurt 12 тиков)
        // общий конверт 6/6 съедает половину клипа, и удар получается вялым
        public float FadeIn { get; private set; } = DefaultOneShotFade;
        public float FadeOut { get; private set; } = DefaultOneShotFade;

        private readonly Dictionary<string, List<Keyframe>> _tracks = new();
        private readonly HashSet<string> _tracksWithoutTangents = new(); // касательные пересчитываются лениво
        private readonly List<(float Time, string Name)> _events = new();
        public IReadOnlyList<(float Time, string Name)> Events => _events;

        public AnimClip(string name, float duration, bool loop)
        {
            Name = name;
            Duration = Math.Max(1f, duration);
            Loop = loop;
        }

        public AnimClip Envelope(float fadeIn, float fadeOut)
        {
            FadeIn = Math.Max(0.001f, fadeIn);
            FadeOut = Math.Max(0.001f, fadeOut);
            return this;
        }

        // Ключ слоя. Авторить можно в любом порядке — трек держится отсортированным.
        // scale — однородный масштаб; scaleY задаётся отдельно, только когда нужен неоднородный
        // (squash & stretch клешни/короны). Не задан — берётся от scale, поведение как было.
        public AnimClip Key(string layer, float time, float rot = 0f, float ox = 0f, float oy = 0f,
            float scale = 1f, float aux = 0f, EaseMode ease = EaseMode.Smooth,
            float aux2 = 0f, float scaleY = float.NaN)
        {
            if (!_tracks.TryGetValue(layer, out List<Keyframe> track))
                _tracks[layer] = track = new List<Keyframe>();

            Keyframe kf = new()
            {
                Time = time,
                Pose = new LayerPose
                {
                    Rotation = rot,
                    Offset = new Vector2(ox, oy),
                    Scale = new Vector2(scale, float.IsNaN(scaleY) ? scale : scaleY),
                    Aux = aux,
                    Aux2 = aux2,
                },
                Ease = ease,
            };
            int at = track.FindIndex(k => k.Time > time);
            if (at < 0)
                track.Add(kf);
            else
                track.Insert(at, kf);
            _tracksWithoutTangents.Add(layer);
            return this;
        }

        // Метка времени: AnimPlayer дёрнет OnEvent/выставит полл-флаг, когда плейхед её пересечёт
        public AnimClip Event(float time, string name)
        {
            int at = _events.FindIndex(e => e.Time > time);
            if (at < 0)
                _events.Add((time, name));
            else
                _events.Insert(at, (time, name));
            return this;
        }

        public LayerPose Sample(string layer, float time)
        {
            if (!_tracks.TryGetValue(layer, out List<Keyframe> track) || track.Count == 0)
                return LayerPose.Identity;
            if (track.Count == 1)
                return track[0].Pose;
            if (_tracksWithoutTangents.Remove(layer))
                BuildTangents(track);

            if (Loop)
                time = ((time % Duration) + Duration) % Duration;
            else
                time = MathHelper.Clamp(time, 0f, Duration);

            Keyframe prev, next;
            float span, local;
            Keyframe first = track[0];
            Keyframe last = track[^1];

            if (time <= first.Time)
            {
                if (!Loop)
                    return first.Pose;
                prev = last; // луп: интерполяция последний → первый через край клипа
                next = first;
                span = Duration - last.Time + first.Time;
                local = Duration - last.Time + time;
            }
            else if (time >= last.Time)
            {
                if (!Loop)
                    return last.Pose;
                prev = last;
                next = first;
                span = Duration - last.Time + first.Time;
                local = time - last.Time;
            }
            else
            {
                int i = 0;
                while (track[i + 1].Time <= time)
                    i++;
                prev = track[i];
                next = track[i + 1];
                span = next.Time - prev.Time;
                local = time - prev.Time;
            }

            float t = span <= 0f ? 1f : MathHelper.Clamp(local / span, 0f, 1f);
            return prev.Ease switch
            {
                EaseMode.Linear => LayerPose.Lerp(prev.Pose, next.Pose, t),
                EaseMode.Snap => t < 1f ? prev.Pose : next.Pose,
                _ => Hermite(prev.Pose, prev.OutTangent, next.Pose, next.InTangent, span, t),
            };
        }

        // Кубический сплайн Эрмита по всем каналам позы
        private static LayerPose Hermite(in LayerPose p0, in LayerPose m0, in LayerPose p1, in LayerPose m1,
            float span, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = (t3 - 2f * t2 + t) * span;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = (t3 - t2) * span;

            LayerPose result = default;
            for (int c = 0; c < LayerPose.ChannelCount; c++)
                result.Set(c, h00 * p0.Get(c) + h10 * m0.Get(c) + h01 * p1.Get(c) + h11 * m1.Get(c));
            return result;
        }

        // Касательные сплайна.
        // Раньше каждый отрезок сглаживался сам по себе (SmoothStep), и скорость падала до нуля
        // на КАЖДОМ ключе: движение шло рывками «разгон — стоп — разгон», а EaseIn перед обычным
        // ключом давал мгновенный стоп с полной скорости. Отсюда и «топорность».
        //
        // Теперь (Smooth) движение проходит сквозь ключ со скоростью, а замирает только там, где
        // ключ — экстремум канала: пик замаха, крайняя точка качания, дрожь. Касательные ограничены
        // по Фричу–Карлсону, поэтому сплайн никогда не перелетает авторские значения.
        // EaseIn/EaseOut воспроизводят старые кривые t² и 1-(1-t)² ТОЧНО — удары приходят в
        // землю на полной скорости, как и были задуманы, — а соседние Smooth-отрезки
        // подстраиваются под них без скачка скорости.
        private void BuildTangents(List<Keyframe> track)
        {
            int n = track.Count;
            if (n < 2)
                return;

            // 1. Авто-касательные: одна скорость на ключ, по соседям слева и справа
            for (int i = 0; i < n; i++)
            {
                Keyframe key = track[i];
                LayerPose tangent = default;
                if (TryNeighbor(track, i, -1, out Keyframe prev, out float prevTime)
                    && TryNeighbor(track, i, 1, out Keyframe next, out float nextTime))
                {
                    for (int c = 0; c < LayerPose.ChannelCount; c++)
                    {
                        tangent.Set(c, AutoTangent(prev.Pose.Get(c), prevTime,
                            key.Pose.Get(c), key.Time, next.Pose.Get(c), nextTime));
                    }
                }
                // Без соседа с одной стороны (края не-лупа) касательная нулевая: клип
                // начинается и заканчивается в покое
                key.InTangent = tangent;
                key.OutTangent = tangent;
                track[i] = key;
            }

            // 2. Явные EaseIn/EaseOut — те же кривые, что были: t² и 1-(1-t)²
            for (int i = 0; i < n; i++)
            {
                if (!Loop && i == n - 1)
                    break;
                int j = (i + 1) % n;
                Keyframe from = track[i];
                Keyframe to = track[j];
                if (from.Ease is not (EaseMode.EaseIn or EaseMode.EaseOut))
                    continue;

                float span = j > i ? to.Time - from.Time : Duration - from.Time + to.Time;
                if (span <= 0.001f)
                    continue;

                LayerPose doubledSlope = default;
                for (int c = 0; c < LayerPose.ChannelCount; c++)
                    doubledSlope.Set(c, 2f * (to.Pose.Get(c) - from.Pose.Get(c)) / span);

                if (from.Ease == EaseMode.EaseIn)
                {
                    from.OutTangent = default;   // старт с места
                    to.InTangent = doubledSlope; // приход на полной скорости
                }
                else
                {
                    from.OutTangent = doubledSlope;
                    to.InTangent = default;
                }
                track[i] = from;
                track[j] = to;
            }

            // 3. Стыки с плавными отрезками — без скачка скорости:
            //    • в старт EaseIn плавный отрезок приходит в покой, из финиша EaseOut — стартует из покоя;
            //    • из финиша EaseIn (разгон) плавный отрезок продолжает с той же скоростью, в старт
            //      EaseOut (срыв) — приходит с ней. Если движение на стыке разворачивается (удар и
            //      отскок), скорость не переносится: там разрыв и задуман
            for (int i = 0; i < n; i++)
            {
                bool hasIncoming = i > 0 || Loop;
                bool hasOutgoing = i < n - 1 || Loop;
                if (!hasIncoming || !hasOutgoing)
                    continue;

                int prevIndex = (i - 1 + n) % n;
                int nextIndex = (i + 1) % n;
                EaseMode incoming = track[prevIndex].Ease;
                EaseMode outgoing = track[i].Ease;
                Keyframe key = track[i];

                if (outgoing == EaseMode.EaseIn && incoming == EaseMode.Smooth)
                    key.InTangent = default;
                if (incoming == EaseMode.EaseOut && outgoing == EaseMode.Smooth)
                    key.OutTangent = default;
                if (incoming == EaseMode.EaseIn && outgoing == EaseMode.Smooth)
                    key.OutTangent = CarryTangent(key.InTangent, key.OutTangent, key, track[nextIndex], i, nextIndex);
                if (outgoing == EaseMode.EaseOut && incoming == EaseMode.Smooth)
                    key.InTangent = CarryTangent(key.OutTangent, key.InTangent, track[prevIndex], key, prevIndex, i);
                track[i] = key;
            }
        }

        // Переносит скорость через стык по каналам, где отрезок идёт в ту же сторону. Ограничение то же,
        // что у авто-касательных (не круче трёх наклонов отрезка), — чтобы не перелететь ключ
        private LayerPose CarryTangent(in LayerPose carried, in LayerPose fallback, in Keyframe from, in Keyframe to,
            int fromIndex, int toIndex)
        {
            float span = toIndex > fromIndex ? to.Time - from.Time : Duration - from.Time + to.Time;
            if (span <= 0.001f)
                return fallback;

            LayerPose result = fallback;
            for (int c = 0; c < LayerPose.ChannelCount; c++)
            {
                float slope = (to.Pose.Get(c) - from.Pose.Get(c)) / span;
                float speed = carried.Get(c);
                if (speed * slope <= 0f)
                    continue;
                result.Set(c, Math.Sign(speed) * Math.Min(Math.Abs(speed), 3f * Math.Abs(slope)));
            }
            return result;
        }

        // Ближайший сосед ключа в сторону dir (±1) со временем, развёрнутым через край лупа.
        // Ключ на том же тике (луп с дублем на 0 и на Duration) пропускаем: направления он не даёт
        private bool TryNeighbor(List<Keyframe> track, int i, int dir, out Keyframe neighbor, out float time)
        {
            int n = track.Count;
            float ownTime = track[i].Time;
            for (int step = 1; step <= n; step++)
            {
                int raw = i + dir * step;
                if (!Loop && (raw < 0 || raw >= n))
                    break;

                int index = ((raw % n) + n) % n;
                float wraps = (float)Math.Floor(raw / (float)n);
                time = track[index].Time + wraps * Duration;
                if (Math.Abs(time - ownTime) > 0.001f)
                {
                    neighbor = track[index];
                    return true;
                }
            }
            neighbor = default;
            time = 0f;
            return false;
        }

        // Монотонная касательная (Фрич–Карлсон): ноль в экстремуме, иначе наклон по соседям,
        // но не круче трёх наклонов ближайшего отрезка — тогда кривая не перелетает ключи
        private static float AutoTangent(float prevValue, float prevTime, float value, float time,
            float nextValue, float nextTime)
        {
            float slopeLeft = (value - prevValue) / (time - prevTime);
            float slopeRight = (nextValue - value) / (nextTime - time);
            if (slopeLeft * slopeRight <= 0f)
                return 0f;

            float slope = (nextValue - prevValue) / (nextTime - prevTime);
            float limit = 3f * Math.Min(Math.Abs(slopeLeft), Math.Abs(slopeRight));
            return Math.Sign(slope) * Math.Min(Math.Abs(slope), limit);
        }
    }

    public class AnimPlayer
    {
        private const float CrossfadeTicks = 10f;  // блендинг при смене базового клипа

        // Инерция кроссфейда: старая поза не замирает снимком, а докатывается по своей
        // последней скорости и гаснет. Иначе на каждой смене клипа движение обрывалось в ноль —
        // тот же «стоп» на стыке, что был внутри клипов
        private const float FadeMomentumDamping = 0.72f;
        // Предел скорости, которую поза уносит в кроссфейд (за тик, по каналам LayerPose):
        // Snap-ключ дал бы скачок, а экстраполировать скачок нельзя
        private static readonly float[] FadeMomentumMax = { 0.08f, 6f, 6f, 0.04f, 0.04f, 0.12f, 0.12f };

        private readonly Dictionary<string, AnimClip> _clips = new();
        private readonly string[] _layers;

        private AnimClip _base;
        private float _time;
        private float _prevTime;

        private AnimClip _oneShot;
        private float _oneShotTime;
        private float _oneShotPrevTime;

        // Кроссфейд: снимок баз-поз всех слоёв в момент переключения клипа
        private readonly Dictionary<string, LayerPose> _fadeFrom = new();
        private readonly Dictionary<string, LayerPose> _fadeMomentum = new();
        private float _fadeLeft;
        private float _fadeDuration = CrossfadeTicks;

        // Итоговые позы тика: базовая (для снимка кроссфейда), она же тиком раньше (скорость
        // для инерции) и финальная (база + один-шот)
        private readonly Dictionary<string, LayerPose> _baseCache = new();
        private readonly Dictionary<string, LayerPose> _prevBaseCache = new();
        private readonly Dictionary<string, LayerPose> _cache = new();

        // События: колбэк в момент пересечения метки + полл-флаги для чтения снаружи
        public Action<string> OnEvent;
        private readonly HashSet<string> _pendingEvents = new();

        public AnimPlayer(IEnumerable<AnimClip> clips, params string[] layers)
        {
            _layers = layers;
            foreach (AnimClip clip in clips)
                _clips[clip.Name] = clip;
        }

        public string CurrentClip => _base?.Name;
        public bool IsPlaying(string name) => _base?.Name == name || _oneShot?.Name == name;

        // Сменить базовый клип (с кроссфейдом из текущей позы). Тот же клип — no-op.
        // fade — длина блендинга в тиках: короче для резких выпадов, длиннее для плавных смен.
        public void Play(string name, bool restart = false, float fade = CrossfadeTicks)
        {
            if (!_clips.TryGetValue(name, out AnimClip clip))
                return;
            if (_base == clip && !restart)
                return;

            _fadeFrom.Clear();
            _fadeMomentum.Clear();
            foreach (string layer in _layers)
            {
                LayerPose now = _baseCache.TryGetValue(layer, out LayerPose p) ? p : LayerPose.Identity;
                LayerPose before = _prevBaseCache.TryGetValue(layer, out LayerPose q) ? q : now;
                _fadeFrom[layer] = now;

                LayerPose momentum = default;
                for (int c = 0; c < LayerPose.ChannelCount; c++)
                {
                    float max = FadeMomentumMax[c];
                    momentum.Set(c, MathHelper.Clamp(now.Get(c) - before.Get(c), -max, max));
                }
                _fadeMomentum[layer] = momentum;
            }
            _fadeDuration = Math.Max(1f, fade);
            _fadeLeft = _base != null ? _fadeDuration : 0f;

            _base = clip;
            _time = 0f;
            _prevTime = 0f;
        }

        // Реакция поверх базы (once): играет один раз с конвертом входа/выхода и снимается сама
        public void PlayOnce(string name)
        {
            if (!_clips.TryGetValue(name, out AnimClip clip))
                return;
            _oneShot = clip;
            _oneShotTime = 0f;
            _oneShotPrevTime = 0f;
        }

        // Полл-вариант событий: вернёт true один раз после пересечения метки
        public bool ConsumeEvent(string name) => _pendingEvents.Remove(name);

        // Сглаживание итоговой позы (0..1]: 1 — выключено, меньше — поза догоняет цель за
        // несколько тиков. Страховка от любых скачков, которые не закрывает кроссфейд:
        // Snap-ключи, перезапуск клипа, резкий вход реакции. Цена — отставание на ~1 тик
        public float OutputResponse { get; set; } = 1f;

        // rate — скорость базового клипа (темп атаки владельца). Реакции (один-шоты)
        // и кроссфейд идут в реальном времени: удар по боссу не должен замедляться с ним
        public void Update(float rate = 1f)
        {
            if (_base != null)
            {
                _prevTime = _time;
                _time += rate;
                if (_base.Loop)
                {
                    if (_time >= _base.Duration)
                        _time -= _base.Duration;
                }
                else
                {
                    _time = Math.Min(_time, _base.Duration);
                }
                FireEvents(_base, _prevTime, _time);
            }

            if (_oneShot != null)
            {
                _oneShotPrevTime = _oneShotTime;
                _oneShotTime += 1f;
                FireEvents(_oneShot, _oneShotPrevTime, _oneShotTime);
                if (_oneShotTime >= _oneShot.Duration)
                    _oneShot = null;
            }

            if (_fadeLeft > 0f)
                _fadeLeft -= 1f;

            // Кривые блендинга плавные (smoothstep): на линейных вес менялся с постоянной
            // скоростью от первого тика до последнего, и начало/конец перехода читались изломом
            float fadeWeight = SmoothWeight(_fadeLeft / _fadeDuration);
            float oneShotEnvelope = _oneShot == null ? 0f : SmoothWeight(Math.Min(
                _oneShotTime / _oneShot.FadeIn, (_oneShot.Duration - _oneShotTime) / _oneShot.FadeOut));

            foreach (string layer in _layers)
            {
                LayerPose basePose = _base?.Sample(layer, _time) ?? LayerPose.Identity;
                if (_fadeLeft > 0f && _fadeFrom.TryGetValue(layer, out LayerPose from))
                {
                    from = CoastFadePose(layer, from);
                    basePose = LayerPose.Lerp(basePose, from, fadeWeight);
                }

                if (_baseCache.TryGetValue(layer, out LayerPose previous))
                    _prevBaseCache[layer] = previous;
                _baseCache[layer] = basePose;

                if (_oneShot != null)
                    basePose = LayerPose.Combine(basePose, LayerPose.Faded(_oneShot.Sample(layer, _oneShotTime), oneShotEnvelope));
                _cache[layer] = OutputResponse < 1f && _cache.TryGetValue(layer, out LayerPose shown)
                    ? LayerPose.Lerp(shown, basePose, OutputResponse)
                    : basePose;
            }
        }

        // Старая поза докатывается по инерции: шаг скорости, затем её затухание
        private LayerPose CoastFadePose(string layer, LayerPose from)
        {
            if (!_fadeMomentum.TryGetValue(layer, out LayerPose momentum))
                return from;

            for (int c = 0; c < LayerPose.ChannelCount; c++)
            {
                from.Set(c, from.Get(c) + momentum.Get(c));
                momentum.Set(c, momentum.Get(c) * FadeMomentumDamping);
            }
            _fadeFrom[layer] = from;
            _fadeMomentum[layer] = momentum;
            return from;
        }

        private static float SmoothWeight(float t)
        {
            t = MathHelper.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        // Итоговая аддитивная поза слоя. До первого Update (бестиарий, первый кадр) — Identity.
        public LayerPose Pose(string layer)
            => _cache.TryGetValue(layer, out LayerPose p) ? p : LayerPose.Identity;

        private void FireEvents(AnimClip clip, float prev, float now)
        {
            bool wrapped = clip.Loop && now < prev; // луп перескочил через край клипа
            foreach ((float time, string name) in clip.Events)
            {
                bool crossed = wrapped
                    ? time > prev || time <= now
                    : time > prev && time <= now;
                if (!crossed)
                    continue;
                _pendingEvents.Add(name);
                OnEvent?.Invoke(name);
            }
        }
    }
}
