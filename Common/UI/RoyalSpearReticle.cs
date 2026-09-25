using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using SoA.Common.Graphics;
using SoA.Common.Players;

namespace SoA.Common.UI
{
    // Прицел приливного рывка Королевского копья: пока шкала полная и зажата ПКМ,
    // вместо курсора висит прицел — голубой, если рывок возможен, красный, если на
    // пути стена или в точке не помещается персонаж. В мире над целью подсвечено
    // место, куда встанет игрок. Рисуется только у владельца: курсор есть только у него.
    public class RoyalSpearReticle : ModSystem
    {
        private const string TexturePath = "SoA/Assets/Textures/Vfx/TeleportReticle";
        // Текстура 64x64 рисуется 1:1: тонкие линии прицела при масштабе мылились бы
        private const float ReticleSize = 64f;

        private static readonly Color ValidColor = new(120, 210, 255, 0);
        private static readonly Color InvalidColor = new(255, 90, 90, 0);

        private static Asset<Texture2D> reticle;
        private static bool reticleChecked;

        private static RoyalSpearPlayer AimingPlayer
        {
            get
            {
                if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
                    return null;
                var spearPlayer = Main.LocalPlayer.GetModPlayer<RoyalSpearPlayer>();
                return spearPlayer.AimingTeleport ? spearPlayer : null;
            }
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            RoyalSpearPlayer spearPlayer = AimingPlayer;
            if (spearPlayer == null)
                return;

            // Ванильный курсор прячем, на его место — прицел. Если слоя с таким именем
            // в этой версии нет, прицел просто рисуется поверх обычного курсора
            int cursor = layers.FindIndex(layer => layer.Name == "Vanilla: Cursor");
            if (cursor >= 0)
                layers[cursor].Active = false;

            layers.Insert(cursor >= 0 ? cursor : layers.Count, new LegacyGameInterfaceLayer(
                "SoA: Tide Dash Reticle", () => { DrawReticle(spearPlayer); return true; }, InterfaceScaleType.UI));

            // Метка прибытия живёт в мировых координатах — отдельный слой с зумом игры
            layers.Insert(0, new LegacyGameInterfaceLayer(
                "SoA: Tide Dash Marker", () => { DrawDestination(spearPlayer); return true; }, InterfaceScaleType.Game));
        }

        private static void DrawReticle(RoyalSpearPlayer spearPlayer)
        {
            SpriteBatch sb = Main.spriteBatch;
            Vector2 at = Main.MouseScreen;
            Color color = spearPlayer.TeleportValid ? ValidColor : InvalidColor;
            float pulse = 0.85f + 0.15f * (float)System.Math.Sin(Main.GlobalTimeWrappedHourly * 8f);
            float spin = Main.GlobalTimeWrappedHourly * (spearPlayer.TeleportValid ? 1.5f : 0f);

            Texture2D custom = CustomReticle();
            if (custom != null)
            {
                float scale = ReticleSize / custom.Width * pulse;
                sb.Draw(custom, at, null, color, spin, custom.Size() / 2f, scale, SpriteEffects.None, 0f);
                return;
            }

            // Пока текстуры нет — прицел из четырёх штрихов и точки по центру
            Texture2D streak = SoAVfx.SoftStreak;
            Texture2D glow = SoAVfx.SoftGlow;
            float radius = ReticleSize * 0.42f * pulse;
            for (int i = 0; i < 4; i++)
            {
                float angle = spin + MathHelper.PiOver2 * i;
                Vector2 offset = angle.ToRotationVector2() * radius;
                sb.Draw(streak, at + offset, null, color, angle, streak.Size() / 2f,
                    new Vector2(14f / streak.Width, 5f / streak.Height), SpriteEffects.None, 0f);
            }
            sb.Draw(glow, at, null, color, 0f, glow.Size() / 2f, 10f / glow.Width, SpriteEffects.None, 0f);
        }

        // Где встанет игрок: мягкий столб света ростом с персонажа
        private static void DrawDestination(RoyalSpearPlayer spearPlayer)
        {
            if (!spearPlayer.TeleportValid)
                return;

            Player player = spearPlayer.Player;
            Vector2 center = spearPlayer.TeleportDestination + player.Size / 2f - Main.screenPosition;
            float pulse = 0.7f + 0.3f * (float)System.Math.Sin(Main.GlobalTimeWrappedHourly * 6f);

            Texture2D streak = SoAVfx.SoftStreak;
            Texture2D glow = SoAVfx.SoftGlow;
            Main.spriteBatch.Draw(streak, center, null, ValidColor * (0.5f * pulse), MathHelper.PiOver2,
                streak.Size() / 2f, new Vector2(player.height * 1.4f / streak.Width, player.width * 1.2f / streak.Height),
                SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(glow, center, null, ValidColor * (0.35f * pulse), 0f, glow.Size() / 2f,
                player.height * 1.3f / glow.Width, SpriteEffects.None, 0f);
        }

        private static Texture2D CustomReticle()
        {
            if (!reticleChecked)
            {
                reticleChecked = true;
                ModContent.RequestIfExists(TexturePath, out reticle, AssetRequestMode.ImmediateLoad);
            }
            return reticle?.Value;
        }
    }
}
