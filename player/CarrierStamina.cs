using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Stamina is the carrier's breath, the way Valheim does it: walking on the flat and downhill is
/// free, climbing a steep bit costs a little, sprinting and jumping are what spend it (more with a
/// heavier load). It comes back quickly a moment after the last effort, while standing and (a bit
/// slower) while walking. At zero the carrier is out of breath: still walking, a little slower, but
/// no sprinting or jumping until stamina is back to ExhaustedRecoverLevel. Hunger lowers the maximum
/// and thirst slows recovery (CarrierNeeds). See docs/survival-loop.md.
/// </summary>
public partial class CarrierStamina : Node
{
    [Export] public float Max { get; set; } = 100.0f;

    [ExportGroup("Walking")]
    /// <summary>Drain while walking on the flat with no load, per second (none: walking is free).</summary>
    [Export] public float WalkDrain { get; set; } = 0.0f;
    /// <summary>Drain while walking straight up a slope of grade 1 (45°), per second; gentle climbs cost little.</summary>
    [Export] public float UphillDrain { get; set; } = 5.0f;
    /// <summary>Extra walk and sprint drain per kg of load, as a fraction: drain × (1 + this × kg).</summary>
    [Export] public float LoadDrainPerKg { get; set; } = 0.02f;
    /// <summary>Extra drain per unit of uphill grade (sine of the slope along the walk).</summary>
    [Export] public float UphillDrainPerGrade { get; set; } = 0.0f;
    /// <summary>Extra drain per unit of downhill grade: steep descents work the legs too.</summary>
    [Export] public float DownhillDrainPerGrade { get; set; } = 0.0f;
    /// <summary>Drain multiplier for careful braking steps.</summary>
    [Export] public float BrakeDrainFactor { get; set; } = 0.8f;

    [ExportGroup("Sprint and jump")]
    /// <summary>Drain while sprinting with no load, per second (on top of walking).</summary>
    [Export] public float SprintDrain { get; set; } = 12.0f;
    [Export] public float JumpCost { get; set; } = 10.0f;

    [ExportGroup("Recovery")]
    /// <summary>Recovery while standing still, per second.</summary>
    [Export] public float RecoveryPerSecond { get; set; } = 18.0f;
    /// <summary>Recovery while walking, as a share of the standing rate.</summary>
    [Export] public float WalkingRecovery { get; set; } = 0.75f;
    /// <summary>Seconds after the last effort before stamina starts recovering.</summary>
    [Export] public float RecoveryDelay { get; set; } = 0.8f;
    /// <summary>Out of breath, the carrier walks at this share of its speed.</summary>
    [Export] public float ExhaustedWalkFactor { get; set; } = 0.7f;
    /// <summary>Once out of breath, stamina must recover to this before the carrier can sprint or jump again.</summary>
    [Export] public float ExhaustedRecoverLevel { get; set; } = 20.0f;

    public float Current { get; private set; }
    /// <summary>Out of stamina: slower walking, no sprinting or jumping until it recovers.</summary>
    public bool Exhausted { get; private set; }
    /// <summary>Scales recovery; thirst lowers it (CarrierNeeds).</summary>
    public float RecoveryMultiplier { get; set; } = 1.0f;
    /// <summary>Scales the maximum; hunger lowers it (CarrierNeeds).</summary>
    public float MaxMultiplier { get; set; } = 1.0f;
    public float EffectiveMax => Mathf.Max(0.0f, Max * MaxMultiplier * (_run?.Character.Stats.Stamina ?? 1));
    /// <summary>Recovery threshold, capped so a reduced maximum can always reach it.</summary>
    public float EffectiveRecoverLevel => Mathf.Min(ExhaustedRecoverLevel, EffectiveMax);
    /// <summary>Seconds of the recovery delay still left, for route estimates.</summary>
    public float RecoveryWaitSeconds => Mathf.Max(0.0f, RecoveryDelay - _sinceUse);
    public bool CanSprint => !Exhausted && Current > 0.0f;
    /// <summary>Recovery per second right now, with needs applied.</summary>
    public float EffectiveRecovery => RecoveryPerSecond * RecoveryMultiplier * (_run?.Character.Stats.Recovery ?? 1);

