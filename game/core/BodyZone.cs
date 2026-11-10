namespace DownhillRoguelite.Core;

/// <summary>
/// Where a blow lands on a body (BoneHitboxes.ZoneOf names a bone's zone; MeleeCombat weighs the
/// damage by it, BloodEffects how hard it bleeds). Body is a part with no special weight (a tail, a
/// horn, a capsule without height).
/// </summary>
public enum BodyZone
{
    Body,
    Head,
    Neck,
    Torso,
    Arm,
    Leg,
}
