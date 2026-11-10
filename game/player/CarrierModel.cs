using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The carrier's look: loads the chosen character (CarrierRoster, picked on the start screen or in
/// Options, saved in GameOptions) and makes it follow the mannequin rig that animations, IK and the
/// ragdoll drive (RigFollower). Changing the character swaps the model on the spot. If no character
/// model is there (the Mixamo carrier is kept out of git), the mannequin stays visible, so a fresh
/// clone still runs. Worn gear shows on the character: the hard hat (hair otherwise) and the hi-vis
/// vest from the model's own pieces where it has them, everything else through CarrierWornGear.
/// </summary>
public partial class CarrierModel : Node3D
{
    [Export(PropertyHint.File, "*.tres")] public string RosterPath { get; set; } = CarrierRoster.DefaultPath;
    /// <summary>Command-line override after --, e.g. --character=vucic (for renders and tests).</summary>
    [Export] public string CharacterArg { get; set; } = "--character=";
    /// <summary>Used when there is no roster: the Mixamo carrier and its pieces.</summary>
    [Export(PropertyHint.File, "*.fbx,*.glb,*.tscn")] public string ModelPath { get; set; } = "res://art/characters/mixamo/carrier.fbx";
    /// <summary>The driving skeleton (the mannequin rig).</summary>
    [Export] public Skeleton3D? Driver { get; set; }
    /// <summary>The mannequin's model node: its step offset and visibility (hidden while packing) are mirrored.</summary>
    [Export] public Node3D? DriverModel { get; set; }
    [Export] public string HelmetMesh { get; set; } = "Ch17_Helmet";
    [Export] public string HairMesh { get; set; } = "Ch17_Hair";
    [Export] public string VestMesh { get; set; } = "Ch17_Vest";
    /// <summary>The items whose look can be the model's own helmet and vest (other clothes: CarrierWornGear).</summary>
    [Export] public string HelmetItem { get; set; } = "hard_hat";
    [Export] public string VestItem { get; set; } = "hi_vis_vest";

    public Node3D? Model { get; private set; }
    public RigFollower? Follower { get; private set; }
    public ClothingVisualController? Clothing { get; private set; }
    /// <summary>The id of the character shown ("" while it's the mannequin).</summary>
    public string CharacterId { get; private set; } = "";
    /// <summary>
    /// Another player's carrier (network/RemoteCarrier): shows RemoteCharacter and the gear it is
    /// given (ShowRemoteGear) instead of following this machine's choice, options and worn gear.
    /// </summary>
    public bool Remote { get; set; }
    public string RemoteCharacter { get; set; } = "";
    private GearSnapshot _remoteGear = GearSnapshot.None;

    private MeshInstance3D? _helmet;
    private MeshInstance3D? _hair;
    private MeshInstance3D? _vest;
    private CarrierWornGear? _worn;
    private Vector3 _home;
    private bool _homeSet;

