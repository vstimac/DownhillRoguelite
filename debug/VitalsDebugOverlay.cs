using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug health and stamina bars (bottom-left) with the last hit's size and surface. Reads
/// VitalsSnapshot events only. Hidden with F3 and while packing. Debug only: the final
/// presentation of vitals is still to be decided (no flat UI during runs).
/// </summary>
public partial class VitalsDebugOverlay : CanvasLayer
{
    [Export] public Vector2 BarSize { get; set; } = new(260.0f, 14.0f);
    [Export] public float BarGap { get; set; } = 26.0f;
    /// <summary>Distance of the bars from the bottom-left corner, in pixels.</summary>
    [Export] public Vector2 Margin { get; set; } = new(16.0f, 16.0f);
    [Export] public int FontSize { get; set; } = 16;
    [Export] public Color HealthColor { get; set; } = new(0.85f, 0.2f, 0.2f);
    [Export] public Color StaminaColor { get; set; } = new(0.3f, 0.75f, 1.0f);
    [Export] public Color ExhaustedColor { get; set; } = new(0.45f, 0.45f, 0.5f);
    [Export] public Color BackColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.5f);

    private ColorRect? _healthBack;
    private ColorRect? _healthFill;
    private ColorRect? _staminaBack;
    private ColorRect? _staminaFill;
    private Label? _label;

    public override void _Ready()
    {
        _healthBack = AddRect(BackColor);
        _healthFill = AddRect(HealthColor);
        _staminaBack = AddRect(BackColor);
        _staminaFill = AddRect(StaminaColor);
        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", FontSize / 4);
        AddChild(_label);
        GameEvents.VitalsUpdated += OnVitalsUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.VitalsUpdated -= OnVitalsUpdated;
    }

    private ColorRect AddRect(Color color)
    {
        ColorRect rect = new() { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(rect);
        return rect;
    }

    private void OnVitalsUpdated(VitalsSnapshot vitals)
    {
        if (_healthBack == null || _healthFill == null || _staminaBack == null || _staminaFill == null || _label == null)
        {
            return;
        }

        Visible = DebugDraw3D.Enabled && !GameEvents.IsPacking;
        if (!Visible)
        {
            return;
        }

        float bottom = GetViewport().GetVisibleRect().Size.Y - Margin.Y;
        Vector2 staminaPosition = new(Margin.X, bottom - BarSize.Y);
        Vector2 healthPosition = new(Margin.X, staminaPosition.Y - BarGap);
        PlaceBar(_healthBack, _healthFill, healthPosition, Fraction(vitals.Health, vitals.MaxHealth));
        PlaceBar(_staminaBack, _staminaFill, staminaPosition, Fraction(vitals.Stamina, vitals.MaxStamina));

        if (vitals.Exhausted)
        {
            _staminaFill.Color = ExhaustedColor;
        }
        else
        {
            _staminaFill.Color = StaminaColor;
        }

        string status = "";
        if (vitals.Dead)
        {
            status = "  DEAD";
        }
        else if (vitals.Sprinting)
        {
            status = "  sprinting";
        }
        else if (vitals.Exhausted)
        {
            status = "  exhausted";
        }

        string hit = "";
        if (vitals.LastDamage > 0.0f)
        {
            hit = $"   last hit {vitals.LastDamage:0.0} on {vitals.LastDamageSource}";
        }

        _label.Text = $"HP {vitals.Health:0}/{vitals.MaxHealth:0}{hit}\nStamina {vitals.Stamina:0}{status}";
        _label.Position = new Vector2(Margin.X + BarSize.X + 12.0f, healthPosition.Y - 4.0f);
    }

    private void PlaceBar(ColorRect back, ColorRect fill, Vector2 position, float fraction)
    {
        back.Position = position;
        back.Size = BarSize;
        fill.Position = position;
        fill.Size = new Vector2(BarSize.X * fraction, BarSize.Y);
    }

    private static float Fraction(float value, float max)
    {
        if (max <= 0.0f)
        {
            return 0.0f;
        }
        return Mathf.Clamp(value / max, 0.0f, 1.0f);
    }
}
