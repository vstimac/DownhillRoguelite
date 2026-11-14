using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// `-- --perf` prints where a frame's time goes, every few seconds: frames per second, script
/// (process) and physics time, the renderer's CPU and GPU time for the main viewport, draw calls,
/// triangles, nodes and objects. For finding what to optimize; run without --fixed-fps and with
/// `--disable-vsync` so the frame rate isn't capped. `--perf-breakdown` then hides each top-level part
/// of the scene in turn (and the sun's shadows) and prints what the frame saves without it.
/// </summary>
public partial class PerfLog : Node
{
    [Export] public float EverySeconds { get; set; } = 3.0f;
    [Export] public bool Breakdown { get; set; }
    /// <summary>After the render breakdown, also stop each part's scripts in turn (`--perf-cpu`).</summary>
    [Export] public bool CpuBreakdown { get; set; }
    /// <summary>After the warm-up, count the nodes that run every frame by script (`--perf-nodes`).</summary>
    [Export] public bool Counting { get; set; }
    /// <summary>Seconds to wait before the breakdown, and to settle and to measure each part.</summary>
    [Export] public float WarmUp { get; set; } = 20.0f;
    [Export] public float Settle { get; set; } = 0.5f;
    [Export] public float Sample { get; set; } = 1.5f;

    private float _timer;
    private int _frames;
    private double _process;
    private double _physics;
    private double _renderCpu;
    private double _renderGpu;
    private double _drawCalls;
    private double _triangles;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Measure the uncapped frame: the options' frame-rate cap would hide where the time goes.
        Engine.MaxFps = 0;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            // `--perf-physics=30`: physics ticks per second, to see the physics share of a frame.
            if (arg.StartsWith("--perf-physics=", System.StringComparison.Ordinal) && int.TryParse(arg["--perf-physics=".Length..], out int ticks))
            {
                Engine.PhysicsTicksPerSecond = Mathf.Max(ticks, 1);
            }
        }
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        if (Counting)
        {
            GetTree().CreateTimer(WarmUp).Timeout += CountProcessing;
        }
        if (Breakdown && !CpuBreakdown)
        {
            RunBreakdown();
        }
        else if (CpuBreakdown)
        {
            RunCpuOnly();
        }
    }

    /// <summary>Prints which kinds of node run every frame (process or physics), most first.</summary>
    private void CountProcessing()
    {
        System.Collections.Generic.Dictionary<string, int> counts = new();
        System.Collections.Generic.Stack<Node> stack = new();
        stack.Push(GetTree().Root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            foreach (Node child in node.GetChildren())
            {
                stack.Push(child);
            }
            if (!node.IsProcessing() && !node.IsPhysicsProcessing())
            {
                continue;
            }
            string kind = node.GetClass();
            if (node.GetScript().AsGodotObject() is Script script)
            {
                kind = script.ResourcePath.GetFile();
            }
            counts.TryGetValue(kind, out int count);
            counts[kind] = count + 1;
        }
        System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> list = new(counts);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        int total = 0;
        foreach (System.Collections.Generic.KeyValuePair<string, int> entry in list)
        {
            total += entry.Value;
        }
        GD.Print($"[perf-nodes] {total} nodes run every frame");
        System.Collections.Generic.Dictionary<string, int> bodies = new();
        stack.Push(GetTree().Root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            foreach (Node child in node.GetChildren())
            {
                stack.Push(child);
            }
            if (node is CollisionObject3D)
            {
                string kind = node.GetClass();
                if (node is RigidBody3D rigid && !rigid.Freeze && !rigid.Sleeping)
                {
                    kind += " (awake)";
                }
                bodies.TryGetValue(kind, out int count);
                bodies[kind] = count + 1;
            }
        }
        foreach (System.Collections.Generic.KeyValuePair<string, int> entry in bodies)
        {
            GD.Print($"[perf-bodies] {entry.Value} × {entry.Key}");
        }
        for (int i = 0; i < Mathf.Min(25, list.Count); i++)
        {
            GD.Print($"[perf-nodes] {list[i].Value} × {list[i].Key}");
        }
    }

    private async void RunCpuOnly()
    {
        await ToSignal(GetTree().CreateTimer(WarmUp), SceneTreeTimer.SignalName.Timeout);
        Node? root = GetTree().CurrentScene;
        if (root != null)
        {
            await RunCpuBreakdown(root);
        }
    }

    private async void RunBreakdown()
    {
        await ToSignal(GetTree().CreateTimer(WarmUp), SceneTreeTimer.SignalName.Timeout);
        Node? root = GetTree().CurrentScene;
        if (root == null)
        {
            return;
        }
        Vector4 all = await Measure();
        GD.Print($"[perf-part] everything: gpu {all.X:0.00} ms, render cpu {all.Y:0.00} ms, draw calls {all.Z:0}, triangles {all.W / 1000.0f:0}k");
        foreach (Node child in root.GetChildren())
        {
            if (child is DirectionalLight3D sun && sun.ShadowEnabled)
            {
                sun.ShadowEnabled = false;
                Vector4 noShadow = await Measure();
                sun.ShadowEnabled = true;
                Report(child.Name + " shadows", all, noShadow);
            }
            if (child is Node3D spatial && spatial.Visible)
            {
                spatial.Visible = false;
                Vector4 without = await Measure();
                spatial.Visible = true;
                Report(child.Name, all, without);
            }
            else if (child is CanvasLayer layer && layer.Visible)
            {
                layer.Visible = false;
                Vector4 without = await Measure();
                layer.Visible = true;
                Report(child.Name, all, without);
            }
        }
        GD.Print("[perf-part] done");
    }

    /// <summary>Stops each part's scripts and physics in turn (autoloads included) and prints how much shorter a frame gets.</summary>
    private async System.Threading.Tasks.Task RunCpuBreakdown(Node scene)
    {
        float all = await FrameMs();
        GD.Print($"[perf-cpu] everything: {all:0.00} ms a frame ({1000.0f / Mathf.Max(all, 0.01f):0} fps)");
        System.Collections.Generic.List<Node> parts = new();
        foreach (Node child in GetTree().Root.GetChildren())
        {
            if (child != scene)
            {
                parts.Add(child);
            }
        }
        parts.AddRange(scene.GetChildren());
        foreach (Node part in parts)
        {
            if (part == this || part.ProcessMode == ProcessModeEnum.Disabled)
            {
                continue;
            }
            // With and without back to back, so streaming and the clock don't drift the comparison.
            float with = await FrameMs();
            ProcessModeEnum mode = part.ProcessMode;
            part.ProcessMode = ProcessModeEnum.Disabled;
            float without = await FrameMs();
            part.ProcessMode = mode;
            float saved = with - without;
            if (saved > 0.15f)
            {
                GD.Print($"[perf-cpu] {part.Name}: -{saved:0.00} ms (of {with:0.00})");
            }
        }
        GD.Print("[perf-cpu] done");
    }

    /// <summary>Waits Settle, then the average real frame time over Sample seconds, in ms.</summary>
    private async System.Threading.Tasks.Task<float> FrameMs()
    {
        await ToSignal(GetTree().CreateTimer(Settle), SceneTreeTimer.SignalName.Timeout);
        ulong start = Time.GetTicksUsec();
        int frames = 0;
        while (Time.GetTicksUsec() - start < (ulong)(Sample * 1000000.0f))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
        }
        return (Time.GetTicksUsec() - start) / 1000.0f / Mathf.Max(frames, 1);
    }

    private static void Report(string part, Vector4 all, Vector4 without)
    {
        GD.Print($"[perf-part] {part}: gpu -{all.X - without.X:0.00} ms, render cpu -{all.Y - without.Y:0.00} ms, "
            + $"draw calls -{all.Z - without.Z:0}, triangles -{(all.W - without.W) / 1000.0f:0}k");
    }

    /// <summary>Waits Settle, then averages gpu ms, render cpu ms, draw calls and triangles over Sample seconds.</summary>
    private async System.Threading.Tasks.Task<Vector4> Measure()
    {
        await ToSignal(GetTree().CreateTimer(Settle), SceneTreeTimer.SignalName.Timeout);
        Rid viewport = GetViewport().GetViewportRid();
        Vector4 sum = Vector4.Zero;
        int frames = 0;
        ulong end = Time.GetTicksMsec() + (ulong)(Sample * 1000.0f);
        while (Time.GetTicksMsec() < end)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            sum += new Vector4(
                (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport),
                (float)(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu()),
                (float)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                (float)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
            frames++;
        }
        return sum / Mathf.Max(frames, 1);
    }

    public override void _Process(double delta)
    {
        Rid viewport = GetViewport().GetViewportRid();
        _frames++;
        _process += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        _physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        _renderCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu();
        _renderGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        _drawCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _triangles += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        _timer += (float)delta;
        if (_timer < EverySeconds)
        {
            return;
        }
        float n = Mathf.Max(_frames, 1);
        GD.Print($"[perf] {Engine.GetFramesPerSecond():0} fps | process {_process / n:0.00} ms, physics {_physics / n:0.00} ms, "
            + $"render cpu {_renderCpu / n:0.00} ms, gpu {_renderGpu / n:0.00} ms | draw calls {_drawCalls / n:0}, "
            + $"triangles {_triangles / n / 1000.0:0}k | nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}, "
            + $"objects {Performance.GetMonitor(Performance.Monitor.ObjectCount):0} | physics active {Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects):0}, "
            + $"pairs {Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs):0}, islands {Performance.GetMonitor(Performance.Monitor.Physics3DIslandCount):0}");
        _timer = 0.0f;
        _frames = 0;
        _process = 0.0;
        _physics = 0.0;
        _renderCpu = 0.0;
        _renderGpu = 0.0;
        _drawCalls = 0.0;
        _triangles = 0.0;
    }
}
