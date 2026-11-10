using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// One playable character: a rigged model on a Mixamo-named skeleton (any prefix, e.g. mixamorig1_ or
/// mixamorig:) that follows the carrier's mannequin rig. Optional mesh names say which of the model's
/// own pieces show the hard hat, the hair and the hi-vis vest; when they're empty, worn gear is drawn
/// on the character instead (CarrierWornGear).
/// </summary>
[Tool]
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
    [Export] public CharacterStats? Stats { get; set; }
    /// <summary>
    /// Presets of one model share its clothing fits: the id the model's fitted clothes are saved under
    /// (player/clothing_fit.tres). Empty: the character's own Id.
    /// </summary>
    [Export] public string ModelId { get; set; } = "";
    /// <summary>Clothes this preset starts every run wearing (item ids from inventory/items).</summary>
    [Export] public string[] StartingWear { get; set; } = System.Array.Empty<string>();

    /// <summary>The id clothing fits and per-model clothes are looked up by.</summary>
    public string FitId
    {
        get
        {
            if (ModelId.Length > 0)
            {
                return ModelId;
            }
            return Id;
        }
    }

    /// <summary>Whether the model file is in the project (models kept out of git may be missing).</summary>
    public bool Available => ModelPath.Length > 0 && ResourceLoader.Exists(ModelPath);
}
