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
/// steps. The carrier's movement (GameEvents.LocomotionUpdated) ploughs through it and each
/// footfall (GameEvents.FootPlacementUpdated) stirs it. Colour follows the time of day.
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
    public Vector3 Feet => _feet;

    private readonly Queue<Vector2I> _queue = new();
    private readonly List<Vector2I> _fresh = new();
    private double _accumulator;
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
        QueueUnknown();
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
        int steps = 0;
        while (_accumulator >= step && steps < Settings.MaxStepsPerFrame)
        {
            Simulation.Push(_feet, _velocity, (float)step);
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
            + $"density under the carrier {sim.DensityAt(_feet.X, _feet.Z):0.00}, fastest flow {sim.FastestFlow():0.00} m/s, footfalls {Footfalls}";
    }
}
