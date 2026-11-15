using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment.Fog;

/// <summary>
/// Ground fog around the carrier: owns the simulation (GroundFogSimulation), learns the ground
/// under it with physics (PhysicsFogGround, a budget of cells per physics frame, nearest first,
/// cells whose chunk hasn't streamed in tried again later), keeps the patch centred on the carrier
/// by whole-cell shifts, steps it at a fixed rate and has GroundFogRenderer draw it, blended between
/// steps. It lies only among dense trees and low rocks (whatever is in the FogCover.Group says where
/// those are). The bodies in FogCover.BodyGroup (the carriers' skeletons) part it bone by bone, so it
/// moves away from the character's model and from nothing else, and each footfall
/// (GameEvents.FootPlacementUpdated) stirs it. Colour follows the time of day.
///
/// Without a carrier it follows <see cref="Target"/> (the lab's walker). The console's /fog turns it
/// on and off, picks a debug view, changes any setting live and prints timings. Nothing else reads
/// it; it reads only events, so it can be dropped into any scene with terrain.
/// </summary>
public partial class GroundFog : Node3D
{
    public const string DefaultSettingsPath = "res://environment/fog/default_ground_fog.tres";

    public enum View { Off, Density, Flow, Ground }

    [Export] public GroundFogSettings? Settings { get; set; }
    [Export] public bool FogEnabled { get; set; } = true;
    /// <summary>Followed when no carrier publishes its movement (a lab); the carrier wins when it does.</summary>
    [Export] public Node3D? Target { get; set; }
    [Export] public View DebugView { get; set; } = View.Off;
    /// <summary>Draws the fog in headless runs too (tests of the renderer); off, only the simulation runs there.</summary>
    [Export] public bool DrawWhenHeadless { get; set; }
    /// <summary>The Low quality preset turns the fog off (it costs about 1.5 ms a frame on a laptop RTX 3060 at 1080p).</summary>
    [Export] public bool OffOnLowQuality { get; set; } = true;

    public GroundFogSimulation? Simulation { get; private set; }
    public GroundFogRenderer? Renderer { get; private set; }
    public PhysicsFogGround? Sampler { get; private set; }
    public int Sampled { get; private set; }
    public int Missed { get; private set; }
    public double LastSampleMs { get; private set; }
    public double LastFrameMs { get; private set; }
    public int Footfalls { get; private set; }
    /// <summary>Parts of bodies in the fog at the last step, and how many were looked at.</summary>
    public int PartsInFog { get; private set; }
    public int PartsSeen => _points.Count;
    /// <summary>Share of the patch where fog may lie (0-1).</summary>
    public float CoverShare { get; private set; }
    public Vector3 Feet => _feet;

    private readonly Queue<Vector2I> _queue = new();
    private readonly List<Vector2I> _fresh = new();
    private double _accumulator;
    private double _sinceBodies;
    private bool _haveFeet;
    private bool _fromCarrier;
    private Vector3 _feet;
    private Vector3 _velocity;
    private Vector3 _lastTarget;
    private bool _leftPlanted;
    private bool _rightPlanted;
    private double _retryAt;
    private WorldClock? _clock;
    private GroundFogDebugView? _debug;
    private readonly System.Diagnostics.Stopwatch _watch = new();
    private readonly List<Vector2> _coverTrees = new();
    private readonly List<Vector3> _coverRocks = new();
    private int _coverVersion = -1;
    private double _coverAt;
    private readonly List<Vector3> _points = new();
    private readonly List<Vector3> _pointVelocities = new();
    private readonly Dictionary<ulong, BodyTrack> _bodies = new();
    private ulong _bodyPass;

    /// <summary>One body's bones that part the fog, and where each was a frame ago.</summary>
    private sealed class BodyTrack
    {
        public int[] Bones = Array.Empty<int>();
        public int[] Children = Array.Empty<int>();
        public Vector3[] Last = Array.Empty<Vector3>();
        public bool Moved;
        public ulong Pass;
    }

    public override void _Ready()
    {
        Settings ??= GD.Load<GroundFogSettings>(DefaultSettingsPath) ?? new GroundFogSettings();
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Build();
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.FootPlacementUpdated += OnFeet;
        GameEvents.NewMountain += OnNewMountain;
        GameOptions.Changed += OnOptions;
        OnOptions();
        RegisterCommands();
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.FootPlacementUpdated -= OnFeet;
        GameEvents.NewMountain -= OnNewMountain;
        GameOptions.Changed -= OnOptions;
    }

