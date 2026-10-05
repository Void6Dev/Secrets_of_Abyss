using Microsoft.Xna.Framework;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ==========================================================================================
    //  НАСТРОЙКА РИГА КОРОЛЯ-КРАБА — все числа позы, размеров и походки
    // ==========================================================================================
    public partial class King_crab
    {
        // ---------- ТЕЛО ----------
        private const float BodyLift = 10f;          // на сколько px панцирь приподнят над землёй: краб сидит низко, на разведённых лапах
        private const float BodySquashAmount = 0.32f; // предел деформации тела (squash & stretch)

        // ---------- НОГИ ----------
        // Досягаемость ноги = (LegBoneUpperPx + LegBoneLowerPx) * LegScale и должна быть заметно
        // БОЛЬШЕ расстояния бедро→грунт, иначе IK клампится, колени распрямляются в спички и
        // стопы повисают над землёй. Сейчас задействовано 0.74–0.91 длины.
        private const float LegScale = 2f;           // размер ног относительно тела
        private const float LegBoneUpperPx = 40f;    // пивот→колено в KingCrabLegUpper.png
        private const float LegBoneLowerPx = 44f;    // колено→лапка в KingCrabLegLower.png

        // Пивоты (сустав ВНУТРИ текстуры) — менять только при перерисовке спрайта
        private static readonly Vector2 UpperPivot = new(2f, 8f);
        private static readonly Vector2 LowerPivot = new(2f, 6f);

        // Бёдра относительно центра тела (правая сторона; левая зеркалится по X).
        // 4 ноги на бок, все на боковой кромке панциря, сверху вниз. Раньше бёдра сидели
        // под брюхом, стопы — почти под ними, и ноги висели гребёнкой прямых спичек.
        private static readonly Vector2[] HipLocal =
        {
            new(96f, 2f),
            new(104f, 16f),
            new(104f, 30f),
            new(96f, 44f),
        };

        // Насколько стопа стоит наружу от бедра по X (крабья раскоряка), в px ноги до LegScale.
        // Нижние бёдра ниже — им и шагать дальше: стопы расходятся веером, колени аркой над ними.
        // Длина массива обязана совпадать с HipLocal — это число ног на бок.
        private static readonly float[] FootSpread = { 30f, 46f, 60f, 68f };

        // ---------- ПОХОДКА ----------
        private const int StepDuration = 12;         // тиков на шаг в покое
        private const int StepDurationFast = 6;      // тиков на шаг на полной скорости
        private const float StepTrigger = 30f;       // отход стопы до перестановки в покое
        private const float StepTriggerFast = 18f;   // то же на полной скорости (короче шаг)
        private const float StepLift = 14f;          // высота подъёма стопы в покое
        private const float StepLiftFast = 24f;      // выше на скорости — читаемая рысь
        private const float StepLead = 12f;          // базовый заброс стопы вперёд
        private const float StepLeadPerSpeed = 2.4f; // доп. заброс за каждую единицу скорости
        private const float FullSpeed = 7f;          // при какой |velocity.X| походка «на полной»
        private const float FootProbeDepth = 240f;   // как глубоко искать землю под стопой
        private const float LegReachSafety = 0.95f;  // доля длины ноги, дальше которой стопу не ставим

        // ---------- РУКА (плечо → локоть → запястье) ----------
        // Сегменты нарисованы «вниз» от сустава: верхний сустав у кромки текстуры, ось кости — +Y.
        // Каждый поворачивается вдоль своей кости (раньше спрайты считались круглыми шарами и
        // рисовались без поворота — продолговатые сегменты висели кубиками и не стыковались).
        // ArmBone*Px — расстояние сустав→сустав ВНУТРИ текстуры, длина руки = их сумма * ArmScale.
        // Если запястье уносится дальше (клипы прибавляют ox до 44), сегменты вытягиваются
        // вдоль кости ровно до следующего сустава — рука не рвётся.
        private const float ArmScale = 1.5f;         // размер сегментов руки относительно тела
        private const float ArmBoneUpperPx = 23f;    // плечо→локоть
        private const float ArmBoneLowerPx = 20f;    // локоть→запястье
        private const float ArmElbowSign = -1f;       // сторона сгиба локтя: +1 вверх, -1 вниз

        // Верхний сустав (точка крепления) ВНУТРИ текстур — менять только при перерисовке спрайтов
        private static readonly Vector2 ArmUpperJoint = new(15f, 7f); // KingCrabArmUpper.png (30x33)
        private static readonly Vector2 ArmLowerJoint = new(10f, 5f); // KingCrabArmLower.png (20x27)

        // ---------- КЛЕШНИ ----------
        // Плечевой шар садится на КРОМКУ панциря (на этой высоте она в -95px от центра),
        // наполовину перекрывая её: так он читается суставом, а не блямбой посреди морды.
        private const float ClawShoulderX = 110f;    // вынос плеча вперёд от центра тела
        private const float ClawShoulderY = -20f;   // высота плеча относительно центра тела
        // Запястье — тоже от ЦЕНТРА ТЕЛА, а не от плеча. Поэтому плечо и клешню можно
        // двигать порознь. Разница между ними и есть длина, которую перекрывает рука.
        private static readonly Vector2 ClawWristRest = new(140.5f, -40f);

        private const float ClawScale = 1f;          // размер клешни относительно панциря
        private const float ClawStanceRaise = 0.3f;  // базовый подъём клешни в стойке (рад)
        private const float ClawRestOpen = 0.25f;    // пинцер приоткрыт в стойке (0..1)
        private const float ClawOpenAngle = 0.5f;    // разворот когтя при полном раскрытии (рад)
        private const float ClawBackRestBias = 0.45f; // доп. разворот дальней клешни наружу
        private const float ClawSquashDip = 12f;     // просадка запястий при плюхе тела (px на ед. squash)
        private const float ClawDirFix = 1f;         // = -1, если клешни смотрят в противоположную сторону

        // Анкеры ВНУТРИ текстур клешни — менять только при перерисовке спрайтов
        private static readonly Vector2 ClawBaseShoulder = new(57f, 15f); // крепление к запястью
        private static readonly Vector2 ClawBaseHinge = new(66f, 53f);    // шарнир когтя в base
        private static readonly Vector2 ClawTipHinge = new(10f, 17f);     // тот же шарнир в tip

        // ---------- ПОЛИРОВКА: ВЕС И ИНЕРЦИЯ ----------
        // Эти числа добавлены поверх рига и на существующие позы не влияют, пока равны нулю.
        private const float GaitBobHeight = 5f;      // на сколько корпус оседает в фазе переноса лап
        private const float StepLiftRearBias = 0.12f; // насколько ниже поднимает стопу каждая следующая (задняя) нога
        private const float LegSpreadPerAux = 1f;    // множитель канала Aux слоя legs на FootSpread
        private const int IdleStepMinDelay = 180;    // раз в столько тиков одна нога переступает в покое
        private const int IdleStepMaxDelay = 300;
        private const float IdleStepDistance = 8f;   // и на столько px
        private const float CliffProbeLeadCut = 0.5f; // насколько урезать заброс стопы у обрыва
        private const float SquashSpringK = 0.22f;   // жёсткость пружины плюхи (даёт отскок)
        private const float SquashSpringDamp = 0.42f; // и её демпфирование
        private const float ClawSquashDipExtra = 4f;  // доп. просадка запястий именно на приземлении

        // ---------- КОРОНА ----------
        private const float CrownOffsetX = 10f;       // вынос короны вперёд: сидит на «лбу»
        private const float CrownOffsetY = -60f;     // высота короны над центром тела
        private const float CrownCareTilt = 0.1f;    // наклон, когда король её придерживает
        private const float CrownCarePress = 4f;     // и прижатие вниз (px)
    }
}
