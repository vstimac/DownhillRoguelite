using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// The wind the grass and trees feel. From the weather (GameEvents.WeatherUpdated: wind with its
/// gusts) it sets the foliage shader globals each frame: the wind's direction, its strength (0 calm,
/// 1 a full gale, never below a light breeze, eased so a gust change doesn't snap the meadow) and how
/// far the gust waves have drifted downwind; and from the carrier (LocomotionUpdated) where the grass
/// parts round his legs. terrain/forest_grass.gdshader and terrain/foliage_tree.gdshader read them.
/// TerrainStreamer adds one (debug/FoliageLab.tscn shows it). `-- --gale` blows a full gale and
/// `-- --wind=0.6` a fixed strength (renders).
/// </summary>
public partial class FoliageWind : Node
{
    /// <summary>Even on a still day the grass breathes this much (share of a full wind).</summary>
    [Export] public float CalmBreeze { get; set; } = 0.18f;
    /// <summary>A wind this fast (m/s) bends the foliage fully.</summary>
    [Export] public float FullWindSpeed { get; set; } = 14.0f;
    /// <summary>Seconds for the foliage to follow a change of wind.</summary>
    [Export] public float Easing { get; set; } = 1.2f;
    /// <summary>The gust waves drift downwind this fast (m/s), plus this much per unit of strength.</summary>
    [Export] public float WaveDrift { get; set; } = 1.5f;
    [Export] public float WaveDriftPerStrength { get; set; } = 7.0f;
    /// <summary>The grass parts this far round the carrier (m).</summary>
    [Export] public float PushRadius { get; set; } = 0.7f;
    /// <summary>The wind before the weather says anything (and the direction kept when it falls still).</summary>
    [Export] public Vector2 StartDirection { get; set; } = new(0.8f, 0.6f);

    /// <summary>Debug and renders: a fixed strength instead of the weather's (negative: the weather's).</summary>
    public float Override { get; set; } = -1.0f;
    /// <summary>Debug: where the grass parts (else the carrier's feet).</summary>
    public Vector3 Pusher
    {
        get => _pusher;
        set => _pusher = value;
    }
    public float Strength => _strength;
    public Vector2 Direction => _direction;
    public Vector2 Scroll => _scroll;

    private float _strength;
    private float _targetStrength;
    private Vector2 _direction;
    private Vector2 _targetDirection;
    private Vector2 _scroll;
    private Vector3 _pusher = new(0.0f, -1000.0f, 0.0f);
    private bool _gale;

    public override void _Ready()
    {
        _direction = StartDirection.Normalized();
        _targetDirection = _direction;
        _strength = CalmBreeze;
        _targetStrength = CalmBreeze;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--gale")
            {
                _gale = true;
            }
            if (arg.StartsWith("--wind=") && float.TryParse(arg.Substring(7), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wind))
            {
                Override = wind;
            }
        }
        GameEvents.WeatherUpdated += OnWeather;
        GameEvents.LocomotionUpdated += OnLocomotion;
        OnWeather(GameEvents.CurrentWeather);
    }

    public override void _ExitTree()
    {
        GameEvents.WeatherUpdated -= OnWeather;
        GameEvents.LocomotionUpdated -= OnLocomotion;
    }

    private void OnWeather(WeatherSnapshot weather)
    {
        Vector2 wind = new(weather.Wind.X, weather.Wind.Z);
        float speed = wind.Length();
        _targetStrength = Mathf.Max(CalmBreeze, speed / Mathf.Max(FullWindSpeed, 0.1f));
        if (speed > 0.2f)
        {
            _targetDirection = wind / speed;
        }
        if (_gale)
        {
            _targetStrength = 1.0f;
        }
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _pusher = snapshot.Position;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float follow = 1.0f - Mathf.Exp(-dt / Mathf.Max(Easing, 0.01f));
        if (Override >= 0.0f)
        {
            _targetStrength = Override;
        }
        _strength = Mathf.Lerp(_strength, _targetStrength, follow);
        Vector2 direction = _direction.Lerp(_targetDirection, follow);
        if (direction.LengthSquared() > 0.0001f)
        {
            _direction = direction.Normalized();
        }
        _scroll += _direction * (WaveDrift + WaveDriftPerStrength * _strength) * dt;
        // Keep the drift small (the noise repeats every few hundred metres anyway), so precision holds.
        if (_scroll.Length() > 5000.0f)
        {
            _scroll = Vector2.Zero;
        }
        RenderingServer.GlobalShaderParameterSet("wind_direction", _direction);
        RenderingServer.GlobalShaderParameterSet("wind_strength", _strength);
        RenderingServer.GlobalShaderParameterSet("wind_scroll", _scroll);
        RenderingServer.GlobalShaderParameterSet("foliage_pusher", new Vector4(_pusher.X, _pusher.Y, _pusher.Z, PushRadius));
    }
}