    private void OnOptions()
    {
        bool wanted = GameOptions.GroundFog;
        if (OffOnLowQuality && GameOptions.Quality == GraphicsQuality.Low)
        {
            wanted = false;
        }
        SetFogEnabled(wanted);
    }

    /// <summary>(Re)makes the simulation, sampler and renderer from the settings (after the grid's size changes).</summary>
    public void Build()
    {
        GroundFogSettings s = Settings!;
        Simulation = new GroundFogSimulation(s);
        Sampler = new PhysicsFogGround(s);
        _queue.Clear();
        _accumulator = 0.0;
        if (_haveFeet)
        {
            Simulation.CentreOn(_feet.X, _feet.Z);
            LayCover();
            QueueUnknown();
        }
        Renderer?.QueueFree();
        Renderer = null;
        if (DisplayServer.GetName() != "headless" || DrawWhenHeadless)
        {
            Renderer = new GroundFogRenderer { Name = "Renderer" };
            AddChild(Renderer);
            Renderer.Shown = FogEnabled;
            Renderer.Setup(Simulation);
        }
        _debug?.QueueFree();
        _debug = new GroundFogDebugView { Name = "DebugView", Fog = this };
        AddChild(_debug);
    }

    /// <summary>Forgets the ground (a new mountain): every cell is looked for again.</summary>
    public void Relearn()
    {
        if (Simulation == null)
        {
            return;
        }
        Simulation.Reset(Simulation.OriginX, Simulation.OriginZ);
        LayCover();
        _queue.Clear();
        QueueUnknown();
    }

    private void OnNewMountain(ulong seed)
    {
        _haveFeet = false;
        _fromCarrier = false;
        Simulation?.Reset(0, 0);
        _queue.Clear();
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _fromCarrier = true;
        Follow(snapshot.Position, snapshot.Velocity);
    }

    private void OnFeet(FootPlacementSnapshot snapshot)
    {
        if (snapshot.Left.Planted && !_leftPlanted && snapshot.Left.HasGround)
        {
            Footfall(snapshot.Left.GroundPoint, true);
        }
        if (snapshot.Right.Planted && !_rightPlanted && snapshot.Right.HasGround)
        {
            Footfall(snapshot.Right.GroundPoint, false);
        }
        _leftPlanted = snapshot.Left.Planted;
        _rightPlanted = snapshot.Right.Planted;
    }

    /// <summary>A foot comes down here: it stirs the fog if it's in it.</summary>
    public void Footfall(Vector3 at, bool left)
    {
        if (Simulation == null || !FogEnabled || !Simulation.CellAt(at.X, at.Z, out int x, out int z))
        {
            return;
        }
        int i = Simulation.Index(x, z);
        if (Simulation.Ground[i] == GroundFogSimulation.Known && at.Y - Simulation.Height[i] > Settings!.StirHeight)
        {
            return;
        }
        Simulation.Footfall(at, left);
        Footfalls++;
    }

    /// <summary>Where the carrier (or the lab's walker) is and how it moves.</summary>
    public void Follow(Vector3 feet, Vector3 velocity)
    {
        _feet = feet;
        _velocity = velocity;
        if (Simulation == null)
        {
            return;
        }
        if (!_haveFeet)
        {
            _haveFeet = true;
            Simulation.CentreOn(feet.X, feet.Z);
            LayCover();
            _queue.Clear();
            QueueUnknown();
            return;
        }
        Vector2 centre = Simulation.Centre;
        int dx = Mathf.RoundToInt((feet.X - centre.X) / Simulation.Cell);
        int dz = Mathf.RoundToInt((feet.Z - centre.Y) / Simulation.Cell);
        int far = Math.Max(1, Settings!.ShiftCells);
        if (Math.Abs(dx) < far && Math.Abs(dz) < far)
        {
            return;
        }
        if (Math.Abs(dx) < far)
        {
            dx = 0;
        }
        if (Math.Abs(dz) < far)
        {
            dz = 0;
        }
        Simulation.Shift(dx, dz);
        LayCover();
        QueueUnknown();
    }

