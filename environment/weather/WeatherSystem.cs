using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Publishes the weather where the carrier is, every physics frame: the run's seed, the circle the
/// carrier has descended into (RunState.Depth) and the clock decide the front (WeatherForecast);
/// gusts come on top in real time. Other systems react through GameEvents.CurrentWeather: the
/// day/night cycle and the particles show it, the controller loses grip, balance feels the wind,
/// needs feel the chill and the heat. A debug override can force a front.
/// </summary>
public partial class WeatherSystem : Node
{
    [Export] public WeatherTable? Table { get; set; }
    /// <summary>Intensity of a forced front (debug).</summary>
    [Export] public float ForcedIntensity { get; set; } = 1.0f;

    /// <summary>A front kind forced by debug controls or tests; null follows the forecast.</summary>
    public WeatherKind? Forced { get; set; }
    /// <summary>Wind direction of a forced front, radians around Y.</summary>
    public float ForcedWindAngle { get; set; }
    /// <summary>Gusts on (off makes forced wind steady for tests).</summary>
    public bool Gusts { get; set; } = true;
    public WeatherSnapshot Current { get; private set; } = WeatherSnapshot.Calm;
    public WeatherForecast? Forecast => _forecast;

    private RunState? _run;
    private WorldClock? _clock;
    private WeatherForecast? _forecast;
    private float _seconds;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Table ??= new WeatherTable();
    }

    public override void _ExitTree()
    {
        GameEvents.PublishWeather(WeatherSnapshot.Calm);
    }

    public override void _PhysicsProcess(double delta)
    {
        _seconds += (float)delta;
        Current = Sample();
        GameEvents.PublishWeather(Current);
    }

    /// <summary>The weather right now, gusts included.</summary>
    public WeatherSnapshot Sample()
    {
        if (_run == null || _clock == null || Table == null)
        {
            return WeatherSnapshot.Calm;
        }
        if (_forecast == null || _forecast.Seed != _run.Seed || _forecast.Table != Table)
        {
            _forecast = new WeatherForecast(Table, _run.Seed);
        }
        WeatherFront? forced = null;
        if (Forced != null)
        {
            forced = new WeatherFront(Forced.Value, ForcedIntensity, ForcedWindAngle);
        }
        float darkness = 1.0f - _clock.Daylight;
        WeatherSnapshot weather = _forecast.Sample(_run.Depth, _clock.TotalHours, darkness, _run.Circles, forced);
        if (Gusts)
        {
            weather = _forecast.WithGusts(weather, _seconds);
        }
        return weather;
    }

    /// <summary>Debug: forecast, then each kind forced in turn, then back to the forecast.</summary>
    public void CycleForced()
    {
        if (Forced == null)
        {
            Forced = WeatherKind.Clear;
        }
        else if (Forced.Value == WeatherKind.Heat)
        {
            Forced = null;
        }
        else
        {
            Forced = Forced.Value + 1;
        }
        string name = "forecast";
        if (Forced != null)
        {
            name = Forced.Value.ToString();
        }
        GD.Print($"Weather: {name}");
    }
}
