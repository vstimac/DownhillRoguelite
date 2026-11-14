using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The parchment note top right: elevation and depth, the circle you are in, the weather, the day
/// and hour. Where you are, not where you're going: the way to the next fire and the open job are
/// the objectives note's (ObjectivePanel), so they aren't said twice. Reads RunState, WorldClock and
/// GameEvents; refreshes a few times a second. A line that changes gives a brief ink smudge so the
/// eye catches it.
/// </summary>
public partial class DescentPanel : ParchmentPanel
{
    public string Text => _body?.Text ?? "";
    /// <summary>The note never gets shorter than this, in px (its height in the HUD layout).</summary>
    [Export] public float MinHeight { get; set; } = 172.0f;

    private Label? _body;
    private RunState? _run;
    private WorldClock? _clock;
    private float _timer;
    private string _lastPlace = "";

    public override void _Ready()
    {
        Seed = 7.0f;
        base._Ready();
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _body = UiStyle.MakeLabel("", UiStyle.TextSize);
        _body.Position = new Vector2(20, 14);
        AddChild(_body);
    }

    public override void _Process(double delta)
    {
        _timer -= (float)delta;
        if (_timer > 0.0f || _body == null)
        {
            return;
        }
        _timer = 0.25f;
        int depth = 0;
        string circle = "";
        if (_run != null)
        {
            depth = _run.Depth;
            int index = _run.Circles.CircleAt(depth);
            circle = _run.Circles.NameOf(index);
            if (index < 9)
            {
                circle = $"{Numeral(index + 1)} · {circle}";
            }
        }
        float elevation = GameEvents.CarrierPosition.Y;
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        string sky = Weather(weather);
        string time = "";
        if (_clock != null)
        {
            // Real time, not the in-game clock: how long until it gets dark (or light).
            string until;
            if (_clock.IsNight)
            {
                float toDawn = Mathf.PosMod(_clock.SunriseHour - _clock.TimeOfDay, 24.0f);
                until = $"light in {TimeText.FromHours(toDawn, _clock.HoursPerSecond)}";
            }
            else
            {
                until = $"dark in {TimeText.FromHours(_clock.HoursUntilSunset, _clock.HoursPerSecond)}";
            }
            time = $"Day {_clock.Day} · {TimeText.PartOfDay(_clock.TimeOfDay)} · {until}";
        }
        _body.Text = $"{depth} m below the summit\nCircle {circle}\nWeather: {sky}\n{time}";
        // The note grows with its lines (a long weather or time line wraps), so none falls off the paper.
        float needed = _body.Position.Y + _body.GetMinimumSize().Y + 16.0f;
        if (!Mathf.IsEqualApprox(Size.Y, Mathf.Max(needed, MinHeight)))
        {
            Size = new Vector2(Size.X, Mathf.Max(needed, MinHeight));
        }
        if (circle != _lastPlace)
        {
            _lastPlace = circle;
            _body.Modulate = new Color(0.6f, 0.4f, 0.3f);
            CreateTween().TweenProperty(_body, "modulate", Colors.White, 0.8);
        }
    }

    /// <summary>"Job: The Glass Truth → Station 3 · 82% (needs 70%) · 1 h 20 min left" for the open job.</summary>
    public static string JobLine(ContractNote job, double nowHours, float hoursPerSecond)
    {
        string line = "Job: " + job.Title + " → Station " + job.ToStation;
        if (job.Breakable)
        {
            line += " · " + Mathf.RoundToInt(job.Condition) + "%";
            if (job.MinCondition > 0.0f)
            {
                line += " (needs " + Mathf.RoundToInt(job.MinCondition) + "%)";
            }
        }
        if (job.DeadlineHours >= 0.0)
        {
            float left = (float)System.Math.Max(job.DeadlineHours - nowHours, 0.0);
            line += " · " + TimeText.FromHours(left, hoursPerSecond) + " left";
        }
        return line;
    }

    /// <summary>The weather, and for a real wind its speed and how it pushes the carrier as he faces now.</summary>
    private string Weather(WeatherSnapshot weather)
    {
        string kind = weather.Kind.ToString();
        if (weather.Kind == WeatherKind.Clear)
        {
            kind = "Clear";
        }
        string wind = "";
        if (weather.WindSpeed > 3.0f)
        {
            wind = $", wind {weather.WindSpeed:0} m/s {WindPush(weather.Wind)}";
        }
        return kind + wind;
    }

    /// <summary>How the wind pushes him relative to the view: at his back, in his face, or to a side.</summary>
    private string WindPush(Vector3 wind)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return "";
        }
        Vector3 forward = -camera.GlobalBasis.Z;
        forward.Y = 0.0f;
        if (forward.IsZeroApprox())
        {
            return "";
        }
        forward = forward.Normalized();
        Vector3 right = forward.Cross(Vector3.Up);
        float along = wind.Dot(forward);
        float across = wind.Dot(right);
        if (Mathf.Abs(along) >= Mathf.Abs(across))
        {
            if (along > 0.0f)
            {
                return "at your back";
            }
            return "in your face";
        }
        if (across > 0.0f)
        {
            return "pushing you right";
        }
        return "pushing you left";
    }

    private static string Numeral(int n)
    {
        string[] numerals = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        if (n >= 0 && n < numerals.Length)
        {
            return numerals[n];
        }
        return n.ToString();
    }
}
