using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Environment.Fog;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The ground fog. The simulation on its own: it runs downhill, fills hollows, parts around what
/// blocks it, the carrier clears a bubble that closes again within the recovery time, a sprint
/// leaves a bigger wake than a walk, a footfall leaves a whirl turning the foot's way, still fog keeps
/// moving, shifting the patch keeps what was there, and nothing blows up under hard use. Then with
/// physics: the sampler finds a height-map ground through a rock standing on it and marks the rock
/// and a trunk as blocked; the GroundFog node learns all of its patch, follows a walker across
/// (shifting), and uploads to its renderer. Timings for the README at the end.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/GroundFogTests.tscn
/// </summary>
public partial class GroundFogTests : Node3D
{
    private const float Slope = 0.25f;
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        Downhill();
        Hollows();
        Obstacles();
        WakeAndRecovery();
        SprintWake();
        Footfalls();
        StillFogMoves();
        ShiftKeeps();
        Stable();
        Benchmark();
        await Physics();

        if (_failures.Count == 0)
        {
            GD.Print("GROUND FOG TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("GROUND FOG TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    // --- The simulation ---

    private static GroundFogSettings Small()
    {
        return new GroundFogSettings { GridSize = 64, CellSize = 0.75f, Turbulence = 0.0f };
    }

    private static GroundFogSimulation Sim(GroundFogSettings settings, Func<float, float, float> ground)
    {
        GroundFogSimulation sim = new(settings);
        sim.CentreOn(0.0f, 0.0f);
        for (int z = 0; z < sim.Size; z++)
        {
            for (int x = 0; x < sim.Size; x++)
            {
                Vector2 at = sim.CellCentre(x, z);
                sim.SetGround(x, z, ground(at.X, at.Y), false);
            }
        }
        return sim;
    }

    private static void Run(GroundFogSimulation sim, float seconds)
    {
        float dt = 1.0f / 30.0f;
        for (float t = 0.0f; t < seconds; t += dt)
        {
            sim.Step(dt);
        }
    }

    private void Downhill()
    {
        GroundFogSimulation sim = Sim(Small(), (x, z) => -Slope * x);
        Run(sim, 3.0f);
        Vector2 flow = sim.FlowAt(0.0f, 0.0f);
        Check("fog runs downhill (toward +X on ground falling that way)", flow.X > 0.3f && Mathf.Abs(flow.Y) < flow.X * 0.3f,
            $"flow {flow.X:0.00}, {flow.Y:0.00} m/s");
    }

    private void Hollows()
    {
        GroundFogSettings settings = Small();
        GroundFogSimulation sim = Sim(settings, (x, z) =>
        {
            float r = Mathf.Sqrt(x * x + z * z);
            return -1.2f * Mathf.Max(0.0f, 1.0f - r / 4.0f);
        });
        Run(sim, 6.0f);
        float hollow = sim.DensityAt(0.0f, 0.0f);
        float open = sim.DensityAt(15.0f, 15.0f);
        Check("fog pools in a hollow", hollow > open * 1.3f, $"hollow {hollow:0.00}, open ground {open:0.00}");
    }

    private void Obstacles()
    {
        GroundFogSimulation sim = Sim(Small(), (x, z) => -Slope * x);
        // A wall across the flow, three cells wide, at x = 3 m.
        List<int> wall = new();
        for (float z = -1.0f; z <= 1.0f; z += sim.Cell)
        {
            sim.CellAt(3.0f, z, out int cx, out int cz);
            sim.SetGround(cx, cz, -Slope * 3.0f, true);
            wall.Add(sim.Index(cx, cz));
        }
        Run(sim, 3.0f);
        bool empty = true;
        foreach (int i in wall)
        {
            empty &= sim.Density[i] == 0.0f && sim.FlowX[i] == 0.0f;
        }
        sim.CellAt(3.0f - sim.Cell, 0.0f, out int ux, out int uz);
        float into = sim.FlowX[sim.Index(ux, uz)];
        Vector2 aside = sim.FlowAt(2.4f, 1.9f);
        float beyond = sim.DensityAt(5.0f, 0.0f);
        Check("nothing flows into or fills a blocked cell", empty && into <= 0.0001f, $"flow into the wall {into:0.000}");
        Check("fog parts around what blocks it and closes behind", Mathf.Abs(aside.Y) > 0.02f && beyond > 0.2f,
            $"sideways beside it {aside.Y:0.00} m/s, density behind {beyond:0.00}");
    }

    private void WakeAndRecovery()
    {
        GroundFogSettings settings = Small();
        GroundFogSimulation sim = Sim(settings, (x, z) => 0.0f);
        float settled = sim.DensityAt(0.0f, 0.0f);
        float dt = 1.0f / 30.0f;
        for (int i = 0; i < 30; i++)
        {
            sim.Push(Vector3.Zero, Vector3.Zero, dt);
            sim.Step(dt);
        }
        float bubble = sim.MeanDensity(Vector2.Zero, settings.PushRadius * 0.6f);
        Check("the carrier's body clears a bubble in the fog", bubble < settled * 0.5f, $"in the bubble {bubble:0.00} of {settled:0.00}");
        Run(sim, settings.RecoveryTime * 2.0f);
        float closed = sim.MeanDensity(Vector2.Zero, settings.PushRadius * 0.6f);
        Check("the gap closes again within the recovery time", closed > settled * 0.8f, $"after {settings.RecoveryTime * 2.0f:0.0} s {closed:0.00} of {settled:0.00}");
    }

    /// <summary>Cells left with under half the settled density after walking or sprinting 3 s across flat ground.</summary>
    private static int Wake(float speed)
    {
        GroundFogSettings settings = Small();
        GroundFogSimulation sim = Sim(settings, (x, z) => 0.0f);
        float dt = 1.0f / 30.0f;
        Vector3 feet = new(-speed * 1.5f, 0.0f, 0.0f);
        Vector3 velocity = new(speed, 0.0f, 0.0f);
        for (int i = 0; i < 90; i++)
        {
            sim.Push(feet, velocity, dt);
            sim.Step(dt);
            feet += velocity * dt;
        }
        int cleared = 0;
        foreach (float d in sim.Density)
        {
            if (d < settings.Ambient * 0.5f)
            {
                cleared++;
            }
        }
        return cleared;
    }

    private void SprintWake()
    {
        int walk = Wake(1.5f);
        int sprint = Wake(6.0f);
        Check("a sprint leaves a bigger wake than a walk", sprint > walk * 1.5f && walk > 0, $"cleared cells: walking {walk}, sprinting {sprint}");
    }

    private void Footfalls()
    {
        GroundFogSimulation left = Sim(Small(), (x, z) => 0.0f);
        GroundFogSimulation right = Sim(Small(), (x, z) => 0.0f);
        left.Footfall(Vector3.Zero, true);
        right.Footfall(Vector3.Zero, false);
        // Above the foot (toward +Z... the whirl's tangent at +X side points along ±Z).
        float l = left.FlowAt(0.3f, 0.0f).Y;
        float r = right.FlowAt(0.3f, 0.0f).Y;
        Check("a footfall stirs a whirl, turning one way for the left foot and the other for the right", Mathf.Abs(l) > 0.3f && l * r < 0.0f,
            $"swirl beside the foot: left {l:0.00}, right {r:0.00} m/s");
    }

    private void StillFogMoves()
    {
        GroundFogSettings settings = Small();
        settings.Turbulence = new GroundFogSettings().Turbulence;
        GroundFogSimulation sim = Sim(settings, (x, z) => 0.0f);
        Run(sim, 5.0f);
        float before = sim.DensityAt(3.0f, -2.0f);
        Vector2 flow = sim.FlowAt(3.0f, -2.0f);
        Run(sim, 2.0f);
        Check("still fog on flat ground keeps drifting", sim.FastestFlow() > 0.05f && flow.Length() > 0.005f,
            $"fastest {sim.FastestFlow():0.000} m/s, here {flow.Length():0.000}, density {before:0.00}");
    }

    private void ShiftKeeps()
    {
        GroundFogSimulation sim = Sim(Small(), (x, z) => 0.1f * x);
        sim.CellAt(2.0f, 3.0f, out int x, out int z);
        int i = sim.Index(x, z);
        sim.Density[i] = 1.7f;
        sim.FlowX[i] = 0.9f;
        Vector2 at = sim.CellCentre(x, z);
        int originX = sim.OriginX;
        sim.Shift(5, -3);
        bool kept = sim.CellAt(at.X, at.Y, out int nx, out int nz);
        int j = sim.Index(nx, nz);
        bool same = kept && nx == x - 5 && nz == z + 3 && sim.Density[j] == 1.7f && sim.FlowX[j] == 0.9f && sim.Ground[j] == GroundFogSimulation.Known;
        int fresh = sim.Index(sim.Size - 1, 10);
        bool newCell = sim.Ground[fresh] == GroundFogSimulation.Unknown && Mathf.IsEqualApprox(sim.Density[fresh], sim.Settings.Ambient);
        Check("shifting the patch keeps density, flow and ground in the overlap", same && sim.OriginX == originX + 5, $"moved cell {nx},{nz} from {x},{z}");
        Check("cells that come in are settled fog over unknown ground", newCell, $"ground state {sim.Ground[fresh]}, density {sim.Density[fresh]:0.00}");
    }

    private void Stable()
    {
        GroundFogSettings settings = new() { GridSize = 64 };
        GroundFogSimulation sim = Sim(settings, (x, z) => -0.6f * x + Mathf.Sin(z * 0.7f) * 1.5f);
        Random random = new(7);
        float dt = 1.0f / 30.0f;
        for (int i = 0; i < 1500; i++)
        {
            Vector3 feet = new((float)random.NextDouble() * 30.0f - 15.0f, 0.0f, (float)random.NextDouble() * 30.0f - 15.0f);
            sim.Push(feet, new Vector3((float)random.NextDouble() * 16.0f - 8.0f, 0.0f, (float)random.NextDouble() * 16.0f - 8.0f), dt);
            if (i % 7 == 0)
            {
                sim.Footfall(feet, i % 2 == 0);
            }
            if (i % 200 == 0)
            {
                sim.Shift(random.Next(-9, 10), random.Next(-9, 10));
            }
            sim.Step(dt);
        }
        float most = 0.0f;
        foreach (float d in sim.Density)
        {
            most = Mathf.Max(most, d);
        }
        Check("50 s of hard use on steep ground: no blow-up, density and flow within their limits",
            sim.Finite() && most <= settings.MaxDensity + 0.001f && sim.FastestFlow() <= settings.MaxFlow + 0.01f,
            $"densest {most:0.00}, fastest {sim.FastestFlow():0.00} m/s");
    }

    private void Benchmark()
    {
        GroundFogSettings settings = new();
        GroundFogSimulation sim = Sim(settings, (x, z) => -0.4f * x + Mathf.Sin(z * 0.3f));
        float dt = 1.0f / 30.0f;
        for (int i = 0; i < 30; i++)
        {
            sim.Step(dt);
        }
        double total = 0.0;
        double worst = 0.0;
        int steps = 300;
        for (int i = 0; i < steps; i++)
        {
            sim.Push(new Vector3(i * 0.05f - 7.0f, 0.0f, 0.0f), new Vector3(6.0f, 0.0f, 0.0f), dt);
            sim.Step(dt);
            total += sim.LastStepMs;
            worst = Math.Max(worst, sim.LastStepMs);
        }
        float[] fog = new float[sim.Size * sim.Size * 4];
        float[] heights = new float[sim.Size * sim.Size];
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 30; i++)
        {
            sim.Export(fog, heights);
        }
        double export = watch.Elapsed.TotalMilliseconds / 30.0;
        double mean = total / steps;
        GD.Print($"BENCH ground fog {sim.Size}x{sim.Size}: step mean {mean:0.000} ms, worst {worst:0.000} ms, export {export:0.000} ms; "
            + $"at {settings.StepsPerSecond} steps/s about {mean * settings.StepsPerSecond / 1000.0 * 100.0:0.0}% of one core");
        Check("a 128x128 step costs under 3 ms on average", mean < 3.0, $"{mean:0.000} ms");
    }

    // --- With physics ---

    private static float Ground(float x, float z) => -Slope * x + 0.4f * Mathf.Sin(z * 0.35f);

    private async Task Physics()
    {
        const int points = 72;
        StaticBody3D terrain = new() { Name = "Ground" };
        float[] map = new float[points * points];
        float half = (points - 1) * 0.5f;
        for (int z = 0; z < points; z++)
        {
            for (int x = 0; x < points; x++)
            {
                map[z * points + x] = Ground(x - half, z - half);
            }
        }
        terrain.AddChild(new CollisionShape3D { Shape = new HeightMapShape3D { MapWidth = points, MapDepth = points, MapData = map } });
        AddChild(terrain);
        StaticBody3D trunk = Block("Trunk", new Vector3(4.0f, Ground(4.0f, -3.0f) + 1.5f, -3.0f), new Vector3(0.6f, 3.0f, 0.6f));
        StaticBody3D rock = Block("Rock", new Vector3(-5.0f, Ground(-5.0f, 5.0f) + 1.0f, 5.0f), new Vector3(2.0f, 2.0f, 2.0f));
        await Frames(2);

        GroundFogSettings settings = new() { GridSize = 64 };
        PhysicsFogGround sampler = new(settings);
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        bool open = sampler.Sample(space, new Vector2(1.0f, 2.0f), 30.0f, 0.75f, out float openHeight, out bool openSolid);
        bool throughRock = sampler.Sample(space, new Vector2(-5.0f, 5.0f), 30.0f, 0.75f, out float rockGround, out bool rockSolid);
        bool atTrunk = sampler.Sample(space, new Vector2(4.0f, -3.0f), 30.0f, 0.75f, out float trunkGround, out bool trunkSolid);
        bool off = sampler.Sample(space, new Vector2(200.0f, 0.0f), 30.0f, 0.75f, out _, out _);
        Check("the sampler finds the height-map ground", open && !openSolid && Mathf.Abs(openHeight - Ground(1.0f, 2.0f)) < 0.08f,
            $"found {open} at {openHeight:0.00}, ground {Ground(1.0f, 2.0f):0.00}");
        Check("it looks through a rock to the ground under it and marks the rock as blocking",
            throughRock && rockSolid && Mathf.Abs(rockGround - Ground(-5.0f, 5.0f)) < 0.08f, $"ground {rockGround:0.00}, blocked {rockSolid}");
        Check("a trunk blocks its cell", atTrunk && trunkSolid && Mathf.Abs(trunkGround - Ground(4.0f, -3.0f)) < 0.08f, $"blocked {trunkSolid}");
        Check("no ground beyond the terrain (tried again later)", !off, "");

        Node3D walker = new() { Name = "Walker", Position = new Vector3(0.0f, Ground(0.0f, 0.0f), 0.0f) };
        AddChild(walker);
        GroundFog fog = new() { Name = "GroundFog", Settings = settings, Target = walker, DrawWhenHeadless = true };
        AddChild(fog);
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        await Until(() => fog.Simulation!.KnownCells >= fog.Simulation.Size * fog.Simulation.Size * 0.98f, 3.0f);
        double learn = watch.Elapsed.TotalMilliseconds;
        GroundFogSimulation sim = fog.Simulation!;
        int known = sim.KnownCells;
        int rays = fog.Sampler!.Rays;
        Check("the fog learns the ground of its whole patch within a second", known >= sim.Size * sim.Size * 0.98f && learn < 1500.0,
            $"{known}/{sim.Size * sim.Size} cells in {learn:0} ms, {rays} rays, sampling {fog.LastSampleMs:0.00} ms in the last frame");
        sim.CellAt(4.0f, -3.0f, out int tx, out int tz);
        Check("the patch knows the trunk is there", sim.Solid[sim.Index(tx, tz)], "");

        int origin = sim.OriginX;
        for (int i = 0; i < 60; i++)
        {
            walker.Position += new Vector3(0.15f, 0.0f, 0.0f);
            walker.Position = new Vector3(walker.Position.X, Ground(walker.Position.X, 0.0f), 0.0f);
            await Frames(1);
        }
        await Frames(30);
        Check("walking 9 m across shifts the patch with the walker", sim.OriginX > origin, $"origin {origin} -> {sim.OriginX}");
        float under = sim.DensityAt(walker.Position.X, 0.0f);
        Check("the walker clears the fog around itself", under < settings.Ambient * 0.8f, $"under it {under:0.00} of {settings.Ambient:0.00}");
        fog.Footfall(walker.Position + new Vector3(2.0f, 0.0f, 0.0f), true);
        Check("a footfall in the fog is counted", fog.Footfalls == 1, $"{fog.Footfalls}");
        fog.Footfall(walker.Position + new Vector3(2.0f, 5.0f, 0.0f), true);
        Check("a foot high above the fog doesn't stir it", fog.Footfalls == 1, $"{fog.Footfalls}");
        Check("each step goes up to the renderer", fog.Renderer != null && fog.Renderer.Uploads > 10 && fog.Renderer.Material != null,
            $"uploads {fog.Renderer?.Uploads}, last {fog.Renderer?.LastUploadMs:0.00} ms");
        string stats = Debug.DevConsole.Execute("fog stats");
        string set = Debug.DevConsole.Execute("fog set wakestrength 1.5");
        Check("/fog stats and /fog set work", stats.Contains("ground known") && Mathf.IsEqualApprox(settings.WakeStrength, 1.5f), set);
        GD.Print(stats);
        trunk.QueueFree();
        rock.QueueFree();
    }

    private StaticBody3D Block(string name, Vector3 at, Vector3 size)
    {
        StaticBody3D body = new() { Name = name, Position = at };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        return body;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Until(Func<bool> done, float timeout)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < timeout && !done(); elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
