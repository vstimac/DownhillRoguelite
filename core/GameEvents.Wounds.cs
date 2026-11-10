using System;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Wounds: a blow that cut into a body (player/MeleeCombat), for blood (environment/BloodEffects),
/// sounds and tests.
/// </summary>
public static partial class GameEvents
{
    /// <summary>
    /// A blade or boot went into a body: (the target node, the spot, the blow's direction, the zone,
    /// the damage after the zone's weight).
    /// </summary>
    public static event Action<Node, Vector3, Vector3, BodyZone, float>? Wounded;

    public static void PublishWounded(Node target, Vector3 point, Vector3 direction, BodyZone zone, float damage) =>
        Wounded?.Invoke(target, point, direction, zone, damage);
}
