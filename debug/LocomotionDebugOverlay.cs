using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug overlay for locomotion: arrows for velocity, ground normal, fall line and input at the
/// feet, plus a text readout and the performance readout (CPU, GPU, memory and why a frame is
/// slow). Reads LocomotionSnapshot events only. Hidden with F3.
/// </summary>
public partial class LocomotionDebugOverlay : CanvasLayer
{
    /// <summary>Velocity arrow length per m/s.</summary>
    [Export] public float VelocityScale { get; set; } = 0.5f;
    [Export] public float NormalLength { get; set; } = 1.0f;
    [Export] public float FallLineLength { get; set; } = 1.0f;
    [Export] public float InputLength { get; set; } = 0.8f;
    /// <summary>Height above the feet where arrows start, so they aren't hidden in the ground.</summary>
    [Export] public float ArrowLift { get; set; } = 0.05f;
    [Export] public int FontSize { get; set; } = 18;
    [Export] public Vector2 TextPosition { get; set; } = new(16.0f, 16.0f);
    /// <summary>The performance box hangs from the right edge, this far in, in px...</summary>
    [Export] public float PerformanceMargin { get; set; } = 16.0f;
    /// <summary>...with its top this far down the screen (0–1), below the HUD notes and the balance readout.</summary>
    [Export] public float PerformanceTop { get; set; } = 0.42f;
    [Export] public int PerformanceFontSize { get; set; } = 15;
    [Export] public Color PerformanceBackground { get; set; } = new(0.0f, 0.0f, 0.0f, 0.62f);

    [Export] public Color VelocityColor { get; set; } = new(0.3f, 1.0f, 0.3f);
    [Export] public Color NormalColor { get; set; } = new(0.3f, 0.6f, 1.0f);
    [Export] public Color FallLineColor { get; set; } = new(1.0f, 0.6f, 0.1f);
    [Export] public Color InputColor { get; set; } = new(1.0f, 1.0f, 1.0f);

    private Label? _label;
    private PerformanceReadout? _performance;
    private Label? _performanceLabel;

    public override void _Ready()
    {
        _label = new Label { Position = TextPosition };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", FontSize / 4);
        AddChild(_label);
        _performance = new PerformanceReadout { Name = "Performance" };
        AddChild(_performance);
        _performanceLabel = new Label
        {
            Visible = false,
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            AnchorTop = PerformanceTop,
            AnchorBottom = PerformanceTop,
            OffsetLeft = -PerformanceMargin,
            OffsetRight = -PerformanceMargin,
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        _performanceLabel.AddThemeFontSizeOverride("font_size", PerformanceFontSize);
        _performanceLabel.AddThemeColorOverride("font_color", Colors.White);
        _performanceLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = PerformanceBackground,
            ContentMarginLeft = 10.0f,
            ContentMarginRight = 10.0f,
            ContentMarginTop = 6.0f,
            ContentMarginBottom = 6.0f,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });
        AddChild(_performanceLabel);

        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        if (_label == null)
        {
            return;
        }

        // Text would cover the packing view.
        bool show = DebugDraw3D.Enabled && !GameEvents.IsPacking;
        _label.Visible = show;
        if (_performance != null)
        {
            _performance.Active = show;
        }
        if (_performanceLabel != null)
        {
            _performanceLabel.Text = _performance?.Text ?? "";
            _performanceLabel.Visible = show && _performanceLabel.Text.Length > 0;
        }
        if (!show)
        {
            return;
        }

        Vector3 origin = snapshot.Position + Vector3.Up * ArrowLift;
        DebugDraw3D.Arrow(origin, origin + snapshot.Velocity * VelocityScale, VelocityColor);
        DebugDraw3D.Arrow(origin, origin + snapshot.GroundNormal * NormalLength, NormalColor);
        DebugDraw3D.Arrow(origin, origin + snapshot.FallLine * FallLineLength, FallLineColor);
        DebugDraw3D.Arrow(origin, origin + snapshot.InputDirection * InputLength, InputColor);

        Vector3 horizontal = new(snapshot.Velocity.X, 0.0f, snapshot.Velocity.Z);
        float speed = horizontal.Length();
        float grade = 0.0f;
        if (speed > Mathf.Epsilon)
        {
            grade = (horizontal / speed).Dot(snapshot.FallLine) * Mathf.Sin(Mathf.DegToRad(snapshot.SlopeDegrees));
        }
        Vector3 horizontalAcceleration = new(snapshot.Acceleration.X, 0.0f, snapshot.Acceleration.Z);

        _label.Text =
            $"State      {snapshot.State}\n" +
            $"Speed      {speed:0.00} m/s  ({speed * 3.6f:0.0} km/h)\n" +
            $"Vertical   {snapshot.Velocity.Y:+0.00;-0.00} m/s\n" +
            $"Slope      {snapshot.SlopeDegrees:0.0}°\n" +
            $"Surface    {snapshot.Surface}  (grip {snapshot.Grip:0.00})\n" +
            $"Grade      {grade:+0.00;-0.00}  (+ = downhill)\n" +
            $"Accel      {horizontalAcceleration.Length():0.0} m/s²\n" +
            $"FPS        {Engine.GetFramesPerSecond():0}\n" +
            "\n" +
            "green velocity · blue normal · orange fall line · white input\n" +
            "C brake · Space jump · Shift sprint · F1 fly camera · F3 overlay · R give up · Esc pause";
    }
}
