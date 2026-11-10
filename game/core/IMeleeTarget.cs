using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Something the carrier's sword (or boot) can hit: an enemy, a night beast, an animal. Nodes
/// implementing it join the "melee_target" group so the carrier's combat can find them without
/// knowing their kind. Its body is a standing capsule (from HitBase up HitHeight metres, HitRadius
/// thick), or, when it gives its skinned skeleton (HitSkeleton), boxes that follow its bones
/// (BoneHitboxes): then the blade has to pass through where the body really is.
/// </summary>
public interface IMeleeTarget
{
    public const string Group = "melee_target";

    Vector3 HitBase { get; }
    float HitHeight { get; }
    float HitRadius { get; }
    /// <summary>False once it is beaten (fleeing or down) and can't be hit any more.</summary>
    bool CanBeHit { get; }
    /// <summary>The skeleton its skinned body moves with, for hits on the body itself; null for the capsule.</summary>
    Skeleton3D? HitSkeleton => null;
    /// <summary>False for what Tab's lock-on should pass over (animals).</summary>
    bool Lockable => true;

    /// <summary>Takes a blow: <paramref name="damage"/> health and a shove (m/s) away from the carrier.</summary>
    void TakeHit(float damage, Vector3 push, string source);

    /// <summary>
    /// Loses <paramref name="damage"/> health to an open wound (environment/Bleeding): no flinch, no
    /// cry, no shove. False once it is down (the bleeding stops). Bodies that don't override it take
    /// it as a quiet blow.
    /// </summary>
    bool Bleed(float damage)
    {
        TakeHit(damage, Vector3.Zero, "bleeding");
        return CanBeHit;
    }
}
