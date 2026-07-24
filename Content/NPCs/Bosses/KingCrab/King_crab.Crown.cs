using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace SoA.Content.NPCs.Bosses.KingCrab
{
    // Корона Короля-краба — отдельный спрайт поверх панциря.
    // Чистая косметика: сидит на теле (едет вместе со squash & stretch), светится как слабое место,
    // при «заботе» прижимается клешнёй, в постановочной смерти слетает и падает на землю.
    // Считается локально на каждом клиенте из синхронизированных State/Timer — по сети ничего не шлём.
    public partial class King_crab
    {
        private const float CrownTiltWalk = 0.03f;    // покачивание в шаг (рад)
        private const float CrownCareTilt = 0.1f;     // наклон, когда король её придерживает
        private const float CrownCarePress = 4f;      // и прижатие вниз (px)
        private const float CrownFallGravity = 0.35f; // гравитация слетевшей короны
        private const float CrownDropAtDying = 0.6f;  // доля Timer стадии Dying, когда корона слетает

        private Asset<Texture2D> _crownTex;
        private Asset<Texture2D> _crownGlowTex;

        // Слетевшая корона: локальная симуляция; старт детерминирован по синхронизированному Timer
        private bool _crownFalling;
        private bool _crownLanded;
        private Vector2 _crownPos;
        private Vector2 _crownVel;
        private float _crownRot;
        private float _crownRotVel;

        private void UpdateCrown()
        {
            bool shouldFall = State == CrabState.Dying && Timer <= DyingTicks * CrownDropAtDying;
            if (!shouldFall)
            {
                _crownFalling = false;
                _crownLanded = false;
                return;
            }

            if (!_crownFalling) // момент срыва: соскальзывает назад и вверх
            {
                _crownFalling = true;
                _crownLanded = false;
                _crownPos = CrownWorldPos(out _crownRot);
                int dir = NPC.spriteDirection;
                _crownVel = new Vector2(-dir * 1.5f, -3f);
                _crownRotVel = -dir * 0.05f;
                SoundEngine.PlaySound(SoundID.Tink with { Pitch = -0.2f, Volume = 0.6f }, _crownPos);
            }

            if (_crownLanded)
                return;

            _crownVel.Y += CrownFallGravity;
            _crownPos += _crownVel;
            _crownRot += _crownRotVel;

            float ground = FindGroundY(_crownPos.X, _crownPos.Y - 8f, 600f, true);
            if (!float.IsNaN(ground) && _crownPos.Y + 10f >= ground)
            {
                _crownPos.Y = ground - 10f;
                _crownRot *= 0.4f; // почти выравнивается, лёжа на песке
                _crownLanded = true;
                SoundEngine.PlaySound(SoundID.Tink with { Pitch = 0.1f, Volume = 0.5f }, _crownPos);
                if (Main.netMode != NetmodeID.Server)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        Dust d = Dust.NewDustPerfect(_crownPos + new Vector2(0f, 8f), DustID.Sand,
                            new Vector2(Main.rand.NextFloatDirection() * 1.5f, -Main.rand.NextFloat(0.5f, 1.5f)));
                        d.scale = 1.1f;
                    }
                }
            }
        }

        // Положение короны на панцире: та же привязка и поправка на squash, что у DrawBody
        private Vector2 CrownWorldPos(out float rotation)
        {
            float sx = 1f + _bodySquash * BodySquashAmount;
            float sy = 1f - _bodySquash * BodySquashAmount;
            Texture2D tex = TextureAssets.Npc[Type].Value;
            int frameCount = Math.Max(1, Main.npcFrameCount[Type]);
            float halfHeightWorld = tex.Height / frameCount / 2f * NPC.scale;
            Vector2 bodyCenter = NPC.Center - new Vector2(0f, BodyLift)
                + new Vector2(0f, halfHeightWorld * (1f - sy));

            float dirSign = NPC.spriteDirection * ClawDirFix;
            Vector2 local = new Vector2(CrownOffsetX * dirSign * sx, CrownOffsetY * sy) * NPC.scale;
            Vector2 world = bodyCenter + local.RotatedBy(NPC.rotation);

            rotation = NPC.rotation;
            if (State == CrabState.Scuttle && Math.Abs(NPC.velocity.X) > 0.5f)
                rotation += (float)Math.Sin(_stepCycle * 0.2f) * CrownTiltWalk;

            // «Забота о короне»: сбилась от удара — король прижимает её клешнёй
            if (_crownCareTimer > 0)
            {
                float care = _crownCareTimer / (float)CrownCareTicks;
                rotation += dirSign * CrownCareTilt * care;
                world.Y += CrownCarePress * care;
            }
            return world;
        }

        // Зовётся из PreDraw между телом и клешнями: клешня «заботы» ложится поверх короны
        private void DrawCrown(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            _crownTex ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabCrown", AssetRequestMode.ImmediateLoad);
            _crownGlowTex ??= ModContent.Request<Texture2D>("SoA/Content/NPCs/Bosses/KingCrab/KingCrabCrown_Glow", AssetRequestMode.ImmediateLoad);
            Texture2D tex = _crownTex.Value;
            if (tex == null)
                return;

            // Под землёй короля не видно — корону тоже прячем
            if ((State == CrabState.Burrow && SubState < 2f) || (State == CrabState.KnightCourt && SubState < 2f))
                return;

            Vector2 pos;
            float rot;
            if (_crownFalling)
            {
                pos = _crownPos;
                rot = _crownRot;
            }
            else
            {
                pos = CrownWorldPos(out rot);
            }

            SpriteEffects fx = NPC.spriteDirection * ClawDirFix > 0f ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 origin = tex.Size() / 2f;
            spriteBatch.Draw(tex, pos - screenPos, null, drawColor, rot, origin, NPC.scale, fx, 0f);

            // Свечение слабого места: слабое в фазе 2, пульс в Desperate и на Crown Command
            float glow = 0f;
            if (State == CrabState.CrownCommand)
                glow = 0.75f + 0.25f * (float)Math.Sin(Main.GameUpdateCount * 0.25f);
            else if (Desperate)
                glow = 0.5f + 0.3f * (float)Math.Sin(Main.GameUpdateCount * 0.15f);
            else if (Phase2)
                glow = 0.3f;

            if (glow > 0f && !_crownFalling && _crownGlowTex.Value != null)
                spriteBatch.Draw(_crownGlowTex.Value, pos - screenPos, null, Color.White * glow, rot, origin, NPC.scale, fx, 0f);
        }
    }
}