    private float _sinceUse;

    private RunState? _run;
    private float _characterCapacity = 1;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (_run != null) { _characterCapacity = _run.Character.Stats.Stamina; _run.CharacterChanged += OnCharacterChanged; }
        Refill();
    }

    public override void _ExitTree() { if (_run != null) { _run.CharacterChanged -= OnCharacterChanged; } }

    private void OnCharacterChanged()
    {
        float next = _run!.Character.Stats.Stamina;
        Current = Mathf.Min(EffectiveMax, Current * next / _characterCapacity);
        _characterCapacity = next;
    }

    public void Restore(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0) { return; }
        Current = Mathf.Min(EffectiveMax, Current + amount);
        if (Current > 0 && Current >= EffectiveRecoverLevel) { Exhausted = false; }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float wait = RecoveryWaitSeconds;
        bool moving = _movingFor > 0.0f;
        _movingFor = Mathf.Max(0.0f, _movingFor - dt);
        _sinceUse += dt;
        Current = Mathf.Min(Current, EffectiveMax);
        float recoveryTime = Mathf.Max(0.0f, dt - wait);
        if (recoveryTime <= 0.0f)
        {
            return;
        }

        float rate = EffectiveRecovery;
        if (moving)
        {
            rate *= WalkingRecovery;
        }
        Current = Mathf.Min(EffectiveMax, Current + rate * recoveryTime);
        if (Exhausted && Current > 0.0f && Current >= EffectiveRecoverLevel)
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
        float drain = WalkDrain * slope + UphillDrain * Mathf.Max(0.0f, -grade);
        if (braking)
        {
            drain *= BrakeDrainFactor;
        }
        if (sprinting)
        {
            drain += SprintDrain;
        }
        // Boons (RunState.Modifiers) can ease walking and the load's cost.
        float walkModifier = 1.0f;
        float loadModifier = 1.0f;
        if (_run != null)
        {
            walkModifier = _run.Modifiers.WalkDrain;
            loadModifier = _run.Modifiers.LoadDrain;
            loadModifier *= _run.Character.Stats.LoadEffort;
        }
        return drain * walkModifier * (1.0f + LoadDrainPerKg * loadModifier * loadMass) * GameEvents.CurrentGear.StaminaFactor * (_run?.Character.StaminaCost ?? 1);
    }

    /// <summary>Drains one physics step of moving under one's own power.</summary>
    public void DrainMovement(float dt, float loadMass, float grade, bool braking, bool sprinting)
    {
        _movingFor = 0.2f;
        Drain(WalkDrainPerSecond(loadMass, grade, braking, sprinting) * dt);
    }

    private float _movingFor;

    /// <summary>Spends a fixed cost (a jump) if there is enough; returns whether it was spent.</summary>
    public bool TrySpend(float amount)
    {
        float cost = amount * GameEvents.CurrentGear.StaminaFactor * (_run?.Character.StaminaCost ?? 1);
        if (!float.IsFinite(cost) || cost < 0.0f || Exhausted || Current < cost)
        {
            return false;
        }
        Drain(cost);
        return true;
    }

    /// <summary>Sets stamina directly (tests and debugging).</summary>
    public void SetCurrent(float value)
    {
        if (value < Current)
        {
            // Like spending it: recovery waits its moment.
            _sinceUse = 0.0f;
        }
        Current = Mathf.Clamp(value, 0.0f, EffectiveMax);
        Exhausted = Current <= 0.0f;
    }

    public void Refill()
    {
        Current = EffectiveMax;
        Exhausted = Current <= 0.0f;
        _sinceUse = RecoveryDelay;
        _movingFor = 0.0f;
    }

    private void Drain(float amount)
    {
        // Effortless steps (walking on the flat or downhill) don't hold recovery back.
        if (amount <= 0.0f)
        {
            return;
        }
        Current -= amount;
        _sinceUse = 0.0f;
        if (Current <= 0.0f)
        {
            Current = 0.0f;
            Exhausted = true;
        }
    }
}
