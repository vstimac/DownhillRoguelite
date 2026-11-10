using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The carrier's sword and shield. Z draws the sword from the left hip (and puts it back); drawn, the
/// left mouse button swings (press again during a swing to chain up to three different blows), the
/// right holds the shield up, and the middle button kicks. Everything costs stamina. A swing or kick
/// hits whatever stands in front within reach (night beasts, Tuntun Sahur: IMeleeTarget), or what
/// the blade itself passes through; each blow wounds and shoves them back. The raised shield stops a
/// beast's charge or Tuntun's bat coming from in front (GameEvents.TryBlock), for stamina.
/// While the sword is out the mouse buttons don't lean the load (GameEvents.SwordDrawn).
/// The motions are Mixamo sword-and-shield clips (SwordAnimator) laid over the carrier's own walking
/// (MixamoOverlay): the upper body for swings and blocks, the legs too for a kick.
/// </summary>
public partial class MeleeCombat : Node
{
    public enum Stance
    {
        Sheathed,
        Drawing,
        Ready,
        Attacking,
        Kicking,
        Blocking,
        Sheathing,
        Staggered,
    }

    public static readonly StringName ActionDraw = "sword_draw";
    public static readonly StringName ActionAttack = "sword_attack";
    public static readonly StringName ActionBlock = "sword_block";
    public static readonly StringName ActionKick = "sword_kick";

    [Export] public PlayerController? Body { get; set; }
    [Export] public CarrierStamina? Stamina { get; set; }
    /// <summary>The driving skeleton (the mannequin rig every carrier model follows).</summary>
    [Export] public Skeleton3D? Driver { get; set; }

    [ExportGroup("Keys")]
    [Export] public Key DrawKey { get; set; } = Key.Z;
    [Export] public MouseButton AttackButton { get; set; } = MouseButton.Left;
    [Export] public MouseButton BlockButton { get; set; } = MouseButton.Right;
    [Export] public MouseButton KickButton { get; set; } = MouseButton.Middle;

    [ExportGroup("Sword")]
    [Export(PropertyHint.File, "*.glb")] public string SwordModel { get; set; } = "res://art/items/swords/sword_5.glb";
    /// <summary>Blade thickness counted for hits along the blade, in m.</summary>
    [Export] public float BladeRadius { get; set; } = 0.1f;
    /// <summary>The sheath on the left hip: out from the hips (m) sideways, forward and up, and the blade's tilt back (degrees from straight down).</summary>
    [Export] public Vector3 SheathOffset { get; set; } = new(0.2f, 0.05f, -0.05f);
    [Export] public float SheathTilt { get; set; } = 35.0f;
    /// <summary>Where the grip sits from the wrist toward the middle knuckle (share of the way).</summary>
    [Export] public float GripShare { get; set; } = 0.6f;
    [Export] public Color ShieldWood { get; set; } = new(0.42f, 0.28f, 0.16f);
    [Export] public Color ShieldIron { get; set; } = new(0.3f, 0.3f, 0.32f);
    [Export] public float ShieldRadius { get; set; } = 0.3f;
    /// <summary>The shield's centre stands this far off the back of the forearm, in m.</summary>
    [Export] public float ShieldStandOff { get; set; } = 0.07f;
    /// <summary>Hand-tuned corrections (the sword fitting scene saves them); loaded from SwordFit.DefaultPath when empty.</summary>
    [Export] public SwordFit? Fit { get; set; }

    [ExportGroup("Swings")]
    /// <summary>The blows of a chain, their playback speed, and when in each (share of the clip) the blow lands.</summary>
    [Export] public string[] Combo { get; set; } = { "attack_1", "attack_2", "attack_3" };
    [Export] public float[] ComboSpeed { get; set; } = { 1.25f, 1.35f, 1.3f };
    [Export] public Vector2[] HitWindow { get; set; } = { new(0.32f, 0.62f), new(0.28f, 0.55f), new(0.4f, 0.68f) };
    /// <summary>Health each blow takes and how hard it shoves (m/s).</summary>
    [Export] public float[] Damage { get; set; } = { 22.0f, 26.0f, 38.0f };
    [Export] public float[] Push { get; set; } = { 3.0f, 3.5f, 5.5f };
    /// <summary>A press after this share of a swing chains the next blow (before, it is remembered).</summary>
    [Export] public float ChainFrom { get; set; } = 0.45f;
    /// <summary>A swing ends (back to the guard) at this share of its clip.</summary>
    [Export] public float SwingEnd { get; set; } = 0.9f;
    [Export] public float AttackCost { get; set; } = 12.0f;
    /// <summary>Reach of a blow in front of the carrier (m, past the target's body) and how wide (degrees either side).</summary>
    [Export] public float Reach { get; set; } = 1.4f;
    [Export] public float HitAngle { get; set; } = 65.0f;

