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

    /// <summary>The carrier wants to eat or drink: (need, how much is missing, 0 to 100). The inventory answers.</summary>
    public static event Action<SupplyNeed, float>? ConsumeRequested;

    /// <summary>An item was eaten or drunk: (need, item id, how much it restores).</summary>
    public static event Action<SupplyNeed, string, float>? ItemConsumed;

    /// <summary>The carrier tried to eat or drink with nothing suitable packed.</summary>
    public static event Action<SupplyNeed>? ConsumeFailed;

    /// <summary>An item went into the pack from the world: (item id, category name).</summary>
    public static event Action<string, string>? ItemPickedUp;

    public static void RequestConsume(SupplyNeed need, float deficit)
    {
        ConsumeRequested?.Invoke(need, deficit);
    }

    public static void PublishItemConsumed(SupplyNeed need, string itemId, float restore)
    {
        ItemConsumed?.Invoke(need, itemId, restore);
    }

    public static void PublishConsumeFailed(SupplyNeed need)
    {
        ConsumeFailed?.Invoke(need);
    }

    public static void PublishItemPickedUp(string itemId, string category)
    {
        ItemPickedUp?.Invoke(itemId, category);
    }

    /// <summary>Whether the carrier is inside a station's shelter (warm, can pack and sleep).</summary>
    public static bool InShelter { get; private set; }

    /// <summary>The carrier entered (true) or left (false) a station's shelter: (inside, station index).</summary>
    public static event Action<bool, int>? ShelterChanged;

    /// <summary>The carrier reached a station for the first time this run: (station index).</summary>
    public static event Action<int>? StationReached;

    /// <summary>The carrier slept through the night: (hours slept).</summary>
    public static event Action<float>? Slept;

    /// <summary>The latest estimate to the next station, from the carrier's route estimator.</summary>
    public static event Action<RouteEstimate>? RouteEstimateUpdated;

    public static RouteEstimate LatestEstimate { get; private set; }

    public static void PublishShelter(bool inside, int station)
    {
        InShelter = inside;
        ShelterChanged?.Invoke(inside, station);
    }

    public static void PublishStationReached(int station)
    {
        StationReached?.Invoke(station);
    }

    public static void PublishSlept(float hours)
    {
        Slept?.Invoke(hours);
    }

    public static void PublishRouteEstimate(RouteEstimate estimate)
    {
        LatestEstimate = estimate;
        RouteEstimateUpdated?.Invoke(estimate);
    }

    /// <summary>Raised by terrain when the deepest point reached this run grows, in metres below the start.</summary>
    public static event Action<float>? DepthChanged;

    public static void PublishDepth(float depth)
    {
        DepthChanged?.Invoke(depth);
    }

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
