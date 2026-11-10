using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Co-op (network/NetSession): who may pick up a shared supply, so two players never both get it.
/// Supplies scattered from the mountain's seed carry a slot key that is the same on every machine;
/// with no session there is no gate and everything is as in singleplayer.
/// </summary>
public static partial class GameEvents
{
    /// <summary>
    /// Asked before a keyed supply is taken: true lets it go into the pack now; false holds it (the
    /// session asks the host first, and takes it again once the host says yes). Null: no co-op.
    /// </summary>
    public static Func<string, bool>? PickupGate { get; set; }

    /// <summary>A keyed supply went into this player's pack.</summary>
    public static event Action<string>? SupplyPickedUp;

    public static bool MayPickUp(string slotKey)
    {
        if (PickupGate == null || slotKey.Length == 0)
        {
            return true;
        }
        return PickupGate(slotKey);
    }

    public static void PublishSupplyPickedUp(string slotKey) => SupplyPickedUp?.Invoke(slotKey);
}
