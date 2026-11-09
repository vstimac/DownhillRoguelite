using Godot;
namespace DownhillRoguelite.Inventory;

/// <summary>Authored visual contract. Imported skinning is accepted only after bind/rest validation.</summary>
[Tool]
[GlobalClass]
public partial class ClothingVisual : Resource
{
    public enum VisualKind { PrototypeShirt, PrototypeJacket, PrototypeTrousers, PrototypeGloves, PrototypeBackpack, Rigid, Skinned }
    [Export] public VisualKind Kind { get; set; }
    [Export] public PackedScene? Scene { get; set; }
    [Export] public string[] MeshNames { get; set; } = System.Array.Empty<string>();
    [Export] public string[] CharacterIds { get; set; } = System.Array.Empty<string>();
    [Export] public string[] HideBodyMeshes { get; set; } = System.Array.Empty<string>();
    [Export] public string AttachmentBone { get; set; } = "Spine1";
    [Export] public Vector3 Offset { get; set; }
    [Export] public Vector3 RotationDegrees { get; set; }
    [Export] public Vector3 Scale { get; set; } = Vector3.One;
    /// <summary>Rigid Back-slot model: measure torso clearance and preserve the authored root under a pivot.</summary>
    [Export] public bool FitBackpackToBody { get; set; }
    [Export] public float BackpackClearance { get; set; } = 0.015f;
    [Export] public Color Colour { get; set; } = new(0.28f, 0.35f, 0.43f);
    [Export] public Color[] MaterialColours { get; set; } = System.Array.Empty<Color>();
    [Export] public float FitSlack { get; set; } = 0.025f;
    [Export] public bool IsPrototype { get; set; } = true;
}
