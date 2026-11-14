using System;
using System.Diagnostics;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The performance part of the F3 overlay: frame time, CPU (main-thread split, the process's share
/// of all cores), GPU (frame time, load, video memory, adapter), memory (engine, .NET, process,
/// system) and the reason a frame is slow: GPU-bound, CPU-bound (and on what), a frame cap or a
/// garbage-collection hitch. Samples every frame, rewrites <see cref="Text"/> a few times a second.
/// Measures GPU time only while <see cref="Active"/> (the overlay shows).
/// </summary>
public partial class PerformanceReadout : Node
{
    /// <summary>How often the text is rewritten (the values are averages over this window), in s.</summary>
    [Export] public float UpdateSeconds { get; set; } = 0.5f;
    /// <summary>GPU-bound when the GPU's time is at least this share of the frame.</summary>
    [Export] public float BoundShare { get; set; } = 0.85f;
    /// <summary>A frame capped by VSync or Max FPS when neither CPU nor GPU fills this share of it.</summary>
    [Export] public float IdleShare { get; set; } = 0.6f;
    /// <summary>A frame this many times the window's average is a hitch.</summary>
    [Export] public float HitchFactor { get; set; } = 2.5f;
    [Export] public int ManyDrawCalls { get; set; } = 3000;
    [Export] public int ManyTrianglesThousands { get; set; } = 3000;
    [Export] public int ManyPhysicsBodies { get; set; } = 400;

    public bool Active { get; set; }
    public string Text { get; private set; } = "";

    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _cpuTime;
    private double _window;
    private int _frames;
    private double _frameSum, _frameWorst;
    private double _scriptSum, _physicsSum, _renderCpuSum, _gpuSum;
    private double _drawSum, _triangleSum;
    private int _gcCount;
    private bool _measuring;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _cpuTime = _process.TotalProcessorTime;
        _gcCount = GcCount();
    }

    public override void _ExitTree()
    {
        Measure(false);
    }

    public override void _Process(double delta)
    {
        Measure(Active);
        if (!Active)
        {
            return;
        }
        Rid viewport = GetViewport().GetViewportRid();
        double frame = delta * 1000.0;
        _frames++;
        _frameSum += frame;
        _frameWorst = Math.Max(_frameWorst, frame);
        _scriptSum += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        _physicsSum += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        _renderCpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu();
        _gpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        _drawSum += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _triangleSum += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        _window += delta;
        if (_window >= UpdateSeconds)
        {
            Text = Compose();
            Reset();
        }
    }

    private void Measure(bool on)
    {
        if (on == _measuring)
        {
            return;
        }
        _measuring = on;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), on);
        if (on)
        {
            Reset();
            _cpuTime = _process.TotalProcessorTime;
        }
    }

    private void Reset()
    {
        _window = 0.0;
        _frames = 0;
        _frameSum = _frameWorst = 0.0;
        _scriptSum = _physicsSum = _renderCpuSum = _gpuSum = 0.0;
        _drawSum = _triangleSum = 0.0;
    }

    private string Compose()
    {
        int n = Math.Max(_frames, 1);
        double frame = _frameSum / n;
        double script = _scriptSum / n;
        double physics = _physicsSum / n;
        double renderCpu = _renderCpuSum / n;
        double gpu = _gpuSum / n;
        double cpu = script + physics + renderCpu;
        double draws = _drawSum / n;
        double triangles = _triangleSum / n / 1000.0;

        // The process's CPU time over the window, as a share of every core.
        _process.Refresh();
        TimeSpan cpuTime = _process.TotalProcessorTime;
        double cpuShare = (cpuTime - _cpuTime).TotalSeconds / Math.Max(_window, 0.001) / System.Environment.ProcessorCount * 100.0;
        _cpuTime = cpuTime;
        int gcCount = GcCount();
        int collections = gcCount - _gcCount;
        _gcCount = gcCount;

        double gpuLoad = Math.Min(gpu / Math.Max(frame, 0.001) * 100.0, 100.0);
        double vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0;
        double textures = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0;
        double engine = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;
        double managed = GC.GetTotalMemory(false) / 1048576.0;
        double process = _process.WorkingSet64 / 1048576.0;
        Godot.Collections.Dictionary memory = OS.GetMemoryInfo();
        double total = memory["physical"].AsInt64() / 1048576.0;
        // Windows can count the page file in "available": fall back to free physical memory then.
        double available = memory["available"].AsInt64() / 1048576.0;
        if (available <= 0.0 || available > total)
        {
            available = memory["free"].AsInt64() / 1048576.0;
        }
        double bodies = Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects);

        string reason = Reason(frame, cpu, script, physics, renderCpu, gpu, draws, triangles, bodies, collections);
        return
            $"Frame      {frame:0.0} ms  ({1000.0 / Math.Max(frame, 0.001):0} fps, worst {_frameWorst:0.0} ms)\n" +
            $"CPU        {cpu:0.0} ms main thread: scripts {script:0.0} · physics {physics:0.0} · render {renderCpu:0.0}\n" +
            $"           process {cpuShare:0}% of {System.Environment.ProcessorCount} cores\n" +
            $"GPU        {gpu:0.0} ms  (load ~{gpuLoad:0}%) · VRAM {vram:0} MB (textures {textures:0}) · {RenderingServer.GetVideoAdapterName()}\n" +
            $"Draw       {draws:0} calls · {triangles:0}k triangles · {bodies:0} active bodies\n" +
            $"Memory     engine {engine:0} MB · .NET {managed:0} MB · process {process:0} MB · system {total - available:0}/{total:0} MB\n" +
            $"Reason     {reason}";
    }

    /// <summary>Why the frame takes as long as it does, in a few words.</summary>
    private string Reason(double frame, double cpu, double script, double physics, double renderCpu, double gpu,
        double draws, double triangles, double bodies, int collections)
    {
        string hitch = "";
        if (_frameWorst > frame * HitchFactor && _frameWorst > 25.0)
        {
            hitch = $" · hitch {_frameWorst:0} ms";
            if (collections > 0)
            {
                hitch += $" ({collections} .NET garbage collection{Plural(collections)})";
            }
        }
        if (gpu < frame * IdleShare && cpu < frame * IdleShare)
        {
            return "frame cap (VSync or Max FPS): CPU and GPU both wait" + hitch;
        }
        // Godot's process time also holds the wait for the graphics card, so a busy GPU makes the
        // scripts look slow: a GPU busy for most of the frame decides first, whatever the CPU says.
        if (gpu >= frame * BoundShare)
        {
            string what = "pixels: resolution, shadows, fog, post effects";
            if (triangles > ManyTrianglesThousands)
            {
                what = $"geometry: {triangles:0}k triangles";
            }
            else if (draws > ManyDrawCalls)
            {
                what = $"{draws:0} draw calls";
            }
            return $"GPU-bound: {what}" + hitch;
        }
        string part = $"scripts (_Process, {script:0.0} ms)";
        if (physics >= script && physics >= renderCpu)
        {
            part = $"physics ({physics:0.0} ms";
            if (bodies > ManyPhysicsBodies)
            {
                part += $", {bodies:0} active bodies";
            }
            part += ")";
        }
        else if (renderCpu >= script)
        {
            part = $"render setup ({renderCpu:0.0} ms, {draws:0} draw calls)";
        }
        return $"CPU-bound: {part}" + hitch;
    }

    private static int GcCount()
    {
        return GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
    }

    private static string Plural(int count)
    {
        if (count == 1)
        {
            return "";
        }
        return "s";
    }
}
