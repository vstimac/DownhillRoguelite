using System.Collections.Generic;
using DownhillRoguelite.Environment.Fog;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Ground fog lab: a slope with a hollow, a gully, trunks and rocks, and a walker (a capsule) that
/// goes round a loop through them, stepping left and right, so the fog can be watched pouring
/// downhill, pooling, parting around the trunks and closing behind. The camera follows the walker.
/// Keys: F fog on/off, 1-4 debug view (off, density, flow, ground), Space sprint, P pause the walker,
/// right-drag orbit, wheel zoom.
/// Benchmark: godot --path . res://debug/GroundFogLab.tscn -- --fog-bench (prints frame times with
/// the fog on and off, then quits).
/// </summary>
public partial class GroundFogLab : Node3D
{
    [Export] public GroundFog? Fog { get; set; }
    [Export] public Camera3D? Camera { get; set; }
    [Export] public int GroundPoints { get; set; } = 161;
    [Export] public float GroundSlope { get; set; } = 0.22f;
    [Export] public float WalkSpeed { get; set; } = 1.6f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    [Export] public float LoopRadius { get; set; } = 14.0f;
    [Export] public float StepLength { get; set; } = 0.75f;
    [Export] public float BenchSeconds { get; set; } = 5.0f;
    [Export] public float BenchWarmup { get; set; } = 3.0f;

    private Node3D? _walker;
    private float _angle;
    private bool _sprint;
    private bool _paused;
    private float _sinceStep;
    private bool _left;
    private float _yaw = 0.4f;
    private float _pitch = -0.32f;
    private float _distance = 7.0f;
    private bool _bench;
    private float _benchClock;
    private int _benchPhase;
    private readonly List<double> _frames = new();
    private double _fogOnMs;

    public float Height(float x, float z)
    {
        float slope = -GroundSlope * x;
        float hollowX = x - 6.0f;
        float hollowZ = z + 8.0f;
        float hollow = -1.4f * Mathf.Max(0.0f, 1.0f - Mathf.Sqrt(hollowX * hollowX + hollowZ * hollowZ) / 6.0f);
        float gully = -0.9f * Mathf.Exp(-Mathf.Pow((z - 9.0f + 2.0f * Mathf.Sin(x * 0.15f)) / 2.2f, 2.0f));
        float bumps = 0.35f * Mathf.Sin(x * 0.31f) * Mathf.Cos(z * 0.27f);
        return slope + hollow + gully + bumps;
    }

