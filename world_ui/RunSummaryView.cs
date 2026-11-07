using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// When a run ends (GameEvents.RunEnded) the journey's summary is written in the air in front of
/// the camera, over the fallen carrier, until the next run begins: how deep, how many stations,
/// which circle, what killed the carrier and the best depth of any run. A 3D label in the world,
/// like the circle titles; it is not a menu.
/// </summary>
public partial class RunSummaryView : Node3D
{
    [Export] public float Distance { get; set; } = 8.0f;
    [Export] public float Height { get; set; } = 1.2f;
    [Export] public int FontSize { get; set; } = 110;
    [Export] public float PixelSize { get; set; } = 0.006f;
    [Export] public Color TextColor { get; set; } = new(0.95f, 0.88f, 0.75f);
    [Export] public Color RecordColor { get; set; } = new(1.0f, 0.8f, 0.4f);
    [Export] public float FadeSeconds { get; set; } = 1.5f;

    public bool Showing => _title != null && _title.Visible;
    public string Text => (_title?.Text ?? "") + "\n" + (_details?.Text ?? "");

    private Label3D? _title;
    private Label3D? _details;
    private float _time;

    public override void _Ready()
    {
        TopLevel = true;
        _title = MakeLabel(FontSize, Vector3.Zero);
        _details = MakeLabel(FontSize / 3, Vector3.Down * (FontSize * PixelSize * 1.6f));
        GameEvents.RunEnded += OnRunEnded;
        GameEvents.RespawnRequested += HideSummary;
    }

    public override void _ExitTree()
    {
        GameEvents.RunEnded -= OnRunEnded;
        GameEvents.RespawnRequested -= HideSummary;
    }

    private Label3D MakeLabel(int size, Vector3 offset)
    {
        Label3D label = new()
        {
            FontSize = size,
            PixelSize = PixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            OutlineSize = size / 10,
            Position = offset,
            Visible = false,
        };
        AddChild(label);
        return label;
    }

    private void OnRunEnded(RunSummary summary)
    {
        if (_title == null || _details == null)
        {
            return;
        }
        _title.Text = "The descent ends";
        string stations = "no station reached";
        if (summary.Station == 1)
        {
            stations = "1 station";
        }
        else if (summary.Station > 1)
        {
            stations = $"{summary.Station} stations";
        }
        string record = $"best ever {summary.BestDepth} m";
        if (summary.NewRecord)
        {
            record = "a new record";
        }
        _details.Text = $"Run {summary.RunNumber} · {summary.Depth} m down · {stations} · day {summary.Day}\n" +
            $"{summary.CircleName} · taken by {summary.Cause} · {record}\n" +
            $"mountain {summary.Seed}\n\n" +
            "A new mountain rises...";
        _details.Modulate = new Color(TextColor, 0.0f);
        if (summary.NewRecord)
        {
            _details.Modulate = new Color(RecordColor, 0.0f);
        }
        _title.Visible = true;
        _details.Visible = true;
        _time = 0.0f;
    }

    private void HideSummary()
    {
        if (_title != null && _details != null)
        {
            _title.Visible = false;
            _details.Visible = false;
        }
    }

    public override void _Process(double delta)
    {
        if (_title == null || _details == null || !_title.Visible)
        {
            return;
        }
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            GlobalPosition = camera.GlobalPosition - camera.GlobalBasis.Z * Distance + camera.GlobalBasis.Y * Height;
        }
        _time += (float)delta;
        float alpha = Mathf.Clamp(_time / FadeSeconds, 0.0f, 1.0f);
        _title.Modulate = new Color(TextColor, alpha);
        Color details = _details.Modulate;
        _details.Modulate = new Color(details.R, details.G, details.B, alpha);
        _title.OutlineModulate = new Color(0.0f, 0.0f, 0.0f, 0.6f * alpha);
        _details.OutlineModulate = new Color(0.0f, 0.0f, 0.0f, 0.6f * alpha);
    }
}