    /// <summary>
    /// Lays out where the fog may lie from everything in FogCover.Group (the terrain's chunks): among
    /// dense trees and low rocks. Done whenever the patch moves and when chunks come or go.
    /// </summary>
    public void LayCover()
    {
        if (Simulation == null || !IsInsideTree())
        {
            return;
        }
        GroundFogSimulation sim = Simulation;
        _coverVersion = FogCover.Version;
        _coverAt = Time.GetTicksMsec() / 1000.0;
        _coverTrees.Clear();
        _coverRocks.Clear();
        foreach (Node node in GetTree().GetNodesInGroup(FogCover.Group))
        {
            if (node is IFogCover cover)
            {
                cover.GatherFogCover(_coverTrees, _coverRocks);
            }
        }
        sim.BeginCover();
        foreach (Vector2 tree in _coverTrees)
        {
            sim.AddTree(tree.X, tree.Y);
        }
        foreach (Vector3 rock in _coverRocks)
        {
            sim.AddRock(rock.X, rock.Y, rock.Z);
        }
        sim.FinishCover();
        int covered = 0;
        foreach (float cover in sim.Cover)
        {
            if (cover > 0.5f)
            {
                covered++;
            }
        }
        CoverShare = covered / (float)sim.Cover.Length;
    }

    /// <summary>
    /// Where the parts of every body in FogCover.BodyGroup are now and how fast each moves: the bones
    /// named in the settings and the middle of each toward its child, so limbs count along their
    /// length. With no body in the scene (a lab's walker) the fog is parted at the followed point.
    /// </summary>
    private void GatherBodies(double elapsed)
    {
        _points.Clear();
        _pointVelocities.Clear();
        _bodyPass++;
        foreach (Node node in GetTree().GetNodesInGroup(FogCover.BodyGroup))
        {
            if (node is not Skeleton3D skeleton || !skeleton.IsInsideTree())
            {
                continue;
            }
            ulong id = skeleton.GetInstanceId();
            if (!_bodies.TryGetValue(id, out BodyTrack? track))
            {
                track = Track(skeleton);
                _bodies[id] = track;
            }
            track.Pass = _bodyPass;
            Transform3D world = skeleton.GlobalTransform;
            int slot = 0;
            for (int k = 0; k < track.Bones.Length; k++)
            {
                Vector3 bone = world * skeleton.GetBoneGlobalPose(track.Bones[k]).Origin;
                AddPart(track, slot++, bone, elapsed);
                if (track.Children[k] >= 0)
                {
                    Vector3 child = world * skeleton.GetBoneGlobalPose(track.Children[k]).Origin;
                    AddPart(track, slot++, (bone + child) * 0.5f, elapsed);
                }
            }
            track.Moved = true;
        }
        if (_bodies.Count > 0 && _bodyPass % 120 == 0)
        {
            List<ulong> gone = new();
            foreach (KeyValuePair<ulong, BodyTrack> body in _bodies)
            {
                if (body.Value.Pass != _bodyPass)
                {
                    gone.Add(body.Key);
                }
            }
            foreach (ulong id in gone)
            {
                _bodies.Remove(id);
            }
        }
    }

    private void AddPart(BodyTrack track, int slot, Vector3 at, double elapsed)
    {
        Vector3 velocity = Vector3.Zero;
        if (track.Moved && elapsed > 0.0)
        {
            velocity = (at - track.Last[slot]) / (float)elapsed;
            // A jump of the whole body (a respawn) is not movement through the fog.
            if (velocity.LengthSquared() > 20.0f * 20.0f)
            {
                velocity = Vector3.Zero;
            }
        }
        track.Last[slot] = at;
        _points.Add(at);
        _pointVelocities.Add(velocity);
    }

    private BodyTrack Track(Skeleton3D skeleton)
    {
        List<int> bones = new();
        string[] wanted = Settings!.BodyBones ?? Array.Empty<string>();
        bool deform = false;
        for (int b = 0; b < skeleton.GetBoneCount(); b++)
        {
            if (skeleton.GetBoneName(b).StartsWith("DEF-", StringComparison.Ordinal))
            {
                deform = true;
                break;
            }
        }
        for (int b = 0; b < skeleton.GetBoneCount(); b++)
        {
            string name = skeleton.GetBoneName(b);
            // A rig with deform bones has helper bones of the same names; only the deform ones follow the mesh.
            if (deform && !name.StartsWith("DEF-", StringComparison.Ordinal))
            {
                continue;
            }
            foreach (string part in wanted)
            {
                if (name.Contains(part, StringComparison.OrdinalIgnoreCase))
                {
                    bones.Add(b);
                    break;
                }
            }
        }
        if (bones.Count == 0)
        {
            for (int b = 0; b < skeleton.GetBoneCount(); b++)
            {
                bones.Add(b);
            }
        }
        BodyTrack track = new() { Bones = bones.ToArray(), Children = new int[bones.Count] };
        int parts = 0;
        for (int k = 0; k < track.Bones.Length; k++)
        {
            int[] children = skeleton.GetBoneChildren(track.Bones[k]);
            track.Children[k] = -1;
            if (children.Length > 0)
            {
                track.Children[k] = children[0];
                parts++;
            }
            parts++;
        }
        track.Last = new Vector3[parts];
        return track;
    }

