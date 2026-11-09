using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// Covers the screen while a mountain is built, so the run starts with every tree, bush and stone around
/// the carrier already in place instead of popping in. The terrain streamer builds the ground at once;
/// this keeps the streamer running (even under the paused start menu), gives the chunks a large plant
/// budget per frame, shows the progress, and lifts once no loaded chunk is still adding plants. It
/// comes back for each new mountain (a new run). The world is paused underneath unless the start
/// menu is managing the pause itself. Skipped in headless runs (tests) and with `-- --no-loading`.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public string Heading { get; set; } = "Raising the mountain";
    [Export] public string Hint { get; set; } = "Trees, stones and paths are taking their places.";
    [Export] public Color Background { get; set; } = new(0.05f, 0.04f, 0.03f, 1.0f);
    [Export] public Color TextColour { get; set; } = new(0.91f, 0.86f, 0.76f);
    [Export] public Color BarColour { get; set; } = new(0.78f, 0.62f, 0.36f);
    /// <summary>Shortest time it stays up, so it never flickers, in seconds.</summary>
    [Export] public float MinimumSeconds { get; set; } = 0.4f;
    [Export] public float FadeSeconds { get; set; } = 0.45f;
    /// <summary>Longest it waits for the plants before letting the player in anyway, in seconds.</summary>
    [Export] public float TimeoutSeconds { get; set; } = 20.0f;

    public bool Active { get; private set; }
    private Control? _root;
    private ColorRect? _fill;
    private Label? _percent;
    private int _total = 1;
    private float _shown;
    private float _progress;
    private bool _pausedByUs;
    private ProcessModeEnum _streamerMode = ProcessModeEnum.Inherit;
    private Tween? _fade;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 90;
        if (DisplayServer.GetName() == "headless" || System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-loading") >= 0 || Streamer == null)
        {
            QueueFree();
            return;
        }
        Build();
        Streamer.Reseeded += Begin;
        Begin();
    }

    public override void _ExitTree()
    {
        if (Streamer != null && IsInstanceValid(Streamer))
        {
            Streamer.Reseeded -= Begin;
        }
        if (Active)
        {
            Finish(false);
        }
    }

    private void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        ColorRect background = new() { Color = Background, MouseFilter = Control.MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(background);

        VBoxContainer column = new() { Alignment = BoxContainer.AlignmentMode.Center };
        column.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 14);
        _root.AddChild(column);
        Label heading = UiStyle.MakeLabel(Heading, 40, true, TextColour);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(heading);
        Label hint = UiStyle.MakeLabel(Hint, 18, false, TextColour with { A = 0.7f });
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(hint);

        CenterContainer centre = new();
        column.AddChild(centre);
        ColorRect track = new() { Color = TextColour with { A = 0.15f }, CustomMinimumSize = new Vector2(360, 6) };
        centre.AddChild(track);
        _fill = new ColorRect { Color = BarColour, Size = new Vector2(0, 6) };
        track.AddChild(_fill);
        _percent = UiStyle.MakeLabel("", 16, false, TextColour with { A = 0.6f });
        _percent.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_percent);
    }

    /// <summary>Shows the screen for the mountain the streamer has just built.</summary>
    private void Begin()
    {
        if (Streamer == null || _root == null)
        {
            return;
        }
        _fade?.Kill();
        _root.Modulate = Colors.White;
        Visible = true;
        _shown = 0.0f;
        _progress = 0.0f;
        _total = Mathf.Max(Streamer.PlantsPendingCount, 1);
        if (!Active)
        {
            _streamerMode = Streamer.ProcessMode;
        }
        Streamer.ProcessMode = ProcessModeEnum.Always;
        TerrainStreamer.Loading = true;
        Active = true;
        // The start menu pauses the world itself once the carrier has landed; elsewhere hold it here.
        if (!GetTree().Paused && !StartMenuWaiting())
        {
            GetTree().Paused = true;
            _pausedByUs = true;
        }
        UpdateBar();
    }

    private bool StartMenuWaiting()
    {
        foreach (Node node in GetParent().GetChildren())
        {
            if (node is StartScreen start && !start.Started)
            {
                return true;
            }
        }
        return false;
    }

    public override void _Process(double delta)
    {
        if (!Active || Streamer == null)
        {
            return;
        }
        _shown += (float)delta;
        int pending = Streamer.PlantsPendingCount;
        _total = Mathf.Max(_total, pending);
        _progress = Mathf.Max(_progress, 1.0f - pending / (float)_total);
        UpdateBar();
        if ((pending == 0 && _shown >= MinimumSeconds) || _shown >= TimeoutSeconds)
        {
            Finish(true);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Nothing reaches the menu or the carrier while the mountain is still being raised.
        if (Active)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void UpdateBar()
    {
        if (_fill?.GetParent() is Control track)
        {
            _fill.Size = new Vector2(track.Size.X * _progress, track.Size.Y);
        }
        if (_percent != null)
        {
            _percent.Text = Mathf.RoundToInt(_progress * 100.0f) + "%";
        }
    }

    private void Finish(bool fade)
    {
        Active = false;
        TerrainStreamer.Loading = false;
        GD.Print($"Loading screen: mountain ready after {_shown:0.0} s");
        if (Streamer != null && IsInstanceValid(Streamer))
        {
            Streamer.ProcessMode = _streamerMode;
        }
        if (_pausedByUs && IsInsideTree())
        {
            GetTree().Paused = false;
        }
        _pausedByUs = false;
        if (!fade || _root == null)
        {
            Visible = false;
            return;
        }
        _fade = CreateTween();
        _fade.TweenProperty(_root, "modulate:a", 0.0f, FadeSeconds);
        _fade.TweenCallback(Callable.From(() => Visible = false));
    }
}
