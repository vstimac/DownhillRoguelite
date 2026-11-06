using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Hunger and thirst, 100 (fine) to 0, draining over in-game hours (WorldClock) — faster when
/// sprinting or heavily loaded, scaled by difficulty. Below LowThreshold, thirst slows stamina
/// recovery and hunger lowers maximum stamina; at 0 they drain health. Out at night without
/// shelter the cold drains health and slows recovery; sleep uses food and water more slowly
/// and refills stamina. F eats and Q drinks: the
/// request goes out as an event and the inventory consumes the best-fitting item.
/// See docs/survival-loop.md.
/// </summary>
public partial class CarrierNeeds : Node
{
    private static readonly StringName ActionEat = "player_eat";
    private static readonly StringName ActionDrink = "player_drink";

    [Export] public PlayerController? Body { get; set; }
    [Export] public CarrierStamina? Stamina { get; set; }
    [Export] public CarrierHealth? Health { get; set; }

    [ExportGroup("Drain (per in-game hour)")]
    /// <summary>Hunger lost per hour: empty after about 30 hours.</summary>
    [Export] public float HungerPerHour { get; set; } = 3.3f;
    /// <summary>Thirst lost per hour: empty after about 18 hours.</summary>
    [Export] public float ThirstPerHour { get; set; } = 5.5f;
    [Export] public float SprintMultiplier { get; set; } = 2.0f;
    /// <summary>Extra drain per kg of load, as a fraction.</summary>
    [Export] public float LoadPerKg { get; set; } = 0.01f;

    [ExportGroup("Effects")]
    [Export] public float LowThreshold { get; set; } = 25.0f;
    /// <summary>Stamina recovery multiplier when thirsty.</summary>
    [Export] public float ThirstyRecovery { get; set; } = 0.5f;
    /// <summary>Maximum stamina multiplier when hungry.</summary>
    [Export] public float HungryMaxStamina { get; set; } = 0.6f;
    /// <summary>Health lost per in-game hour for each need at zero.</summary>
    [Export] public float EmptyHealthPerHour { get; set; } = 8.0f;

    [ExportGroup("Night and sleep")]
    /// <summary>Health lost per in-game hour out in the cold of full night (× difficulty harshness).</summary>
    [Export] public float ColdHealthPerHour { get; set; } = 6.0f;
    /// <summary>Stamina recovery multiplier in the cold of full night.</summary>
    [Export] public float ColdRecovery { get; set; } = 0.5f;
    /// <summary>Hunger and thirst drain while asleep, as a fraction of waking drain.</summary>
    [Export] public float SleepDrainFactor { get; set; } = 0.3f;

    public const float Full = 100.0f;

    public float Hunger { get; private set; } = Full;
    public float Thirst { get; private set; } = Full;
    /// <summary>How cold the carrier is: 0 in daylight or shelter, up to 1 out in full night.</summary>
    public float Cold { get; private set; }

    private WorldClock? _clock;
    private RunState? _run;

    public override void _Ready()
    {
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.ItemConsumed += OnItemConsumed;
        GameEvents.RespawnRequested += Refill;
        GameEvents.Slept += OnSlept;
    }

    public override void _ExitTree()
    {
        GameEvents.ItemConsumed -= OnItemConsumed;
        GameEvents.RespawnRequested -= Refill;
        GameEvents.Slept -= OnSlept;
    }

    public void Refill()
    {
        Hunger = Full;
        Thirst = Full;
    }

    /// <summary>Sets both needs directly (tests and debugging).</summary>
    public void SetNeeds(float hunger, float thirst)
    {
        Hunger = Mathf.Clamp(hunger, 0.0f, Full);
        Thirst = Mathf.Clamp(thirst, 0.0f, Full);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!CanAct())
        {
            return;
        }
        if (@event.IsActionPressed(ActionEat))
        {
            GameEvents.RequestConsume(SupplyNeed.Hunger, Full - Hunger);
        }
        else if (@event.IsActionPressed(ActionDrink))
        {
            GameEvents.RequestConsume(SupplyNeed.Thirst, Full - Thirst);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_clock == null)
        {
            return;
        }

        float hours = _clock.HoursPerSecond * (float)delta;
        float rate = DrainMultiplier();
        Hunger = Mathf.Max(0.0f, Hunger - HungerPerHour * rate * hours);
        Thirst = Mathf.Max(0.0f, Thirst - ThirstPerHour * rate * hours);

        Cold = 0.0f;
        if (_clock.IsNight && !GameEvents.InShelter)
        {
            float harshness = 1.0f;
            if (_run != null)
            {
                harshness = _run.DifficultySettings.NightHarshness;
            }
            Cold = (1.0f - _clock.Daylight) * harshness;
        }

        if (Stamina != null)
        {
            Stamina.RecoveryMultiplier = Mathf.Lerp(1.0f, ColdRecovery, Cold);
            if (Thirst < LowThreshold)
            {
                Stamina.RecoveryMultiplier *= ThirstyRecovery;
            }
            Stamina.MaxMultiplier = 1.0f;
            if (Hunger < LowThreshold)
            {
                Stamina.MaxMultiplier = HungryMaxStamina;
            }
        }

        if (Health != null)
        {
            if (Hunger <= 0.0f)
            {
                Health.TakeDamage(EmptyHealthPerHour * hours, "hunger");
            }
            if (Thirst <= 0.0f)
            {
                Health.TakeDamage(EmptyHealthPerHour * hours, "thirst");
            }
            if (Cold > 0.0f)
            {
                Health.TakeDamage(ColdHealthPerHour * Cold * hours, "cold");
            }
        }
    }

    private float DrainMultiplier()
    {
        float rate = 1.0f + LoadPerKg * GameEvents.CurrentLoad.Mass;
        if (Body != null && Body.Sprinting)
        {
            rate *= SprintMultiplier;
        }
        if (_run != null)
        {
            rate *= _run.DifficultySettings.NeedsDrainMultiplier;
        }
        return rate;
    }

    /// <summary>Asleep, the body still uses food and water, only more slowly; the rest restores stamina.</summary>
    private void OnSlept(float hours)
    {
        float rate = SleepDrainFactor;
        if (_run != null)
        {
            rate *= _run.DifficultySettings.NeedsDrainMultiplier;
        }
        Hunger = Mathf.Max(0.0f, Hunger - HungerPerHour * rate * hours);
        Thirst = Mathf.Max(0.0f, Thirst - ThirstPerHour * rate * hours);
        Stamina?.Refill();
    }

    private void OnItemConsumed(SupplyNeed need, string itemId, float restore)
    {
        if (need == SupplyNeed.Hunger)
        {
            Hunger = Mathf.Min(Full, Hunger + restore);
        }
        else
        {
            Thirst = Mathf.Min(Full, Thirst + restore);
        }
    }

    private bool CanAct()
    {
        if (GameEvents.IsPacking || GameEvents.IsDowned)
        {
            return false;
        }
        return Body == null || Body.InputEnabled;
    }
}
