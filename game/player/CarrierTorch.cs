using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The torch in the carrier's left hand. A torch used from the pack (GameEvents.RequestTorch) is
/// lit: a wooden haft with a pitch-soaked head and a particle fire on it, lighting the way at night,
/// for its burn time; another torch adds its time. It follows the hand but stays upright (or turns
/// with the hand, as the fit says). X puts it away: snuffed, it hangs at the belt and keeps the time
/// it had left; X again lights it (not while packing: there X tidies the pack). In its last seconds the flame shrinks and dims, then it goes out.
/// Where it sits comes from player/torch_fit.tres (debug/TorchFitting.tscn). `-- --torch` lights one
/// at the start (renders).
/// </summary>
public partial class CarrierTorch : Node3D
{
    /// <summary>The driver skeleton: the carrier's model follows it, and so does the torch.</summary>
    [Export] public NodePath DriverPath { get; set; } = "../Model/Rig/Skeleton3D";
    /// <summary>The left hand's bone, by the names the rigs use (Mixamo, Unreal-style, the mannequin's).</summary>
    [Export] public string[] HandBones { get; set; } = { "DEF-hand.L", "LeftHand", "hand_l", "Hand.L" };
    /// <summary>Puts the torch away and takes it out again.</summary>
    [Export] public Key StowKey { get; set; } = Key.X;
    [Export] public float HaftLength { get; set; } = 0.6f;
    [Export] public float HaftRadius { get; set; } = 0.022f;
    [Export] public Color HaftColor { get; set; } = new(0.36f, 0.24f, 0.14f);
    [Export] public Color HeadColor { get; set; } = new(0.12f, 0.09f, 0.07f);
    /// <summary>The flame on top: its size, and the light it throws.</summary>
    [Export] public float FlameSize { get; set; } = 0.32f;
    [Export] public float LightEnergy { get; set; } = 2.4f;
    [Export] public float LightRange { get; set; } = 13.0f;
    /// <summary>The last seconds in which the flame shrinks before it goes out.</summary>
    [Export] public float DyingSeconds { get; set; } = 20.0f;
    [Export] public string LitHint { get; set; } = "The torch catches. It will burn for about {0} minutes. (X puts it away.)";
    [Export] public string OutHint { get; set; } = "Your torch gutters and goes out.";
    [Export] public string StowHint { get; set; } = "You snuff the torch and hang it at your belt. About {0} minutes left in it. (X lights it again.)";
    [Export] public string UnstowHint { get; set; } = "You light the torch again.";

    /// <summary>Seconds of burning left (0: no torch; tests may shorten it). It burns only while not put away.</summary>
    public float Left { get; set; }
    /// <summary>Burning in the hand.</summary>
    public bool Lit => Left > 0.0f && !Stowed;
    /// <summary>Put away at the belt, snuffed, with time left in it.</summary>
    public bool Stowed { get; private set; }
    /// <summary>Another player's torch (co-op): lit and put out by SetRemoteLit only, never by this machine's pack or keys.</summary>
    public bool Remote { get; set; }
    /// <summary>The fitting scene drives the torch itself (no keys, no burning down).</summary>
    public bool Fitting { get; set; }
    public ParticleFire? Fire => _fire;
    /// <summary>The carry pose on the driver's left arm.</summary>
    public TorchArm? Arm { get; private set; }
    /// <summary>Where it sits (loaded from TorchFit.DefaultPath; the fitting scene edits it live).</summary>
    public TorchFit Fit { get; set; } = TorchFit.Load();

    private Node3D? _torch;
    private ParticleFire? _fire;
    private StandardMaterial3D? _head;
    private Skeleton3D? _driver;
    private int _hand = -1;
    private int _stowBone = -1;
    private string _stowBoneName = "";

