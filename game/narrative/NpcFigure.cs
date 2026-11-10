using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// A traveller sitting by a station's fire (M11 NPCs): a hooded, cloaked figure built from simple
/// shapes in their persona's colour, facing the fire. Looking at them shows who they are; E talks
/// (GameEvents.RequestTalkTo opens the conversation page with them). The first time the carrier
/// comes near, GameEvents.VisitorMet is raised. They settle onto the ground once the terrain
/// under them has loaded.
/// </summary>
public partial class NpcFigure : Node3D, IInteractable, IDescribed
{
    [Export] public float MeetDistance { get; set; } = 16.0f;
    [Export] public float VoiceHeight { get; set; } = 1.1f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    [Export] public float LabelPixelSize { get; set; } = 0.004f;
    /// <summary>The rig that plays the sitting clips for travellers with their own model (hidden; the model follows it).</summary>
    [Export(PropertyHint.File, "*.glb")] public string RigPath { get; set; } = "res://art/characters/quaternius_ual/UAL_Mannequin.glb";
    [Export] public string IdleClip { get; set; } = "Sitting_Idle";
    [Export] public string TalkClip { get; set; } = "Sitting_Talking";
    /// <summary>How long a modelled traveller keeps talking (gesturing) after E.</summary>
    [Export] public float TalkSeconds { get; set; } = 8.0f;
    /// <summary>The log a modelled traveller sits on, in the figure's space (front is +Z).</summary>
    [Export] public Vector3 SeatPosition { get; set; } = new(0.0f, 0.2f, -0.1f);
    [Export] public float SeatRadius { get; set; } = 0.2f;
    [Export] public float SeatLength { get; set; } = 1.1f;

    public NpcPersona? Persona { get; private set; }
    public int Station { get; private set; }
    public bool Grounded { get; private set; }
    public bool Met { get; private set; }

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.8f;
    public string Title => Persona?.DisplayName ?? "Traveller";
    public string Description => Persona?.Description ?? "";
    public string Details
    {
        get
        {
            string who = Persona?.Title ?? "";
            if (Errand.Length > 0)
            {
                return who + ". Has a job for you: \"" + Errand + "\", at the board";
            }
            return who;
        }
    }

    /// <summary>The title of the job this traveller asks for (ErrandPosted for their station), or empty.</summary>
    public string Errand { get; private set; } = "";
    public string Prompt => "E  Talk";
    public Resource? IconSource => null;

    private Label3D? _label;
    private Vector3 _fire;
    private float _groundTimer;
    private AnimationPlayer? _animations;
    private float _talkTimer;

    public void Setup(NpcPersona persona, int station, Vector3 fire)
    {
        Persona = persona;
        Station = station;
        _fire = fire;
        Name = $"Traveller_{persona.Id}";
    }

    public override void _ExitTree()
    {
        GameEvents.ErrandPosted -= OnErrandPosted;
    }

    private void OnErrandPosted(int station, string giver, string title)
    {
        if (station == Station && giver == Title)
        {
            Errand = title;
        }
    }

