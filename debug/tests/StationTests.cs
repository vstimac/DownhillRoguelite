using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M7: stations sit on the route one interval of depth apart on level ground;
/// day and night change the light; the route estimate responds to load and time and predicts a real
/// walk; the shelter allows packing; sleep is refused when early and restores at night; night cold
/// hurts outside but not in a shelter; cairns show the estimate.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/StationTests.tscn
/// </summary>
public partial class StationTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public StationManager? Stations { get; set; }
    [Export] public DayNightCycle? DayNight { get; set; }
    [Export] public PackingMode? Packing { get; set; }
    [Export] public float SettleTime { get; set; } = 1.0f;
    [Export] public int StationsChecked { get; set; } = 5;
    /// <summary>The scripted walk starts this far (along Z) before station 1, in m.</summary>
    [Export] public float AccuracyWalk { get; set; } = 250.0f;
    /// <summary>Stamina at the start of the scripted walk, so it has to rest on the way.</summary>
    [Export] public float AccuracyStartStamina { get; set; } = 15.0f;
    /// <summary>Allowed error of the estimated time against the real walk, as a fraction.</summary>
    [Export] public float MaxEstimateError { get; set; } = 0.2f;
    [Export] public float FastClock { get; set; } = 90.0f;

    private readonly List<string> _failures = new();
    private readonly List<int> _reached = new();
    private WorldClock? _clock;
    private RunState? _run;
    private CarrierInventory? _inventory;
    private CarrierNeeds? _needs;
    private RouteEstimator? _estimator;

    public override async void _Ready()
    {
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (PlayerBody == null || Streamer?.Generator == null || Stations == null || DayNight == null || Packing == null || _clock == null || _run?.Route == null)
        {
            GD.PushError("StationTests: scene or autoloads incomplete.");
            GetTree().Quit(1);
            return;
        }
        _inventory = PlayerBody.GetNodeOrNull<CarrierInventory>("Inventory");
        _needs = PlayerBody.GetNodeOrNull<CarrierNeeds>("Needs");
        _estimator = PlayerBody.GetNodeOrNull<RouteEstimator>("RouteEstimator");
        if (_inventory == null || _needs == null || _estimator == null)
        {
            GD.PushError("StationTests: player needs Inventory, Needs and RouteEstimator.");
            GetTree().Quit(1);
            return;
        }

        GameEvents.StationReached += OnStationReached;
        IRouteQuery route = _run.Route;
        _clock.SetTimeOfDay(12.0f);
        await Place(PlayerBody, Streamer.SpawnPoint);

        TestStationPlacement(Streamer.Generator, route);
        await TestDayNight(_clock, DayNight);
        await TestEstimateResponds(PlayerBody, route, _clock, _inventory, _estimator);
        await TestEstimateAccuracy(PlayerBody, route, _clock, _estimator);
        await TestShelterAndPacking(PlayerBody, route, Stations, Packing);
        await TestSleep(PlayerBody, route, Stations, _clock, _needs);
        await TestNightCold(PlayerBody, route, _clock, _needs);
        await TestCairns(PlayerBody, route, Stations);

        GameEvents.StationReached -= OnStationReached;
        if (_failures.Count == 0)
        {
            GD.Print("STATION TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"STATION TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnStationReached(int station)
    {
        _reached.Add(station);
    }

    private void TestStationPlacement(TerrainGenerator generator, IRouteQuery route)
    {
        float interval = generator.Settings.StationDepthInterval;
        float worstSpacing = 0.0f;
        float worstSlope = 0.0f;
        float worstOffset = 0.0f;
        float previousDepth = 0.0f;
        for (int i = 1; i <= StationsChecked; i++)
        {
            Vector3 station = route.Station(i);
            float depth = generator.StartHeight - station.Y;
            worstSpacing = Mathf.Max(worstSpacing, Mathf.Abs(depth - previousDepth - interval));
            previousDepth = depth;
            worstSlope = Mathf.Max(worstSlope, generator.SlopeDegrees(station.X, station.Z));
            worstOffset = Mathf.Max(worstOffset, Mathf.Abs(station.X - generator.SpineX(station.Z)));
        }
        Check($"stations every {interval:0} m of depth", worstSpacing <= 15.0f, $"worst spacing error {worstSpacing:0.0} m over {StationsChecked} stations");
        Check("stations sit on the route on gentle ground", worstOffset <= 0.5f && worstSlope <= generator.Settings.ClearingSlope + 3.0f,
            $"furthest from route {worstOffset:0.0} m, steepest {worstSlope:0.0}°");
    }

    private async Task TestDayNight(WorldClock clock, DayNightCycle cycle)
    {
        clock.SetTimeOfDay(12.0f);
        await WaitSeconds(0.1f);
        float dayDark = cycle.Darkness;
        float dayFog = DayNight!.World!.Environment.FogDensity;
        clock.SetTimeOfDay(23.0f);
        await WaitSeconds(0.1f);
        float nightDark = cycle.Darkness;
        float nightFog = DayNight.World.Environment.FogDensity;
        bool night = clock.IsNight;
        clock.SetTimeOfDay(12.0f);
        await WaitSeconds(0.1f);
        Check("night falls: darker and foggier", dayDark < 0.05f && nightDark > 0.95f && nightFog > dayFog * 2.0f && night,
            $"darkness {dayDark:0.00} -> {nightDark:0.00}, fog {dayFog:0.000} -> {nightFog:0.000}");
    }

    private async Task TestEstimateResponds(PlayerController player, IRouteQuery route, WorldClock clock, CarrierInventory inventory, RouteEstimator estimator)
    {
        clock.SetTimeOfDay(9.0f);
        await Place(player, route.RoutePoint(route.StartZ - 5.0f));
        RouteEstimate light = estimator.Estimate();

        for (int i = 0; i < 3; i++)
        {
            inventory.TryAdd(ResourceLoader.Load<ItemDefinition>("res://inventory/items/water_bottle.tres"));
            inventory.TryAdd(ResourceLoader.Load<ItemDefinition>("res://inventory/items/food_bag.tres"));
        }
        await WaitSeconds(0.1f);
        RouteEstimate heavy = estimator.Estimate();
        inventory.Clear();

        Check("estimate covers the route to station 1", light.Valid && light.Station == 1 && light.DistanceMeters > 1000.0f && light.WalkSeconds > 0.0f,
            $"station {light.Station}, {light.DistanceMeters:0} m, walk {light.WalkSeconds:0} s, rest {light.RestSeconds:0} s, {light.Rests} rests");
        Check("a heavier load costs more stamina and more rests", heavy.StaminaNeeded > light.StaminaNeeded && heavy.Rests >= light.Rests && heavy.RestSeconds > light.RestSeconds,
            $"stamina {light.StaminaNeeded:0} -> {heavy.StaminaNeeded:0} with {GameEvents.CurrentLoad.Mass:0} kg, rests {light.Rests} -> {heavy.Rests}");

        clock.SetTimeOfDay(9.0f);
        bool morning = estimator.Estimate().BeforeNightfall;
        clock.SetTimeOfDay(19.5f);
        RouteEstimate evening = estimator.Estimate();
        clock.SetTimeOfDay(12.0f);
        Check("setting out late arrives after dark", morning && !evening.BeforeNightfall,
            $"from 09:00 before dark {morning}; from 19:30 arrives {evening.ArrivalHour:0.0}h day {evening.ArrivalDay}, before dark {evening.BeforeNightfall}");
    }

    private async Task TestEstimateAccuracy(PlayerController player, IRouteQuery route, WorldClock clock, RouteEstimator estimator)
    {
        Vector3 station = route.Station(1);
        clock.SetTimeOfDay(9.0f);
        await Place(player, route.RoutePoint(station.Z + AccuracyWalk));
        player.Stamina!.SetCurrent(AccuracyStartStamina);
        RouteEstimate estimate = estimator.Estimate();

        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        float elapsed = 0.0f;
        float limit = estimate.TotalSeconds * 3.0f + 10.0f;
        while (elapsed < limit)
        {
            Vector3 p = player.GlobalPosition;
            if (new Vector2(p.X - station.X, p.Z - station.Z).Length() <= estimator.ArrivedDistance)
            {
                break;
            }
            Vector3 aim = route.RoutePoint(Mathf.Max(p.Z - 6.0f, station.Z));
            player.DirectionOverride = new Vector3(aim.X - p.X, 0.0f, aim.Z - p.Z);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            elapsed += dt;
        }
        player.DirectionOverride = null;

        float error = Mathf.Abs(elapsed - estimate.TotalSeconds) / Mathf.Max(estimate.TotalSeconds, 1.0f);
        Check("the estimate predicts a real walk to the station", elapsed < limit && error <= MaxEstimateError,
            $"estimated {estimate.TotalSeconds:0} s ({estimate.WalkSeconds:0} walk + {estimate.RestSeconds:0} rest, {estimate.Rests} rests), took {elapsed:0} s ({error:P0} off)");
    }

    private async Task TestShelterAndPacking(PlayerController player, IRouteQuery route, StationManager stations, PackingMode packing)
    {
        Vector3 station = route.Station(1);
        await Place(player, route.RoutePoint(station.Z + 40.0f));
        bool outsideOpened = packing.TryOpen();
        packing.Close();

        // Station 1 may already have been reached by the scripted walk; the event fires once per run.
        await Place(player, station + new Vector3(0.0f, 0.0f, 2.0f));
        bool sheltered = GameEvents.InShelter && stations.ShelterStation == 1;
        bool insideOpened = packing.TryOpen();
        packing.Close();
        await WaitSeconds(0.2f);

        Check("reaching a station shelters the carrier and counts the stage", sheltered && _reached.Contains(1) && _run!.Stage >= 1,
            $"in shelter {GameEvents.InShelter}, reached {string.Join(",", _reached)}, stage {_run!.Stage}");
        Check("packing only works in a station's shelter", !outsideOpened && insideOpened, $"outside {outsideOpened}, inside {insideOpened}");
    }

    private async Task TestSleep(PlayerController player, IRouteQuery route, StationManager stations, WorldClock clock, CarrierNeeds needs)
    {
        Vector3 station = route.Station(1);
        await Place(player, station + new Vector3(0.0f, 0.0f, 2.0f));
        SleepSpot? bed = null;
        if (stations.Stations.TryGetValue(1, out StationSite? site))
        {
            bed = site.GetNodeOrNull<SleepSpot>("Bedroll");
        }
        if (bed == null)
        {
            Check("station has a bedroll", false, "no SleepSpot at station 1");
            return;
        }

        clock.SetTimeOfDay(10.0f);
        bool early = bed.Interact(player);

        clock.SetTimeOfDay(21.0f);
        int day = clock.Day;
        CarrierHealth health = player.Health!;
        health.Refill();
        health.TakeDamage(50.0f, "test");
        needs.SetNeeds(80.0f, 80.0f);
        player.Stamina!.SetCurrent(10.0f);
        bool slept = bed.Interact(player);
        bool morning = clock.Day == day + 1 && Mathf.Abs(clock.TimeOfDay - clock.SunriseHour) < 0.01f;

        Check("too early to sleep before evening", !early, $"slept at 10:00: {early}");
        Check("sleeping at night skips to dawn, heals and refills stamina",
            slept && morning && Mathf.Abs(health.Current - 90.0f) < 0.5f && player.Stamina.Current >= player.Stamina.EffectiveMax - 0.01f && needs.Hunger < 80.0f && needs.Thirst < needs.Hunger,
            $"day {day} -> {clock.Day} {clock.TimeOfDay:0.0}h, health 50 -> {health.Current:0}, stamina {player.Stamina.Current:0}, hunger 80 -> {needs.Hunger:0}, thirst 80 -> {needs.Thirst:0}");
        clock.SetTimeOfDay(12.0f);
    }

    private async Task TestNightCold(PlayerController player, IRouteQuery route, WorldClock clock, CarrierNeeds needs)
    {
        float outside = await ColdLoss(player, route.RoutePoint(route.Station(1).Z + 60.0f), clock);
        float inside = await ColdLoss(player, route.Station(1) + new Vector3(0.0f, 0.0f, 2.0f), clock);
        Check("night cold hurts outside, not in a shelter", outside > 3.0f && inside < 0.5f, $"lost {outside:0.0} outside, {inside:0.0} in the shelter over ~2 h");
        clock.SetTimeOfDay(12.0f);
    }

    private async Task<float> ColdLoss(PlayerController player, Vector3 at, WorldClock clock)
    {
        await Place(player, at);
        clock.SetTimeOfDay(23.0f);
        _needs!.Refill();
        CarrierHealth health = player.Health!;
        health.Refill();
        clock.DebugTimeScale = FastClock;
        await WaitSeconds(2.0f);
        clock.DebugTimeScale = 1.0f;
        return health.Max - health.Current;
    }

    private async Task TestCairns(PlayerController player, IRouteQuery route, StationManager stations)
    {
        SetHour(12.0f);
        // Stand a little uphill of the first cairn, inside the distance its label shows from.
        await Place(player, route.RoutePoint(-stations.CairnSpacing + stations.CairnLabelDistance * 0.5f));
        await WaitSeconds(UpdateWait());
        WaypointCairn? cairn = stations.LabelledCairn;
        string text = cairn?.LabelText ?? "";
        Check("cairns stand along the route and the one ahead shows the estimate",
            stations.Cairns.Count > 0 && cairn != null && cairn.ShowingEstimate && text.Contains("Station 1") && text.Contains("arrive"),
            $"{stations.Cairns.Count} cairns loaded; label: {text.Replace("\n", " | ")}");
    }

    private void SetHour(float hour)
    {
        _clock?.SetTimeOfDay(hour);
    }

    private float UpdateWait()
    {
        return (_estimator?.UpdateInterval ?? 0.5f) + 0.2f;
    }

    private async Task Place(PlayerController player, Vector3 position)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        _needs?.Refill();
        await WaitSeconds(SettleTime);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
