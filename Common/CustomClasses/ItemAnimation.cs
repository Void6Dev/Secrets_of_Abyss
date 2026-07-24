using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SoA.Common.CustomClasses;

public enum AnimLoopMode
{
    Loop,     // 0 → N-1 → 0 → ...
    PingPong, // 0 → N-1 → 0 → ...  (reverses direction)
    Once,     // 0 → N-1, then stops (IsComplete = true)
}

public class SimpleItemAnimation(int frameCount, int frameSpeed, AnimLoopMode loopMode = AnimLoopMode.Loop)
{
    // ── Config ────────────────────────────────────────────────────────────────

    /// Number of frames in the sprite sheet (rows).
    public readonly int FrameCount = frameCount;

    /// Ticks per frame advance. Change at any time to adjust speed.
    public int FrameSpeed = Math.Max(1, frameSpeed);

    public AnimLoopMode LoopMode = loopMode;

    // ── State (read-only) ─────────────────────────────────────────────────────

    public int  CurrentFrame  { get; private set; }
    public bool IsPlaying     { get; private set; } = true;
    public bool IsComplete    { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private int _counter;
    private int _direction = 1;

    // ── Control ───────────────────────────────────────────────────────────────


    public void Play()  => IsPlaying = true;
    public void Pause() => IsPlaying = false;

    public void Reset()
    {
        CurrentFrame = 0;
        _counter     = 0;
        _direction   = 1;
        IsComplete   = false;
        IsPlaying    = true;
    }

    /// Jump directly to a specific frame without resetting the counter.
    public void SetFrame(int frame)
    {
        CurrentFrame = Math.Clamp(frame, 0, FrameCount - 1);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    /// Call once per game tick (e.g. in UpdateInventory or PostUpdate).
    public void Update()
    {
        if (!IsPlaying || IsComplete) return;

        if (++_counter < FrameSpeed) return;
        _counter = 0;

        switch (LoopMode)
        {
            case AnimLoopMode.Loop:
                CurrentFrame = (CurrentFrame + 1) % FrameCount;
                break;

            case AnimLoopMode.PingPong:
                CurrentFrame += _direction;
                if (CurrentFrame >= FrameCount - 1) { CurrentFrame = FrameCount - 1; _direction = -1; }
                else if (CurrentFrame <= 0)         { CurrentFrame = 0;              _direction =  1; }
                break;

            case AnimLoopMode.Once:
                if (CurrentFrame < FrameCount - 1)
                    CurrentFrame++;
                else
                {
                    IsComplete = true;
                    IsPlaying  = false;
                }
                break;
        }
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    /// Source rectangle for the current frame. Pass directly to SpriteBatch.Draw / EntitySpriteDraw.
    public Rectangle GetFrame(Texture2D texture)
    {
        int h = texture.Height / FrameCount;
        return new Rectangle(0, CurrentFrame * h, texture.Width, h);
    }

    /// Source rectangle for an arbitrary frame index.
    public Rectangle GetFrame(Texture2D texture, int frame)
    {
        int h = texture.Height / FrameCount;
        frame = Math.Clamp(frame, 0, FrameCount - 1);
        return new Rectangle(0, frame * h, texture.Width, h);
    }

    /// Center origin of the current frame — use as the `origin` parameter in Draw calls.
    public Vector2 GetOrigin(Texture2D texture)
    {
        int h = texture.Height / FrameCount;
        return new Vector2(texture.Width * 0.5f, h * 0.5f);
    }

    /// Frame height in pixels (texture.Height / FrameCount).
    public int GetFrameHeight(Texture2D texture) => texture.Height / FrameCount;
}