    public override void _Ready()
    {
        GameEvents.ErrandPosted += OnErrandPosted;
        AddToGroup(IInteractable.Group);
        if (!BuildModeled())
        {
            BuildCloaked();
        }
        _label = new Label3D
        {
            Text = $"{Title}\n{Persona?.Title ?? ""}",
            PixelSize = LabelPixelSize,
            FontSize = 40,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            OutlineSize = 8,
            Position = Vector3.Up * 1.65f,
            Visible = false,
        };
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_talkTimer > 0.0f)
        {
            _talkTimer -= (float)delta;
            if (_talkTimer <= 0.0f)
            {
                PlayClip(IdleClip);
            }
        }
    }

    /// <summary>
    /// The persona's own model, sitting on a log: a hidden mannequin rig plays the sitting clips and
    /// the model follows it bone for bone (RigFollower), the same way the carrier's character does.
    /// False when the persona has no model or it can't be used.
    /// </summary>
    private bool BuildModeled()
    {
        if (Persona == null || Persona.ModelPath.Length == 0)
        {
            return false;
        }
        if (ResourceLoader.Load(Persona.ModelPath) is not PackedScene modelScene || ResourceLoader.Load(RigPath) is not PackedScene rigScene)
        {
            GD.PushWarning($"NpcFigure: can't load {Persona.ModelPath} or the rig; {Persona.Id} keeps the cloak.");
            return false;
        }
        Node3D? rig = rigScene.Instantiate() as Node3D;
        Node3D? model = modelScene.Instantiate() as Node3D;
        Skeleton3D? driver = FindSkeleton(rig);
        Skeleton3D? body = FindSkeleton(model);
        AnimationPlayer? animations = null;
        if (rig != null)
        {
            foreach (Node node in rig.FindChildren("*", "AnimationPlayer", true, false))
            {
                animations = node as AnimationPlayer;
                break;
            }
        }
        if (rig == null || model == null || driver == null || body == null || animations == null)
        {
            GD.PushWarning($"NpcFigure: {Persona.ModelPath} needs a skeleton (and the rig its animations); {Persona.Id} keeps the cloak.");
            rig?.Free();
            model?.Free();
            return false;
        }
        rig.Name = "Rig";
        AddChild(rig);
        model.Name = "Model";
        model.Scale = Vector3.One * Persona.ModelScale;
        AddChild(model);
        body.AddChild(new RigFollower { Name = "RigFollower", Driver = driver });
        _animations = animations;
        PlayClip(IdleClip);
        StandardMaterial3D wood = new() { AlbedoColor = new Color(0.33f, 0.23f, 0.15f), Roughness = 1.0f };
        MeshInstance3D seat = AddMesh(new CylinderMesh { TopRadius = SeatRadius, BottomRadius = SeatRadius, Height = SeatLength, RadialSegments = 10 }, SeatPosition, wood);
        seat.RotationDegrees = new Vector3(0, 0, 90.0f);
        return true;
    }

    private static Skeleton3D? FindSkeleton(Node? root)
    {
        if (root == null)
        {
            return null;
        }
        foreach (Node node in root.FindChildren("*", "Skeleton3D", true, false))
        {
            return node as Skeleton3D;
        }
        return null;
    }

    /// <summary>Plays a looping clip on the rig (Godot drops the "_Loop" suffix on import; either name works).</summary>
    private void PlayClip(string clip)
    {
        if (_animations == null)
        {
            return;
        }
        string name = clip;
        if (!_animations.HasAnimation(name))
        {
            name = clip + "_Loop";
        }
        if (!_animations.HasAnimation(name))
        {
            return;
        }
        _animations.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        _animations.Play(name, 0.4);
    }

    /// <summary>A hooded, cloaked figure built from simple shapes in the persona's colour.</summary>
    private void BuildCloaked()
    {
        Color cloak = Persona?.CloakColor ?? new Color(0.3f, 0.25f, 0.2f);
        StandardMaterial3D cloth = new() { AlbedoColor = cloak, Roughness = 1.0f };
        StandardMaterial3D skin = new() { AlbedoColor = new Color(0.62f, 0.5f, 0.4f), Roughness = 0.9f };
        StandardMaterial3D wood = new() { AlbedoColor = new Color(0.35f, 0.24f, 0.15f), Roughness = 1.0f };
        // Sitting: a wide cloak low on the ground, shoulders, a face under the hood, a staff.
        AddMesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.5f, Height = 0.85f, RadialSegments = 10 }, new Vector3(0, 0.42f, 0), cloth);
        AddMesh(new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 10, Rings = 6 }, new Vector3(0, 0.98f, 0.04f), skin);
        AddMesh(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.21f, Height = 0.42f, RadialSegments = 10 }, new Vector3(0, 1.07f, -0.02f), cloth);
        MeshInstance3D staff = AddMesh(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 1.7f, RadialSegments = 6 }, new Vector3(0.38f, 0.8f, 0.1f), wood);
        staff.RotationDegrees = new Vector3(0, 0, -12.0f);
    }

    private MeshInstance3D AddMesh(Mesh mesh, Vector3 at, Material material)
    {
        MeshInstance3D instance = new() { Mesh = mesh, Position = at, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(instance);
        return instance;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Grounded)
        {
            _groundTimer -= (float)delta;
            if (_groundTimer <= 0.0f)
            {
                _groundTimer = 0.5f;
                Settle();
            }
        }
        if (!Met && GlobalPosition.DistanceTo(GameEvents.CarrierPosition) < MeetDistance)
        {
            Met = true;
            GameEvents.PublishVisitorMet(Title, Details);
        }
    }

    /// <summary>Drops onto the ground below once it has loaded, and turns to the fire.</summary>
    private void Settle()
    {
        if (!IsInsideTree())
        {
            return;
        }
        Vector3 at = GlobalPosition;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 20.0f, at + Vector3.Down * 40.0f, GroundMask));
        if (hit.Count == 0)
        {
            return;
        }
        GlobalPosition = (Vector3)hit["position"];
        Vector3 look = new(_fire.X, GlobalPosition.Y, _fire.Z);
        if (!look.IsEqualApprox(GlobalPosition))
        {
            // Face the fire: the figure's front is +Z.
            LookAt(look, Vector3.Up, true);
        }
        Grounded = true;
    }

    public void SetFocused(bool focused)
    {
        if (_label != null)
        {
            _label.Visible = focused;
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (Persona == null)
        {
            return false;
        }
        GameEvents.RequestTalkTo(Persona.Speaker(GlobalPosition + Vector3.Up * VoiceHeight));
        if (_animations != null)
        {
            PlayClip(TalkClip);
            _talkTimer = TalkSeconds;
        }
        return true;
    }
}
