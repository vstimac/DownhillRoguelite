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

    public static void PublishLocomotion(LocomotionSnapshot snapshot)
    {
        LocomotionUpdated?.Invoke(snapshot);
    }

    public static void PublishFootPlacement(FootPlacementSnapshot snapshot)
    {
        FootPlacementUpdated?.Invoke(snapshot);
    }
}