    public override void _Ready()
    {
        BuildGround();
        BuildObstacles();
        _walker = new Node3D { Name = "Walker" };
        AddChild(_walker);
        _walker.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.75f },
            Position = new Vector3(0.0f, 0.875f, 0.0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.35f, 0.2f) },
        });
        if (Fog != null)
        {
            Fog.Target = _walker;
        }
        MoveWalker(0.0f);
        string[] args = OS.GetCmdlineUserArgs();
        _bench = System.Array.IndexOf(args, "--fog-bench") >= 0;
        foreach (string arg in args)
        {
            // --fog=Setting:value changes a setting (to compare looks), --no-fog starts with it off, --view=N picks a debug view.
            if (arg.StartsWith("--fog=") && arg.Contains(':'))
            {
                string[] pair = arg.Substring(6).Split(':');
                GD.Print(DevConsole.Execute("fog set " + pair[0] + " " + pair[1]));
            }
            else if (arg == "--no-fog")
            {
                Fog?.SetFogEnabled(false);
            }
            else if (arg.StartsWith("--view=") && Fog != null)
            {
                Fog.DebugView = (GroundFog.View)int.Parse(arg.Substring(7));
            }
            else if (arg.StartsWith("--pitch="))
            {
                _pitch = float.Parse(arg.Substring(8), System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (arg.StartsWith("--distance="))
            {
                _distance = float.Parse(arg.Substring(11), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        if (_bench)
        {
            _sprint = true;
            GD.Print("Fog bench: warming up...");
        }
    }

    private void BuildGround()
    {
        int n = GroundPoints;
        float half = (n - 1) * 0.5f;
        float[] map = new float[n * n];
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                map[z * n + x] = Height(x - half, z - half);
            }
        }
        for (int z = 0; z < n - 1; z++)
        {
            for (int x = 0; x < n - 1; x++)
            {
                Vector3 a = Point(map, n, x, z, half);
                Vector3 b = Point(map, n, x + 1, z, half);
                Vector3 c = Point(map, n, x, z + 1, half);
                Vector3 d = Point(map, n, x + 1, z + 1, half);
                Color tint = new Color(0.32f, 0.42f, 0.24f).Lerp(new Color(0.45f, 0.43f, 0.36f), 0.5f + 0.5f * Mathf.Sin(x * 0.7f + z * 0.3f) * Mathf.Cos(z * 0.9f));
                tool.SetColor(tint);
                tool.AddVertex(a);
                tool.AddVertex(b);
                tool.AddVertex(c);
                tool.AddVertex(b);
                tool.AddVertex(d);
                tool.AddVertex(c);
            }
        }
        tool.GenerateNormals();
        StaticBody3D body = new() { Name = "Ground" };
        body.AddToGroup(PhysicsFogGround.GroundGroup);
        body.AddChild(new CollisionShape3D { Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = map } });
        body.AddChild(new MeshInstance3D
        {
            Mesh = tool.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f },
        });
        AddChild(body);
    }

    private static Vector3 Point(float[] map, int n, int x, int z, float half) => new(x - half, map[z * n + x], z - half);

    private void BuildObstacles()
    {
        RandomNumberGenerator random = new() { Seed = 1907 };
        StandardMaterial3D bark = new() { AlbedoColor = new Color(0.3f, 0.22f, 0.16f) };
        StandardMaterial3D stone = new() { AlbedoColor = new Color(0.5f, 0.5f, 0.52f), Roughness = 0.9f };
        for (int i = 0; i < 26; i++)
        {
            float x = random.RandfRange(-30.0f, 30.0f);
            float z = random.RandfRange(-30.0f, 30.0f);
            if (Mathf.Abs(new Vector2(x, z).Length() - LoopRadius) < 1.6f)
            {
                continue;
            }
            float radius = random.RandfRange(0.2f, 0.4f);
            Add("Trunk", new Vector3(x, Height(x, z) + 4.0f, z), new CylinderShape3D { Radius = radius, Height = 8.0f },
                new CylinderMesh { TopRadius = radius * 0.8f, BottomRadius = radius, Height = 8.0f }, bark);
        }
        for (int i = 0; i < 10; i++)
        {
            float x = random.RandfRange(-25.0f, 25.0f);
            float z = random.RandfRange(-25.0f, 25.0f);
            if (Mathf.Abs(new Vector2(x, z).Length() - LoopRadius) < 2.5f)
            {
                continue;
            }
            float size = random.RandfRange(0.8f, 2.2f);
            Add("Rock", new Vector3(x, Height(x, z) + size * 0.3f, z), new SphereShape3D { Radius = size * 0.5f },
                new SphereMesh { Radius = size * 0.5f, Height = size * 0.8f }, stone);
        }
    }

    private void Add(string name, Vector3 at, Shape3D shape, Mesh mesh, Material material)
    {
        StaticBody3D body = new() { Name = name, Position = at };
        body.AddChild(new CollisionShape3D { Shape = shape });
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        AddChild(body);
    }

    private void MoveWalker(float dt)
    {
        if (_walker == null)
        {
            return;
        }
        float speed = WalkSpeed;
        if (_sprint)
        {
            speed = SprintSpeed;
        }
        if (_paused)
        {
            speed = 0.0f;
        }
        _angle += speed * dt / LoopRadius;
        Vector2 flat = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle)) * LoopRadius;
        Vector3 at = new(flat.X, Height(flat.X, flat.Y), flat.Y);
        Vector3 ahead = new(-Mathf.Sin(_angle), 0.0f, Mathf.Cos(_angle));
        _walker.GlobalPosition = at;
        _walker.LookAt(at + ahead, Vector3.Up);
        _sinceStep += speed * dt;
        if (_sinceStep >= StepLength && Fog != null)
        {
            _sinceStep = 0.0f;
            _left = !_left;
            Vector3 side = ahead.Cross(Vector3.Up) * 0.15f;
            if (_left)
            {
                side = -side;
            }
            Fog.Footfall(at + side, _left);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        MoveWalker(dt);
        if (Camera != null && _walker != null)
        {
            Vector3 target = _walker.GlobalPosition + Vector3.Up * 1.0f;
            Basis turn = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);
            Camera.GlobalPosition = target + turn * new Vector3(0.0f, 0.0f, _distance);
            Camera.LookAt(target, Vector3.Up);
            _yaw += 0.05f * dt;
        }
        if (_bench)
        {
            Bench(delta);
        }
    }

    private void Bench(double delta)
    {
        _benchClock += (float)delta;
        if (_benchClock < BenchWarmup)
        {
            return;
        }
        _frames.Add(delta * 1000.0);
        if (_benchClock < BenchWarmup + BenchSeconds)
        {
            return;
        }
        double mean = 0.0;
        foreach (double f in _frames)
        {
            mean += f;
        }
        mean /= Mathf.Max(1, _frames.Count);
        _frames.Sort();
        double p95 = _frames[(int)(_frames.Count * 0.95f)];
        string gpu = RenderingServer.GetVideoAdapterName();
        if (_benchPhase == 0)
        {
            _fogOnMs = mean;
            GD.Print($"BENCH fog on:  {mean:0.00} ms mean ({1000.0 / mean:0} fps), 95th {p95:0.00} ms, sim step {Fog?.Simulation?.LastStepMs:0.00} ms, {gpu}");
            Fog?.SetFogEnabled(false);
            _benchPhase = 1;
            _benchClock = 0.0f;
            _frames.Clear();
            return;
        }
        GD.Print($"BENCH fog off: {mean:0.00} ms mean ({1000.0 / mean:0} fps), 95th {p95:0.00} ms; the fog costs {_fogOnMs - mean:0.00} ms a frame");
        GetTree().Quit(0);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.F:
                    Fog?.SetFogEnabled(!Fog.FogEnabled);
                    break;
                case Key.Space:
                    _sprint = !_sprint;
                    break;
                case Key.P:
                    _paused = !_paused;
                    break;
                case Key.Key1:
                case Key.Key2:
                case Key.Key3:
                case Key.Key4:
                    if (Fog != null)
                    {
                        Fog.DebugView = (GroundFog.View)(key.PhysicalKeycode - Key.Key1);
                    }
                    break;
            }
        }
        if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Right) != 0)
        {
            _yaw -= motion.Relative.X * 0.005f;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.005f, -1.3f, 0.2f);
        }
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Mathf.Max(2.0f, _distance * 0.9f);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Mathf.Min(40.0f, _distance * 1.1f);
            }
        }
    }
}
