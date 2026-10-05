using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent.Drawing;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using SoA.Common.Graphics;
using SoA.Common.Graphics.Atmosphere;
using SoA.Common.Systems;

namespace SoA.Content.Tiles.Shrines
{
    // Замок печати 3x3 — та самая штука, по которой игрок понимает, ЧТО именно
    // держит проход. Сам по себе ничего не перекрывает: перекрывает мембрана
    // TideSealBarrier_tile вокруг, а замок показывает состояние и по ПКМ называет
    // условие. Когда босс ступени убит, на замке проступает трещина — стоит подойти
    // (или ПКМ), и начинается ритуал снятия (TideSealSystem): печать раскручивается,
    // разгорается и исчезает с треском.
    //
    // Рисунок печати (кольца, пентаграмма, руны) медленно вращается — его рисует шейдер
    // SoA:SealSigil поверх каменной плиты листа. В центре — знак ступени (у первой — краб).
    //
    // Над замком — пять символов прилива: первый погас задолго до игрока, остальные
    // гаснут по одному с каждой снятой печатью.
    //
    // Лист 106x52: клетки 0-2 — плита с гнездом под рисунок, клетки 3-5 — потухшая плита
    // (старые миры, где печать сняли, пока замок ещё не исчезал). Состояние хранится
    // в frameX самого тайла, отдельного поля не нужно
    public class TideSeal_tile : ModTile
    {
        public const int SizeInTiles = 3;
        public const int FrameStep = 18;                      // клетка 16 + разбежка 2
        public const int StyleWidth = SizeInTiles * FrameStep; // сдвиг frameX между состояниями

        private const int Symbols = 5;
        private const float SymbolSpacing = 14f;
        private const float SymbolHeight = 38f;     // над центром замка
        private const float SymbolArcDrop = 2.5f;   // крайние символы ниже — дуга арки
        private const float StrokeLength = 9f;
        private const float StrokeThickness = 1.8f;
        private const float CrackThickness = 2.2f;

        private static readonly Vector3 SealedLight = new(0.32f, 0.10f, 0.42f);
        private static readonly Vector3 OpenLight = new(0.16f, 0.55f, 0.68f);
        private static readonly Vector3 ReadyLight = new(0.20f, 0.50f, 0.75f);

        private static readonly Color SymbolLit = new(175, 115, 255);

        // Рисунок печати: медленно крутится, готовая — быстрее, в ритуале раскручивается
        private const float SigilSizePx = 48f;
        private const float SigilSpeed = 0.12f;        // рад/с, запечатана
        private const float SigilReadySpeed = 0.3f;    // трещина — печать ожила
        private const float SigilRitualSpin = 6f;      // сколько радиан добавляет раскрутка за ритуал

        private static Asset<Texture2D> _stepIconCrab;
        private static readonly Color SymbolDark = new(38, 32, 56);

        // Штрихи каждого символа — углы в радианах, у каждого своя руна
        private static readonly float[][] SymbolStrokes =
        {
            new[] { 0f, MathHelper.PiOver2 },
            new[] { MathHelper.PiOver2, 0.5f, -0.5f },
            new[] { MathHelper.PiOver4, -MathHelper.PiOver4 },
            new[] { MathHelper.PiOver2, 0f, MathHelper.Pi / 3f },
            new[] { MathHelper.PiOver2, 1.1f, -1.1f, 0f },
        };

        // Излом трещины от верха замка к низу, px от центра
        private static readonly Vector2[] CrackPoints =
        {
            new(-1f, -21f), new(3f, -13f), new(-2f, -6f), new(2f, 1f), new(-3f, 8f), new(1f, 14f), new(-1f, 21f),
        };

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileWaterDeath[Type] = false;
            Main.tileLavaDeath[Type] = false;

            TileID.Sets.DisableSmartCursor[Type] = true;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style3x3);
            TileObjectData.newTile.CoordinateWidth = 16;
            TileObjectData.newTile.CoordinatePadding = 2;
            TileObjectData.newTile.CoordinateHeights = new[] { 16, 16, 16 };
            TileObjectData.newTile.Origin = new Point16(1, 1);
            // Печать висит в шахте, опоры под ней нет — все якоря сняты,
            // иначе генератор не сможет её поставить в воде посреди прохода
            TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
            TileObjectData.newTile.AnchorTop = AnchorData.Empty;
            TileObjectData.newTile.AnchorLeft = AnchorData.Empty;
            TileObjectData.newTile.AnchorRight = AnchorData.Empty;
            TileObjectData.newTile.StyleHorizontal = true;
            TileObjectData.newTile.WaterDeath = false;
            TileObjectData.newTile.WaterPlacement = LiquidPlacement.Allowed;
            TileObjectData.newTile.LavaPlacement = LiquidPlacement.NotAllowed;
            TileObjectData.addTile(Type);

