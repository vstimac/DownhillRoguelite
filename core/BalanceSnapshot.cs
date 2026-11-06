using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// One physics frame of the balance system. Two-axis values are (roll, pitch): roll is positive
/// toward the carrier's right, pitch positive toward its forward.
/// </summary>
/// <param name="State">Current balance state.</param>
/// <param name="StateTime">Seconds spent in the current state.</param>
/// <param name="Tilt">Upper body and load tilt about the feet, in radians.</param>
/// <param name="Imbalance">Centre-of-mass offset over the support, as a fraction of the support half size (1 = at the edge).</param>
/// <param name="LeanInput">Player lean input, -1 (left) to 1 (right).</param>
/// <param name="Bracing">Whether the player is bracing.</param>
/// <param name="Forward">Carrier forward in world space (horizontal).</param>
/// <param name="Right">Carrier right in world space (horizontal).</param>
/// <param name="SupportCenter">Centre of the support area on the ground, in world space.</param>
/// <param name="SupportHalfExtents">Half width (right) and half length (forward) of the support area, in m.</param>
/// <param name="CenterOfMassGround">Combined centre of mass projected onto the ground, in world space.</param>
/// <param name="CenterOfMassHeight">Combined centre of mass height above the feet, in m.</param>
/// <param name="GravityTorque">Toppling torque from gravity and load offset, in N·m.</param>
/// <param name="InertialTorque">Torque from the carrier's acceleration, in N·m.</param>
/// <param name="SlopeTorque">Torque from the ground's slope under the feet, in N·m.</param>
/// <param name="CorrectionTorque">The carrier's own correcting torque, including player lean, in N·m.</param>
/// <param name="LoadName">Name of the current load.</param>
/// <param name="LoadMass">Mass of the current load, in kg.</param>
public readonly record struct BalanceSnapshot(
    BalanceState State,
    float StateTime,
    Vector2 Tilt,
    Vector2 Imbalance,
    float LeanInput,
    bool Bracing,
    Vector3 Forward,
    Vector3 Right,
    Vector3 SupportCenter,
    Vector2 SupportHalfExtents,
    Vector3 CenterOfMassGround,
    float CenterOfMassHeight,
    Vector2 GravityTorque,
    Vector2 InertialTorque,
    Vector2 SlopeTorque,
    Vector2 CorrectionTorque,
    string LoadName,
    float LoadMass)
{
    /// <summary>Overall imbalance: the larger of the two axes.</summary>
    public float ImbalanceMagnitude => Mathf.Max(Mathf.Abs(Imbalance.X), Mathf.Abs(Imbalance.Y));
}
