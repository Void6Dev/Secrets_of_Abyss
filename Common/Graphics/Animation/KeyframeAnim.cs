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
        Smooth,   // SmoothStep — дефолт
        EaseIn,   // разгон (медленный старт)
        EaseOut,  // торможение (медленный финиш)
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
    }

    public struct Keyframe
    {
        public float Time;     // тик внутри клипа (60/с)
        public LayerPose Pose;
        public EaseMode Ease;  // сглаживание на пути К СЛЕДУЮЩЕМУ ключу
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
            return LayerPose.Lerp(prev.Pose, next.Pose, Apply(prev.Ease, t));
        }

        private static float Apply(EaseMode ease, float t) => ease switch
        {
            EaseMode.Smooth => t * t * (3f - 2f * t),
            EaseMode.EaseIn => t * t,
            EaseMode.EaseOut => 1f - (1f - t) * (1f - t),
            EaseMode.Snap => t < 1f ? 0f : 1f,
            _ => t,
        };
    }

    public class AnimPlayer
    {
        private const float CrossfadeTicks = 10f;  // блендинг при смене базового клипа

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
        private float _fadeLeft;
        private float _fadeDuration = CrossfadeTicks;

        // Итоговые позы тика: базовая (для снимка кроссфейда) и финальная (база + один-шот)
        private readonly Dictionary<string, LayerPose> _baseCache = new();
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
            foreach (string layer in _layers)
                _fadeFrom[layer] = _baseCache.TryGetValue(layer, out LayerPose p) ? p : LayerPose.Identity;
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

        public void Update()
        {
            if (_base != null)
            {
                _prevTime = _time;
                _time += 1f;
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

            foreach (string layer in _layers)
            {
                LayerPose basePose = _base?.Sample(layer, _time) ?? LayerPose.Identity;
                if (_fadeLeft > 0f && _fadeFrom.TryGetValue(layer, out LayerPose from))
                    basePose = LayerPose.Lerp(basePose, from, _fadeLeft / _fadeDuration);
                _baseCache[layer] = basePose;

                if (_oneShot != null)
                {
                    float env = MathHelper.Clamp(Math.Min(_oneShotTime / _oneShot.FadeIn,
                        (_oneShot.Duration - _oneShotTime) / _oneShot.FadeOut), 0f, 1f);
                    basePose = LayerPose.Combine(basePose, LayerPose.Faded(_oneShot.Sample(layer, _oneShotTime), env));
                }
                _cache[layer] = basePose;
            }
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
