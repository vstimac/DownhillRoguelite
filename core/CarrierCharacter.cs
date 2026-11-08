using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// One playable character: a rigged model on a Mixamo-named skeleton (any prefix, e.g. mixamorig1_ or
/// mixamorig:) that follows the carrier's mannequin rig. Optional mesh names say which of the model's
/// own pieces show the hard hat, the hair and the hi-vis vest; when they're empty, worn gear is drawn
/// on the character instead (CarrierWornGear).
/// </summary>
[GlobalClass]
public partial class CarrierCharacter : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>A line or two about the character for the start screen's character page.</summary>
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export(PropertyHint.File, "*.fbx,*.glb,*.gltf,*.tscn")] public string ModelPath { get; set; } = "";
    [Export] public string HelmetMesh { get; set; } = "";
    [Export] public string HairMesh { get; set; } = "";
    [Export] public string VestMesh { get; set; } = "";

    /// <summary>Whether the model file is in the project (models kept out of git may be missing).</summary>
    public bool Available => ModelPath.Length > 0 && ResourceLoader.Exists(ModelPath);
}
