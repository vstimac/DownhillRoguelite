using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug overlay for balance: the support area between the feet (coloured by state), the combined
/// centre of mass and its ground projection, the tilt, and a text readout of the state, imbalance
/// and each torque. Reads BalanceSnapshot events only. Hidden with F3.
/// </summary>
public partial class BalanceDebugOverlay : CanvasLayer
{
    [Export] public Color StableColor { get; set; } = new(0.3f, 1.0f, 0.4f);
    [Export] public Color WobblingColor { get; set; } = new(1.0f, 0.85f, 0.2f);
    [Export] public Color StumblingColor { get; set; } = new(1.0f, 0.45f, 0.1f);
    [Export] public Color FallenColor { get; set; } = new(1.0f, 0.15f, 0.15f);
    [Export] public Color CenterOfMassColor { get; set; } = new(1.0f, 0.3f, 1.0f);
    /// <summary>Height above the ground of the drawn support area, so it isn't hidden in the surface.</summary>
    [Export] public float GroundLift { get; set; } = 0.03f;
    [Export] public float MarkerSize { get; set; } = 0.08f;
    [Export] public int FontSize { get; set; } = 18;
    /// <summary>Distance of the text from the top-right corner, in pixels.</summary>
    [Export] public Vector2 TextMargin { get; set; } = new(16.0f, 16.0f);
    [Export] public float TextWidth { get; set; } = 520.0f;

    private Label? _label;

    public override void _Ready()
    {
        _label = new Label { CustomMinimumSize = new Vector2(TextWidth, 0.0f), HorizontalAlignment = HorizontalAlignment.Right };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", FontSize / 4);
        AddChild(_label);

        GameEvents.BalanceUpdated += OnBalanceUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.BalanceUpdated -= OnBalanceUpdated;
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        if (_label == null)
        {
            return;
        }

        // Text would cover the packing view.
        bool show = DebugDraw3D.Enabled && !GameEvents.IsPacking;
        _label.Visible = show;
        if (!show)
        {
            return;
        }

        Color stateColor = StateColor(snapshot.State);
        Vector3 lift = Vector3.Up * GroundLift;
        Vector3 halfRight = snapshot.Right * snapshot.SupportHalfExtents.X;
        Vector3 halfForward = snapshot.Forward * snapshot.SupportHalfExtents.Y;
        Vector3 center = snapshot.SupportCenter + lift;
        Vector3 frontLeft = center + halfForward - halfRight;
        Vector3 frontRight = center + halfForward + halfRight;
        Vector3 backRight = center - halfForward + halfRight;
        Vector3 backLeft = center - halfForward - halfRight;
        DebugDraw3D.Line(frontLeft, frontRight, stateColor);
        DebugDraw3D.Line(frontRight, backRight, stateColor);
        DebugDraw3D.Line(backRight, backLeft, stateColor);
        DebugDraw3D.Line(backLeft, frontLeft, stateColor);

        Vector3 comGround = snapshot.CenterOfMassGround + lift;
        Vector3 comPoint = snapshot.CenterOfMassGround + Vector3.Up * snapshot.CenterOfMassHeight;
        DebugDraw3D.Cross(comGround, CenterOfMassColor, MarkerSize);
        DebugDraw3D.Cross(comPoint, CenterOfMassColor, MarkerSize);
        DebugDraw3D.Line(comPoint, comGround, CenterOfMassColor);
        DebugDraw3D.Arrow(center, comGround, stateColor, MarkerSize);

        string brace = "";
        if (snapshot.Bracing)
        {
            brace = "  BRACING";
        }

        _label.Text =
            $"Balance    {snapshot.State}  ({snapshot.StateTime:0.0} s)\n" +
            $"Load       {snapshot.LoadName}  ({snapshot.LoadMass:0} kg)\n" +
            $"COM height {snapshot.CenterOfMassHeight:0.00} m\n" +
            $"Imbalance  roll {snapshot.Imbalance.X:+0.00;-0.00}  pitch {snapshot.Imbalance.Y:+0.00;-0.00}  (1 = edge)\n" +
            $"Tilt       roll {Mathf.RadToDeg(snapshot.Tilt.X):+0.0;-0.0}°  pitch {Mathf.RadToDeg(snapshot.Tilt.Y):+0.0;-0.0}°\n" +
            $"Lean       {snapshot.LeanInput:+0.00;-0.00}{brace}\n" +
            $"Torque     gravity {Format(snapshot.GravityTorque)}\n" +
            $"           inertial {Format(snapshot.InertialTorque)}\n" +
            $"           slope {Format(snapshot.SlopeTorque)}\n" +
            $"           correction {Format(snapshot.CorrectionTorque)}\n" +
            "\nLMB / RMB lean · both brace · L cycle load";

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _label.Position = new Vector2(viewport.X - TextWidth - TextMargin.X, TextMargin.Y);
    }

    private Color StateColor(BalanceState state)
    {
        if (state == BalanceState.Wobbling)
        {
            return WobblingColor;
        }
        if (state == BalanceState.Stumbling)
        {
            return StumblingColor;
        }
        if (state == BalanceState.Fallen)
        {
            return FallenColor;
        }
        return StableColor;
    }

    private static string Format(Vector2 torque)
    {
        return $"{torque.X:+0;-0} / {torque.Y:+0;-0} N·m";
    }
}
