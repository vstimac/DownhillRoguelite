using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// One physics frame of the carrier's movement. Balance, camera and debug systems read this
/// instead of referencing the player directly.
/// </summary>
/// <param name="Position">Feet position in world space.</param>
/// <param name="Velocity">Real velocity after collision, in m/s.</param>
/// <param name="Acceleration">Change in real velocity since the previous frame, in m/s².</param>
/// <param name="GroundNormal">Normal of the surface under or against the feet; up when airborne.</param>
/// <param name="SlopeDegrees">Angle between the ground normal and up.</param>
/// <param name="FallLine">Horizontal, normalized downhill direction; zero on flat ground.</param>
/// <param name="InputDirection">Camera-relative movement input in world space, length 0 to 1.</param>
/// <param name="State">High-level movement state.</param>
/// <param name="Surface">Display name of the surface underfoot.</param>
/// <param name="Grip">Grip of the surface underfoot, 0 to 1.</param>
public readonly record struct LocomotionSnapshot(
    Vector3 Position,
    Vector3 Velocity,
    Vector3 Acceleration,
    Vector3 GroundNormal,
    float SlopeDegrees,
    Vector3 FallLine,
    Vector3 InputDirection,
    LocomotionState State,
    string Surface,
    float Grip);
