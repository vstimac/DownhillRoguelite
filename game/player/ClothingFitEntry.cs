using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// One hand-made correction of a worn piece on one character (saved by the clothing fitting scene,
/// debug/ClothingFitting.tscn): a move, turn and scale in the body's frame (X right, Y up, Z back),
/// in metres and degrees, about the piece's own centre at the rest pose.
/// </summary>
[Tool]
[GlobalClass]
public partial class ClothingFitEntry : Resource
{
    /// <summary>The character (CarrierCharacter.Id) this fit is for.</summary>
    [Export] public string CharacterId { get; set; } = "";
    /// <summary>The piece: an item id, a base piece (body_shirt, base_pack…) or one part of it (leather_gloves-Left).</summary>
    [Export] public string Piece { get; set; } = "";
    [Export] public Vector3 Offset { get; set; }
    [Export] public Vector3 RotationDegrees { get; set; }
    [Export] public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>The correction as a transform, composed like a Node3D's (YXZ rotation, then local scale).</summary>
    public Transform3D Local => new(Basis.FromEuler(RotationDegrees * (Mathf.Pi / 180.0f)) * Basis.FromScale(Scale), Offset);
}
