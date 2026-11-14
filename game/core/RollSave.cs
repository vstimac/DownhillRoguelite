using System;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>How a roll press turned out.</summary>
public enum RollGrade
{
    None,
    /// <summary>Pressed outside the window, or too winded: he takes the fall.</summary>
    Miss,
    /// <summary>Inside the window: he rolls onto his feet.</summary>
    Good,
    /// <summary>Right at the sweet spot: he rolls through and keeps his speed, a little more even.</summary>
    Perfect,
}

/// <summary>The Options choice for the roll: timed by the player, always taken for them, or never.</summary>
public enum RollMode
{
    Timed,
    Automatic,
    Off,
}

/// <summary>
/// The saving throw on a fall. Every time the carrier is about to hit the ground hard (a fall off
/// a ledge, a thrown jump or dive, a lost balance, a blow), he gets one chance: Space shortly
/// before impact rolls him out of it. Timing is in seconds to impact, not height, so a short drop
/// and a long one feel the same. A well-timed press is a tool: a perfect roll keeps the speed of
/// the fall and turns it into ground covered.
///
/// The window is the character's: heavy loads and an empty stamina bar narrow it, the Balance boons
/// and impact resistance (the carrier's own and their H ability) widen it. A roll costs stamina.
/// After a chance is used (rolled or missed) there is none for a moment, so a missed roll doesn't
/// throw a second prompt at a player who is already tumbling.
///
/// Dive (falls on his feet) and PlayerRagdoll (falls as a ragdoll) both ask here, so they show the
/// same cue (VaultPrompt) and follow the same rules.
/// </summary>
public static class RollSave
{
    /// <summary>How long before impact a press counts, before the character's scale, in s.</summary>
    public const float Window = 0.45f;
    /// <summary>The best moment, in s before impact, and how far either side of it is still perfect.</summary>
    public const float Sweet = 0.16f;
    public const float PerfectBand = 0.06f;
    /// <summary>After a chance is used, the next one waits this long (game time, s).</summary>
    public const float Cooldown = 1.5f;
    /// <summary>Stamina a roll costs; a perfect one costs half.</summary>
    public const float StaminaCost = 12.0f;
    /// <summary>A perfect roll's speed against the fall's own sideways speed.</summary>
    public const float PerfectCarry = 1.1f;
    /// <summary>Load at which the window has narrowed the most, in kg, and that narrowest share.</summary>
    public const float HeavyLoad = 40.0f;
    public const float HeavyScale = 0.7f;
    /// <summary>Stamina share under which the window narrows, down to this share at empty.</summary>
    public const float TiredShare = 0.3f;
    public const float TiredScale = 0.6f;

    /// <summary>Raised when a chance is used: the grade, for the cue's word and the roll's speed.</summary>
    public static event Action<RollGrade>? Resolved;

    /// <summary>How the last chance turned out (Dive reads it to choose the roll's speed).</summary>
    public static RollGrade LastGrade { get; private set; } = RollGrade.None;

    private static ulong _usedAtFrame;
    private static bool _used;

    /// <summary>True when a new chance may be shown: the last one was long enough ago.</summary>
    public static bool Ready
    {
        get
        {
            if (!_used || GameOptions.Roll == RollMode.Off)
            {
                return GameOptions.Roll != RollMode.Off;
            }
            ulong frames = Engine.GetPhysicsFrames() - _usedAtFrame;
            return frames >= (ulong)Mathf.CeilToInt(Cooldown * Engine.PhysicsTicksPerSecond);
        }
    }

    /// <summary>Seconds until something falling at <paramref name="downSpeed"/> covers <paramref name="height"/>.</summary>
    public static float TimeToImpact(float height, float downSpeed, float gravity)
    {
        if (height <= 0.0f)
        {
            return 0.0f;
        }
        float v = Mathf.Max(downSpeed, 0.0f);
        float g = Mathf.Max(gravity, 0.1f);
        return (-v + Mathf.Sqrt(v * v + 2.0f * g * height)) / g;
    }

    /// <summary>How wide the window is for this character right now (1 is the plain window).</summary>
    public static float Scale(float loadKg, float staminaShare, float balance, float impactFactor)
    {
        float scale = Mathf.Lerp(1.0f, HeavyScale, Mathf.Clamp(loadKg / HeavyLoad, 0.0f, 1.0f));
        if (staminaShare < TiredShare)
        {
            scale *= Mathf.Lerp(TiredScale, 1.0f, Mathf.Clamp(staminaShare / TiredShare, 0.0f, 1.0f));
        }
        scale *= Mathf.Clamp(balance, 0.5f, 1.5f);
        scale *= 1.0f + (1.0f - Mathf.Clamp(impactFactor, 0.0f, 1.0f)) * 0.5f;
        return Mathf.Clamp(scale, 0.4f, 1.6f);
    }

    /// <summary>The scale for the local carrier: load, stamina share, boons and character from the run.</summary>
    public static float ScaleFor(float staminaShare)
    {
        float balance = 1.0f;
        float impact = 1.0f;
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            RunState? run = tree.Root.GetNodeOrNull<RunState>("RunState");
            if (run != null)
            {
                balance = run.Modifiers.Balance;
                impact = run.Character.ImpactFactor;
            }
        }
        return Scale(GameEvents.CurrentLoad.Mass, staminaShare, balance, impact);
    }

    /// <summary>The cue for a fall <paramref name="timeToImpact"/> seconds from the ground.</summary>
    public static VaultCue Cue(float timeToImpact, float scale)
    {
        return new VaultCue(true, timeToImpact, 0.0f, Window * scale, Sweet, PerfectBand * scale, true);
    }

    /// <summary>How a press on <paramref name="cue"/> turns out.</summary>
    public static RollGrade Grade(VaultCue cue)
    {
        if (!cue.InWindow)
        {
            return RollGrade.Miss;
        }
        if (Mathf.Abs(cue.Distance - cue.Sweet) <= cue.Perfect)
        {
            return RollGrade.Perfect;
        }
        return RollGrade.Good;
    }

    /// <summary>
    /// The chance is used: pays the stamina for a roll (a perfect one half), starts the cooldown and
    /// tells the cue. Returns the grade it settled on: too little stamina for a roll is a miss.
    /// </summary>
    public static RollGrade Use(RollGrade grade, Func<float, bool>? spend)
    {
        if (grade != RollGrade.Miss && spend != null)
        {
            float cost = StaminaCost;
            if (grade == RollGrade.Perfect)
            {
                cost *= 0.5f;
            }
            if (!spend(cost))
            {
                grade = RollGrade.Miss;
            }
        }
        LastGrade = grade;
        _used = true;
        _usedAtFrame = Engine.GetPhysicsFrames();
        Resolved?.Invoke(grade);
        return grade;
    }

    /// <summary>
    /// A cue was shown and the ground came with no press: the chance is gone and the cooldown starts,
    /// so the tumble that follows doesn't offer another.
    /// </summary>
    public static void Lapse()
    {
        LastGrade = RollGrade.Miss;
        _used = true;
        _usedAtFrame = Engine.GetPhysicsFrames();
    }

    /// <summary>A new scene or test: no cooldown, no last grade.</summary>
    public static void Reset()
    {
        _used = false;
        LastGrade = RollGrade.None;
    }
}
