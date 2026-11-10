using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// One model of a set piece: which file, where it stands relative to the piece's centre (x across,
/// z down the slope, y lifted above the ground there), its turn, the height it is scaled to and
/// whether the carrier bumps into it (a box around the model).
/// </summary>
[GlobalClass]
public partial class SetPiecePart : Resource
{
    [Export(PropertyHint.File, "*.glb,*.gltf")] public string Model { get; set; } = "";
    [Export] public Vector3 Offset { get; set; } = Vector3.Zero;
    /// <summary>Turn about the vertical, in degrees.</summary>
    [Export] public float Yaw { get; set; }
    /// <summary>Tilt about the piece's x and z axes, in degrees (a fallen post, a leaning wall).</summary>
    [Export] public Vector2 Tilt { get; set; } = Vector2.Zero;
    /// <summary>Height the model is scaled to, in m.</summary>
    [Export] public float Height { get; set; } = 1.0f;
    [Export] public bool Solid { get; set; } = true;
}