    [ExportGroup("Kick")]
    [Export] public float KickSpeed { get; set; } = 1.3f;
    [Export] public Vector2 KickWindow { get; set; } = new(0.12f, 0.4f);
    [Export] public float KickDamage { get; set; } = 8.0f;
    [Export] public float KickPush { get; set; } = 7.5f;
    [Export] public float KickReach { get; set; } = 1.0f;
    [Export] public float KickCost { get; set; } = 15.0f;

    [ExportGroup("Shield")]
    /// <summary>Blows from within this many degrees either side of the carrier's facing are stopped.</summary>
    [Export] public float BlockAngle { get; set; } = 70.0f;
    [Export] public float BlockCost { get; set; } = 15.0f;
    [Export] public float ImpactSpeed { get; set; } = 1.4f;

    [ExportGroup("Draw")]
    [Export] public float DrawSpeed { get; set; } = 1.1f;
    [Export] public float SheatheSpeed { get; set; } = 1.3f;
    /// <summary>The sword changes from hip to hand (drawing) or hand to hip (sheathing) at this share of the clip.</summary>
    [Export] public float DrawSwap { get; set; } = 0.35f;
    [Export] public float SheatheSwap { get; set; } = 0.7f;

    [ExportGroup("Feel")]
    /// <summary>How quickly the sword motions fade in and out over the walking (per s).</summary>
    [Export] public float BlendRate { get; set; } = 6.0f;
    [Export] public float LegBlendRate { get; set; } = 10.0f;
    /// <summary>Cross-fade between sword clips, in s.</summary>
    [Export] public float ClipBlend { get; set; } = 0.15f;
    [Export] public float HitShake { get; set; } = 0.05f;
    [Export] public float BlockShake { get; set; } = 0.08f;

    [ExportGroup("Sounds")]
    [Export] public string[] SwingSounds { get; set; } = { "res://audio/combat/sword_swing_0.wav", "res://audio/combat/sword_swing_1.wav", "res://audio/combat/sword_swing_2.wav" };
    [Export] public string[] HitSounds { get; set; } = { "res://audio/combat/sword_hit_0.wav", "res://audio/combat/sword_hit_1.wav" };
    [Export] public string[] BlockSounds { get; set; } = { "res://audio/combat/sword_block_0.wav", "res://audio/combat/sword_block_1.wav" };
    [Export] public string DrawSound { get; set; } = "res://audio/combat/sword_draw_0.wav";
    [Export] public string SheatheSound { get; set; } = "res://audio/combat/sword_sheathe_0.wav";
    [Export] public string KickSound { get; set; } = "res://audio/combat/sword_kick_0.wav";
    [Export] public float VolumeDb { get; set; } = -2.0f;

    public Stance State { get; private set; } = Stance.Sheathed;
    public bool Drawn => State != Stance.Sheathed;
    /// <summary>Which blow of the chain is playing (0-based).</summary>
    public int ComboIndex { get; private set; }
    /// <summary>Blows landed, kicks landed and blows stopped by the shield (tests, stats).</summary>
    public int Landed { get; private set; }
    public int KicksLanded { get; private set; }
    public int BlocksMade { get; private set; }
    public bool SwordInHand { get; private set; }
    public SwordAnimator? Animator => _animator;
    public MixamoOverlay? Overlay => _overlay;
    public Node3D? Sword => _sword;
    public Node3D? Shield => _shield;
    /// <summary>Blade base and tip in the world, as of this frame.</summary>
    public Vector3 BladeBase { get; private set; }
    public Vector3 BladeTip { get; private set; }

