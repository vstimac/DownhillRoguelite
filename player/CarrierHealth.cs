using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The carrier's health. Damage comes from physical impacts while ragdolling (PlayerRagdoll), hunger,
/// thirst and night cold. No regeneration during the day; a night's sleep at a station heals part.
/// At zero the carrier is dead for good (CarrierDied; the run loop in RunState ends the run and
/// starts the next on a new mountain). KillRequested (the abyss behind the summit) kills at once.
/// Publishes a VitalsSnapshot with stamina every physics frame.
/// </summary>
public partial class CarrierHealth : Node
{
    [Export] public CarrierStamina? Stamina { get; set; }
    [Export] public PlayerController? Body { get; set; }
    [Export] public CarrierNeeds? Needs { get; set; }
    [Export] public float Max { get; set; } = 100.0f;
    /// <summary>Fraction of maximum health a night's sleep in a station restores.</summary>
    [Export] public float SleepHealFraction { get; set; } = 0.4f;

    public float Current { get; private set; }
    public bool Dead { get; private set; }

    private float _lastDamage;
    private string _lastSource = "";

    public override void _Ready()
    {
        Current = Max;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.Slept += OnSlept;
        GameEvents.KillRequested += OnKillRequested;
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.Slept -= OnSlept;
        GameEvents.KillRequested -= OnKillRequested;
    }

    private void OnKillRequested(string cause)
    {
        TakeDamage(Max * 10.0f, cause);
    }

    /// <summary>Health doesn't regenerate on its own; only sleep (and later medicine) heals.</summary>
    public void Heal(float amount)
    {
        if (Dead || amount <= 0.0f)
        {
            return;
        }
        Current = Mathf.Min(Max, Current + amount);
    }

    private void OnSlept(float hours)
    {
        float modifier = 1.0f;
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run != null)
        {
            modifier = run.Modifiers.SleepHeal;
        }
        Heal(Max * SleepHealFraction * modifier);
    }

    public void TakeDamage(float amount, string source)
    {
        if (Dead || amount <= 0.0f)
        {
            return;
        }

        Current = Mathf.Max(0.0f, Current - amount);
        _lastDamage = amount;
        _lastSource = source;
        GameEvents.PublishDamaged(amount);
        if (Current <= 0.0f)
        {
            Dead = true;
            GameEvents.PublishCarrierDied(source);
        }
    }

    public void Refill()
    {
        Current = Max;
        Dead = false;
        _lastDamage = 0.0f;
        _lastSource = "";
        Stamina?.Refill();
    }

    public override void _PhysicsProcess(double delta)
    {
        float stamina = 0.0f;
        float maxStamina = 0.0f;
        bool exhausted = false;
        if (Stamina != null)
        {
            stamina = Stamina.Current;
            maxStamina = Stamina.Max;
            exhausted = Stamina.Exhausted;
        }
        bool sprinting = Body != null && Body.Sprinting;
        float hunger = CarrierNeeds.Full;
        float thirst = CarrierNeeds.Full;
        if (Needs != null)
        {
            hunger = Needs.Hunger;
            thirst = Needs.Thirst;
        }
        GameEvents.PublishVitals(new VitalsSnapshot(Current, Max, stamina, maxStamina, exhausted, sprinting, Dead, _lastDamage, _lastSource, hunger, thirst));
    }

    private void OnRespawnRequested()
    {
        Refill();
    }
}