    /// <summary>Queues every cell whose ground is unknown, nearest the carrier first.</summary>
    private void QueueUnknown(byte state = GroundFogSimulation.Unknown)
    {
        GroundFogSimulation sim = Simulation!;
        _fresh.Clear();
        for (int z = 0; z < sim.Size; z++)
        {
            for (int x = 0; x < sim.Size; x++)
            {
                if (sim.Ground[sim.Index(x, z)] == state)
                {
                    _fresh.Add(new Vector2I(sim.OriginX + x, sim.OriginZ + z));
                    sim.MarkGround(x, z, GroundFogSimulation.Queued);
                }
            }
        }
        float fx = _feet.X / sim.Cell;
        float fz = _feet.Z / sim.Cell;
        _fresh.Sort((a, b) =>
        {
            float da = (a.X - fx) * (a.X - fx) + (a.Y - fz) * (a.Y - fz);
            float db = (b.X - fx) * (b.X - fx) + (b.Y - fz) * (b.Y - fz);
            return da.CompareTo(db);
        });
        foreach (Vector2I cell in _fresh)
        {
            _queue.Enqueue(cell);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Simulation == null || Sampler == null || !_haveFeet)
        {
            return;
        }
        GroundFogSimulation sim = Simulation;
        _watch.Restart();
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        float fromY = _feet.Y + Settings!.RayTop;
        int budget = Math.Max(1, Settings.SamplesPerFrame);
        while (budget > 0 && _queue.Count > 0)
        {
            Vector2I cell = _queue.Dequeue();
            int x = cell.X - sim.OriginX;
            int z = cell.Y - sim.OriginZ;
            if (x < 0 || z < 0 || x >= sim.Size || z >= sim.Size || sim.Ground[sim.Index(x, z)] != GroundFogSimulation.Queued)
            {
                continue;
            }
            budget--;
            if (Sampler.Sample(space, sim.CellCentre(x, z), fromY, sim.Cell, out float height, out bool solid))
            {
                sim.SetGround(x, z, height, solid);
                Sampled++;
            }
            else
            {
                sim.MarkGround(x, z, GroundFogSimulation.Missed);
                Missed++;
            }
        }
        LastSampleMs = _watch.Elapsed.TotalMilliseconds;
        double now = Time.GetTicksMsec() / 1000.0;
        // Chunks arrive in bursts: lay the cover out again a moment after the last change.
        if (_coverVersion != FogCover.Version && now - _coverAt >= 0.25)
        {
            LayCover();
        }
        if (_queue.Count == 0 && now >= _retryAt)
        {
            _retryAt = now + Settings.RetrySeconds;
            QueueUnknown(GroundFogSimulation.Missed);
        }
    }

    public override void _Process(double delta)
    {
        if (Simulation == null || Settings == null)
        {
            return;
        }
        if (!_fromCarrier && Target != null && IsInstanceValid(Target))
        {
            Vector3 at = Target.GlobalPosition;
            Vector3 velocity = Vector3.Zero;
            if (_haveFeet && delta > 0.0)
            {
                velocity = (at - _lastTarget) / (float)delta;
            }
            _lastTarget = at;
            Follow(at, velocity);
        }
        if (!FogEnabled || !_haveFeet)
        {
            return;
        }
        _watch.Restart();
        double step = 1.0 / Math.Max(1.0f, Settings.StepsPerSecond);
        _accumulator += delta;
        _sinceBodies += delta;
        int steps = 0;
        if (_accumulator >= step)
        {
            GatherBodies(_sinceBodies);
            _sinceBodies = 0.0;
        }
        while (_accumulator >= step && steps < Settings.MaxStepsPerFrame)
        {
            if (_points.Count > 0)
            {
                PartsInFog = Simulation.PushBody(_points, _pointVelocities, (float)step);
            }
            else
            {
                PartsInFog = Simulation.Push(_feet, _velocity, (float)step);
            }
            Simulation.Step((float)step);
            _accumulator -= step;
            steps++;
            Renderer?.Upload();
        }
        if (_accumulator > step)
        {
            _accumulator = step;
        }
        if (Renderer != null)
        {
            Renderer.SetBlend((float)(_accumulator / step));
            float daylight = 1.0f;
            if (_clock != null)
            {
                daylight = _clock.Daylight;
            }
            Renderer.Look(Settings, daylight, (int)DebugView);
        }
        LastFrameMs = _watch.Elapsed.TotalMilliseconds;
    }

