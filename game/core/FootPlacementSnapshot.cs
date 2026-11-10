namespace DownhillRoguelite.Core;

/// <summary>
/// Foot placement for one frame. Balance (M2) can read where the feet are planted to judge
/// the support area under the centre of mass.
/// </summary>
/// <param name="Left">Left foot.</param>
/// <param name="Right">Right foot.</param>
/// <param name="HipOffset">Vertical hip adjustment in metres; negative lowers the hips.</param>
/// <param name="IkWeight">Overall IK blend, 0 when airborne or sliding.</param>
public readonly record struct FootPlacementSnapshot(
    FootSnapshot Left,
    FootSnapshot Right,
    float HipOffset,
    float IkWeight);
