using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using SoA.Common.Utils;
using SoA.Content.Worldgen;

namespace SoA.Common.Graphics.Atmosphere
{
    // Настроение воды в данной зоне. Значения — для середины зоны, между серединами
    // соседних зон профиль смешивается по непрерывной глубине
    public struct TideAtmosphereProfile
    {
        public Vector3 Tint;            // оттенок толщи воды, 0..1
        public float TintStrength;      // насколько сильно кадр уходит в оттенок
        public float Vignette;          // затемнение краёв, 0..1
        public float Warp;              // колыхание картинки, px экрана
        public float SnowDensity;       // морской снег, штук на экран 1920x1080
        public float PlanktonDensity;   // светящийся планктон
        public float EyeDensity;        // пары «глаз» в темноте
        public float VentRate;          // шанс за тик найти новый источник пузырей

        public static TideAtmosphereProfile Lerp(in TideAtmosphereProfile a, in TideAtmosphereProfile b, float t)
        {
            return new TideAtmosphereProfile
            {
                Tint = Vector3.Lerp(a.Tint, b.Tint, t),
                TintStrength = MathHelper.Lerp(a.TintStrength, b.TintStrength, t),
                Vignette = MathHelper.Lerp(a.Vignette, b.Vignette, t),
                Warp = MathHelper.Lerp(a.Warp, b.Warp, t),
                SnowDensity = MathHelper.Lerp(a.SnowDensity, b.SnowDensity, t),
                PlanktonDensity = MathHelper.Lerp(a.PlanktonDensity, b.PlanktonDensity, t),
                EyeDensity = MathHelper.Lerp(a.EyeDensity, b.EyeDensity, t),
                VentRate = MathHelper.Lerp(a.VentRate, b.VentRate, t),
            };
        }
    }

    // Атмосфера Прилива Теней по глубине игрока. Сама ничего не рисует: считает сглаженное
    // состояние, которое читают экранный фильтр (TideUnderwaterFx) и частицы (TideAmbience).
    //
    // Глубина по концепту передаётся не общей темнотой (сумрак над биомом убран намеренно),
    // а оттенком, виньеткой по краям, светом с поверхности и тем, что плавает в воде.
    //
    // Только клиент, только локальный игрок: всё выводится из позиции и синхронных
    // обмеров биома, по сети ничего не шлётся
    public class TideAtmosphere : ModSystem
    {
        private static readonly TideAtmosphereProfile[] ZoneProfiles =
        {
            // I. Затопленный порт: бирюза, свет сверху, почти чисто
            new() { Tint = new Vector3(0.27f, 0.74f, 0.78f), TintStrength = 0.20f, Vignette = 0.14f, Warp = 1.2f,
                SnowDensity = 45f, PlanktonDensity = 4f, EyeDensity = 0f, VentRate = 0.020f },
            // II. Чёрные леса: сине-фиолетовая толща, биолюминесценция
            new() { Tint = new Vector3(0.28f, 0.34f, 0.72f), TintStrength = 0.36f, Vignette = 0.28f, Warp = 1.0f,
                SnowDensity = 80f, PlanktonDensity = 70f, EyeDensity = 0f, VentRate = 0.015f },
            // III. Погружённый город: холодная серая синева, планктон редеет
            new() { Tint = new Vector3(0.34f, 0.40f, 0.62f), TintStrength = 0.40f, Vignette = 0.34f, Warp = 0.8f,
                SnowDensity = 70f, PlanktonDensity = 22f, EyeDensity = 0f, VentRate = 0.010f },
            // IV. Разлом: биолюминесценцию сменяют красные точки
            new() { Tint = new Vector3(0.46f, 0.24f, 0.32f), TintStrength = 0.42f, Vignette = 0.42f, Warp = 0.6f,
                SnowDensity = 40f, PlanktonDensity = 4f, EyeDensity = 10f, VentRate = 0.006f },
            // V. Бездна: пусто. Почти ничего не плавает — пустота читается как масштаб
            new() { Tint = new Vector3(0.16f, 0.17f, 0.32f), TintStrength = 0.48f, Vignette = 0.50f, Warp = 0.5f,
                SnowDensity = 10f, PlanktonDensity = 0f, EyeDensity = 1.5f, VentRate = 0.001f },
        };

        private const float PresenceFadePerTick = 0.025f;   // вход/выход из биома, ~40 тиков
        private const float SubmergeFadePerTick = 0.06f;    // нырок/всплытие, ~16 тиков
        private const float DepthFollowRate = 0.04f;        // доля разрыва глубины за тик
        private const float HeadOffsetPx = 12f;             // голова игрока от центра

        // 0..1: игрок в биоме
        public static float Presence { get; private set; }

        // 0..1: голова под водой — оттенок, виньетка и колыхание кадра
        public static float Submersion { get; private set; }

        // Непрерывная глубина игрока 0..5 (см. DepthLevelAt), сглаженная
        public static float DepthLevel { get; private set; }

        // Профиль на текущей глубине
        public static TideAtmosphereProfile Profile { get; private set; } = ZoneProfiles[0];

        public static bool IsActive => Presence > 0.001f;

        public override void OnWorldUnload()
        {
            Presence = 0f;
            Submersion = 0f;
            DepthLevel = 0f;
            Profile = ZoneProfiles[0];
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            Player player = Main.LocalPlayer;
            bool inBiome = player.active && !player.dead && player.InModBiome<TideOfShadowsBiome>();
            Vector2 head = player.Center - new Vector2(0f, HeadOffsetPx * player.gravDir);
            bool submerged = inBiome && SoACombat.IsInWater(head);

            Presence = Approach(Presence, inBiome ? 1f : 0f, PresenceFadePerTick);
            Submersion = Approach(Submersion, submerged ? 1f : 0f, SubmergeFadePerTick);

            Point tile = player.Center.ToTileCoordinates();
            float targetDepth = TideOfShadowsWorldData.DepthLevelAt(tile.X, tile.Y);
            if (targetDepth >= 0f)
                DepthLevel += (targetDepth - DepthLevel) * DepthFollowRate;

            Profile = SampleProfile(DepthLevel);
        }

        // Середина зоны N (0-based) лежит на глубине N + 0.5
        private static TideAtmosphereProfile SampleProfile(float depthLevel)
        {
            float position = Math.Clamp(depthLevel - 0.5f, 0f, ZoneProfiles.Length - 1);
            int lower = Math.Min((int)position, ZoneProfiles.Length - 2);
            float t = MathHelper.SmoothStep(0f, 1f, position - lower);
            return TideAtmosphereProfile.Lerp(ZoneProfiles[lower], ZoneProfiles[lower + 1], t);
        }

        private static float Approach(float value, float target, float step)
            => value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    }
}
