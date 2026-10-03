using System;
using Microsoft.Xna.Framework;
using Terraria;
using SoA.Common.Config;
using SoA.Content.Worldgen;

namespace SoA.Common.Systems
{
    // Течения Прилива: поле скоростей воды, px за тик. Своё у каждой зоны:
    //   I   — приливный размах: вода медленно ходит к берегу и обратно;
    //   II  — ленивые петли между водорослями;
    //   III — слабые нисходящие потоки в шахтах города;
    //   IV  — Разлом: чередующиеся вертикальные полосы подъёма и провала, самые сильные;
    //   V   — Бездна стоит неподвижно.
    // Поле — чистая функция места и времени: у всех клиентов одинаковое без синхронизации.
    // Пока это экспериментальная физика (SoAClientConfig.ExperimentalWaterPhysics)
    public static class TideCurrents
    {
        private const float TidePeriodSeconds = 40f;
        private const float TideStrength = 0.35f;
        private const float ForestStrength = 0.25f;
        private const float CityDowndraft = 0.2f;
        private const float RiftStrength = 1.1f;
        private const float RiftBandTiles = 18f;          // ширина полосы подъёма или провала

        public static bool Enabled => SoAClientConfig.Instance.ExperimentalWaterPhysics;

        public static Vector2 At(Vector2 worldPosition)
        {
            Point tile = worldPosition.ToTileCoordinates();
            float depth = TideOfShadowsWorldData.DepthLevelAt(tile.X, tile.Y);
            if (depth < 0f)
                return Vector2.Zero;

            float time = Main.GlobalTimeWrappedHourly;
            float towardShore = TideOfShadowsWorldData.InlandDir;

            Vector2 current = depth switch
            {
                < 1f => new Vector2(towardShore * (float)Math.Sin(time * MathHelper.TwoPi / TidePeriodSeconds) * TideStrength, 0f),
                < 2f => new Vector2((float)Math.Sin(tile.Y * 0.035f + time * 0.12f),
                    (float)Math.Sin(tile.X * 0.03f - time * 0.09f) * 0.4f) * ForestStrength,
                < 3f => new Vector2(0f, CityDowndraft * Math.Max(0f, (float)Math.Sin(tile.X * 0.09f + time * 0.05f))),
                < 4f => new Vector2((float)Math.Sin(tile.Y * 0.02f + time * 0.07f) * 0.25f,
                    (float)Math.Sin(tile.X / RiftBandTiles * MathHelper.Pi + time * 0.06f)) * RiftStrength,
                _ => Vector2.Zero,
            };

            // На стыке зон течения сливаются, а не обрываются на одной строке тайлов
            // (у поверхности стыка нет — там течение в полную силу)
            float boundary = (float)Math.Round(depth);
            float blend = boundary == 0f ? 1f : MathHelper.SmoothStep(0f, 1f, Math.Abs(depth - boundary) * 8f);
            return current * blend;
        }
    }
}
