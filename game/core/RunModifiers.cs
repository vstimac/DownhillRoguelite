namespace DownhillRoguelite.Core;

/// <summary>
/// What this run's boons have changed about the carrier (M12): multipliers that start at 1 and an
/// additive cold resistance that starts at 0. Held by RunState and reset with every new run; each
/// system reads the value that concerns it.
/// </summary>
public sealed class RunModifiers
{
    /// <summary>Maximum stamina.</summary>
    public float StaminaMax { get; set; } = 1.0f;
    /// <summary>Stamina recovered per second while resting.</summary>
    public float StaminaRecovery { get; set; } = 1.0f;
    /// <summary>Stamina spent walking (all of it).</summary>
    public float WalkDrain { get; set; } = 1.0f;
    /// <summary>The extra stamina a heavy load costs.</summary>
    public float LoadDrain { get; set; } = 1.0f;
    /// <summary>Hunger and thirst drain.</summary>
    public float NeedsDrain { get; set; } = 1.0f;
    /// <summary>Fraction of night cold and weather chill kept off (0 to 0.9).</summary>
    public float ColdResistance { get; set; }
    /// <summary>Grip on every surface (capped at full grip).</summary>
    public float Grip { get; set; } = 1.0f;
    /// <summary>The carrier's balance strength.</summary>
    public float Balance { get; set; } = 1.0f;
    /// <summary>Walking speed.</summary>
    public float WalkSpeed { get; set; } = 1.0f;
    /// <summary>Health a night's sleep restores.</summary>
    public float SleepHeal { get; set; } = 1.0f;

    public void Reset()
    {
        StaminaMax = 1.0f;
        StaminaRecovery = 1.0f;
        WalkDrain = 1.0f;
        LoadDrain = 1.0f;
        NeedsDrain = 1.0f;
        ColdResistance = 0.0f;
        Grip = 1.0f;
        Balance = 1.0f;
        WalkSpeed = 1.0f;
        SleepHeal = 1.0f;
    }
}
