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

/// <summary>Co-op creatures and kill loot (network/NetMobs).</summary>
public static partial class GameEvents
{
    /// <summary>A puppet creature (another machine's) took a blow here: its owner applies it. Damage, push (m/s, world), source.</summary>
    public static event Action<INetMob, float, Godot.Vector3, string>? NetMobHit;
    /// <summary>A kill dropped an item here: the item node, its id, and where it lands (shared with the others in co-op).</summary>
    public static event Action<Godot.Node3D, string, Godot.Vector3>? LootDropped;

    public static void PublishNetMobHit(INetMob mob, float damage, Godot.Vector3 push, string source) => NetMobHit?.Invoke(mob, damage, push, source);

    public static void PublishLootDropped(Godot.Node3D item, string itemId, Godot.Vector3 landing) => LootDropped?.Invoke(item, itemId, landing);
}

/// <summary>Co-op: creatures hunt every carrier, not only the one whose machine runs them (network/NetMobs).</summary>
public static partial class GameEvents
{
    private static NetCarrier[] _otherCarriers = Array.Empty<NetCarrier>();

    /// <summary>The other players' carriers as shown here (empty with no session): creatures may go for them too.</summary>
    public static NetCarrier[] OtherCarriers => _otherCarriers;

    /// <summary>A creature run here struck another player's carrier: their machine takes the blow.</summary>
    public static event Action<NetStrike>? NetCarrierStruck;

    public static void PublishOtherCarriers(NetCarrier[] carriers) => _otherCarriers = carriers;

    public static void PublishNetStrike(NetStrike strike) => NetCarrierStruck?.Invoke(strike);

    /// <summary>A blow from a creature on another machine lands on this carrier: the shield may take it, else it hurts and shoves.</summary>
    public static void TakeNetStrike(NetStrike strike)
    {
        if (TryBlock(strike.Away, strike.Cause))
        {
            return;
        }
        RequestDamage(strike.Damage, strike.Cause);
        RequestCameraShake(0.05f + strike.Push * 0.02f);
        if (strike.Knockdown)
        {
            RequestKnockdown(strike.Away * strike.Push + Godot.Vector3.Up * strike.Push * strike.Lift, strike.Cause);
        }
        else
        {
            RequestLaunch(strike.Away * strike.Push, strike.StaggerHeight, strike.Cause);
        }
    }
}
