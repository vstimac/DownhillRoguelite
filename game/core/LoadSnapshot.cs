using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Mass properties of everything the carrier has on its back, in carrier space: origin at the
/// feet, X to the carrier's right, Y up, Z forward. Published by the inventory (a test load
/// until I1) whenever the load changes; balance reads it.
/// </summary>
/// <param name="Name">Display name of the load.</param>
/// <param name="Mass">Total load mass in kg.</param>
/// <param name="CenterOfMass">Load centre of mass in carrier space, in m.</param>
/// <param name="RollInertia">Moment of inertia about the forward axis through the feet, in kg·m².</param>
/// <param name="PitchInertia">Moment of inertia about the right axis through the feet, in kg·m².</param>
/// <param name="ItemMasses">Per-item masses, for visuals.</param>
/// <param name="ItemPositions">Per-item centres in carrier space, for visuals.</param>
/// <param name="ItemSizes">Per-item box sizes, for visuals.</param>
public readonly record struct LoadSnapshot(
    string Name,
    float Mass,
    Vector3 CenterOfMass,
    float RollInertia,
    float PitchInertia,
    float[] ItemMasses,
    Vector3[] ItemPositions,
    Vector3[] ItemSizes)
{
    public static LoadSnapshot Empty => new("Empty", 0.0f, Vector3.Zero, 0.0f, 0.0f,
        System.Array.Empty<float>(), System.Array.Empty<Vector3>(), System.Array.Empty<Vector3>());
}
