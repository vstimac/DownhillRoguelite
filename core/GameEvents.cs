using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Static C# event hub that systems use to talk without referencing each other.
/// Subscribers must unsubscribe in _ExitTree, because these events outlive scenes.
/// </summary>
public static class GameEvents
{
    /// <summary>Raised by the player controller once per physics frame.</summary>
    public static event Action<LocomotionSnapshot>? LocomotionUpdated;

    /// <summary>Raised by the foot IK once per rendered frame, after the feet are placed.</summary>
    public static event Action<FootPlacementSnapshot>? FootPlacementUpdated;

    /// <summary>Raised by the inventory (a test load until I1) whenever what the carrier holds changes.</summary>
    public static event Action<LoadSnapshot>? LoadChanged;

    /// <summary>Raised by the balance system once per physics frame.</summary>
    public static event Action<BalanceSnapshot>? BalanceUpdated;

    /// <summary>The most recent load, so systems created after the last change can catch up.</summary>
    public static LoadSnapshot CurrentLoad { get; private set; } = LoadSnapshot.Empty;

    /// <summary>Raised when the carrier sets the pack down to repack it (true) or puts it back on (false).</summary>
    public static event Action<bool>? PackingModeChanged;

    /// <summary>Whether the carrier is packing; movement, balance input and debug keys stand down.</summary>
    public static bool IsPacking { get; private set; }

    public static void PublishPackingMode(bool packing)
    {
        IsPacking = packing;
        PackingModeChanged?.Invoke(packing);
    }

    /// <summary>Raised when the carrier touches down after being airborne, with the downward speed in m/s.</summary>
    public static event Action<float>? Landed;

    /// <summary>Raised when the carrier goes down as a ragdoll (true) and when it is back on its feet (false).</summary>
    public static event Action<bool>? DownedChanged;

    /// <summary>Whether the carrier is a ragdoll or getting up: no movement or balance input.</summary>
    public static bool IsDowned { get; private set; }

    /// <summary>Raised when the carrier takes damage, with the amount.</summary>
    public static event Action<float>? Damaged;

    /// <summary>Raised by the health system once per physics frame.</summary>
    public static event Action<VitalsSnapshot>? VitalsUpdated;

    /// <summary>Raised when the carrier died and should be brought back (debug respawn until runs exist).</summary>
    public static event Action? RespawnRequested;

    public static void PublishLanded(float downwardSpeed)
    {
        Landed?.Invoke(downwardSpeed);
    }

    public static void PublishDowned(bool downed)
    {
        IsDowned = downed;
        DownedChanged?.Invoke(downed);
    }

    public static void PublishDamaged(float amount)
    {
        Damaged?.Invoke(amount);
    }

    public static void PublishVitals(VitalsSnapshot snapshot)
    {
        VitalsUpdated?.Invoke(snapshot);
    }

    public static void PublishRespawnRequest()
    {
        RespawnRequested?.Invoke();
    }

    public static void PublishLoad(LoadSnapshot snapshot)
    {
        CurrentLoad = snapshot;
        LoadChanged?.Invoke(snapshot);
    }

    public static void PublishBalance(BalanceSnapshot snapshot)
    {
        BalanceUpdated?.Invoke(snapshot);
    }

    public static void PublishLocomotion(LocomotionSnapshot snapshot)
    {
        LocomotionUpdated?.Invoke(snapshot);
    }

    public static void PublishFootPlacement(FootPlacementSnapshot snapshot)
    {
        FootPlacementUpdated?.Invoke(snapshot);
    }
}
