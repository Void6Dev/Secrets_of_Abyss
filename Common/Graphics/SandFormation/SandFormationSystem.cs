using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace SoA.Common.Graphics.SandFormation
{
    // Тик и отрисовка сборок песка. Эффекты, привязанные к владельцу (снаряд, NPC),
    // рисует сам владелец из PreDraw; здесь остаются только самостоятельные,
    // запущенные через SandFormationEffect.Start().
    public class SandFormationSystem : ModSystem
    {
        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;
            SandFormationEffect.UpdateAll();
        }

        public override void PostDrawTiles()
        {
            if (Main.dedServ || Main.gameMenu)
                return;

            SpriteBatch spriteBatch = Main.spriteBatch;

            // Батч здесь не начат — открываем свой. PointClamp обязателен:
            // на любой другой фильтрации зёрна размылятся
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null,
                Main.GameViewMatrix.TransformationMatrix);

            SandFormationEffect.DrawStandalone(spriteBatch);

            spriteBatch.End();
        }

        public override void Unload()
        {
            SandFormationEffect.ClearAll();
            SandFormationSilhouette.ClearCache();
        }
    }
}