    public override void _Ready()
    {
        if (Remote)
        {
            Show(CarrierRoster.Load(RosterPath)?.Pick(RemoteCharacter));
            return;
        }
        GameOptions.Load();
        GameEvents.GearChanged += OnGear;
        GameOptions.Changed += OnOptions;
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run != null) { run.CharacterChanged += OnOptions; }
        Show(Chosen());
    }

    public override void _ExitTree()
    {
        if (Remote)
        {
            return;
        }
        GameEvents.GearChanged -= OnGear;
        GameOptions.Changed -= OnOptions;
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run != null) { run.CharacterChanged -= OnOptions; }
    }

    /// <summary>Shows a character by id without saving it as the choice (tests); false if it isn't available.</summary>
    public bool ShowCharacter(string id)
    {
        CarrierCharacter? character = CarrierRoster.Load(RosterPath)?.Pick(id);
        if (character == null || character.Id != id)
        {
            return false;
        }
        Show(character);
        return Model != null;
    }

    /// <summary>A remote carrier switches to another character (their choice changed).</summary>
    public void ShowRemoteCharacter(string id)
    {
        if (!Remote || id == CharacterId)
        {
            return;
        }
        RemoteCharacter = id;
        Show(CarrierRoster.Load(RosterPath)?.Pick(id));
    }

    /// <summary>A remote carrier puts on what its player wears.</summary>
    public void ShowRemoteGear(GearSnapshot gear)
    {
        _remoteGear = gear;
        _worn?.ShowGear(gear);
        Clothing?.ShowGear(gear);
        OnGear(gear);
    }

    /// <summary>The worn-gear layer of the shown character (null with the mannequin).</summary>
    public CarrierWornGear? Worn => _worn;

    private void OnOptions()
    {
        CarrierCharacter? wanted = Chosen();
        if (wanted != null && wanted.Id != CharacterId)
        {
            Show(wanted);
        }
    }

    /// <summary>The character to show: the command line's, else the saved choice, else the first available.</summary>
    private CarrierCharacter? Chosen()
    {
        string id = GameOptions.Character;
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run?.CharacterRunStarted == true) { id = run.Character.Id; }
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(CharacterArg))
            {
                id = arg.Substring(CharacterArg.Length);
            }
        }
        CarrierRoster? roster = CarrierRoster.Load(RosterPath);
        if (roster != null)
        {
            return roster.Pick(id);
        }
        CarrierCharacter fallback = new() { Id = "default", ModelPath = ModelPath, HelmetMesh = HelmetMesh, HairMesh = HairMesh, VestMesh = VestMesh };
        if (fallback.Available)
        {
            return fallback;
        }
        return null;
    }

    private void Show(CarrierCharacter? character)
    {
        Unload();
        if (Driver == null || character == null || ResourceLoader.Load(character.ModelPath) is not PackedScene scene)
        {
            GD.Print("CarrierModel: no character model found; the mannequin stays.");
            UseFallbackOutfit();
            return;
        }
        if (!scene.CanInstantiate()) { UseFallbackOutfit(); return; }
        Node instance = scene.Instantiate();
        if (instance is not Node3D characterModel) { instance.Free(); UseFallbackOutfit(); return; }
        Model = characterModel;
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
            GD.PushWarning($"CarrierModel: {character.ModelPath} has no skeleton.");
            Unload();
            UseFallbackOutfit();
            return;
        }
        bool textured = false;
        foreach (Node node in Model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null) { continue; }
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is BaseMaterial3D material && material.AlbedoTexture != null) { textured = true; }
            }
        }
        if (!textured)
        {
            GD.PushWarning("CarrierModel: no usable albedo textures for " + character.Id + "; showing the default rig.");
            Unload();
            UseFallbackOutfit();
            return;
        }
        CharacterId = character.Id;
        Follower = new RigFollower { Name = "RigFollower", Driver = Driver };
        Follower.UseRigOf(skeleton);
        skeleton.AddChild(Follower);
        _helmet = Piece(skeleton, character.HelmetMesh);
        _hair = Piece(skeleton, character.HairMesh);
        _vest = Piece(skeleton, character.VestMesh);
        // Everything else (and the hard hat and vest when the model has no such pieces) rides the bones.
        string helmetItem = "";
        if (_helmet != null)
        {
            helmetItem = HelmetItem;
        }
        string vestItem = "";
        if (_vest != null)
        {
            vestItem = VestItem;
        }
        _worn = new CarrierWornGear { Name = "WornGear", ListenToGear = !Remote, Skeleton = skeleton, CharacterId = character.FitId, Vest = _vest, HelmetItem = helmetItem, VestItem = vestItem };
        AddChild(_worn);
        Clothing = new ClothingVisualController { Name = "ClothingVisuals", ListenToGear = !Remote, Skeleton = skeleton, CharacterId = character.FitId };
        AddChild(Clothing);
        if (Remote)
        {
            ShowRemoteGear(_remoteGear);
            return;
        }
        OnGear(GameEvents.CurrentGear);
    }

    private void UseFallbackOutfit()
    {
        if (Driver == null) { return; }
        Clothing = new ClothingVisualController { Name = "ClothingVisuals", ListenToGear = !Remote, Skeleton = Driver, CharacterId = "mannequin" };
        AddChild(Clothing);
    }

    private static MeshInstance3D? Piece(Skeleton3D skeleton, string name)
    {
        if (name.Length == 0)
        {
            return null;
        }
        return skeleton.GetNodeOrNull<MeshInstance3D>(name);
    }

    /// <summary>Removes the shown character and brings the mannequin's own meshes back.</summary>
    private void Unload()
    {
        if (Clothing != null)
        {
            RemoveChild(Clothing);
            Clothing.QueueFree();
            Clothing = null;
        }
        _worn?.QueueFree();
        _worn = null;
        if (Model != null)
        {
            RemoveChild(Model);
            Model.QueueFree();
            // The follower had hidden the mannequin; show it until the next character takes over.
            if (Driver != null)
            {
                foreach (Node node in Driver.FindChildren("*", "MeshInstance3D", true, false))
                {
                    if (node is MeshInstance3D mesh)
                    {
                        mesh.Visible = true;
                    }
                }
            }
        }
        Model = null;
        Follower = null;
        _helmet = null;
        _hair = null;
        _vest = null;
        CharacterId = "";
    }

    public override void _Process(double delta)
    {
        if (DriverModel == null || Model == null)
        {
            return;
        }
        if (!_homeSet)
        {
            _home = Position - DriverModel.Position;
            _homeSet = true;
        }
        // The animator nudges the mannequin up and down for steps and hides it while packing: follow both.
        Position = _home + DriverModel.Position;
        Visible = DriverModel.Visible;
    }

    private void OnGear(GearSnapshot gear)
    {
        bool head = gear.In(EquipSlot.Head).ItemId == HelmetItem;
        bool anyHat = !gear.In(EquipSlot.Head).Empty;
        bool torso = gear.In(EquipSlot.Torso).ItemId == VestItem;
        if (_helmet != null)
        {
            _helmet.Visible = head;
        }
        if (_hair != null)
        {
            _hair.Visible = !anyHat;
        }
        if (_vest != null)
        {
            _vest.Visible = torso;
        }
    }
}
