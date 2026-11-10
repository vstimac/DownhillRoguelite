using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The torch in the carrier's left hand. A torch used from the pack (GameEvents.RequestTorch) is
/// lit: a wooden haft with a pitch-soaked head and a particle fire on it, lighting the way at night,
/// for its burn time; another torch adds its time. It follows the hand but stays upright, so it
/// leans only a little as the arm swings. In its last seconds the flame shrinks and dims, then it
/// goes out. `-- --torch` lights one at the start (renders).
/// </summary>
public partial class CarrierTorch : Node3D
{
    /// <summary>The driver skeleton: the carrier's model follows it, and so does the torch.</summary>
    [Export] public NodePath DriverPath { get; set; } = "../Model/Rig/Skeleton3D";
    /// <summary>The left hand's bone, by the names the rigs use (Mixamo, Unreal-style, the mannequin's).</summary>
    [Export] public string[] HandBones { get; set; } = { "DEF-hand.L", "LeftHand", "hand_l", "Hand.L" };
    /// <summary>The grip sits this far from the hand bone, in the hand's frame (m), and the haft leans this far forward (degrees).</summary>
    [Export] public Vector3 GripOffset { get; set; } = new(0.0f, 0.08f, 0.02f);
    [Export] public float Lean { get; set; } = 12.0f;
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
    [Export] public string LitHint { get; set; } = "The torch catches. It will burn for about {0} minutes.";
    [Export] public string OutHint { get; set; } = "Your torch gutters and goes out.";

    /// <summary>Seconds of burning left (0: not lit; tests may shorten it).</summary>
    public float Left { get; set; }
    public bool Lit => Left > 0.0f;
    public ParticleFire? Fire => _fire;

    private Node3D? _torch;
    private ParticleFire? _fire;
    private Skeleton3D? _driver;
    private int _hand = -1;

    public override void _Ready()
    {
        TopLevel = true;
        BuildTorch();
        GameEvents.TorchRequested += OnTorchRequested;
        _driver = GetNodeOrNull<Skeleton3D>(DriverPath);
        _hand = Hand(_driver);
        if (_driver != null)
        {
            _driver.SkeletonUpdated += Follow;
        }
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--torch")
            {
                CallDeferred(MethodName.OnTorchRequested, 600.0f);
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
        if (Lit)
        {
            GameEvents.PublishTorch(false);
        }
    }

    private void OnTorchRequested(float seconds)
    {
        bool was = Lit;
        Left += seconds;
        if (_torch != null)
        {
            _torch.Visible = true;
        }
        _fire?.SetBurning(true);
        if (!was)
        {
            GameEvents.PublishTorch(true);
        }
        GameEvents.PublishHint(string.Format(LitHint, Mathf.RoundToInt(Left / 60.0f)));
    }

    public override void _Process(double delta)
    {
        if (!Lit)
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
            _fire?.SetBurning(false);
            if (_torch != null)
            {
                _torch.Visible = false;
            }
            GameEvents.PublishTorch(false);
            GameEvents.PublishHint(OutHint);
        }
    }

    /// <summary>Puts the torch in the left hand, upright with a little lean the way the carrier faces.</summary>
    private void Follow()
    {
        if (!Lit || _driver == null || _hand < 0)
        {
            return;
        }
        // The driver's final pose (after its clips, IK and lean), like the sword in the right hand.
        Transform3D hand = _driver.GlobalTransform * _driver.GetBoneGlobalPose(_hand);
        // The rig's bones may carry a scale (centimetre armatures): the offset is in metres either way.
        Vector3 grip = hand.Origin + hand.Basis.Orthonormalized() * GripOffset;
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
        }
        Vector3 side = forward.Cross(Vector3.Up);
        if (side.LengthSquared() < 0.0001f)
        {
            side = Vector3.Right;
        }
        Basis upright = new Basis(side.Normalized(), Mathf.DegToRad(Lean));
        GlobalTransform = new Transform3D(upright, grip);
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
        _torch.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = HaftRadius * 2.0f, BottomRadius = HaftRadius * 1.6f, Height = 0.12f, RadialSegments = 10 },
            Position = Vector3.Up * (top - 0.04f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = HeadColor,
                Roughness = 1.0f,
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.35f, 0.08f),
                EmissionEnergyMultiplier = 0.6f,
            },
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
