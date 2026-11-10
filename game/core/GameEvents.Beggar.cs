using System;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The newspaper seller who begs (narrative/NewspaperSeller.cs): an ambush she calls down when struck,
/// and what happens between her and the carrier, for the journal, tests and sounds.
/// </summary>
public static partial class GameEvents
{
    /// <summary>
    /// Asks the enemy director to bring this many enemies around a spot and set them on the carrier at
    /// once: (where, how many, who called them).
    /// </summary>
    public static event Action<Vector3, int, string>? AmbushRequested;

    public static void RequestAmbush(Vector3 at, int count, string cause) => AmbushRequested?.Invoke(at, count, cause);

    /// <summary>The newspaper seller's mood changed: (her name, the new state: "begging", "given", "struck").</summary>
    public static event Action<string, string>? BeggarChanged;

    public static void PublishBeggarChanged(string who, string state) => BeggarChanged?.Invoke(who, state);
}
