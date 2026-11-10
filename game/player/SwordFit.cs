using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Hand-tuned corrections for where the sword and shield sit, on top of the places MeleeCombat works
/// out from the rig: the sword in the right hand, the sword in its sheath on the left hip, and the
/// shield on the left forearm. Each is a shift (m) and a turn (degrees, X then Y then Z) in the
/// thing's own frame: for the sword, Y runs along the blade from the grip to the tip, X across the
/// guard. Set in the sword fitting scene (debug/SwordFitting.tscn) and saved to player/sword_fit.tres.
/// </summary>
[GlobalClass]
public partial class SwordFit : Resource
{
    public const string DefaultPath = "res://player/sword_fit.tres";

    [Export] public Vector3 HandPosition { get; set; }
    [Export] public Vector3 HandRotation { get; set; }
    [Export] public Vector3 HipPosition { get; set; }
    [Export] public Vector3 HipRotation { get; set; }
    [Export] public Vector3 ShieldPosition { get; set; }
    [Export] public Vector3 ShieldRotation { get; set; }
    /// <summary>The sword's size, and the shield's, as a share of the model's own.</summary>
    [Export] public float SwordScale { get; set; } = 1.0f;
    [Export] public float ShieldScale { get; set; } = 1.0f;

    /// <summary>The correction for one place as a transform in the thing's own frame.</summary>
    public static Transform3D Offset(Vector3 position, Vector3 rotationDegrees, float scale)
    {
        Basis turn = Basis.FromEuler(new Vector3(
            Mathf.DegToRad(rotationDegrees.X), Mathf.DegToRad(rotationDegrees.Y), Mathf.DegToRad(rotationDegrees.Z)));
        return new Transform3D(turn.Scaled(Vector3.One * Mathf.Max(scale, 0.01f)), position);
    }

    public Transform3D Hand => Offset(HandPosition, HandRotation, SwordScale);
    public Transform3D Hip => Offset(HipPosition, HipRotation, SwordScale);
    public Transform3D Shield => Offset(ShieldPosition, ShieldRotation, ShieldScale);
}
