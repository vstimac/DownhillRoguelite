using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Co-op (network/NetSession): who may pick up a shared supply, so two players never both get it.
/// Supplies scattered from the mountain's seed carry a slot key that is the same on every machine;
/// with no session there is no gate and everything is as in singleplayer.
/// </summary>
public static partial class GameEvents
{
    public static bool SharedWorldActive { get; private set; }
    public static bool SharedWorldHost { get; private set; }
    public static bool MaySimulateWorld => !SharedWorldActive || SharedWorldHost;
    public static event Action? WorldAuthorityChanged;
    public static event Action<Godot.Vector3>? SharedCarrierFell;
    public static void SetWorldAuthority(bool active, bool host)
    {
        bool changed = SharedWorldActive != active || SharedWorldHost != host;
        SharedWorldActive = active;
        SharedWorldHost = active && host;
        if (changed) { WorldAuthorityChanged?.Invoke(); }
    }
    public static void PublishSharedCarrierFell(Godot.Vector3 at) => SharedCarrierFell?.Invoke(at);
    public static Func<string, Action, bool>? SharedNpcUseGate { get; set; }
    public static bool MayUseSharedNpc(string key, Action retry) => SharedNpcUseGate?.Invoke(key, retry) ?? true;
    /// <summary>
    /// Asked before a keyed supply is taken: true lets it go into the pack now; false holds it (the
    /// session asks the host first, and takes it again once the host says yes). Null: no co-op.
    /// </summary>
    public static Func<string, bool>? PickupGate { get; set; }
    public static Func<string, bool>? PickupWasTaken { get; set; }
    public static event Action<string, IInteractable>? SharedPickupAttempted;
    public static event Action<string>? SharedPickupTaken;
    public static bool WasSharedPickupTaken(string key) => PickupWasTaken?.Invoke(key) == true;
    public static void PublishSharedPickupTaken(string key) => SharedPickupTaken?.Invoke(key);

    /// <summary>A keyed supply went into this player's pack.</summary>
    public static event Action<string>? SupplyPickedUp;

    public static bool MayPickUp(string slotKey, IInteractable? preferred = null)
    {
        if (preferred != null) { SharedPickupAttempted?.Invoke(slotKey, preferred); }
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
    /// <summary>A kill or the carrier dropped an item here: the item node, its id, and where it lands (shared with the others in co-op).</summary>
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

    /// <summary>Co-op: this carrier is down and bleeding out (creatures leave them alone; a friend can help them up).</summary>
    public static bool CarrierBleeding { get; private set; }

    public static void PublishBleeding(bool bleeding) => CarrierBleeding = bleeding;

    public static void PublishNetStrike(NetStrike strike) => NetCarrierStruck?.Invoke(strike);

    /// <summary>A blow from a creature on another machine lands on this carrier: the shield may take it, else it hurts and shoves.</summary>
    public static void TakeNetStrike(NetStrike strike)
    {
        if (TryBlock(strike.Away, strike.Cause))
        {
            return;
        }
        if (strike.Damage > 0.0f)
        {
            RequestDamage(strike.Damage, strike.Cause);
        }
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