    private SwordAnimator? _animator;
    private MixamoOverlay? _overlay;
    private Node3D? _sword;
    private float _bladeLength = 0.8f;
    /// <summary>
    /// Holders that follow the hand, hips and left forearm. Not attachments under the driver: the
    /// carrier model hides every mesh under the driver (its own body), and these must stay seen.
    /// They take the driver's final pose (after the overlay, IK and lean) on its SkeletonUpdated.
    /// </summary>
    private Node3D? _handMount;
    private Node3D? _hipMount;
    private Node3D? _armMount;
    private readonly List<(Node3D Mount, int Bone)> _mounts = new();
    private System.Func<Vector3, string, bool>? _blocker;
    private Node3D? _shield;
    /// <summary>Where the rig puts them, before the fit's corrections.</summary>
    private Transform3D _inHand = Transform3D.Identity;
    private Transform3D _atHip = Transform3D.Identity;
    private Transform3D _onArm = Transform3D.Identity;
    private bool _chainQueued;
    private bool _blockHeld;
    private readonly HashSet<ulong> _struck = new();
    private Vector3 _previousTip;
    private bool _haveTip;
    private AudioStreamPlayer3D? _voice;
    private readonly RandomNumberGenerator _random = new();
    private int _setupFrames = 3;
    private bool _built;

    public override void _Ready()
    {
        _random.Randomize();
        AddInputs();
        GameEvents.RespawnRequested += OnRespawn;
        GameEvents.LaunchRequested += OnLaunched;
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnRespawn;
        GameEvents.LaunchRequested -= OnLaunched;
        if (_blocker != null && GameEvents.Blocker == _blocker)
        {
            GameEvents.Blocker = null;
        }
        if (Driver != null && _mounts.Count > 0)
        {
            Driver.SkeletonUpdated -= FollowBones;
        }
        if (GameEvents.SwordDrawn)
        {
            GameEvents.PublishSwordDrawn(false);
        }
    }

    /// <summary>The sword's keys, added to the input map unless the project already has them.</summary>
    private void AddInputs()
    {
        if (!InputMap.HasAction(ActionDraw))
        {
            InputMap.AddAction(ActionDraw);
            InputMap.ActionAddEvent(ActionDraw, new InputEventKey { PhysicalKeycode = DrawKey });
        }
        foreach ((StringName action, MouseButton button) in new[] { (ActionAttack, AttackButton), (ActionBlock, BlockButton), (ActionKick, KickButton) })
        {
            if (!InputMap.HasAction(action))
            {
                InputMap.AddAction(action);
                InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
            }
        }
    }

    /// <summary>
    /// Builds the hidden Mixamo rig with the sword clips, the overlay on the driver, and the sword and
    /// shield on their bones. Waits a few frames first, so the carrier model has hidden the driver's
    /// own meshes (and doesn't hide these with them).
    /// </summary>
    private void Build()
    {
        _built = true;
        if (Driver == null)
        {
            GD.PushWarning("MeleeCombat: no driver skeleton.");
            return;
        }
        if (Fit == null && ResourceLoader.Exists(SwordFit.DefaultPath))
        {
            Fit = ResourceLoader.Load<SwordFit>(SwordFit.DefaultPath);
        }
        _animator = new SwordAnimator { Name = "SwordAnimator" };
        AddChild(_animator);
        if (!_animator.Build())
        {
            GD.PushWarning("MeleeCombat: the sword animations could not be loaded.");
        }
        _overlay = new MixamoOverlay { Name = "SwordOverlay", Source = _animator.Skeleton };
        Driver.AddChild(_overlay);
        // Straight after the animation, before the spine lean, foot IK and body dynamics.
        int first = -1;
        foreach (Node child in Driver.GetChildren())
        {
            if (child is SkeletonModifier3D && child != _overlay)
            {
                first = child.GetIndex();
                break;
            }
        }
        if (first >= 0)
        {
            Driver.MoveChild(_overlay, first);
        }
        _overlay.Setup();
        _animator.Play("idle", 0.0f, 1.0f);

        MountSword();
        MountShield();
        if (DisplayServer.GetName() != "headless" && Body != null)
        {
            _voice = new AudioStreamPlayer3D { Name = "SwordVoice", VolumeDb = VolumeDb, UnitSize = 6.0f, Position = Vector3.Up * 1.2f };
            Body.AddChild(_voice);
        }
        _blocker = TryBlock;
        GameEvents.Blocker = _blocker;
        if (_mounts.Count > 0)
        {
            Driver.SkeletonUpdated += FollowBones;
        }
    }

    private Node3D Mount(string name, int bone)
    {
        Node3D mount = new() { Name = name, TopLevel = true };
        AddChild(mount);
        _mounts.Add((mount, bone));
        return mount;
    }

