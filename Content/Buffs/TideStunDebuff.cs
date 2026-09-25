using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using SoA.Common.Graphics;

namespace SoA.Content.Buffs
{
    // Оглушение приливом: копьё приливного рывка (полная шкала, ПКМ) вбивает цель на
    // месте. ИИ стоит, контактного урона нет, над головой кружат водяные искры.
    // Через бафф, а не через своё поле: NPC.AddBuff с клиента сам уходит пакетом на
    // сервер, а ИИ NPC в мультиплеере крутится именно там.
    public class TideStunDebuff : ModBuff
    {
        public const int NormalTicks = 180;
        public const int BossTicks = 30;

        // Иконка у NPC нигде не видна, поэтому ванильная «Оглушённость» как заглушка
        public override string Texture => "Terraria/Images/Buff_" + BuffID.Dazed;

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
        }

        // Боссы и сегменты червей оглушаются коротко: полная пауза ломала бы их фазы
        public static void Apply(NPC npc)
        {
            bool bossLike = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type] || npc.realLife >= 0;
            npc.AddBuff(ModContent.BuffType<TideStunDebuff>(), bossLike ? BossTicks : NormalTicks);
        }
    }

    public class TideStunGlobalNPC : GlobalNPC
    {
        private const int Sparks = 3;
        private const float SparkOrbitHeight = 7f;
        private static readonly Color SparkColor = new(120, 205, 255, 0);

        private static bool Stunned(NPC npc) => npc.HasBuff<TideStunDebuff>();

        // ИИ не крутится вовсе: цель не бьёт, не стреляет и не бежит. Физику оставляем —
        // наземный враг тормозит и падает, летающий зависает, а не замирает в рывке
        public override bool PreAI(NPC npc)
        {
            if (!Stunned(npc))
                return true;

            npc.velocity.X *= 0.85f;
            if (npc.noGravity)
                npc.velocity.Y *= 0.85f;
            else
                npc.velocity.Y = Math.Min(npc.velocity.Y + 0.3f, 10f);
            return false;
        }

        public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) => !Stunned(npc);

        // Искры по кругу над головой — классическое «звёзды из глаз», только водяные.
        // Батч сущностей обычный, аддитив даёт цвет с A = 0
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            int index = npc.FindBuffIndex(ModContent.BuffType<TideStunDebuff>());
            if (index < 0)
                return;

            // Последние полсекунды искры гаснут, чтобы конец оглушения читался заранее
            float fade = MathHelper.Clamp(npc.buffTime[index] / 30f, 0f, 1f);
            float spin = Main.GlobalTimeWrappedHourly * 5f;
            float radius = Math.Max(npc.width * 0.45f, 10f);
            Vector2 crown = npc.Top + new Vector2(0f, -10f + npc.gfxOffY);

            for (int i = 0; i < Sparks; i++)
            {
                float angle = spin + MathHelper.TwoPi * i / Sparks;
                Vector2 at = crown + new Vector2((float)Math.Cos(angle) * radius,
                    (float)Math.Sin(angle) * SparkOrbitHeight);

                // Дальняя половина круга тусклее — кольцо читается объёмным
                float depth = 0.55f + 0.45f * (float)Math.Sin(angle);
                Color color = SparkColor * (fade * depth);

                SoAVfx.DrawTintedGlow(spriteBatch, at, new Vector2(18f), color * 0.6f);
                SoAVfx.DrawTintedQuad(spriteBatch, at, new Vector2(14f, 3f), 0f, color);
                SoAVfx.DrawTintedQuad(spriteBatch, at, new Vector2(14f, 3f), MathHelper.PiOver2, color);
            }
        }
    }
}
