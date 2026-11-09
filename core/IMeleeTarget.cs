using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Something the carrier's sword (or boot) can hit: a night beast, Tuntun Sahur. Nodes implementing
/// it join the "melee_target" group so the carrier's combat can find them without knowing their kind.
/// Its body is a standing capsule: from HitBase up HitHeight metres, HitRadius thick.
/// </summary>
public interface IMeleeTarget
{
    public const string Group = "melee_target";

    Vector3 HitBase { get; }
    float HitHeight { get; }
    float HitRadius { get; }
    /// <summary>False once it is beaten (fleeing or down) and can't be hit any more.</summary>
    bool CanBeHit { get; }

    /// <summary>Takes a blow: <paramref name="damage"/> health and a shove (m/s) away from the carrier.</summary>
    void TakeHit(float damage, Vector3 push, string source);
}