    /// <summary>Moves the holders onto their bones' final pose.</summary>
    private void FollowBones()
    {
        if (Driver == null)
        {
            return;
        }
        Transform3D skeleton = Driver.GlobalTransform;
        foreach ((Node3D mount, int bone) in _mounts)
        {
            mount.GlobalTransform = skeleton * Driver.GetBoneGlobalPose(bone);
        }
    }

    // --- Mounting: where the sword and shield sit, worked out from the rig's own rest pose ---

    private void MountSword()
    {
        Skeleton3D driver = Driver!;
        if (ResourceLoader.Load(SwordModel) is not PackedScene scene)
        {
            GD.PushWarning("MeleeCombat: no sword at " + SwordModel);
            return;
        }
        _sword = scene.Instantiate<Node3D>();
        _sword.Name = "Sword";
        Aabb bounds = ModelFit.Bounds(_sword);
        _bladeLength = Mathf.Max(0.2f, bounds.End.Y);
        float unscale = 1.0f / Mathf.Max(driver.GlobalBasis.Scale.X, 0.0001f);

        int hand = driver.FindBone("DEF-hand.R");
        int index = driver.FindBone("DEF-f_index.01.R");
        int pinky = driver.FindBone("DEF-f_pinky.01.R");
        int middle = driver.FindBone("DEF-f_middle.01.R");
        if (hand >= 0 && index >= 0 && pinky >= 0 && middle >= 0)
        {
            Transform3D handRest = driver.GetBoneGlobalRest(hand);
            Vector3 wrist = handRest.Origin;
            Vector3 knuckles = driver.GetBoneGlobalRest(middle).Origin;
            Vector3 fingers = (knuckles - wrist).Normalized();
            // In a fist the blade runs from the little finger out past the index finger, its edge along the fingers.
            Vector3 blade = driver.GetBoneGlobalRest(index).Origin - driver.GetBoneGlobalRest(pinky).Origin;
            blade = (blade - fingers * blade.Dot(fingers)).Normalized();
            Vector3 edge = (fingers - blade * fingers.Dot(blade)).Normalized();
            Basis grip = new Basis(edge, blade, edge.Cross(blade)).Scaled(Vector3.One * unscale);
            Transform3D held = new(grip, wrist.Lerp(knuckles, GripShare));
            _inHand = handRest.AffineInverse() * held;
            _handMount = Mount("SwordHand", hand);
        }

        int hips = driver.FindBone("DEF-hips");
        Basis? frame = MixamoOverlay.BodyFrame(driver, "DEF-hips", "DEF-head", "DEF-thigh.L", "DEF-thigh.R");
        if (hips >= 0 && frame != null)
        {
            Transform3D hipsRest = driver.GetBoneGlobalRest(hips);
            Vector3 right = frame.Value.X;
            Vector3 up = frame.Value.Y;
            Vector3 forward = Forward(driver, up, frame.Value.Z);
            Vector3 left = -right;
            // Blade down and tilted back, grip up at the front of the left hip.
            float tilt = Mathf.DegToRad(SheathTilt);
            Vector3 blade = (-up * Mathf.Cos(tilt) - forward * Mathf.Sin(tilt)).Normalized();
            Vector3 edge = (forward - blade * forward.Dot(blade)).Normalized();
            Basis grip = new Basis(edge, blade, edge.Cross(blade)).Scaled(Vector3.One * unscale);
            Vector3 at = hipsRest.Origin + (left * SheathOffset.X + forward * SheathOffset.Y + up * SheathOffset.Z) * unscale;
            _atHip = hipsRest.AffineInverse() * new Transform3D(grip, at);
            _hipMount = Mount("SwordHip", hips);
        }
        PlaceSword(false);
    }

    /// <summary>The way the body faces in skeleton space: along the feet (heel to toe).</summary>
    private static Vector3 Forward(Skeleton3D driver, Vector3 up, Vector3 fallback)
    {
        int foot = driver.FindBone("DEF-foot.L");
        int toe = driver.FindBone("DEF-toe.L");
        if (foot < 0 || toe < 0)
        {
            return fallback;
        }
        Vector3 along = driver.GetBoneGlobalRest(toe).Origin - driver.GetBoneGlobalRest(foot).Origin;
        along -= up * along.Dot(up);
        if (along.LengthSquared() < 1e-8f)
        {
            return fallback;
        }
        return along.Normalized();
    }

