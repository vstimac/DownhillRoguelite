using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Turns the mouse counts gathered over a frame into look movement, in a fixed order so the
/// options combine predictably:
/// 1. desktop pointer speed and acceleration, only with Raw input off (DesktopPointer);
/// 2. in-game acceleration (Mouse acceleration; 0 leaves the response linear);
/// 3. Mouse sensitivity (a plain multiplier);
/// 4. smoothing (Mouse smoothing), which eases the result in without changing the total turn.
/// Raw input therefore never disables the in-game acceleration. The camera owns one and feeds it.
/// </summary>
public sealed class MouseLookFilter
{
    /// <summary>Mouse speed (counts per second) at which full acceleration doubles the turn per count.</summary>
    public float AccelerationReferenceSpeed { get; set; } = 1500.0f;
    /// <summary>Most extra gain acceleration can add (on top of 1).</summary>
    public float AccelerationMaxBoost { get; set; } = 3.0f;
    /// <summary>Smoothing time constant, in seconds.</summary>
    public float SmoothingSeconds { get; set; } = 0.035f;

    private Vector2 _counts;
    private Vector2 _pending;

    /// <summary>Adds a mouse event's counts to this frame.</summary>
    public void Add(Vector2 counts)
    {
        _counts += counts;
    }

    /// <summary>Drops anything gathered or still easing in (a snap, a menu opening).</summary>
    public void Clear()
    {
        _counts = Vector2.Zero;
        _pending = Vector2.Zero;
    }

    /// <summary>This frame's look movement, in counts scaled by the options (multiply by degrees per count).</summary>
    public Vector2 Take(float delta, float sensitivity, float acceleration, bool smoothing, bool raw)
    {
        Vector2 counts = _counts;
        _counts = Vector2.Zero;
        float seconds = Mathf.Max(delta, 0.0001f);
        float length = counts.Length();

        Vector2 move = counts;
        if (!raw)
        {
            move *= DesktopPointer.Gain(length, seconds);
        }
        if (acceleration > 0.0f)
        {
            float speed = move.Length() / seconds;
            float boost = Mathf.Min(speed / Mathf.Max(AccelerationReferenceSpeed, 1.0f), AccelerationMaxBoost);
            move *= 1.0f + acceleration * boost;
        }
        move *= sensitivity;

        if (!smoothing)
        {
            Vector2 all = move + _pending;
            _pending = Vector2.Zero;
            return all;
        }
        _pending += move;
        float share = 1.0f - Mathf.Exp(-seconds / Mathf.Max(SmoothingSeconds, 0.001f));
        Vector2 step = _pending * share;
        _pending -= step;
        if (_pending.LengthSquared() < 0.000001f)
        {
            step += _pending;
            _pending = Vector2.Zero;
        }
        return step;
    }
}
