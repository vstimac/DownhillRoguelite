using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Works out what the walk to the next station will cost from where the carrier stands: it follows
/// the route (RunState.Route) in steps, using the controller's walking speed and the stamina drain
/// for each step's grade and the current load, then adds the rests stamina will force (the carrier
/// stops when exhausted and rests back to ExhaustedRecoverLevel, at the current recovery rate, so
/// thirst and cold count) and converts the total to an arrival time on the clock. Publishes a
/// RouteEstimate a few times a second; cairns show it. See docs/survival-loop.md.
/// </summary>
public partial class RouteEstimator : Node
{
    [Export] public PlayerController? Body { get; set; }
    [Export] public CarrierStamina? Stamina { get; set; }
    /// <summary>Seconds between estimates.</summary>
    [Export] public float UpdateInterval { get; set; } = 0.5f;
    /// <summary>Route sampling step, in m along Z.</summary>
    [Export] public float SampleStep { get; set; } = 5.0f;
    /// <summary>Within this distance of a station the carrier counts as there; the estimate moves on to the next.</summary>
    [Export] public float ArrivedDistance { get; set; } = 9.0f;

    private RunState? _run;
    private WorldClock? _clock;
    private float _timer;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
    }

    public override void _PhysicsProcess(double delta)
    {
        _timer -= (float)delta;
        if (_timer > 0.0f)
        {
            return;
        }
        _timer = UpdateInterval;
        GameEvents.PublishRouteEstimate(Estimate());
    }

    /// <summary>The estimate from the carrier's current position, load, stamina and time.</summary>
    public RouteEstimate Estimate()
    {
        IRouteQuery? route = _run?.Route;
        if (Body == null || Stamina == null || _clock == null || route == null)
        {
            return default;
        }
        return EstimateFrom(Body.GlobalPosition, route);
    }

    public RouteEstimate EstimateFrom(Vector3 from, IRouteQuery route)
    {
        int station = route.NextStationIndex(from.Z - ArrivedDistance);
        Vector3 target = route.Station(station);
        float load = GameEvents.CurrentLoad.Mass;

        // Off the route: walk across to it on the flat first.
        Vector3 previous = route.RoutePoint(from.Z);
        float across = Mathf.Abs(from.X - previous.X);
        float distance = across;
        float walk = across / Body!.WalkingSpeed(0.0f);
        float drain = Stamina!.WalkDrainPerSecond(load, 0.0f, false, false) * walk;

        for (float z = from.Z - SampleStep; ; z -= SampleStep)
        {
            float stepZ = Mathf.Max(z, target.Z);
            Vector3 point = route.RoutePoint(stepZ);
            Vector3 step = point - previous;
            float length = step.Length();
            float horizontal = new Vector2(step.X, step.Z).Length();
            if (length > Mathf.Epsilon)
            {
                float grade = (previous.Y - point.Y) / length;
                float seconds = horizontal / Body.WalkingSpeed(grade);
                walk += seconds;
                drain += Stamina.WalkDrainPerSecond(load, grade, false, false) * seconds;
                distance += length;
            }
            previous = point;
            if (stepZ <= target.Z)
            {
                break;
            }
        }

        // Rests: every time stamina runs out the carrier stands until it's back to the recover level.
        float recovery = Mathf.Max(Stamina.EffectiveRecovery, 0.01f);
        float available = Stamina.Current;
        float rest = 0.0f;
        if (Stamina.Exhausted)
        {
            rest += (Stamina.ExhaustedRecoverLevel - Stamina.Current) / recovery + Stamina.RecoveryDelay;
            available = Stamina.ExhaustedRecoverLevel;
        }
        float deficit = Mathf.Max(0.0f, drain - available);
        int rests = Mathf.CeilToInt(deficit / Stamina.ExhaustedRecoverLevel);
        rest += deficit / recovery + rests * Stamina.RecoveryDelay;

        float hours = (walk + rest) * _clock!.HoursPerSecond;
        float arrival = _clock.TimeOfDay + hours;
        int day = _clock.Day + Mathf.FloorToInt(arrival / WorldClock.HoursPerDay);
        float arrivalHour = Mathf.PosMod(arrival, WorldClock.HoursPerDay);
        bool beforeNightfall = !_clock.IsNight && day == _clock.Day && arrivalHour < _clock.SunsetHour;

        return new RouteEstimate(true, station, distance, walk, rest, rests, drain, day, arrivalHour, beforeNightfall);
    }
}
