using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Works out what the walk to the next station will cost from where the carrier stands: it follows
/// the route (RunState.Route) in steps, using the controller's walking speed and the stamina drain
/// for each step's grade and the current load, then adds the rests stamina will force (the carrier
/// stops when exhausted and rests back to ExhaustedRecoverLevel, at the current recovery rate, so
/// thirst and cold count) and converts the total to an arrival time on the clock. Publishes a
/// RouteEstimate a few times a second; cairns show it. Follows whichever path of the stage the
/// carrier is on; near the top of a stage with a choice of paths it also publishes an estimate
/// for every path (RouteOptionsUpdated) for the lookout sign. See docs/survival-loop.md.
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
    /// <summary>Options are published from this far above a stage's split down to this far below it, in m.</summary>
    [Export] public Vector2 OptionsReach { get; set; } = new(160.0f, 40.0f);

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
        PublishOptions();
    }

    /// <summary>Near the top of a stage with branches: one estimate per path, from where the carrier stands.</summary>
    private void PublishOptions()
    {
        IRouteQuery? route = _run?.Route;
        if (Body == null || Stamina == null || _clock == null || route == null)
        {
            return;
        }
        Vector3 from = Body.GlobalPosition;
        int stage = route.NextStationIndex(from.Z - ArrivedDistance) - 1;
        int paths = route.PathCount(stage);
        Vector2 zone = route.BranchZone(stage);
        if (paths < 2 || from.Z > zone.X + OptionsReach.X || from.Z < zone.X - OptionsReach.Y)
        {
            return;
        }
        RouteEstimate[] options = new RouteEstimate[paths];
        for (int path = 0; path < paths; path++)
        {
            options[path] = EstimateAlong(from, route, stage, path);
        }
        GameEvents.PublishRouteOptions(stage, options);
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

    /// <summary>The estimate along whichever path of the stage the carrier is closest to.</summary>
    public RouteEstimate EstimateFrom(Vector3 from, IRouteQuery route)
    {
        int stage = route.NextStationIndex(from.Z - ArrivedDistance) - 1;
        int path = 0;
        if (from.Z - ArrivedDistance <= route.BranchZone(stage).X)
        {
            path = route.NearestPath(from, out float _);
        }
        return EstimateAlong(from, route, stage, path);
    }

    /// <summary>The estimate to the stage's bottom station along one of its paths.</summary>
    public RouteEstimate EstimateAlong(Vector3 from, IRouteQuery route, int stage, int path)
    {
        if (Body == null || Stamina == null || _clock == null || !float.IsFinite(SampleStep) || SampleStep <= 0.0f)
        {
            return default;
        }
        int station = stage + 1;
        Vector3 target = route.Station(station);
        if (target.Z > from.Z)
        {
            return default;
        }
        float load = CarriedLoad.Mass;

        float walk = 0.0f;
        float drain = 0.0f;
        float rest = 0.0f;
        int rests = 0;
        float available = Mathf.Min(Stamina.Current, Stamina.EffectiveMax);
        float wait = Stamina.RecoveryWaitSeconds;
        bool exhausted = Stamina.Exhausted;
        float recovery = Stamina.EffectiveRecovery;
        float threshold = Stamina.EffectiveRecoverLevel;

        // Follow the stamina budget in route order: effortless sections recover it before climbs.
        bool Travel(float length, float grade)
        {
            if (length <= 0.0f)
            {
                return true;
            }
            float speed = Body.WalkingSpeed(grade);
            if (!float.IsFinite(speed) || speed <= 0.0f)
            {
                return false;
            }
            if (exhausted)
            {
                if (threshold <= 0.0f || recovery <= 0.0f)
                {
                    return false;
                }
                rest += wait + Mathf.Max(0.0f, threshold - available) / recovery;
                rests++;
                available = Mathf.Max(available, threshold);
                wait = 0.0f;
                exhausted = false;
            }

            float seconds = length / speed;
            float cost = Stamina.WalkDrainPerSecond(load, grade, false, false) * seconds;
            walk += seconds;
            drain += cost;
            if (cost <= 0.0f)
            {
                float recoveryTime = Mathf.Max(0.0f, seconds - wait);
                wait = Mathf.Max(0.0f, wait - seconds);
                available = Mathf.Min(Stamina.EffectiveMax, available + recoveryTime * recovery * Stamina.WalkingRecovery);
                return true;
            }

            float deficit = cost - available;
            if (deficit > 0.0f)
            {
                if (threshold <= 0.0f || recovery <= 0.0f)
                {
                    return false;
                }
                int stops = Mathf.CeilToInt(deficit / threshold);
                rest += stops * (Stamina.RecoveryDelay + threshold / recovery);
                rests += stops;
                available += stops * threshold;
            }
            available = Mathf.Max(0.0f, available - cost);
            exhausted = available <= 0.0f;
            wait = Stamina.RecoveryDelay;
            return true;
        }

        // Off the path: walk across to it on the flat first.
        Vector3 previous = route.PathPoint(stage, path, from.Z);
        float across = Mathf.Abs(from.X - previous.X);
        float distance = across;
        if (!Travel(across, 0.0f))
        {
            return default;
        }

        for (float z = from.Z; z > target.Z;)
        {
            float stepZ = Mathf.Max(z - SampleStep, target.Z);
            if (stepZ >= z)
            {
                // Float precision at great depth must not turn sampling into an infinite loop.
                return default;
            }
            Vector3 point = route.PathPoint(stage, path, stepZ);
            Vector3 step = point - previous;
            float length = step.Length();
            if (length > Mathf.Epsilon)
            {
                float grade = (previous.Y - point.Y) / length;
                // FloorConstantSpeed maintains metres per second along the slope.
                if (!Travel(length, grade))
                {
                    return default;
                }
                distance += length;
            }
            previous = point;
            z = stepZ;
        }

        float hours = (walk + rest) * _clock.HoursPerSecond;
        float arrival = _clock.TimeOfDay + hours;
        int day = _clock.Day + Mathf.FloorToInt(arrival / WorldClock.HoursPerDay);
        float arrivalHour = Mathf.PosMod(arrival, WorldClock.HoursPerDay);
        bool beforeNightfall = !_clock.IsNight && day == _clock.Day && arrivalHour < _clock.SunsetHour;

        return new RouteEstimate(true, station, distance, walk, rest, rests, drain, day, arrivalHour, beforeNightfall, route.PathKind(stage, path));
    }
}
