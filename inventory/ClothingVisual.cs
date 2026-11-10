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
    [Export] public Color Colour { get; set; } = new(0.28f, 0.35f, 0.43f);
    [Export] public Color[] MaterialColours { get; set; } = System.Array.Empty<Color>();
    [Export] public float FitSlack { get; set; } = 0.025f;
    [Export] public bool IsPrototype { get; set; } = true;
    /// <summary>Keep the model's own textures (the wear, dirt and wet show over them), tinted by Colour.</summary>
    [Export] public bool Textured { get; set; }
    /// <summary>Surfaces with these materials (by resource name) keep them as modelled, e.g. a sleeve's bare hand.</summary>
    [Export] public string[] KeepMaterials { get; set; } = System.Array.Empty<string>();
    /// <summary>The body's skin on these bones isn't drawn while this is worn (so the body can't poke through).</summary>
    [Export] public string[] HideBodyBones { get; set; } = System.Array.Empty<string>();
    /// <summary>How far (m) a skinned garment's rig may rest from the body's and still be worn (bones follow the body's).</summary>
    [Export] public float RestTolerance { get; set; }
    /// <summary>Slots whose clothes this covers and hides while worn (e.g. a jerkin over the shirt): EquipSlot names.</summary>
    [Export] public string[] HidesSlots { get; set; } = System.Array.Empty<string>();
}
