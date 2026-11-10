using Godot;

namespace DownhillRoguelite.Core;

/// <summary>One foot's placement for a frame, published inside a FootPlacementSnapshot.</summary>
/// <param name="Target">World position the leg IK pulls the ankle to.</param>
/// <param name="Landing">Predicted landing point of the foot (on the ground when it was found).</param>
/// <param name="GroundPoint">Ground under the foot.</param>
/// <param name="GroundNormal">Surface normal at GroundPoint.</param>
/// <param name="Pole">Knee pole target in world space.</param>
/// <param name="Planted">Whether the foot is locked to the ground.</param>
/// <param name="Weight">IK influence on this leg, 0 to 1.</param>
/// <param name="HasGround">Whether ground was found under the foot.</param>
public readonly record struct FootSnapshot(
    Vector3 Target,
    Vector3 Landing,
    Vector3 GroundPoint,
    Vector3 GroundNormal,
    Vector3 Pole,
    bool Planted,
    float Weight,
    bool HasGround);
