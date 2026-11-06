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
