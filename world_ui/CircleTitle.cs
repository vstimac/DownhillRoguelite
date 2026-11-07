using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The circle's name written in the air ahead of the camera, fading in and out: at the start of
/// a run and whenever the carrier descends into a new circle ("I · Limbo"). A 3D label in the
/// world, not flat UI. The first one also says how to see the controls.
/// </summary>
public partial class CircleTitle : Node3D
{
    private static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    [Export] public float Distance { get; set; } = 9.0f;
    [Export] public float Height { get; set; } = 1.8f;
    [Export] public float FadeSeconds { get; set; } = 1.5f;
    [Export] public float HoldSeconds { get; set; } = 3.5f;
    [Export] public float StartDelay { get; set; } = 1.0f;
    [Export] public int FontSize { get; set; } = 160;
    [Export] public float PixelSize { get; set; } = 0.006f;
    [Export] public Color TextColor { get; set; } = new(0.95f, 0.88f, 0.75f);
    [Export] public string FirstHint { get; set; } = "Esc for controls";

    private Label3D? _title;
    private Label3D? _subtitle;
    private RunState? _run;
    private int _circle;
    private float _timer = -1.0f;
    private float _startTimer;
    private bool _started;

    public override void _Ready()
    {
        TopLevel = true;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _title = MakeLabel(FontSize, Vector3.Zero);
        _subtitle = MakeLabel(FontSize / 3, Vector3.Down * (FontSize * PixelSize * 0.75f));
        GameEvents.DepthChanged += OnDepthChanged;
        GameEvents.RespawnRequested += OnRespawn;
    }

    public override void _ExitTree()
    {
        GameEvents.DepthChanged -= OnDepthChanged;
        GameEvents.RespawnRequested -= OnRespawn;
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
            OutlineSize = size / 12,
            OutlineModulate = new Color(0.0f, 0.0f, 0.0f, 0.6f),
            Modulate = new Color(TextColor, 0.0f),
            Position = offset,
            Visible = false,
        };
        AddChild(label);
        return label;
    }

    public void Show(int circle, string hint)
    {
        if (_title == null || _subtitle == null || _run == null)
        {
            return;
        }
        string name = _run.Circles.NameOf(circle);
        string numeral = "";
        if (circle >= 0 && circle < Numerals.Length)
        {
            numeral = Numerals[circle] + " · ";
        }
        _title.Text = numeral + name;
        _subtitle.Text = hint;
        _timer = 0.0f;
        _title.Visible = true;
        _subtitle.Visible = hint.Length > 0;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!_started)
        {
            _startTimer += dt;
            if (_startTimer >= StartDelay)
            {
                _started = true;
                Show(0, FirstHint);
            }
        }
        if (_timer < 0.0f || _title == null || _subtitle == null)
        {
            return;
        }

        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            // In the camera's view: Distance ahead, Height above the centre of the screen.
            GlobalPosition = camera.GlobalPosition - camera.GlobalBasis.Z * Distance + camera.GlobalBasis.Y * Height;
        }

        _timer += dt;
        float total = FadeSeconds * 2.0f + HoldSeconds;
        float alpha = Mathf.Min(Mathf.Clamp(_timer / FadeSeconds, 0.0f, 1.0f), Mathf.Clamp((total - _timer) / FadeSeconds, 0.0f, 1.0f));
        _title.Modulate = new Color(TextColor, alpha);
        _subtitle.Modulate = new Color(TextColor, alpha * 0.8f);
        _title.OutlineModulate = new Color(0.0f, 0.0f, 0.0f, 0.6f * alpha);
        _subtitle.OutlineModulate = new Color(0.0f, 0.0f, 0.0f, 0.6f * alpha);
        if (_timer >= total)
        {
            _timer = -1.0f;
            _title.Visible = false;
            _subtitle.Visible = false;
        }
    }

    private void OnDepthChanged(float depth)
    {
        if (_run == null)
        {
            return;
        }
        int circle = _run.Circles.CircleAt(depth);
        if (circle != _circle)
        {
            _circle = circle;
            Show(circle, "");
        }
    }

    private void OnRespawn()
    {
        _circle = 0;
        Show(0, "");
    }
}
