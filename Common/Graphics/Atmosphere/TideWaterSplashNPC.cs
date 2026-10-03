using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using SoA.Content.Worldgen;

namespace SoA.Common.Graphics.Atmosphere
{
    // Существа, пересекающие поверхность воды Прилива: выпрыгнувшая рыба, нырнувший
    // враг — всплеск и рябь по скорости. Только клиент, по синхронным позиции и wet
    public class TideWaterSplashNPC : GlobalNPC
    {
        private const float MinSpeed = 2f;
        private const float FullSpeed = 12f;
        private const int MaxBodySize = 160;   // огромные боссы своими всплесками заняты сами

        private bool _wasWet;
        private Vector2 _lastVelocity;

        public override bool InstancePerEntity => true;

        public override void PostAI(NPC npc)
        {
            if (Main.dedServ)
                return;

            bool wet = npc.wet && !npc.lavaWet && !npc.honeyWet && !npc.shimmerWet;
            if (wet != _wasWet && Math.Max(npc.width, npc.height) <= MaxBodySize
                && TideOfShadowsWorldData.Contains(npc.Center))
                Splash(npc, wet);

            _wasWet = wet;
            _lastVelocity = npc.velocity;
        }

        private void Splash(NPC npc, bool entering)
        {
            float speed = _lastVelocity.Length();
            if (speed < MinSpeed)
                return;

            float strength = (speed - MinSpeed) / (FullSpeed - MinSpeed) * Math.Min(npc.width / 24f, 1f);
            if (entering)
            {
                if (TideWaterFx.TryFindSurface(npc.Bottom - new Vector2(0f, 2f), out Vector2 surface))
                    TideWaterFx.EntrySplash(surface, strength, _lastVelocity);
            }
            else
            {
                TideWaterFx.ExitSplash(new Vector2(npc.Center.X, npc.Bottom.Y), strength);
            }
        }
    }
}