    private void MountShield()
    {
        Skeleton3D driver = Driver!;
        int forearm = driver.FindBone("DEF-forearm.L");
        int hand = driver.FindBone("DEF-hand.L");
        Basis? frame = MixamoOverlay.BodyFrame(driver, "DEF-hips", "DEF-head", "DEF-thigh.L", "DEF-thigh.R");
        if (forearm < 0 || hand < 0 || frame == null)
        {
            return;
        }
        float unscale = 1.0f / Mathf.Max(driver.GlobalBasis.Scale.X, 0.0001f);
        Transform3D rest = driver.GetBoneGlobalRest(forearm);
        Vector3 along = driver.GetBoneGlobalRest(hand).Origin - rest.Origin;
        Vector3 mid = rest.Origin + along * 0.5f;
        // In the T-pose (palms down) the back of the forearm faces up: the shield's face looks that way.
        Vector3 face = frame.Value.Y;
        Vector3 x = along.Normalized();
        x = (x - face * x.Dot(face)).Normalized();
        Basis disc = new Basis(x, face, x.Cross(face)).Scaled(Vector3.One * unscale);
        Transform3D onArm = rest.AffineInverse() * new Transform3D(disc, mid + face * ShieldStandOff * unscale);
        _armMount = Mount("ShieldArm", forearm);
        _onArm = onArm;
        _shield = new Node3D { Name = "Shield", Transform = ShieldPlace(), Visible = false };
        StandardMaterial3D wood = new() { AlbedoColor = ShieldWood, Roughness = 1.0f };
        StandardMaterial3D iron = new() { AlbedoColor = ShieldIron, Roughness = 0.5f, Metallic = 0.7f };
        _shield.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = ShieldRadius, BottomRadius = ShieldRadius, Height = 0.035f, RadialSegments = 20 }, MaterialOverride = wood });
        _shield.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = ShieldRadius - 0.02f, OuterRadius = ShieldRadius + 0.01f, Rings = 24, RingSegments = 6 },
            MaterialOverride = iron,
        });
        _shield.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.07f, RadialSegments = 10, Rings = 5, IsHemisphere = true },
            Position = Vector3.Up * 0.015f,
            MaterialOverride = iron,
        });
        _armMount.AddChild(_shield);
    }

    /// <summary>Sword out (in the guard) or away at once, without the draw or sheathe motion (fitting, tests).</summary>
    public void SetDrawnNow(bool drawn)
    {
        if (!_built)
        {
            Build();
        }
        _chainQueued = false;
        _blockHeld = false;
        if (drawn)
        {
            State = Stance.Ready;
            PlaceSword(true);
            _animator?.Play("idle", 0.0f, 1.0f);
        }
        else
        {
            State = Stance.Sheathed;
            PlaceSword(false);
        }
        if (_overlay != null)
        {
            _overlay.UpperWeight = 0.0f;
            if (drawn)
            {
                _overlay.UpperWeight = 1.0f;
            }
            _overlay.LegWeight = 0.0f;
        }
        GameEvents.PublishSwordDrawn(drawn);
    }

    /// <summary>Puts the sword and shield where the fit now says (the fitting scene calls it after each change).</summary>
    public void ApplyFit()
    {
        PlaceSword(SwordInHand);
        if (_shield != null)
        {
            _shield.Transform = ShieldPlace();
        }
    }

    private Transform3D ShieldPlace()
    {
        if (Fit == null)
        {
            return _onArm;
        }
        return _onArm * Fit.Shield;
    }

    /// <summary>Moves the sword to the hand or to the hip sheath; the shield shows while the sword is out.</summary>
    private void PlaceSword(bool inHand)
    {
        SwordInHand = inHand;
        if (_sword == null)
        {
            return;
        }
        Node3D? mount = _hipMount;
        Transform3D place = _atHip;
        if (Fit != null)
        {
            place = _atHip * Fit.Hip;
        }
        if (inHand)
        {
            mount = _handMount;
            place = _inHand;
            if (Fit != null)
            {
                place = _inHand * Fit.Hand;
            }
        }
        if (mount == null)
        {
            return;
        }
        if (_sword.GetParent() != mount)
        {
            _sword.GetParent()?.RemoveChild(_sword);
            mount.AddChild(_sword);
        }
        _sword.Transform = place;
        _sword.Visible = true;
        if (_shield != null)
        {
            _shield.Visible = inHand;
        }
    }

    // --- Input and the stances ---

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_built || !CanAct())
        {
            return;
        }
        if (@event.IsActionPressed(ActionDraw))
        {
            ToggleDraw();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!Drawn || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }
        if (@event.IsActionPressed(ActionAttack))
        {
            Attack();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(ActionKick))
        {
            Kick();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool CanAct()
    {
        if (GameEvents.IsPacking || GameEvents.IsDowned || GameEvents.IsScreenOpen || GameEvents.IsMapOpen || GameEvents.IsConversing)
        {
            return false;
        }
        return Body == null || Body.InputEnabled;
    }

    /// <summary>Draws the sword (Sheathed) or puts it away (in the guard).</summary>
    public void ToggleDraw()
    {
        if (State == Stance.Sheathed)
        {
            State = Stance.Drawing;
            _animator?.Play("draw", ClipBlend, DrawSpeed);
            Say(DrawSound);
            GameEvents.PublishSwordDrawn(true);
        }
        else if (State == Stance.Ready || State == Stance.Blocking)
        {
            State = Stance.Sheathing;
            _animator?.Play("sheathe", ClipBlend, SheatheSpeed);
        }
    }

    /// <summary>Starts a swing, or chains the next blow if one is under way.</summary>
    public bool Attack()
    {
        if (State == Stance.Attacking)
        {
            if (ComboIndex + 1 < Combo.Length)
            {
                _chainQueued = true;
            }
            return false;
        }
        if (State != Stance.Ready && State != Stance.Blocking)
        {
            return false;
        }
        return StartSwing(0);
    }

    private bool StartSwing(int index)
    {
        if (Combo.Length == 0 || (Stamina != null && !Stamina.TrySpend(AttackCost)))
        {
            return false;
        }
        ComboIndex = Mathf.Clamp(index, 0, Combo.Length - 1);
        State = Stance.Attacking;
        _chainQueued = false;
        _struck.Clear();
        _haveTip = false;
        _animator?.Play(Combo[ComboIndex], ClipBlend, Pick(ComboSpeed, ComboIndex, 1.0f));
        SayOne(SwingSounds);
        PublishSwing("swing");
        return true;
    }

    public bool Kick()
    {
        if (State != Stance.Ready && State != Stance.Blocking)
        {
            return false;
        }
        if (Stamina != null && !Stamina.TrySpend(KickCost))
        {
            return false;
        }
        State = Stance.Kicking;
        _struck.Clear();
        _animator?.Play("kick", ClipBlend, KickSpeed);
        PublishSwing("kick");
        return true;
    }

    /// <summary>Tells enemies a blow is coming (they may guard or jump back).</summary>
    private void PublishSwing(string kind)
    {
        if (Body == null)
        {
            return;
        }
        Vector3 facing = -Body.GlobalBasis.Z;
        facing.Y = 0.0f;
        if (facing.LengthSquared() < 0.0001f)
        {
            facing = Vector3.Forward;
        }
        GameEvents.PublishMeleeSwung(Body.GlobalPosition, facing.Normalized(), kind);
    }

    /// <summary>Holds the shield up (or lowers it); also for tests and automation.</summary>
    public void SetBlock(bool held)
    {
        _blockHeld = held;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!_built)
        {
            _setupFrames--;
            if (_setupFrames <= 0)
            {
                Build();
            }
            return;
        }
        if (_animator == null || !_animator.Loaded)
        {
            return;
        }
        if (Drawn && CanAct() && Input.MouseMode == Input.MouseModeEnum.Captured && InputMap.HasAction(ActionBlock))
        {
            _blockHeld = Input.IsActionPressed(ActionBlock);
        }
        float progress = _animator.Progress;
        switch (State)
        {
            case Stance.Drawing:
                if (!SwordInHand && progress >= DrawSwap)
                {
                    PlaceSword(true);
                }
                if (progress >= 0.98f)
                {
                    PlaceSword(true);
                    ToGuard();
                }
                break;
            case Stance.Sheathing:
                if (SwordInHand && progress >= SheatheSwap)
                {
                    PlaceSword(false);
                    Say(SheatheSound);
                }
                if (progress >= 0.98f)
                {
                    PlaceSword(false);
                    State = Stance.Sheathed;
                    GameEvents.PublishSwordDrawn(false);
                }
                break;
            case Stance.Ready:
                if (_blockHeld)
                {
                    State = Stance.Blocking;
                    _animator.Play("block", ClipBlend, 1.4f);
                }
                break;
            case Stance.Blocking:
                if (!_blockHeld)
                {
                    ToGuard();
                }
                else if (_animator.Current != "block_idle" && progress >= 0.98f)
                {
                    _animator.Play("block_idle", ClipBlend, 1.0f);
                }
                break;
            case Stance.Attacking:
            {
                Vector2 window = Pick(HitWindow, ComboIndex, new Vector2(0.3f, 0.6f));
                if (progress >= window.X && progress <= window.Y)
                {
                    Strike(Pick(Damage, ComboIndex, 20.0f), Pick(Push, ComboIndex, 3.0f), Reach, false);
                }
                if (_chainQueued && progress >= Mathf.Max(ChainFrom, window.Y))
                {
                    StartSwing(ComboIndex + 1);
                }
                else if (progress >= SwingEnd)
                {
                    ToGuard();
                }
                break;
            }
            case Stance.Kicking:
                if (progress >= KickWindow.X && progress <= KickWindow.Y)
                {
                    Strike(KickDamage, KickPush, KickReach, true);
                }
                if (progress >= SwingEnd)
                {
                    ToGuard();
                }
                break;
            case Stance.Staggered:
                if (progress >= 0.95f)
                {
                    ToGuard();
                }
                break;
        }
        UpdateBlade();
        UpdateWeights(dt);
    }

    private void ToGuard()
    {
        State = Stance.Ready;
        _chainQueued = false;
        _animator?.Play("idle", ClipBlend * 1.5f, 1.0f);
    }

    /// <summary>The sword motions fade over the walking: the upper body while drawn, the legs for a kick; none while down.</summary>
    private void UpdateWeights(float dt)
    {
        if (_overlay == null)
        {
            return;
        }
        float upper = 0.0f;
        float legs = 0.0f;
        if (Drawn && !GameEvents.IsDowned)
        {
            upper = 1.0f;
            if (State == Stance.Kicking)
            {
                legs = 1.0f;
            }
        }
        _overlay.UpperWeight = Mathf.MoveToward(_overlay.UpperWeight, upper, BlendRate * dt);
        _overlay.LegWeight = Mathf.MoveToward(_overlay.LegWeight, legs, LegBlendRate * dt);
    }

    private void UpdateBlade()
    {
        if (_sword == null || !_sword.IsInsideTree())
        {
            return;
        }
        Transform3D at = _sword.GlobalTransform;
        BladeBase = at.Origin;
        BladeTip = at * (Vector3.Up * _bladeLength);
    }

    // --- Hits ---

    /// <summary>
    /// Lands the blow on every target in front within reach, or that the blade (and its path since the
    /// last frame) passes through; each target once per blow.
    /// </summary>
    private void Strike(float damage, float push, float reach, bool kick)
    {
        if (Body == null)
        {
            return;
        }
        Vector3 feet = Body.GlobalPosition;
        Vector3 facing = -Body.GlobalBasis.Z;
        facing.Y = 0.0f;
        if (facing.LengthSquared() < 0.0001f)
        {
            facing = Vector3.Forward;
        }
        facing = facing.Normalized();
        float cosLimit = Mathf.Cos(Mathf.DegToRad(HitAngle));
        foreach (Node node in GetTree().GetNodesInGroup(IMeleeTarget.Group))
        {
            if (node is not IMeleeTarget target || !IsInstanceValid(node) || !target.CanBeHit || _struck.Contains(node.GetInstanceId()))
            {
                continue;
            }
            bool hit = InFront(target, feet, facing, reach, cosLimit);
            if (!hit && !kick)
            {
                hit = BladeTouches(target);
            }
            if (!hit)
            {
                continue;
            }
            _struck.Add(node.GetInstanceId());
            Vector3 away = target.HitBase - feet;
            away.Y = 0.0f;
            if (away.LengthSquared() < 0.0001f)
            {
                away = facing;
            }
            target.TakeHit(damage, away.Normalized() * push, "sword");
            if (kick)
            {
                KicksLanded++;
                Say(KickSound);
            }
            else
            {
                Landed++;
                SayOne(HitSounds);
            }
            GameEvents.RequestCameraShake(HitShake);
        }
        _previousTip = BladeTip;
        _haveTip = true;
    }

    private static bool InFront(IMeleeTarget target, Vector3 feet, Vector3 facing, float reach, float cosLimit)
    {
        Vector3 to = target.HitBase - feet;
        float rise = to.Y;
        to.Y = 0.0f;
        float distance = to.Length();
        if (distance - target.HitRadius > reach || rise > 1.8f || rise + target.HitHeight < -0.5f)
        {
            return false;
        }
        if (distance < 0.05f)
        {
            return true;
        }
        return (to / distance).Dot(facing) >= cosLimit;
    }

    private bool BladeTouches(IMeleeTarget target)
    {
        Vector3 bottom = target.HitBase + Vector3.Up * target.HitRadius;
        Vector3 top = target.HitBase + Vector3.Up * Mathf.Max(target.HitHeight - target.HitRadius, target.HitRadius);
        float limit = target.HitRadius + BladeRadius;
        Vector3[] blade = Geometry3D.GetClosestPointsBetweenSegments(BladeBase, BladeTip, bottom, top);
        if (blade[0].DistanceTo(blade[1]) <= limit)
        {
            return true;
        }
        if (_haveTip)
        {
            Vector3[] sweep = Geometry3D.GetClosestPointsBetweenSegments(_previousTip, BladeTip, bottom, top);
            return sweep[0].DistanceTo(sweep[1]) <= limit;
        }
        return false;
    }

    // --- The shield ---

    /// <summary>
    /// GameEvents.TryBlock: with the shield up, a blow coming from in front is stopped (for stamina);
    /// out of stamina, the guard breaks and it goes through.
    /// </summary>
    public bool TryBlock(Vector3 blowDirection, string source)
    {
        if (State != Stance.Blocking || Body == null || GameEvents.IsDowned)
        {
            return false;
        }
        Vector3 facing = -Body.GlobalBasis.Z;
        facing.Y = 0.0f;
        Vector3 from = -blowDirection;
        from.Y = 0.0f;
        if (facing.LengthSquared() < 0.0001f || from.LengthSquared() < 0.0001f)
        {
            return false;
        }
        if (facing.Normalized().Dot(from.Normalized()) < Mathf.Cos(Mathf.DegToRad(BlockAngle)))
        {
            return false;
        }
        if (Stamina != null && !Stamina.TrySpend(BlockCost))
        {
            return false;
        }
        BlocksMade++;
        _animator?.Play("block_impact", 0.05f, ImpactSpeed);
        SayOne(BlockSounds);
        GameEvents.RequestCameraShake(BlockShake);
        return true;
    }

    /// <summary>Shoved (a beast's blow, a slow bat): the guard flinches.</summary>
    private void OnLaunched(Vector3 velocity, float height, string cause)
    {
        if (State == Stance.Ready || State == Stance.Attacking || State == Stance.Kicking || State == Stance.Blocking)
        {
            State = Stance.Staggered;
            string clip = "impact";
            if (_random.Randf() < 0.5f)
            {
                clip = "impact_2";
            }
            _animator?.Play(clip, 0.05f, ImpactSpeed);
        }
    }

    private void OnRespawn()
    {
        State = Stance.Sheathed;
        _chainQueued = false;
        _blockHeld = false;
        PlaceSword(false);
        _animator?.Play("idle", 0.0f, 1.0f);
        if (_overlay != null)
        {
            _overlay.UpperWeight = 0.0f;
            _overlay.LegWeight = 0.0f;
        }
        GameEvents.PublishSwordDrawn(false);
    }

    // --- Sounds ---

    private void SayOne(string[] sounds)
    {
        if (sounds.Length == 0)
        {
            return;
        }
        Say(sounds[_random.RandiRange(0, sounds.Length - 1)]);
    }

    private void Say(string path)
    {
        if (_voice == null || path.Length == 0 || !ResourceLoader.Exists(path))
        {
            return;
        }
        _voice.Stream = ResourceLoader.Load<AudioStream>(path);
        _voice.PitchScale = _random.RandfRange(0.93f, 1.07f);
        _voice.Play();
    }

    private static float Pick(float[] values, int index, float fallback)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }
        return fallback;
    }

    private static Vector2 Pick(Vector2[] values, int index, Vector2 fallback)
    {
        if (index >= 0 && index < values.Length)
        {
            return values[index];
        }
        return fallback;
    }
}
