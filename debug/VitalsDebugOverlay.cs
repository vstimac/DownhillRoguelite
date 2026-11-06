using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug health, stamina, hunger and thirst bars (bottom-left) with the last hit's size and
/// surface, and the clock (day and time). Reads VitalsSnapshot events and the WorldClock
/// autoload. Hidden with F3 and while packing. Debug only: the final
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
    [Export] public Color HungerColor { get; set; } = new(0.9f, 0.65f, 0.25f);
    [Export] public Color ThirstColor { get; set; } = new(0.35f, 0.55f, 1.0f);

    private ColorRect? _healthBack;
    private ColorRect? _healthFill;
    private ColorRect? _staminaBack;
    private ColorRect? _staminaFill;
    private ColorRect? _hungerBack;
    private ColorRect? _hungerFill;
    private ColorRect? _thirstBack;
    private ColorRect? _thirstFill;
    private Label? _label;
    private WorldClock? _clock;
    private RunState? _run;

    public override void _Ready()
    {
        _healthBack = AddRect(BackColor);
        _healthFill = AddRect(HealthColor);
        _staminaBack = AddRect(BackColor);
        _staminaFill = AddRect(StaminaColor);
        _hungerBack = AddRect(BackColor);
        _hungerFill = AddRect(HungerColor);
        _thirstBack = AddRect(BackColor);
        _thirstFill = AddRect(ThirstColor);
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _run = GetNodeOrNull<RunState>("/root/RunState");
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

        // The bars double as the demo's HUD, so F3 doesn't hide them.
        Visible = !GameEvents.IsPacking;
        if (!Visible)
        {
            return;
        }

        float bottom = GetViewport().GetVisibleRect().Size.Y - Margin.Y;
        Vector2 staminaPosition = new(Margin.X, bottom - BarSize.Y);
        Vector2 healthPosition = new(Margin.X, staminaPosition.Y - BarGap);
        PlaceBar(_healthBack, _healthFill, healthPosition, Fraction(vitals.Health, vitals.MaxHealth));
        PlaceBar(_staminaBack, _staminaFill, staminaPosition, Fraction(vitals.Stamina, vitals.MaxStamina));
        Vector2 thirstPosition = new(Margin.X, healthPosition.Y - BarGap);
        Vector2 hungerPosition = new(Margin.X, thirstPosition.Y - BarGap);
        if (_hungerBack != null && _hungerFill != null && _thirstBack != null && _thirstFill != null)
        {
            PlaceBar(_hungerBack, _hungerFill, hungerPosition, Fraction(vitals.Hunger, 100.0f));
            PlaceBar(_thirstBack, _thirstFill, thirstPosition, Fraction(vitals.Thirst, 100.0f));
        }

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

        string clock = "";
        if (_clock != null)
        {
            int hour = Mathf.FloorToInt(_clock.TimeOfDay);
            int minute = Mathf.FloorToInt((_clock.TimeOfDay - hour) * 60.0f);
            clock = $"Day {_clock.Day}  {hour:00}:{minute:00}    F eat · Q drink · E pick up\n";
        }
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        string circle = "";
        if (_run != null)
        {
            circle = _run.Circles.NameOf(_run.Circles.CircleAt(_run.Depth)) + "  ·  ";
        }
        clock += $"{circle}{weather.Kind}  fog {weather.Fog:0.0} rain {weather.Rain:0.0} snow {weather.Snow:0.0} heat {weather.Heat:0.0}  wind {weather.WindSpeed:0} m/s  grip ×{weather.GripFactor:0.00}  (F4)\n";
        _label.Text = $"{clock}Hunger {vitals.Hunger:0}  ·  Thirst {vitals.Thirst:0}\nHP {vitals.Health:0}/{vitals.MaxHealth:0}{hit}\nStamina {vitals.Stamina:0}{status}";
        _label.Position = new Vector2(Margin.X + BarSize.X + 12.0f, hungerPosition.Y - 4.0f);
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
