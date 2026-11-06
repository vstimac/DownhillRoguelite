using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Stamina for sprinting and jumping. Sprinting drains it (faster with a heavier load), jumps cost
/// a fixed amount, and it recovers after a short pause. At zero the carrier is exhausted and can't
/// sprint or jump until it recovers to ExhaustedRecoverLevel.
/// </summary>
public partial class CarrierStamina : Node
{
    [Export] public float Max { get; set; } = 100.0f;
    /// <summary>Drain while sprinting with no load, per second.</summary>
    [Export] public float SprintDrain { get; set; } = 18.0f;
    /// <summary>Extra sprint drain per kg of load, as a fraction: drain × (1 + this × kg).</summary>
    [Export] public float LoadDrainPerKg { get; set; } = 0.02f;
    [Export] public float JumpCost { get; set; } = 15.0f;
    [Export] public float RecoveryPerSecond { get; set; } = 14.0f;
    /// <summary>Seconds after the last use before stamina starts recovering.</summary>
    [Export] public float RecoveryDelay { get; set; } = 0.8f;
    /// <summary>Once exhausted, stamina must recover to this before sprinting or jumping again.</summary>
    [Export] public float ExhaustedRecoverLevel { get; set; } = 30.0f;

    public float Current { get; private set; }
    public bool Exhausted { get; private set; }
    public bool CanSprint => !Exhausted && Current > 0.0f;

    private float _sinceUse;

    public override void _Ready()
    {
        Current = Max;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _sinceUse += dt;
        if (_sinceUse < RecoveryDelay)
        {
            return;
        }

        Current = Mathf.Min(Max, Current + RecoveryPerSecond * dt);
        if (Exhausted && Current >= ExhaustedRecoverLevel)
        {
            Exhausted = false;
        }
    }

    /// <summary>Drains one physics step of sprinting.</summary>
    public void DrainSprint(float dt, float loadMass)
    {
        Current -= SprintDrain * (1.0f + LoadDrainPerKg * loadMass) * dt;
        _sinceUse = 0.0f;
        if (Current <= 0.0f)
        {
            Current = 0.0f;
            Exhausted = true;
        }
    }

    /// <summary>Spends a fixed cost (a jump) if there is enough; returns whether it was spent.</summary>
    public bool TrySpend(float amount)
    {
        if (Exhausted || Current < amount)
        {
            return false;
        }
        Current -= amount;
        _sinceUse = 0.0f;
        return true;
    }

    public void Refill()
    {
        Current = Max;
        Exhausted = false;
    }
}