            DustType = DustID.BlueTorch;
            AddMapEntry(new Color(128, 96, 190), CreateMapEntryName());
        }

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            blockDamaged = false;
            return false;
        }

        public override bool CanExplode(int i, int j) => false;

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Vector3 light;
            if (IsOpen(i, j))
                light = OpenLight;
            else if (TideSealSystem.IsReady(TideSealSystem.StepAt(i, j)))
                light = ReadyLight * (0.75f + 0.25f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f));
            else
                light = SealedLight;

            r = light.X;
            g = light.Y;
            b = light.Z;
        }

        public override bool RightClick(int i, int j) => Interact(i, j);

        // ПКМ по замку или по любому тайлу мембраны: замок висит посреди стекла,
        // и дотянуться до него самого почти невозможно
        public static bool Interact(int i, int j)
        {
            int step = TideSealSystem.StepAt(i, j);
            if (step == 0)
                return false;

            if (TideSealSystem.IsReady(step))
            {
                if (!TideSealCinematic.IsPlaying)
                    TideSealSystem.RequestActivation(step);
                return true;
            }

            string key = TideSealSystem.IsOpened(step) ? "SealOpened" : "SealLocked" + step;
            Main.NewText(Language.GetTextValue("Mods.SoA.Misc." + key), 150, 200, 255);
            return true;
        }

        // Символы и трещина живые — рисуются каждый кадр в SpecialDraw, а не в кэше тайлов.
        // Точка одна на замок: его левый верхний тайл
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (tile.TileFrameX % StyleWidth == 0 && tile.TileFrameY == 0)
                Main.instance.TilesRenderer.AddSpecialPoint(i, j, TileDrawing.TileCounterType.CustomNonSolid);
            return true;
        }

        public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
        {
            Tile tile = Main.tile[i, j];
            if (!tile.HasTile || tile.TileType != Type)
                return;

            int step = TideSealSystem.StepAt(i, j);
            if (step == 0)
                return;

            Vector2 center = new Vector2(i + SizeInTiles / 2f, j + SizeInTiles / 2f) * 16f - Main.screenPosition;
            if (!IsOpen(i, j))
                DrawSigil(spriteBatch, center, step);
            DrawSymbols(spriteBatch, center);

            if (!IsOpen(i, j) && TideSealSystem.IsReady(step))
                DrawCrack(spriteBatch, center, step);
        }

        // Вращающийся рисунок и знак ступени в центре. Цвет и накал — по состоянию печати
        private static void DrawSigil(SpriteBatch spriteBatch, Vector2 center, int step)
        {
            float time = Main.GlobalTimeWrappedHourly;
            float ritual = TideSealCinematic.RitualProgress(step);
            bool ready = TideSealSystem.IsReady(step);
            Color readyColor = Color.Lerp(SymbolLit, TideSealCinematic.CrackColor, 0.5f);

            float angle = step * 1.3f + time * (ready ? SigilReadySpeed : SigilSpeed);
            float intensity;
            Color color;
            if (ritual >= 0f)
            {
                angle += SigilRitualSpin * ritual * ritual * ritual; // раскручивается к слому
                intensity = 1.05f + 1.5f * ritual;
                color = Color.Lerp(readyColor, TideSealCinematic.CrackColor, ritual);
            }
            else if (ready)
            {
                intensity = 1.05f + 0.25f * (float)Math.Sin(time * 3f);
                color = readyColor;
            }
            else
            {
                intensity = 0.85f + 0.08f * (float)Math.Sin(time * 1.4f + step);
                color = SymbolLit;
            }

            SoAVfx.BeginPixelImmediate(spriteBatch);
            MiscShaderData shader = GameShaders.Misc["SoA:SealSigil"];
            shader.UseOpacity(1f);
            shader.UseColor(color);
            shader.Shader.Parameters["uAngle"]?.SetValue(angle);
            shader.Shader.Parameters["uIntensity"]?.SetValue(intensity);
            shader.Apply();
            Texture2D quad = SoAVfx.Quad;
            spriteBatch.Draw(quad, center - new Vector2(SigilSizePx / 2f), null, Color.White, 0f, Vector2.Zero,
                SigilSizePx / quad.Width, SpriteEffects.None, 0f);
            SoAVfx.EndPixelBatch(spriteBatch);

            // Знак ступени не крутится: он — «чья» это печать
            Texture2D icon = StepIcon(step);
            if (icon == null)
                return;
            Color iconColor = Color.Lerp(color, Color.White, 0.45f) * Math.Min(intensity, 1.2f);
            iconColor.A = 255;
            Vector2 at = new((float)Math.Round(center.X - icon.Width / 2f), (float)Math.Round(center.Y - icon.Height / 2f));
            spriteBatch.Draw(icon, at, iconColor);
        }

        // Знак ступени. Пока нарисован только краб первой; у остальных центр пустой
        private static Texture2D StepIcon(int step)
        {
            if (step != 1)
                return null;
            _stepIconCrab ??= ModContent.Request<Texture2D>("SoA/Content/Tiles/Shrines/TideSealCrab", AssetRequestMode.ImmediateLoad);
            return _stepIconCrab.Value;
        }

        // Символ 0 погас давно, символ N принадлежит ступени N
        private static void DrawSymbols(SpriteBatch spriteBatch, Vector2 center)
        {
            float time = Main.GlobalTimeWrappedHourly;
            for (int index = 0; index < Symbols; index++)
            {
                float fromMiddle = index - (Symbols - 1) / 2f;
                Vector2 at = center + new Vector2(fromMiddle * SymbolSpacing, -SymbolHeight + fromMiddle * fromMiddle * SymbolArcDrop);

                float lit = index == 0 || TideSealSystem.IsOpened(index) ? 0f : TideSealCinematic.SymbolFade(index);
                DrawStrokes(spriteBatch, at, SymbolStrokes[index], SymbolDark);
                if (lit <= 0f)
                    continue;

                float pulse = 0.8f + 0.2f * (float)Math.Sin(time * 2f + index * 1.3f);
                Color glow = SoAVfx.Additive(SymbolLit) * (lit * pulse);
                DrawStrokes(spriteBatch, at, SymbolStrokes[index], glow);
                DrawGlow(spriteBatch, at, glow * 0.5f, 22f);
            }
        }

        private static void DrawCrack(SpriteBatch spriteBatch, Vector2 center, int step)
        {
            float boost = TideSealCinematic.CrackBoost(step);
            if (boost <= 0f)
                return;

            float pulse = 0.65f + 0.35f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f);
            float intensity = pulse * boost;
            Color halo = SoAVfx.Additive(TideSealCinematic.CrackColor) * Math.Min(intensity * 0.5f, 1f);
            Color core = SoAVfx.Additive(Color.Lerp(TideSealCinematic.CrackColor, Color.White, 0.6f)) * Math.Min(intensity, 1f);
            float width = CrackThickness * (1f + 0.4f * (boost - 1f));

            for (int k = 0; k < CrackPoints.Length - 1; k++)
            {
                Vector2 from = center + CrackPoints[k];
                Vector2 to = center + CrackPoints[k + 1];
                DrawSegment(spriteBatch, from, to, halo, width * 3f);
                DrawSegment(spriteBatch, from, to, core, width);
            }
            DrawGlow(spriteBatch, center, halo, 36f * (0.8f + 0.4f * boost));
        }

        private static void DrawStrokes(SpriteBatch spriteBatch, Vector2 at, float[] angles, Color color)
        {
            Texture2D streak = SoAVfx.SoftStreak;
            var size = new Vector2(StrokeLength / streak.Width, StrokeThickness / streak.Height);
            foreach (float angle in angles)
                spriteBatch.Draw(streak, at, null, color, angle, streak.Size() / 2f, size, SpriteEffects.None, 0f);
        }

        private static void DrawSegment(SpriteBatch spriteBatch, Vector2 from, Vector2 to, Color color, float thickness)
        {
            Texture2D streak = SoAVfx.SoftStreak;
            Vector2 delta = to - from;
            // Штрих мягкий по краям — чуть длиннее отрезка, чтобы изломы не рвались
            var size = new Vector2((delta.Length() + thickness) / streak.Width, thickness / streak.Height);
            spriteBatch.Draw(streak, (from + to) / 2f, null, color, delta.ToRotation(), streak.Size() / 2f, size,
                SpriteEffects.None, 0f);
        }

        private static void DrawGlow(SpriteBatch spriteBatch, Vector2 at, Color color, float sizePx)
        {
            Texture2D glow = SoAVfx.SoftGlow;
            spriteBatch.Draw(glow, at, null, color, 0f, glow.Size() / 2f, sizePx / glow.Width, SpriteEffects.None, 0f);
        }

        public static bool IsOpen(int i, int j) => Main.tile[i, j].TileFrameX >= StyleWidth;
    }
}
