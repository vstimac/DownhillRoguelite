using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Network;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: walks the carrier through every animated state (idle, walk, jog, sprint, crouch,
/// jumps, a stumble and a fall, rolls, get-ups, the sword, the pack, eating, a heavy
/// pack, slopes) seen from the side, with a co-op partner's copy of him beside him (fed what the
/// game sends), photographs each state and lays every state's photos out as a contact sheet. It
/// also measures what a picture can miss: planted-foot slide, arms held out like a T-pose, the feet
/// floating above or sinking into the ground, and which clip and state he is in (states.csv,
/// summary.txt). Run it windowed: `-- --shots-directory=C:/somewhere [--states=walk,crouch]
/// [--every=6] [--no-partner] [--set=CharacterAnimator.WalkToJog=1.8,2.6]`. `--list` prints the state
/// names. CarrierAnimationTests runs the same states headless (Checking) and checks the numbers.
/// </summary>
public partial class CarrierAnimShots : Node3D
{
    [Export] public int EveryFrames { get; set; } = 6;
    [Export] public int SheetColumns { get; set; } = 6;
    [Export] public int SheetCells { get; set; } = 18;
    [Export] public int CellWidth { get; set; } = 400;
    [Export] public float SendRate { get; set; } = 15.0f;

    private sealed record Segment(string Name, string Note, Func<Task> Run);

    /// <summary>What one state measured (CarrierAnimationTests checks these).</summary>
    public sealed record Result(string Name, int Frames, int TposeFrames, float Slide, int SlideSamples,
        float FootMean, float FootMin, float FootMax, float StepsPerMinute, float AirShare, float PackGap, float PackTilt,
        int ActionFrames, float HipsHeight, float PopMax, Dictionary<string, int> States, float SlideMedian, float DriverSlip, float HipsMin, string PopWhere);

    /// <summary>Tests: set before adding it; it then waits for RunChecks instead of photographing.</summary>
    public bool Checking { get; set; }
    public Dictionary<string, Result> Results { get; } = new();
    public PlayerController Body => _body;
    public CharacterAnimator CharacterAnimator => _animator;
    public string[] StateNames => Segments().Select(s => s.Name).ToArray();

    /// <summary>Tests: sets the scene up and measures the given states (all if empty), no pictures.</summary>
    public async Task RunChecks(params string[] states)
    {
        _withPartner = false;
        await Setup();
        foreach (Segment segment in Segments())
        {
            if (states.Length > 0 && !states.Contains(segment.Name)) { continue; }
            await RunSegment(segment);
        }
        ReleaseAll();
        GameEvents.PublishLoad(LoadSnapshot.Empty);
    }

    private Node3D _world = null!;
    private PlayerController _body = null!;
    private PlayerRagdoll _ragdoll = null!;
    private BalanceController _balance = null!;
    private CharacterAnimator _animator = null!;
    private MeleeCombat? _melee;
    private Dive? _dive;
    private Skeleton3D _driver = null!;
    private Node3D _model = null!;
    private CarrierModel? _look;
    private RemoteCarrier? _partner;
    private Camera3D _camera = null!;
    private Label _label = null!;
    private string _directory = "";
    private bool _withPartner = true;
    private Vector3 _partnerOffset = new(0, 0, 0);
    private readonly StringBuilder _csv = new();
    /// <summary>`--set=Animator.WalkToJog=1.6,6` style tweaks, applied before the run (trying settings).</summary>
    private readonly List<string> _tweaks = new();
    private readonly StringBuilder _summary = new();

    // Per-segment measurements.
    private string _segment = "";
    private List<Image> _shots = new();
    private int _frame;
    private int _tposeFrames;
    private float _cadenceSum;
    private int _cadenceCount;
    private int _airFrames;
    private float _packGapSum;
    private float _packGapMax;
    private int _packGapCount;
    private float _packTilt;
    private float _packTiltSum;
    private float _packTiltMax;
    private int _actionFrames;
    private float _hipsSum;
    private int _hipsCount;
    private float _popMax;
    private string _popWhere = "";
    private float _hipsMin;
    private Vector3[] _lastLocal = Array.Empty<Vector3>();
    private Vector3[] _lastLocal2 = Array.Empty<Vector3>();
    private int _frames;
    private readonly Dictionary<string, int> _clips = new();
    private readonly Dictionary<string, int> _states = new();
    private float _footSlideSum;
    private int _footSlideCount;
    private float _footSlideMax;
    private float _floatMin = float.MaxValue;
    private float _floatMax = float.MinValue;
    private float _floatSum;
    private int _floatCount;
    private Vector3[] _lastFeet = Array.Empty<Vector3>();
    private Vector3[] _lastDriverToes = Array.Empty<Vector3>();
    private int _lastDriverFrame = -10;
    private readonly List<float> _slides = new();
    private readonly List<float> _driverSlips = new();
    private float _lastFootFrame = -1;
    private int[] _feet = Array.Empty<int>();
    private int[] _arms = Array.Empty<int>();
    private int[] _hands = Array.Empty<int>();
    private Skeleton3D? _shown;
    private RigFollower? _follower;

    // Partner pose stream.
    private int[] _indices = Array.Empty<int>();
    private float[] _data = Array.Empty<float>();
    private int _sequence;
    private float _poseTimer;
    private float _stateTimer;

    public override async void _Ready()
    {
        if (Checking) { return; }
        _directory = ProjectSettings.GlobalizePath("res://../../carrier-anim-shots");
        string[] only = Array.Empty<string>();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { _directory = arg["--shots-directory=".Length..]; }
            if (arg.StartsWith("--states=", StringComparison.Ordinal)) { only = arg["--states=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries); }
            if (arg.StartsWith("--every=", StringComparison.Ordinal)) { EveryFrames = Math.Max(1, int.Parse(arg["--every=".Length..], CultureInfo.InvariantCulture)); }
            if (arg == "--no-partner") { _withPartner = false; }
            if (arg.StartsWith("--set=", StringComparison.Ordinal)) { _tweaks.Add(arg["--set=".Length..]); }
        }
        try
        {
            await Setup();
            List<Segment> segments = Segments();
            if (OS.GetCmdlineUserArgs().Contains("--list"))
            {
                GD.Print("CarrierAnimShots states: " + string.Join(", ", segments.Select(s => s.Name)));
            }
            else
            {
                Directory.CreateDirectory(_directory);
                _csv.AppendLine("segment,frame,state,action_clip,action_time,action_weight,sword,ragdoll,speed,foot_height,arm_drop_l,arm_drop_r");
                foreach (Segment segment in segments)
                {
                    if (only.Length > 0 && !only.Contains(segment.Name)) { continue; }
                    await RunSegment(segment);
                }
                File.WriteAllText(Path.Combine(_directory, "states.csv"), _csv.ToString());
                File.WriteAllText(Path.Combine(_directory, "summary.txt"), _summary.ToString());
                                GD.Print("CarrierAnimShots: done, " + _directory);
            }
        }
        catch (Exception error)
        {
            GD.PrintErr("CarrierAnimShots: " + error);
        }
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        GetTree().Quit();
    }

    private async Task Setup()
    {
        _world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(_world);
        _world.GetNode("BlueStep").Free();
        _world.GetNode("OrangeBlock").Free();
        // A bigger floor, and two long ramps (25° and 35°) beside it for slopes.
        Node3D ground = _world.GetNode<Node3D>("Ground");
        ground.Scale = new Vector3(4, 1, 4);
        AddRamp(new Vector3(30, 0, 0), 25.0f);
        AddRamp(new Vector3(48, 0, 0), 35.0f);

        _body = _world.GetNode<PlayerController>("Player");
        _ragdoll = _body.GetNode<PlayerRagdoll>("Ragdoll");
        _balance = _body.GetNode<BalanceController>("Balance");
        _animator = _body.GetNode<CharacterAnimator>("Animator");
        _melee = _body.GetNodeOrNull<MeleeCombat>("MeleeCombat");
        _dive = _body.GetNodeOrNull<Dive>("Dive");
        _driver = _body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
        _model = _body.GetNode<Node3D>("Model");
        _look = _body.GetNodeOrNull<CarrierModel>("CarrierModel");
        _body.Health?.Refill();
        foreach (string tweak in _tweaks) { Apply(tweak); }
        await PhysicsFrames(30);

        _follower = _look?.Follower;
        _shown = _follower?.GetSkeleton();
        if (_follower != null) { _follower.RecordPose = true; }
        Skeleton3D measured = _shown ?? _driver;
        _feet = Bones(measured, new[] { "ball_l", "LeftToeBase", "mixamorig1_LeftToeBase", "DEF-toe.L" }, new[] { "ball_r", "RightToeBase", "mixamorig1_RightToeBase", "DEF-toe.R" },
            new[] { "foot_l", "LeftFoot", "mixamorig1_LeftFoot", "DEF-foot.L" }, new[] { "foot_r", "RightFoot", "mixamorig1_RightFoot", "DEF-foot.R" });
        _arms = Bones(measured, new[] { "upperarm_l", "mixamorig1_LeftArm", "LeftArm", "DEF-upper_arm.L" }, new[] { "upperarm_r", "mixamorig1_RightArm", "RightArm", "DEF-upper_arm.R" });
        _hands = Bones(measured, new[] { "hand_l", "mixamorig1_LeftHand", "LeftHand", "DEF-hand.L" }, new[] { "hand_r", "mixamorig1_RightHand", "RightHand", "DEF-hand.R" });
        GD.Print($"CarrierAnimShots: character '{_look?.CharacterId}', measuring {(_shown != null ? "the shown model" : "the mannequin")}, feet {string.Join(",", _feet)}, arms {string.Join(",", _arms)}");

        if (_withPartner)
        {
            _partner = new RemoteCarrier { Name = "Partner" };
            _partner.UseModelPlacement(_model.Transform, _body.GetNode<Node3D>("CarrierModel").Transform);
            _world.AddChild(_partner);
            _indices = NetPoseCodec.Bind(_driver);
            _data = new float[NetPoseCodec.Size];
            _driver.SkeletonUpdated += SendPose;
        }

        _camera = new Camera3D { Fov = 45.0f };
        _world.AddChild(_camera);
        _camera.MakeCurrent();
        CanvasLayer layer = new();
        AddChild(layer);
        _label = new Label { Position = new Vector2(16, 12) };
        _label.AddThemeFontSizeOverride("font_size", 24);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 8);
        layer.AddChild(_label);
    }

    private void Apply(string tweak)
    {
        string[] parts = tweak.Split('=', 2);
        string[] path = parts[0].Split('.', 2);
        Node? node = _body.GetNodeOrNull(path[0]);
        if (node == null)
        {
            // A class name: the first node of that type under the player (e.g. BackpackHarness).
            node = _body.FindChildren("*", "", true, false).FirstOrDefault(n => n.GetType().Name == path[0]);
        }
        if (node == null || parts.Length < 2) { GD.PrintErr("CarrierAnimShots: bad --set " + tweak); return; }
        Variant current = node.Get(path[1]);
        float[] numbers = parts[1].Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        Variant value = current.VariantType switch
        {
            Variant.Type.Vector2 => new Vector2(numbers[0], numbers[1]),
            Variant.Type.Bool => numbers[0] != 0,
            _ => numbers[0],
        };
        node.Set(path[1], value);
        GD.Print($"CarrierAnimShots: {path[0]}.{path[1]} = {node.Get(path[1])} on {node.GetPath()}");
    }

    private void AddRamp(Vector3 foot, float degrees)
    {
        // A long ramp rising toward +Z... built so its low end meets the floor at `foot`.
        float length = 40.0f;
        float angle = Mathf.DegToRad(degrees);
        StaticBody3D ramp = new() { Name = $"Ramp{degrees:0}" };
        BoxShape3D shape = new() { Size = new Vector3(8, 1, length) };
        ramp.AddChild(new CollisionShape3D { Shape = shape });
        ramp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = shape.Size, Material = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.33f, 0.28f) } } });
        _world.AddChild(ramp);
        // Rotated about X: +Z end goes up. Centre half a length up the slope, top face through `foot`.
        Basis basis = new(Vector3.Right, -angle);
        Vector3 centre = foot + basis * new Vector3(0, -0.5f, length * 0.5f);
        ramp.GlobalTransform = new Transform3D(basis, centre);
    }

    private static int[] Bones(Skeleton3D skeleton, params string[][] candidates)
    {
        List<int> found = new();
        foreach (string[] names in candidates)
        {
            int index = -1;
            foreach (string name in names)
            {
                index = skeleton.FindBone(name);
                if (index >= 0) { break; }
            }
            found.Add(index);
        }
        return found.ToArray();
    }

    // ---------------------------------------------------------------- segments

    private List<Segment> Segments()
    {
        Vector3 forward = Vector3.Forward;
        return new List<Segment>
        {
            new("idle", "standing, no input", async () => { await Hold(2.5f); }),
            new("walk", "stick half pushed", async () => { await Move(forward * 0.35f, 3.0f); }),
            new("run", "full stick (the normal pace)", async () => { await Move(forward, 3.0f); }),
            new("sprint", "full stick + sprint", async () => { Press("player_sprint"); await Move(forward, 3.0f); }),
            new("stop", "running, then letting go", async () => { await Move(forward, 1.6f); await Move(Vector3.Zero, 1.4f); }),
            new("crouch", "hold C standing still (stealth)", async () => { Press("player_brake"); await Hold(2.5f); }),
            new("crouch_walk", "hold C and walk", async () => { Press("player_brake"); await Move(forward, 3.0f); }),
            new("crouch_sneak", "hold C, stick half pushed", async () => { Press("player_brake"); await Move(forward * 0.4f, 3.0f); }),
            new("jump", "standing jump", async () => { await Hold(0.3f); Tap("player_jump"); await Hold(2.0f); }),
            new("run_jump", "running jump", async () => { await Move(forward, 1.0f); Tap("player_jump"); await Move(forward, 1.8f); }),
            new("drop_land", "dropped from 2.5 m", async () => { Teleport(new Vector3(0, 2.5f, 0)); await Hold(2.0f); }),
            new("stumble", "a push while walking", async () => { await Move(forward, 0.8f); _balance.ApplyImpulse(new Vector2(1.6f, 0.0f)); await Move(forward, 2.2f); }),
            new("fall_back", "knocked down onto his back, gets up", async () => { await Hold(0.3f); _ragdoll.GoDown(_body.GlobalBasis.Z * 2.5f, Vector3.Zero); await UntilStanding(9.0f); }),
            new("fall_belly", "knocked down onto his belly, gets up", async () => { await Hold(0.3f); _ragdoll.GoDown(-_body.GlobalBasis.Z * 2.5f, Vector3.Zero); await UntilStanding(9.0f); }),
            new("roll_timed", "falls 6 m and rolls, timed", async () => { await RollFall(false); }),
            new("roll_perfect", "falls 6 m and rolls, perfect", async () => { await RollFall(true); }),
            new("sword_draw", "draws the sword, stands", async () => { await Hold(0.2f); _melee?.ToggleDraw(); await Hold(2.2f); }),
            new("sword_swing", "three swings", async () => { await Drawn(); for (int i = 0; i < 3; i++) { _melee?.Attack(); await Hold(0.7f); } await Hold(0.8f); }),
            new("sword_block", "raises the block and holds it", async () => { await Drawn(); _melee?.SetBlock(true); await Hold(1.8f); _melee?.SetBlock(false); await Hold(0.6f); }),
            new("sword_kick", "kicks", async () => { await Drawn(); _melee?.Kick(); await Hold(1.6f); }),
            new("sword_walk", "walks with the sword drawn", async () => { await Drawn(); await Move(forward, 2.5f); }),
            new("sword_sheathe", "puts the sword away", async () => { await Drawn(); _melee?.ToggleDraw(); await Hold(2.0f); }),
            new("pack_down", "sets the pack down, then shoulders it", async () => { await PackCycle(); }),
            new("eat", "eats (E)", async () => { await Hold(0.2f); Tap("player_eat"); await Hold(2.5f); }),
            new("heavy_run", "45 kg pack, full stick", async () => { HeavyLoad(45.0f); await Move(forward, 3.0f); }),
            new("heavy_idle", "45 kg pack, standing", async () => { HeavyLoad(45.0f); await Hold(2.0f); }),
            new("downhill", "down a 25° slope", async () => { Teleport(new Vector3(30, 0.15f, 30) + Vector3.Up * (30 * Mathf.Tan(Mathf.DegToRad(25))), 0.0f); await Move(Vector3.Forward, 3.0f); }),
            new("uphill", "up a 25° slope", async () => { Teleport(new Vector3(30, 0.1f, 4), Mathf.Pi); await Move(Vector3.Back, 3.0f); }),
            new("downhill_steep", "down a 35° slope", async () => { Teleport(new Vector3(48, 0.15f, 24) + Vector3.Up * (24 * Mathf.Tan(Mathf.DegToRad(35))), 0.0f); await Move(Vector3.Forward, 3.0f); }),
            new("clip_roll", "the Roll clip alone through the action slot", async () => { await Hold(0.2f); _animator.PlayAction("Roll", 0.0f, 1.0f, false); await Hold(1.6f); _animator.StopAction(); await Hold(0.4f); }),
            new("clip_getup", "the GetUp_Back clip alone through the action slot", async () => { await Hold(0.2f); _animator.PlayAction("GetUp_Back", 0.0f, 1.0f, false); await Hold(1.6f); _animator.StopAction(); await Hold(0.4f); }),
            new("stand_on_slope", "standing still on a 25° slope, no input", async () => { Teleport(new Vector3(30, 0.15f, 15) + Vector3.Up * (15 * Mathf.Tan(Mathf.DegToRad(25))), 0.0f); await Hold(3.0f); }),
        };
    }

    private async Task RunSegment(Segment segment)
    {
        await Reset();
        BeginMeasure(segment.Name);
        _label.Text = segment.Name;
        Task run = segment.Run();
        while (!run.IsCompleted)
        {
            await PhysicsFrames(1);
            Step(segment);
        }
        await run;
        ReleaseAll();
        EndMeasure(segment);
    }

    private async Task Reset()
    {
        ReleaseAll();
        _body.DirectionOverride = null;
        _body.InputEnabled = true;
        if (_melee != null && _melee.Drawn) { _melee.SetDrawnNow(false); }
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        if (GameEvents.IsPacking) { GameEvents.PublishPackingMode(false); }
        _balance.ResetBalance();
        Teleport(new Vector3(0, 0.05f, 30), 0.0f);
        _body.Health?.Refill();
        _body.Stamina?.Refill();
        await PhysicsFrames(45);
        int wait = 0;
        while ((_ragdoll.Downed || _ragdoll.Rising) && wait++ < 600) { await PhysicsFrames(1); }
        Teleport(new Vector3(0, 0.05f, 30), 0.0f);
        await PhysicsFrames(30);
    }

    private void Teleport(Vector3 at, float yaw = 0.0f)
    {
        _body.Teleport(at, yaw);
        _partnerOffset = new Vector3(0, 0, 0);
    }

    private static readonly string[] Actions = { "player_sprint", "player_brake", "player_jump", "player_eat", "player_move_forward" };
    private readonly HashSet<string> _pressed = new();
    private void Press(string action) { Input.ActionPress(action); _pressed.Add(action); }
    private async void Tap(string action) { Input.ActionPress(action); await PhysicsFrames(2); Input.ActionRelease(action); }
    private void ReleaseAll()
    {
        foreach (string action in Actions) { Input.ActionRelease(action); }
        _pressed.Clear();
        _body.DirectionOverride = null;
    }

    private async Task Hold(float seconds) { _body.DirectionOverride = Vector3.Zero; await Seconds(seconds); }
    private async Task Move(Vector3 direction, float seconds) { _body.DirectionOverride = direction; await Seconds(seconds); }

    private async Task UntilStanding(float limit)
    {
        _body.DirectionOverride = Vector3.Zero;
        float t = 0;
        int after = 0;
        while (t < limit && after < 50)
        {
            await PhysicsFrames(1);
            t += 1.0f / Engine.PhysicsTicksPerSecond;
            if (!_ragdoll.Downed && !_ragdoll.Rising) { after++; }
        }
    }

    private async Task Drawn()
    {
        _body.DirectionOverride = Vector3.Zero;
        if (_melee == null) { return; }
        if (!_melee.Drawn) { _melee.SetDrawnNow(true); }
        await Seconds(0.6f);
    }

    private async Task RollFall(bool perfect)
    {
        _body.DirectionOverride = Vector3.Forward * 0.6f;
        Teleport(new Vector3(0, 6.0f, 30), 0.0f);
        bool answered = false;
        float t = 0;
        while (t < 4.0f)
        {
            await PhysicsFrames(1);
            t += 1.0f / Engine.PhysicsTicksPerSecond;
            VaultCue cue = GameEvents.CurrentVault;
            if (!answered && _dive != null && cue.Active)
            {
                RollGrade grade = RollSave.Grade(cue);
                if ((perfect && grade == RollGrade.Perfect) || (!perfect && grade == RollGrade.Good))
                {
                    _dive.TryRoll(cue);
                    answered = true;
                }
            }
            if (t > 2.0f) { _body.DirectionOverride = Vector3.Zero; }
        }
        if (!answered) { GD.Print($"CarrierAnimShots: no {(perfect ? "perfect" : "timed")} roll window was seen"); }
    }

    private async Task PackCycle()
    {
        _body.DirectionOverride = Vector3.Zero;
        await Seconds(0.3f);
        GameEvents.PublishPackingMode(true);
        for (float p = 0; p <= 1.0f; p += 1.0f / 40) { GameEvents.PublishPackingProgress(p, true); await PhysicsFrames(1); }
        GameEvents.PublishPackingProgress(1.0f, true);
        await Seconds(1.5f);
        for (float p = 1.0f; p >= 0; p -= 1.0f / 40) { GameEvents.PublishPackingProgress(p, false); await PhysicsFrames(1); }
        GameEvents.PublishPackingProgress(0.0f, false);
        GameEvents.PublishPackingMode(false);
        await Seconds(1.0f);
    }

    private static void HeavyLoad(float mass)
    {
        GameEvents.PublishLoad(new LoadSnapshot($"Test {mass:0} kg", mass, new Vector3(0, 1.2f, 0.25f),
            mass * 1.2f * 1.2f, mass * 1.2f * 1.2f, Array.Empty<float>(), Array.Empty<Vector3>(), Array.Empty<Vector3>()));
    }

    // ---------------------------------------------------------------- per frame

    private void Step(Segment segment)
    {
        _frame++;
        // Camera: beside him (his left, looking across), following.
        Vector3 at = _body.GlobalPosition + Vector3.Up * 0.9f;
        if (_ragdoll.Downed)
        {
            at = _driver.GlobalTransform * _driver.GetBoneGlobalPose(Math.Max(0, _driver.FindBone("DEF-hips"))).Origin;
        }
        Vector3 side = Vector3.Right;
        Vector3 wanted = at + side * 3.6f + Vector3.Up * 0.5f + Vector3.Forward * 0.9f;
        _camera.GlobalPosition = _frame <= 1 ? wanted : _camera.GlobalPosition.Lerp(wanted, 0.2f);
        _camera.LookAt(at + Vector3.Forward * 0.9f, Vector3.Up);

        if (_partner != null)
        {
            _stateTimer += 1.0f / Engine.PhysicsTicksPerSecond;
            if (_stateTimer >= 1.0f / SendRate)
            {
                _stateTimer = 0;
                // Beside him, nearer the camera's far side so both are seen: 1.8 m behind on the camera's axis.
                _partner.Receive(_body.GlobalPosition + Vector3.Forward * 1.8f + Vector3.Left * 0.01f, _model.GlobalRotation.Y, "", 0.0f, 1.0f, false, 0);
            }
        }

        Measure(segment);
        string sword = _melee?.Drawn == true ? $"sword {_melee.State} {_melee.Animator?.Current} {_melee.Overlay?.UpperWeight:0.0}" : "";
        _label.Text = $"{segment.Name}: {segment.Note}\n{_body.State}  {_animator.ActionClip}{(_animator.ActionWeight > 0.01f ? $" {_animator.ActionWeight:0.0}" : "")}  {Phase()}  {sword}  {new Vector2(_body.Velocity.X, _body.Velocity.Z).Length():0.0} m/s";
        if (!Checking && _frame % EveryFrames == 0)
        {
            CaptureAsync(_segment, _frame);
        }
    }

    private async void CaptureAsync(string segment, int frame)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (segment != _segment) { return; }
        Image image = GetViewport().GetTexture().GetImage();
        string folder = Path.Combine(_directory, segment);
        Directory.CreateDirectory(folder);
        image.SaveJpg(Path.Combine(folder, $"f{frame:0000}.jpg"), 0.85f);
        _shots.Add(image);
    }

    private string Phase()
    {
        if (_ragdoll.TurningOver) { return "rolling over"; }
        if (_ragdoll.Downed && _ragdoll.GettingUp) { return "getting up (" + _ragdoll.RiseClip + ")"; }
        if (_ragdoll.Downed) { return "ragdoll"; }
        if (_ragdoll.Rising) { return "rising (" + _ragdoll.RiseClip + ")"; }
        return "";
    }

    private void SendPose()
    {
        if (_partner == null) { return; }
        _poseTimer += (float)GetProcessDeltaTime();
        // As NetPlayerPoses sends it.
        if (_poseTimer < 1.0f / SendRate || !NetPoseCodec.Capture(_driver, _model, _melee, _indices, _data,
            _body.DirectionOverride?.LimitLength(1) ?? Vector3.Zero, _body.Sprinting)) { return; }
        _poseTimer = 0.0f;
        int flags = _ragdoll.Downed ? NetPoseCodec.Falling : 0;
        if (_melee?.Sword?.IsVisibleInTree() == true) { flags |= NetPoseCodec.SwordVisible; }
        if (_melee?.Shield?.IsVisibleInTree() == true) { flags |= NetPoseCodec.ShieldVisible; }
        if (_look?.PackSetDown?.Off == true) { flags |= NetPoseCodec.PackOff; }
        if (WildlifeSpawnerCrouch()) { flags |= NetPoseCodec.Crouching; }
        flags |= NetPoseCodec.LoadFlags(CarriedLoad.Mass);
        _partner.Pose?.Receive(++_sequence, flags, _data);
    }

    private bool WildlifeSpawnerCrouch() => _body.State == LocomotionState.Braking || (_body.State == LocomotionState.Idle && Input.IsActionPressed("player_brake"));

    // ---------------------------------------------------------------- measuring

    private void BeginMeasure(string name)
    {
        _segment = name;
        _shots = new List<Image>();
        _frame = 0;
        _frames = 0;
        _tposeFrames = 0;
        _clips.Clear();
        _states.Clear();
        _footSlideSum = 0; _footSlideCount = 0; _footSlideMax = 0;
        _cadenceSum = 0; _cadenceCount = 0; _airFrames = 0;
        _packGapSum = 0; _packGapMax = 0; _packGapCount = 0; _packTiltSum = 0; _packTiltMax = 0;
        _actionFrames = 0; _hipsSum = 0; _hipsCount = 0; _popMax = 0; _popWhere = ""; _hipsMin = float.MaxValue; _lastLocal = Array.Empty<Vector3>(); _lastLocal2 = Array.Empty<Vector3>();
        _floatMin = float.MaxValue; _floatMax = float.MinValue; _floatSum = 0; _floatCount = 0;
        _lastFeet = Array.Empty<Vector3>();
        _slides.Clear(); _driverSlips.Clear(); _lastDriverToes = Array.Empty<Vector3>();
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0) { return float.NaN; }
        List<float> sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private Vector3[] Pose()
    {
        if (_follower != null && _shown != null && _follower.TargetPose.Length > 0) { return _follower.TargetPose; }
        Vector3[] origins = new Vector3[_driver.GetBoneCount()];
        for (int i = 0; i < origins.Length; i++) { origins[i] = _driver.GetBoneGlobalPose(i).Origin; }
        return origins;
    }

    private void Measure(Segment segment)
    {
        _frames++;
        Skeleton3D skeleton = _shown ?? _driver;
        Vector3[] pose = Pose();
        Transform3D toWorld = skeleton.GlobalTransform;
        string state = _body.State.ToString();
        _states[state] = _states.GetValueOrDefault(state) + 1;
        string clip = _animator.ActionWeight > 0.3f ? _animator.ActionClip.ToString() : "-";
        if (_ragdoll.Downed) { clip = "ragdoll"; }
        if (_melee?.Drawn == true) { clip += "+sword"; }
        _clips[clip] = _clips.GetValueOrDefault(clip) + 1;

        // Arms: how far below horizontal each upper arm points (T-pose ~0°, hanging ~80°).
        float[] drop = new float[2];
        for (int s = 0; s < 2 && _arms.Length == 2 && _hands.Length == 2; s++)
        {
            if (_arms[s] < 0 || _hands[s] < 0 || _hands[s] >= pose.Length) { continue; }
            Vector3 arm = toWorld.Basis * (pose[_hands[s]] - pose[_arms[s]]);
            drop[s] = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(-arm.Normalized().Y, -1, 1)));
        }
        // A T-pose frame: the driving rig left in its rest (T) pose, every bone within a few degrees of it
        // (a clip missing or not evaluated). Arms held out in a roll don't count: the rest of him isn't at rest.
        float restAngle = 0.0f;
        int bones = _driver.GetBoneCount();
        for (int i = 0; i < bones; i++)
        {
            Quaternion now = _driver.GetBoneGlobalPose(i).Basis.Orthonormalized().GetRotationQuaternion();
            Quaternion rest = _driver.GetBoneGlobalRest(i).Basis.Orthonormalized().GetRotationQuaternion();
            restAngle += now.AngleTo(rest);
        }
        bool tpose = bones > 0 && Mathf.RadToDeg(restAngle / bones) < 4.0f;
        if (tpose) { _tposeFrames++; }

        // Feet: the lowest foot point above the ground under him (floating > 0, sinking < 0), and
        // the horizontal speed of a planted foot.
        float footHeight = float.NaN;
        bool grounded = _body.IsOnFloor() && !_ragdoll.Downed && _animator.ActionWeight < 0.3f;
        if (_feet.Length == 4 && _feet.All(f => f >= 0 && f < pose.Length))
        {
            Vector3[] world = _feet.Select(f => toWorld * pose[f]).ToArray();
            // Each foot point against the ground right under it (slopes), the lowest one counts.
            float groundY = GroundAt(_body.GlobalPosition);
            footHeight = world.Min(w => w.Y - GroundAt(w));
            if (grounded && _animator.Cadence > 0) { _cadenceSum += _animator.Cadence; _cadenceCount++; if (footHeight > 0.05f) { _airFrames++; } }
            if (grounded && float.IsFinite(groundY))
            {
                _floatSum += footHeight; _floatCount++;
                _floatMin = Math.Min(_floatMin, footHeight); _floatMax = Math.Max(_floatMax, footHeight);
            }
            if (grounded && _lastFeet.Length == 4 && _lastFootFrame == _frame - 1)
            {
                float dt = 1.0f / Engine.PhysicsTicksPerSecond;
                for (int s = 0; s < 2; s++)
                {
                    // A foot is planted while its toe and heel are both near the ground.
                    Vector3 toe = world[s], heel = world[s + 2];
                    float toeH = toe.Y - GroundAt(toe), heelH = heel.Y - GroundAt(heel);
                    if (toeH < 0.06f && heelH < 0.14f)
                    {
                        Vector3 moved = toe - _lastFeet[s];
                        float slide = new Vector2(moved.X, moved.Z).Length() / dt;
                        _footSlideSum += slide; _footSlideCount++; _slides.Add(slide);
                        _footSlideMax = Math.Max(_footSlideMax, slide);
                    }
                }
            }
            _lastFeet = world;
            _lastFootFrame = _frame;
        }
        // The same as LocomotionTests' foot slip: the driving rig's toes within 4 cm of his feet's level.
        int[] driverToes = { _driver.FindBone("DEF-toe.L"), _driver.FindBone("DEF-toe.R") };
        if (driverToes[0] >= 0 && driverToes[1] >= 0)
        {
            Vector3[] toes = driverToes.Select(t => _driver.GlobalTransform * _driver.GetBoneGlobalPose(t).Origin).ToArray();
            if (grounded && _lastDriverToes.Length == 2 && _lastDriverFrame == _frame - 1)
            {
                float dt = 1.0f / Engine.PhysicsTicksPerSecond;
                for (int s = 0; s < 2; s++)
                {
                    if (toes[s].Y - _body.GlobalPosition.Y < 0.04f)
                    {
                        Vector3 moved = toes[s] - _lastDriverToes[s];
                        _driverSlips.Add(new Vector2(moved.X, moved.Z).Length() / dt);
                    }
                }
            }
            _lastDriverToes = toes;
            _lastDriverFrame = _frame;
        }
        if (_animator.ActionWeight > 0.3f) { _actionFrames++; }
        // Hips above the ground under him (crouching drops them), and the biggest one-frame jump of any
        // bone relative to the hips (a pop between clips), both on the driving rig, off the ragdoll.
        int hipsBone = _driver.FindBone("DEF-hips");
        if (hipsBone >= 0 && !_ragdoll.Downed && !_ragdoll.Rising)
        {
            float groundUnder = GroundAt(_body.GlobalPosition);
            Vector3 hipsWorld = _driver.GlobalTransform * _driver.GetBoneGlobalPose(hipsBone).Origin;
            if (float.IsFinite(groundUnder) && _body.IsOnFloor()) { _hipsSum += hipsWorld.Y - groundUnder; _hipsCount++; _hipsMin = Math.Min(_hipsMin, hipsWorld.Y - groundUnder); }
            Transform3D hipsPose = _driver.GetBoneGlobalPose(hipsBone);
            Vector3[] local = new Vector3[_driver.GetBoneCount()];
            for (int i = 0; i < local.Length; i++) { local[i] = hipsPose.AffineInverse() * _driver.GetBoneGlobalPose(i).Origin; }
            // A pop is a bone changing its speed at once: the second difference of its place,
            // so a fast swinging foot doesn't count, a pose that jumps does.
            if (_lastLocal.Length == local.Length && _lastLocal2.Length == local.Length)
            {
                for (int i = 0; i < local.Length; i++)
                {
                    float jump = (local[i] - 2.0f * _lastLocal[i] + _lastLocal2[i]).Length();
                    if (jump > _popMax) { _popMax = jump; _popWhere = $"{_driver.GetBoneName(i)} frame {_frames}"; }
                }
            }
            _lastLocal2 = _lastLocal;
            _lastLocal = local;
        }
        else
        {
            _lastLocal = Array.Empty<Vector3>(); _lastLocal2 = Array.Empty<Vector3>();
        }
        float packGap = PackGap();
        if (float.IsFinite(packGap) && !_ragdoll.Downed && !GameEvents.IsPacking && _look?.PackSetDown?.Off != true)
        {
            _packGapSum += packGap; _packGapCount++; _packGapMax = Math.Max(_packGapMax, packGap);
            _packTiltSum += _packTilt; _packTiltMax = Math.Max(_packTiltMax, _packTilt);
        }
        _csv.AppendLine(string.Join(",", segment.Name, _frame, state, _animator.ActionClip, _animator.ActionTime.ToString("0.00", CultureInfo.InvariantCulture), _animator.ActionWeight.ToString("0.00", CultureInfo.InvariantCulture),
            _melee?.Drawn == true ? $"{_melee.State}/{_melee.Animator?.Current}" : "", Phase().Replace(',', ' '),
            new Vector2(_body.Velocity.X, _body.Velocity.Z).Length().ToString("0.00", CultureInfo.InvariantCulture),
            footHeight.ToString("0.000", CultureInfo.InvariantCulture), drop[0].ToString("0", CultureInfo.InvariantCulture), drop[1].ToString("0", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// How far the worn pack's middle is from his back: from the pack's centre to the line from his
    /// hips to his neck (m). Standing it is about the pack's half depth; much more and it floats.
    /// </summary>
    private float PackGap()
    {
        BackpackAttachment? pack = _look?.Clothing?.BackpackMount;
        if (pack == null || !pack.IsVisibleInTree() || pack.GetParent() is not Skeleton3D rig) { return float.NaN; }
        int hips = Bones(rig, new[] { "pelvis", "Hips", "mixamorig1_Hips", "DEF-hips" })[0];
        int neck = Bones(rig, new[] { "neck_01", "Neck", "mixamorig1_Neck", "DEF-neck" })[0];
        if (hips < 0 || neck < 0) { return float.NaN; }
        Vector3 a = rig.GlobalTransform * rig.GetBoneGlobalPose(hips).Origin;
        Vector3 b = rig.GlobalTransform * rig.GetBoneGlobalPose(neck).Origin;
        if (_follower != null && rig == _shown && _follower.TargetPose.Length > Math.Max(hips, neck))
        {
            a = rig.GlobalTransform * _follower.TargetPose[hips];
            b = rig.GlobalTransform * _follower.TargetPose[neck];
        }
        Vector3 centre = pack.Pivot.GlobalTransform * pack.Pivot.Transform.AffineInverse() * pack.PackBounds().GetCenter();
        if (pack.Simulated) { centre = pack.Pivot.GlobalTransform * pack.PackBounds().GetCenter(); }
        else { centre = pack.Pivot.GlobalTransform * pack.PackBounds().GetCenter(); }
        Vector3 ab = b - a;
        float t = Mathf.Clamp((centre - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 0.0001f), 0, 1);
        _packTilt = Mathf.RadToDeg(pack.Pivot.GlobalBasis.Y.Normalized().AngleTo(ab.Normalized()));
        return centre.DistanceTo(a + ab * t);
    }

    private float GroundAt(Vector3 at)
    {
        PhysicsDirectSpaceState3D space = _body.GetWorld3D().DirectSpaceState;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.5f, at + Vector3.Down * 3.0f, 1);
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0) { return float.NaN; }
        return ((Vector3)hit["position"]).Y;
    }

    private void EndMeasure(Segment segment)
    {
        float slide = _footSlideCount > 0 ? _footSlideSum / _footSlideCount : 0;
        string feet = _floatCount > 0 ? $"feet above ground mean {_floatSum / _floatCount:0.000} m (min {_floatMin:0.000}, max {_floatMax:0.000})" : "feet: not grounded";
        string line = $"{segment.Name,-15} frames {_frames,4}  states [{string.Join(" ", _states.Select(p => $"{p.Key}:{p.Value}"))}]  clips [{string.Join(" ", _clips.Select(p => $"{p.Key}:{p.Value}"))}]  "
            + (_cadenceCount > 0 ? $"steps/min {_cadenceSum / _cadenceCount * 120:0}  both feet off the ground {100.0f * _airFrames / _cadenceCount:0}%  " : "")
            + (_packGapCount > 0 ?  $"pack off the back mean {_packGapSum / _packGapCount:0.00} m max {_packGapMax:0.00}, turned from the back {_packTiltSum / _packGapCount:0}° max {_packTiltMax:0}°  " : "")
            + $"T-pose frames {_tposeFrames}  planted-foot slide mean {slide:0.00} m/s max {_footSlideMax:0.00} ({_footSlideCount} samples)  {feet}";
        line += $"  slide median {Median(_slides):0.00} m/s  driving-rig toe slip median {Median(_driverSlips):0.00} m/s ({_driverSlips.Count})";
        line += $"  hips {(_hipsCount > 0 ? _hipsSum / _hipsCount : float.NaN):0.00} m  biggest one-frame bone jerk {_popMax:0.000} m ({_popWhere})  lowest hips {_hipsMin:0.00} m";
        _summary.AppendLine(line);
        Results[segment.Name] = new Result(segment.Name, _frames, _tposeFrames, slide, _footSlideCount,
            _floatCount > 0 ? _floatSum / _floatCount : float.NaN, _floatMin, _floatMax,
            _cadenceCount > 0 ? _cadenceSum / _cadenceCount * 120 : 0, _cadenceCount > 0 ? (float)_airFrames / _cadenceCount : 0,
            _packGapCount > 0 ? _packGapSum / _packGapCount : float.NaN, _packGapCount > 0 ? _packTiltSum / _packGapCount : float.NaN,
            _actionFrames, _hipsCount > 0 ? _hipsSum / _hipsCount : float.NaN, _popMax, new Dictionary<string, int>(_states),
            Median(_slides), Median(_driverSlips), _hipsCount > 0 ? _hipsMin : float.NaN, _popWhere);
        if (!Checking) { GD.Print(line); }
        if (!Checking) { SaveSheet(_shots, Path.Combine(_directory, $"sheet-{segment.Name}.jpg"), segment.Name + ": " + segment.Note); }
    }

    private void SaveSheet(List<Image> shots, string path, string title)
    {
        if (shots.Count == 0) { return; }
        int cells = Math.Min(SheetCells, shots.Count);
        int columns = Math.Min(SheetColumns, cells);
        int rows = (cells + columns - 1) / columns;
        int width = CellWidth;
        int height = width * shots[0].GetHeight() / Math.Max(shots[0].GetWidth(), 1);
        Image sheet = Image.CreateEmpty(columns * width, rows * height, false, Image.Format.Rgb8);
        for (int i = 0; i < cells; i++)
        {
            int pick = cells == 1 ? 0 : i * (shots.Count - 1) / (cells - 1);
            Image cell = (Image)shots[pick].Duplicate();
            cell.Convert(Image.Format.Rgb8);
            cell.Resize(width, height, Image.Interpolation.Bilinear);
            sheet.BlitRect(cell, new Rect2I(0, 0, width, height), new Vector2I(i % columns * width, i / columns * height));
        }
        sheet.SaveJpg(path, 0.88f);
    }

    private async Task Seconds(float seconds)
    {
        int frames = Mathf.RoundToInt(seconds * Engine.PhysicsTicksPerSecond);
        await PhysicsFrames(frames);
    }

    private async Task PhysicsFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
