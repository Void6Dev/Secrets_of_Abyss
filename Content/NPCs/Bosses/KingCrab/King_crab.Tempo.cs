using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Темп боя и связки атак.
    //
    // ТЕМП. Король разгоняется по мере того, как теряет здоровье, — плавно, без ступенек
    // по фазам. В начале боя он медленнее прежнего: каждый замах успевается прочитать.
    // У края смерти — быстрее прежней агонии. Длительности атак НЕ меняются: меняется то,
    // с какой скоростью убывает Timer (и с той же скоростью играет клип). Поэтому все
    // привязки «удар на 44-м тике замаха» и телеграфы по доле Timer остаются верными.
    //
    // СВЯЗКИ. Чем меньше у короля HP, тем чаще он продолжает атаку сразу следующей —
    // по таблице осмысленных пар, а не случайно. Решает только сервер.
    public partial class King_crab
    {
        // ---------- ТЕМП ПО ЗДОРОВЬЮ ----------
        private const float TempoFullHealth = 0.8f;    // скорость замахов и отходов в начале боя
        private const float TempoNearDeath = 1.3f;     // и на последних HP
        private const float WalkSpeedFullHealth = 3.4f;
        private const float WalkSpeedNearDeath = 7.8f;
        private const float PauseFullHealth = 110f;    // пауза между атаками, тики
        private const float PauseNearDeath = 24f;
        private const float PauseCurve = 0.8f;         // < 1: пауза сокращается заметнее уже с первых потерь

        // ---------- СВЯЗКИ ----------
        private const float ComboStartHealth = 0.75f;  // выше этой доли HP связок нет
        private const float ComboFullHealth = 0.15f;   // здесь шанс связки максимален
        private const float ComboMaxChance = 0.9f;
        private const int ComboGapTicks = 12;          // короткий вдох между атаками связки
        private const int ComboMaxLengthDesperate = 2; // продолжений подряд в агонии (до неё — одно)

        // ---------- ШАГ НА УСТУП ----------
        private const float MaxStepUp = 40f;           // уступ до двух с половиной блоков — шагом, а не упором
        private const float StepVisualDecay = 0.72f;   // как быстро туша догоняет шаг вверх на картинке

        private float _actionTempo = 1f;  // темп текущей атаки: фиксируется на входе, шлётся по сети
        private float _timerStep = 1f;    // на сколько Timer убыл в прошлом тике
        private int _aiTicks;             // счётчик тиков AI — для периодической косметики

        // Чисто серверное
        private CrabState _queuedCombo = CrabState.Scuttle;
        private int _comboLength;

        private float _stepVisualLift;    // косметика: на сколько туша ещё «отстаёт» от шага вверх

        // 0 — полное здоровье, 1 — при смерти
        private float Fury => MathHelper.Clamp(1f - NPC.life / (float)NPC.lifeMax, 0f, 1f);

        private float TempoForHealth() => MathHelper.Lerp(TempoFullHealth, TempoNearDeath, Fury);

        private float MaxWalkSpeed() => MathHelper.Lerp(WalkSpeedFullHealth, WalkSpeedNearDeath, Fury);

        private float PauseBetweenAttacks()
            => MathHelper.Lerp(PauseFullHealth, PauseNearDeath, (float)Math.Pow(Fury, PauseCurve));

        private static bool IsTempoState(CrabState state) => state
            is CrabState.ClawSlam or CrabState.ClawSweep or CrabState.BubbleVolley or CrabState.JumpCrush
            or CrabState.TideCall or CrabState.Burrow or CrabState.CrushingGrip or CrabState.CrownCommand
            or CrabState.TsunamiClap or CrabState.RoyalRoar;

        // С какой скоростью сейчас идёт время атаки. Вне атак (ходьба, ярость, кат-сцены) — 1
        private float ActionTempo => IsTempoState(State) ? _actionTempo : 1f;

        // Конец тика AI: Timer убывает на темп, а не на единицу
        private void AdvanceTimer()
        {
            _aiTicks++;
            _timerStep = ActionTempo;
            if (Timer > 0f)
                Timer = Math.Max(0f, Timer - _timerStep);
        }

        // Timer в этом тике прошёл отметку. Замена точным сравнениям «Timer == 20»: при дробном
        // темпе Timer через ровные значения не проходит. При темпе 1 срабатывает на том же тике
        private bool TimerPassed(float mark) => Timer <= mark && Timer + _timerStep > mark;

        // Периодическая косметика (пыль, брызги) — по тикам AI, а не по Timer: он теперь дробный
        private bool EveryTicks(int interval) => _aiTicks % interval == 0;

        // --- Связки ---

        // Осмысленные продолжения: каждое пользуется тем, что оставила предыдущая атака
        private CrabState ComboFollowUp(CrabState finished) => finished switch
        {
            CrabState.ClawSlam => CrabState.ClawSweep,                                   // ударил — и сразу рывком вдогонку
            CrabState.ClawSweep => Phase2 ? CrabState.TsunamiClap : CrabState.ClawSlam,  // доехал вплотную — хлопок
            CrabState.Burrow => CrabState.ClawSlam,                                      // вынырнул под игроком — слэм
            CrabState.BubbleVolley => Phase2 ? CrabState.RoyalRoar : CrabState.Burrow,   // рёв разворачивает свой же залп
            CrabState.JumpCrush => CrabState.BubbleVolley,
            CrabState.CrushingGrip => CrabState.ClawSlam,
            CrabState.TsunamiClap => CrabState.JumpCrush,
            CrabState.TideCall => CrabState.JumpCrush,                                   // перепрыгивает собственную стену
            _ => CrabState.Scuttle,
        };

        private float ComboChance()
        {
            float health = 1f - Fury;
            float t = (ComboStartHealth - health) / (ComboStartHealth - ComboFullHealth);
            return MathHelper.Clamp(t, 0f, 1f) * ComboMaxChance;
        }

        // Решение о связке на выходе из атаки. Только сервер: у клиентов свой rand
        private bool TryQueueCombo(CrabState finished)
        {
            _queuedCombo = CrabState.Scuttle;
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return false;

            int maxLength = Desperate ? ComboMaxLengthDesperate : 1;
            CrabState followUp = ComboFollowUp(finished);
            if (followUp == CrabState.Scuttle || _comboLength >= maxLength || Main.rand.NextFloat() >= ComboChance())
                return false;

            _queuedCombo = followUp;
            return true;
        }

        // Забрать продолжение связки, если оно ещё имеет смысл (цель на нужной дистанции и т.п.)
        private bool TryTakeQueuedCombo(float dist, out CrabState combo)
        {
            combo = _queuedCombo;
            _queuedCombo = CrabState.Scuttle;
            if (combo == CrabState.Scuttle || !AttackReady(combo, dist))
            {
                _comboLength = 0;
                return false;
            }
            _comboLength++;
            return true;
        }
    }
}