    public void SetFogEnabled(bool on)
    {
        FogEnabled = on;
        if (Renderer != null)
        {
            Renderer.Shown = on;
            Renderer.Visible = on;
        }
    }

    // --- Console ---

    private void RegisterCommands()
    {
        Debug.DevConsole.Register("fog", "fog [on|off|debug density|flow|ground|off|set <setting> <value>|get <setting>|settings|stats|relearn]",
            "The ground fog: turn it on or off, show its density, flow or sampled ground, change any setting live, timings.", Command);
    }

    private string Command(string[] words)
    {
        if (!IsInstanceValid(this) || Simulation == null || Settings == null)
        {
            return "No ground fog in this scene.";
        }
        if (words.Length == 0 || words[0] == "stats")
        {
            return Stats();
        }
        switch (words[0])
        {
            case "on":
                SetFogEnabled(true);
                return "Ground fog on.";
            case "off":
                SetFogEnabled(false);
                return "Ground fog off.";
            case "relearn":
                Relearn();
                return "Looking for the ground again.";
            case "debug":
                if (words.Length < 2 || !Enum.TryParse(words[1], true, out View view))
                {
                    return "Usage: /fog debug density|flow|ground|off";
                }
                DebugView = view;
                return "Fog debug view: " + view;
            case "settings":
                return Describe();
            case "get":
                if (words.Length < 2)
                {
                    return "Usage: /fog get <setting>";
                }
                return words[1] + " = " + Settings.Get(Property(words[1])).ToString();
            case "set":
                if (words.Length < 3)
                {
                    return "Usage: /fog set <setting> <value>";
                }
                return SetSetting(words[1], words[2]);
        }
        return "Usage: /fog [on|off|debug <view>|set <setting> <value>|get <setting>|settings|stats|relearn]";
    }

    private string Property(string name)
    {
        foreach (Godot.Collections.Dictionary property in Settings!.GetPropertyList())
        {
            string key = property["name"].AsString();
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }
        return name;
    }

    private string SetSetting(string name, string text)
    {
        string key = Property(name);
        Variant current = Settings!.Get(key);
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
        switch (current.VariantType)
        {
            case Variant.Type.Float:
                Settings.Set(key, float.Parse(text, culture));
                break;
            case Variant.Type.Int:
                Settings.Set(key, int.Parse(text, culture));
                break;
            case Variant.Type.Bool:
                Settings.Set(key, text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase));
                break;
            default:
                return "No number setting called '" + name + "'. /fog settings lists them.";
        }
        if (key == nameof(GroundFogSettings.GridSize) || key == nameof(GroundFogSettings.CellSize))
        {
            Build();
        }
        return key + " = " + Settings.Get(key).ToString();
    }

    private string Describe()
    {
        List<string> lines = new();
        foreach (Godot.Collections.Dictionary property in Settings!.GetPropertyList())
        {
            int usage = property["usage"].AsInt32();
            Variant.Type type = (Variant.Type)property["type"].AsInt32();
            if ((usage & (int)PropertyUsageFlags.ScriptVariable) == 0 || (type != Variant.Type.Float && type != Variant.Type.Int && type != Variant.Type.Bool))
            {
                continue;
            }
            string key = property["name"].AsString();
            lines.Add(key + " " + Settings.Get(key).ToString());
        }
        return string.Join("\n", lines);
    }

    public string Stats()
    {
        GroundFogSimulation sim = Simulation!;
        double upload = 0.0;
        if (Renderer != null)
        {
            upload = Renderer.LastUploadMs;
        }
        string state = "off";
        if (FogEnabled)
        {
            state = "on";
        }
        return $"Fog {state}, {sim.Size}x{sim.Size} cells of {sim.Cell:0.00} m, steps {sim.Steps}\n"
            + $"step {sim.LastStepMs:0.00} ms, upload {upload:0.00} ms, frame {LastFrameMs:0.00} ms, sampling {LastSampleMs:0.00} ms\n"
            + $"ground known {sim.KnownCells}/{sim.Size * sim.Size}, queued {_queue.Count}, sampled {Sampled}, missed {Missed}\n"
            + $"density under the carrier {sim.DensityAt(_feet.X, _feet.Z):0.00}, fastest flow {sim.FastestFlow():0.00} m/s, footfalls {Footfalls}\n"
            + $"fog may lie on {CoverShare * 100.0f:0} % of the patch ({_coverTrees.Count} trees, {_coverRocks.Count} low rocks), body parts in the fog {PartsInFog}/{PartsSeen}";
    }
}
