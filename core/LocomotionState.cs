namespace DownhillRoguelite.Core;

/// <summary>High-level movement state of the carrier, published with every LocomotionSnapshot.</summary>
public enum LocomotionState
{
    Idle,
    Striding,
    Braking,
    Sliding,
    Airborne,
}
