using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Stamina is the carrier's endurance. Walking drains it (more with a heavier load, going uphill
/// and on steep descents; careful braking steps cost a little less), sprinting drains it fast and
/// jumps cost a fixed amount. It only recovers while standing still, after a short pause. At zero
/// the carrier is exhausted: it can't walk, sprint or jump until stamina recovers to
/// ExhaustedRecoverLevel, which costs time on the clock. Hunger lowers the maximum and thirst slows
/// recovery (CarrierNeeds). See docs/survival-loop.md.
/// </summary>
public partial class CarrierStamina : Node
{
    [Export] public float Max { get; set; } = 100.0f;

    [ExportGroup("Walking")]
    /// <summary>Drain while walking on the flat with no load, per second.</summary>
    [Export] public float WalkDrain { get; set; } = 0.5f;
    /// <summary>Extra walk and sprint drain per kg of load, as a fraction: drain × (1 + this × kg).</summary>
    [Export] public float LoadDrainPerKg { get; set; } = 0.02f;
    /// <summary>Extra drain per unit of uphill grade (sine of the slope along the walk).</summary>
    [Export] public float UphillDrainPerGrade { get; set; } = 3.0f;
    /// <summary>Extra drain per unit of downhill grade: steep descents work the legs too.</summary>
    [Export] public float DownhillDrainPerGrade { get; set; } = 1.0f;
    /// <summary>Drain multiplier for careful braking steps.</summary>
    [Export] public float BrakeDrainFactor { get; set; } = 0.8f;

    [ExportGroup("Sprint and jump")]
    /// <summary>Drain while sprinting with no load, per second (on top of walking).</summary>
    [Export] public float SprintDrain { get; set; } = 18.0f;
    [Export] public float JumpCost { get; set; } = 15.0f;

    [ExportGroup("Recovery")]
    /// <summary>Recovery while standing still, per second.</summary>
    [Export] public float RecoveryPerSecond { get; set; } = 6.0f;
    /// <summary>Seconds after the last use before stamina starts recovering.</summary>
    [Export] public float RecoveryDelay { get; set; } = 1.0f;
    /// <summary>Once exhausted, stamina must recover to this before the carrier can move again.</summary>
    [Export] public float ExhaustedRecoverLevel { get; set; } = 30.0f;

    public float Current { get; private set; }
    /// <summary>Out of stamina: no walking, sprinting or jumping until it recovers.</summary>
    public bool Exhausted { get; private set; }
    /// <summary>Scales recovery; thirst lowers it (CarrierNeeds).</summary>
    public float RecoveryMultiplier { get; set; } = 1.0f;
    /// <summary>Scales the maximum; hunger lowers it (CarrierNeeds).</summary>
    public float MaxMultiplier { get; set; } = 1.0f;
    public float EffectiveMax => Max * MaxMultiplier;
    public bool CanSprint => !Exhausted && Current > 0.0f;
    /// <summary>Recovery per second right now, with needs applied.</summary>
    public float EffectiveRecovery => RecoveryPerSecond * RecoveryMultiplier;

    private float _sinceUse;

    public override void _Ready()
    {
        Current = Max;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _sinceUse += dt;
        Current = Mathf.Min(Current, EffectiveMax);
        if (_sinceUse < RecoveryDelay)
        {
            return;
        }

        Current = Mathf.Min(EffectiveMax, Current + EffectiveRecovery * dt);
        if (Exhausted && Current >= ExhaustedRecoverLevel)
        {
            Exhausted = false;
        }
    }

    /// <summary>
    /// Stamina per second for walking with this load on this grade (+ downhill, − uphill).
    /// Shared by the carrier and route estimates so both use the same numbers.
    /// </summary>
    public float WalkDrainPerSecond(float loadMass, float grade, bool braking, bool sprinting)
    {
        float slope = 1.0f + DownhillDrainPerGrade * Mathf.Max(0.0f, grade) + UphillDrainPerGrade * Mathf.Max(0.0f, -grade);
        float drain = WalkDrain * slope;
        if (braking)
        {
            drain *= BrakeDrainFactor;
        }
        if (sprinting)
        {
            drain += SprintDrain;
        }
        return drain * (1.0f + LoadDrainPerKg * loadMass);
    }

    /// <summary>Drains one physics step of moving under one's own power.</summary>
    public void DrainMovement(float dt, float loadMass, float grade, bool braking, bool sprinting)
    {
        Drain(WalkDrainPerSecond(loadMass, grade, braking, sprinting) * dt);
    }

    /// <summary>Spends a fixed cost (a jump) if there is enough; returns whether it was spent.</summary>
    public bool TrySpend(float amount)
    {
        if (Exhausted || Current < amount)
        {
            return false;
        }
        Drain(amount);
        return true;
    }

    /// <summary>Sets stamina directly (tests and debugging).</summary>
    public void SetCurrent(float value)
    {
        Current = Mathf.Clamp(value, 0.0f, EffectiveMax);
        Exhausted = Current <= 0.0f;
    }

    public void Refill()
    {
        Current = Max;
        Exhausted = false;
    }

    private void Drain(float amount)
    {
        Current -= amount;
        _sinceUse = 0.0f;
        if (Current <= 0.0f)
        {
            Current = 0.0f;
            Exhausted = true;
        }
    }
}
