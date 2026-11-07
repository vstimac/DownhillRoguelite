using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The carrier's look: loads the character model (the Mixamo carrier, kept out of git; see
/// art/characters/mixamo/README.md) and makes it follow the mannequin rig that animations, IK and
/// the ragdoll drive (RigFollower). If the model file isn't there, the mannequin stays visible, so
/// a fresh clone still runs. Worn gear shows on him: the helmet when something is worn on the head
/// (his hair otherwise), the vest when something is worn on the torso.
/// </summary>
public partial class CarrierModel : Node3D
{
    [Export(PropertyHint.File, "*.fbx,*.glb,*.tscn")] public string ModelPath { get; set; } = "res://art/characters/mixamo/carrier.fbx";
    /// <summary>The driving skeleton (the mannequin rig).</summary>
    [Export] public Skeleton3D? Driver { get; set; }
    [Export] public string HelmetMesh { get; set; } = "Ch17_Helmet";
    [Export] public string HairMesh { get; set; } = "Ch17_Hair";
    [Export] public string VestMesh { get; set; } = "Ch17_Vest";

    public Node3D? Model { get; private set; }
    public RigFollower? Follower { get; private set; }

    private MeshInstance3D? _helmet;
    private MeshInstance3D? _hair;
    private MeshInstance3D? _vest;

    public override void _Ready()
    {
        if (Driver == null || !ResourceLoader.Exists(ModelPath) || ResourceLoader.Load(ModelPath) is not PackedScene scene)
        {
            GD.Print($"CarrierModel: {ModelPath} not found; the mannequin stays.");
            return;
        }
        Model = scene.Instantiate<Node3D>();
        Model.Name = "Character";
        AddChild(Model);
        Skeleton3D? skeleton = null;
        foreach (Node node in Model.FindChildren("*", "Skeleton3D", true, false))
        {
            skeleton = node as Skeleton3D;
            break;
        }
        if (skeleton == null)
        {
            GD.PushWarning("CarrierModel: the model has no skeleton.");
            return;
        }
        Follower = new RigFollower { Name = "RigFollower", Driver = Driver };
        skeleton.AddChild(Follower);
        _helmet = skeleton.GetNodeOrNull<MeshInstance3D>(HelmetMesh);
        _hair = skeleton.GetNodeOrNull<MeshInstance3D>(HairMesh);
        _vest = skeleton.GetNodeOrNull<MeshInstance3D>(VestMesh);
        GameEvents.GearChanged += OnGear;
        OnGear(GameEvents.CurrentGear);
    }

    public override void _ExitTree()
    {
        GameEvents.GearChanged -= OnGear;
    }

    private void OnGear(GearSnapshot gear)
    {
        bool head = !gear.In(EquipSlot.Head).Empty;
        bool torso = !gear.In(EquipSlot.Torso).Empty;
        if (_helmet != null)
        {
            _helmet.Visible = head;
        }
        if (_hair != null)
        {
            _hair.Visible = !head;
        }
        if (_vest != null)
        {
            _vest.Visible = torso;
        }
    }
}