    public override void _Ready()
    {
        TopLevel = true;
        BuildTorch();
        _driver = GetNodeOrNull<Skeleton3D>(DriverPath);
        _hand = Hand(_driver);
        if (_driver != null)
        {
            _driver.SkeletonUpdated += Follow;
            // The left arm comes up to carry it while it burns.
            Arm = new TorchArm { Name = "TorchArm", Torch = this };
            _driver.CallDeferred(Node.MethodName.AddChild, Arm);
        }
        if (Remote)
        {
            return;
        }
        GameEvents.TorchRequested += OnTorchRequested;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--torch")
            {
                CallDeferred(MethodName.OnTorchRequested, 600.0f);
            }
            if (arg == "--torch-stowed")
            {
                CallDeferred(MethodName.OnTorchRequested, 600.0f);
                CallDeferred(MethodName.Stow);
            }
        }
    }

    public override void _ExitTree()
    {
        GameEvents.TorchRequested -= OnTorchRequested;
        if (_driver != null && IsInstanceValid(_driver))
        {
            _driver.SkeletonUpdated -= Follow;
        }
        if (Lit && !Remote)
        {
            GameEvents.PublishTorch(false);
        }
    }

    /// <summary>Another player's torch burns or is out, as their machine says (co-op).</summary>
    public void SetRemoteLit(bool lit)
    {
        if (lit == Lit)
        {
            return;
        }
        Stowed = false;
        Left = 0.0f;
        if (lit)
        {
            Left = float.MaxValue;
        }
        ShowState();
    }

    private void OnTorchRequested(float seconds)
    {
        bool was = Lit;
        Left += seconds;
        Stowed = false;
        ShowState();
        if (!was)
        {
            GameEvents.PublishTorch(true);
        }
        GameEvents.PublishHint(string.Format(LitHint, Mathf.RoundToInt(Left / 60.0f)));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Remote || Fitting || Left <= 0.0f || GameEvents.IsPacking)
        {
            return;
        }
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.PhysicalKeycode == StowKey && !key.CtrlPressed && !key.AltPressed)
        {
            GetViewport().SetInputAsHandled();
            if (Stowed)
            {
                TakeOut();
            }
            else
            {
                Stow();
            }
        }
    }

    /// <summary>Snuffs the torch and hangs it at the belt; it keeps the time it had left.</summary>
    public void Stow()
    {
        if (Stowed || Left <= 0.0f)
        {
            return;
        }
        Stowed = true;
        ShowState();
        if (!Remote && !Fitting)
        {
            GameEvents.PublishTorch(false);
            GameEvents.PublishHint(string.Format(StowHint, Mathf.Max(1, Mathf.RoundToInt(Left / 60.0f))));
        }
    }

    /// <summary>Takes the put-away torch in hand and lights it again.</summary>
    public void TakeOut()
    {
        if (!Stowed)
        {
            return;
        }
        Stowed = false;
        ShowState();
        if (!Remote && !Fitting)
        {
            GameEvents.PublishTorch(true);
            GameEvents.PublishHint(UnstowHint);
        }
    }

    /// <summary>Fitting scene: a torch with time in it, lit in the hand or put away.</summary>
    public void ShowForFitting(bool stowed)
    {
        Fitting = true;
        if (Left <= 0.0f)
        {
            Left = 600.0f;
        }
        Stowed = stowed;
        ShowState();
    }

    private void ShowState()
    {
        if (_torch != null)
        {
            _torch.Visible = Left > 0.0f;
        }
        _fire?.SetBurning(Lit);
        if (_head != null)
        {
            // Snuffed rags stop glowing.
            _head.EmissionEnabled = Lit;
        }
    }

    public override void _Process(double delta)
    {
        if (!Remote && !Fitting)
        {
            GameEvents.PublishTorchLeft(Left);
        }
        if (!Lit || Remote || Fitting)
        {
            return;
        }
        Left = Mathf.Max(0.0f, Left - (float)delta);
        if (_fire != null)
        {
            _fire.Burn = Mathf.Clamp(Left / Mathf.Max(DyingSeconds * 5.0f, 0.1f), 0.0f, 1.0f);
        }
        if (Left <= 0.0f)
        {
            ShowState();
            GameEvents.PublishTorch(false);
            GameEvents.PublishHint(OutHint);
        }
    }

    /// <summary>Puts the torch in the left hand (upright, or turning with it), or at the belt when it's put away.</summary>
    private void Follow()
    {
        if (Left <= 0.0f || _driver == null)
        {
            return;
        }
        if (_torch != null)
        {
            _torch.Scale = Vector3.One * Mathf.Max(Fit.Scale, 0.05f);
        }
        if (Stowed)
        {
            FollowBelt();
            return;
        }
        if (_hand < 0)
        {
            return;
        }
        // The driver's final pose (after its clips, IK and lean), like the sword in the right hand.
        Transform3D hand = _driver.GlobalTransform * _driver.GetBoneGlobalPose(_hand);
        // The rig's bones may carry a scale (centimetre armatures): the offset is in metres either way.
        Basis handBasis = hand.Basis.Orthonormalized();
        Vector3 grip = hand.Origin + handBasis * Fit.HandPosition;
        if (!Fit.Upright)
        {
            GlobalTransform = new Transform3D(handBasis * TorchFit.Turn(Fit.HandRotation), grip);
            return;
        }
        GlobalTransform = new Transform3D(Facing() * TorchFit.Turn(Fit.HandRotation), grip);
    }

    private void FollowBelt()
    {
        if (_stowBoneName != Fit.StowBone)
        {
            _stowBoneName = Fit.StowBone;
            _stowBone = RigFollower.FindBone(_driver!, Fit.StowBone);
        }
        if (_stowBone < 0)
        {
            return;
        }
        Vector3 bone = _driver!.GlobalTransform * _driver.GetBoneGlobalPose(_stowBone).Origin;
        Basis facing = Facing();
        GlobalTransform = new Transform3D(facing * TorchFit.Turn(Fit.StowRotation), bone + facing * Fit.StowPosition);
    }

    /// <summary>Upright, facing the way the carrier faces: X to his right, Y up, Z behind him.</summary>
    private Basis Facing()
    {
        Node3D? body = GetParentOrNull<Node3D>();
        Vector3 forward = Vector3.Forward;
        if (body != null)
        {
            forward = -body.GlobalBasis.Z;
            forward.Y = 0.0f;
            if (forward.LengthSquared() > 0.0001f)
            {
                forward = forward.Normalized();
            }
            else
            {
                forward = Vector3.Forward;
            }
        }
        Vector3 side = forward.Cross(Vector3.Up).Normalized();
        return new Basis(side, Vector3.Up, -forward);
    }

    private int Hand(Skeleton3D? rig)
    {
        if (rig == null)
        {
            return -1;
        }
        foreach (string name in HandBones)
        {
            int bone = RigFollower.FindBone(rig, name);
            if (bone >= 0)
            {
                return bone;
            }
        }
        return -1;
    }

    private void BuildTorch()
    {
        _torch = new Node3D { Name = "Torch", Visible = false };
        AddChild(_torch);
        // The haft, held near its lower third.
        _torch.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = HaftRadius * 1.1f, BottomRadius = HaftRadius * 0.85f, Height = HaftLength, RadialSegments = 8 },
            Position = Vector3.Up * HaftLength * 0.25f,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = HaftColor, Roughness = 0.9f },
        });
        // The head: pitch-soaked rags bound round the top, glowing where they burn.
        float top = HaftLength * 0.75f;
        _head = new StandardMaterial3D
        {
            AlbedoColor = HeadColor,
            Roughness = 1.0f,
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.35f, 0.08f),
            EmissionEnergyMultiplier = 0.6f,
        };
        _torch.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = HaftRadius * 2.0f, BottomRadius = HaftRadius * 1.6f, Height = 0.12f, RadialSegments = 10 },
            Position = Vector3.Up * (top - 0.04f),
            MaterialOverride = _head,
        });
        _fire = new ParticleFire
        {
            Name = "Fire",
            Size = FlameSize,
            LightEnergy = LightEnergy,
            LightRange = LightRange,
            FlameCount = 40,
            SparkCount = 30,
            SmokeCount = 14,
            Position = Vector3.Up * (top + 0.02f),
        };
        _torch.AddChild(_fire);
        _fire.SetBurning(false);
    }
}
