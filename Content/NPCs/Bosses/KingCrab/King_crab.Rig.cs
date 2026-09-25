using Microsoft.Xna.Framework;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // ==========================================================================================
    //  НАСТРОЙКА РИГА КОРОЛЯ-КРАБА — все числа позы, размеров и походки
    // ==========================================================================================
    public partial class King_crab
    {
        // ---------- ТЕЛО ----------
        private const float BodyLift = 30f;          // на сколько px панцирь приподнят над землёй
        private const float BodySquashAmount = 0.32f; // предел деформации тела (squash & stretch)

        // ---------- НОГИ ----------
        // Досягаемость ноги = (LegBoneUpperPx + LegBoneLowerPx) * LegScale и должна быть заметно
        // БОЛЬШЕ расстояния бедро→грунт, иначе IK клампится, колени распрямляются в спички и
        // стопы повисают над землёй. Сейчас задействовано 0.80–0.88 длины.
        private const float LegScale = 1.6f;         // размер ног относительно тела
        private const float LegBoneUpperPx = 40f;    // пивот→колено в KingCrabLegUpper.png
        private const float LegBoneLowerPx = 44f;    // колено→лапка в KingCrabLegLower.png

        // Пивоты (сустав ВНУТРИ текстуры) — менять только при перерисовке спрайта
        private static readonly Vector2 UpperPivot = new(2f, 8f);
        private static readonly Vector2 LowerPivot = new(2f, 6f);

        // Бёдра относительно центра тела (правая сторона; левая зеркалится по X).
        // 4 ноги на бок; посажены ниже по панцирю, чтобы ноге хватало длины до грунта.
        private static readonly Vector2[] HipLocal =
        {
            new(30f, 18f),
            new(52f, 26f),
            new(74f, 30f),
            new(96f, 28f),
        };

        // Насколько стопа стоит наружу от бедра по X (крабья раскоряка). Длина массива
        // обязана совпадать с HipLocal — это число ног на бок.
        private static readonly float[] FootSpread = { 44f, 40f, 42f, 52f };

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
        // ВНИМАНИЕ: ArmBone*Px — это НЕ размер текстуры, а РАССТАНОВКА круглых шаров вдоль руки.
        // Спрайты сегментов круглые, поэтому:
        //   • шары обязаны перекрываться, иначе рука рвётся на отдельные бусины;
        //   • ArmBoneLowerPx задаёт, насколько клешня накрывает локтевой шар: меньше — хоронит
        //     его целиком, больше — между ними появляется просвет;
        //   • ArmScale масштабирует И размер шаров, И расстояния между ними (l = ArmBone*Px *
        //     ArmScale). Хочешь только шары крупнее — подними ArmScale и урежь ArmBone*Px.
        // Длина руки = (ArmBoneUpperPx + ArmBoneLowerPx) * ArmScale. Если запястье уносится
        // дальше (клипы прибавляют ox до 44), рука РАСТЯГИВАЕТСЯ — шары расходятся.
        private const float ArmScale = 1.5f;         // размер сегментов руки относительно тела
        private const float ArmBoneUpperPx = 20f;    // плечо→локоть
        private const float ArmBoneLowerPx = 20f;    // локоть→запястье
        private const float ArmElbowSign = -1f;       // сторона сгиба локтя: +1 вверх, -1 вниз

        // Центры шаров ВНУТРИ текстур — менять только при перерисовке спрайтов
        private static readonly Vector2 ArmUpperPivot = new(10.5f, 18f); // KingCrabArmUpper.png (34x33)
        private static readonly Vector2 ArmLowerPivot = new(5f, 25.5f); // KingCrabArmLower.png (23x22)

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
