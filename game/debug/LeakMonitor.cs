using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Hunts memory leaks (`-- --leak-log`, optionally `--leak-every=10`): every few seconds it writes the
/// renderer's video memory (total, textures, buffers), the counts of objects, resources, nodes and
/// orphan nodes, and the frame time to user://leak_log.csv, and keeps a count of every node class in
/// the scene. On quit (or F8) it prints what grew most since the start, so a slow GPU leak shows as a
/// climbing column and the node classes that pile up. Off unless asked for; never draws anything.
/// </summary>
public partial class LeakMonitor : Node
{
    [Export] public string Arg { get; set; } = "--leak-log";
    [Export] public string EveryArg { get; set; } = "--leak-every=";
    /// <summary>Seconds between samples.</summary>
    [Export] public float Every { get; set; } = 10.0f;
    [Export] public string LogPath { get; set; } = "user://leak_log.csv";
    /// <summary>How many of the fastest-growing node classes the report lists.</summary>
    [Export] public int ReportTop { get; set; } = 12;
    [Export] public Key ReportKey { get; set; } = Key.F8;

    public bool Active { get; private set; }
    public int Samples { get; private set; }

    private float _timer;
    private double _started;
    private FileAccess? _file;
    private Dictionary<string, int> _firstClasses = new();
    private Dictionary<string, int> _lastClasses = new();
    private double[] _first = System.Array.Empty<double>();
    private double[] _last = System.Array.Empty<double>();

    private static readonly (string Name, Performance.Monitor Monitor, double Scale)[] Columns =
    {
        ("video_mb", Performance.Monitor.RenderVideoMemUsed, 1.0 / (1024 * 1024)),
        ("texture_mb", Performance.Monitor.RenderTextureMemUsed, 1.0 / (1024 * 1024)),
        ("buffer_mb", Performance.Monitor.RenderBufferMemUsed, 1.0 / (1024 * 1024)),
        ("static_mb", Performance.Monitor.MemoryStatic, 1.0 / (1024 * 1024)),
        ("objects", Performance.Monitor.ObjectCount, 1.0),
        ("resources", Performance.Monitor.ObjectResourceCount, 1.0),
        ("nodes", Performance.Monitor.ObjectNodeCount, 1.0),
        ("orphan_nodes", Performance.Monitor.ObjectOrphanNodeCount, 1.0),
        ("draw_calls", Performance.Monitor.RenderTotalDrawCallsInFrame, 1.0),
        ("frame_ms", Performance.Monitor.TimeProcess, 1000.0),
    };

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string[] args = OS.GetCmdlineUserArgs();
        Active = System.Array.IndexOf(args, Arg) >= 0;
        foreach (string arg in args)
        {
            if (arg.StartsWith(EveryArg) && float.TryParse(arg.Substring(EveryArg.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float every))
            {
                Every = Mathf.Max(1.0f, every);
                Active = true;
            }
        }
        if (!Active)
        {
            SetProcess(false);
            SetProcessUnhandledInput(false);
            return;
        }
        _file = FileAccess.Open(LogPath, FileAccess.ModeFlags.Write);
        _file?.StoreLine("seconds," + string.Join(",", Columns.Select(c => c.Name)) + ",top_classes");
        _started = Time.GetTicksMsec() / 1000.0;
        _timer = 2.0f;
        GD.Print($"LeakMonitor: logging every {Every:0} s to {ProjectSettings.GlobalizePath(LogPath)} (F8 prints the report).");
    }

    public override void _Process(double delta)
    {
        _timer -= (float)delta;
        if (_timer > 0.0f)
        {
            return;
        }
        _timer = Every;
        Sample();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == ReportKey)
        {
            GD.Print(Report());
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            if (Active && Samples > 1)
            {
                GD.Print(Report());
            }
            _file?.Close();
            _file = null;
        }
    }

    /// <summary>Takes one sample now (also for tests).</summary>
    public void Sample()
    {
        double[] values = new double[Columns.Length];
        for (int i = 0; i < Columns.Length; i++)
        {
            values[i] = Performance.GetMonitor(Columns[i].Monitor) * Columns[i].Scale;
        }
        Dictionary<string, int> classes = new();
        Count(GetTree().Root, classes);
        if (Samples == 0)
        {
            _first = values;
            _firstClasses = classes;
        }
        _last = values;
        _lastClasses = classes;
        Samples++;
        double seconds = Time.GetTicksMsec() / 1000.0 - _started;
        string top = string.Join(" ", Growth().Take(5).Select(g => $"{g.Class}:{g.Grew:+0;-0}"));
        string row = seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ","
            + string.Join(",", values.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))) + "," + top;
        _file?.StoreLine(row);
        _file?.Flush();
    }

    /// <summary>What grew since the first sample: the monitors, then the node classes that piled up most.</summary>
    public string Report()
    {
        if (Samples < 2)
        {
            return "LeakMonitor: not enough samples yet.";
        }
        System.Text.StringBuilder text = new();
        text.AppendLine($"LeakMonitor report ({Samples} samples, {Time.GetTicksMsec() / 1000.0 - _started:0} s):");
        for (int i = 0; i < Columns.Length; i++)
        {
            text.AppendLine($"  {Columns[i].Name,-13} {_first[i],10:0.##} -> {_last[i],10:0.##}  ({_last[i] - _first[i]:+0.##;-0.##})");
        }
        text.AppendLine("  node classes that grew most:");
        foreach ((string name, int grew, int now) in Growth().Take(ReportTop))
        {
            text.AppendLine($"    {name,-28} {grew:+0;-0} (now {now})");
        }
        return text.ToString();
    }

    private IEnumerable<(string Class, int Grew, int Now)> Growth()
    {
        return _lastClasses
            .Select(pair =>
            {
                _firstClasses.TryGetValue(pair.Key, out int before);
                return (pair.Key, pair.Value - before, pair.Value);
            })
            .Where(g => g.Item2 > 0)
            .OrderByDescending(g => g.Item2);
    }

    private static void Count(Node node, Dictionary<string, int> classes)
    {
        string name = node.GetClass();
        classes.TryGetValue(name, out int count);
        classes[name] = count + 1;
        foreach (Node child in node.GetChildren())
        {
            Count(child, classes);
        }
    }
}
