using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The carrier's health. Damage comes from physical impacts while ragdolling (PlayerRagdoll). No
/// regeneration during a run. At zero the carrier is dead: it stays down, and after RespawnDelay a
/// respawn is requested (debug stand-in until runs and a fail state exist). Publishes a
/// VitalsSnapshot with stamina every physics frame.
/// </summary>
public partial class CarrierHealth : Node
{
    [Export] public CarrierStamina? Stamina { get; set; }
    [Export] public PlayerController? Body { get; set; }
    [Export] public float Max { get; set; } = 100.0f;
    /// <summary>Seconds dead before a respawn is requested.</summary>
    [Export] public float RespawnDelay { get; set; } = 4.0f;

    public float Current { get; private set; }
    public bool Dead { get; private set; }

    private float _deadTime;
    private float _lastDamage;
    private string _lastSource = "";

    public override void _Ready()
    {
        Current = Max;
        GameEvents.RespawnRequested += OnRespawnRequested;
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnRespawnRequested;
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
            _deadTime = 0.0f;
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
        if (Dead)
        {
            _deadTime += (float)delta;
            if (_deadTime >= RespawnDelay)
            {
                _deadTime = 0.0f;
                GameEvents.PublishRespawnRequest();
            }
        }

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
        GameEvents.PublishVitals(new VitalsSnapshot(Current, Max, stamina, maxStamina, exhausted, sprinting, Dead, _lastDamage, _lastSource));
    }

    private void OnRespawnRequested()
    {
        Refill();
    }
}
