using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M8: the weather table has a profile per circle; the forecast is the same
/// for the same seed and differs for another; each circle has its own character (Limbo fog, Lust
/// wind, Gluttony rain, Heresy heat, Treachery snow); fronts and circles blend without jumps; night
/// worsens weather and cools heat; and the effects reach gameplay: rain and snow cut grip, snow
/// slows walking, wind pushes the balance (more with a big load), heat speeds thirst, wet and wind
/// chill outside shelter only, fog thickens the fog and dims the sun, and rain and snow fall.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/WeatherTests.tscn
/// </summary>
public partial class WeatherTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public WeatherSystem? Weather { get; set; }
    [Export] public DayNightCycle? DayNight { get; set; }
    [Export] public WeatherParticles? Particles { get; set; }
    [Export] public float SettleTime { get; set; } = 1.0f;
    /// <summary>Clock speed-up used to measure thirst drain.</summary>
    [Export] public float FastClock { get; set; } = 90.0f;
    /// <summary>Fronts sampled per circle when checking its character.</summary>
    [Export] public int FrontSamples { get; set; } = 400;
    /// <summary>Largest change in any intensity allowed between samples a few minutes apart.</summary>
    [Export] public float MaxStep { get; set; } = 0.15f;

    private readonly List<string> _failures = new();
    private CarrierNeeds? _needs;
    private BalanceController? _balance;
    private WorldClock? _clock;
    private RunState? _run;
    private int _kindChanges;

    public override async void _Ready()
    {
        if (PlayerBody == null || Streamer == null || Weather == null || DayNight == null || Particles == null)
        {
            GD.PushError("WeatherTests: PlayerBody, Streamer, Weather, DayNight and Particles must be assigned.");
            GetTree().Quit(1);
            return;
        }
        _needs = PlayerBody.GetNodeOrNull<CarrierNeeds>("Needs");
        _balance = PlayerBody.GetNodeOrNull<BalanceController>("Balance");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (_needs == null || _balance == null || _clock == null || _run == null || Weather.Table == null)
        {
            GD.PushError("WeatherTests: player is missing Needs or Balance, autoloads are missing, or the weather has no table.");
            GetTree().Quit(1);
            return;
        }

        GameEvents.WeatherChanged += OnWeatherChanged;
        _clock.SetTimeOfDay(12.0f);
        Weather.Gusts = false;
        Weather.Forced = WeatherKind.Clear;
        await Place(PlayerBody, Streamer.SpawnPoint);

        TestTable(Weather.Table, _run.Circles);
        TestDeterminism(Weather.Table);
        TestCircleCharacter(Weather.Table);
        TestBlending(Weather.Table, _run.Circles);
        TestNight(Weather.Table);
        await TestGripAndSpeed(PlayerBody, Weather);
        await TestWind(Weather, _balance);
        await TestNeeds(Weather, _needs, _clock);
        await TestVisuals(Weather, DayNight, Particles);

        GameEvents.WeatherChanged -= OnWeatherChanged;
        Weather.Forced = null;
        if (_failures.Count == 0)
        {
            GD.Print("WEATHER TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"WEATHER TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnWeatherChanged(WeatherKind kind)
    {
        _kindChanges++;
    }

    private void TestTable(WeatherTable table, CircleTable circles)
    {
        int expected = circles.Names.Length + 1;
        Check("a weather profile per circle plus beyond", table.Profiles.Length == expected,
            $"{table.Profiles.Length} profiles (expected {expected}), first {table.ProfileFor(0).DisplayName}, last {table.ProfileFor(99).DisplayName}");
        Check("circle depth maps to names", circles.NameOf(circles.CircleAt(0.0f)) == "Limbo" && circles.NameOf(circles.CircleAt(circles.DepthPerCircle * 8.5f)) == "Treachery"
            && circles.NameOf(circles.CircleAt(circles.DepthPerCircle * 12.0f)) == circles.BeyondName,
            $"{circles.DepthPerCircle:0} m per circle");
    }

    private void TestDeterminism(WeatherTable table)
    {
        WeatherForecast a = new(table, 1234ul);
        WeatherForecast b = new(table, 1234ul);
        WeatherForecast other = new(table, 98765ul);
        bool same = true;
        int differ = 0;
        for (int circle = 0; circle < 10; circle++)
        {
            for (long front = 0; front < 50; front++)
            {
                if (a.FrontAt(circle, front) != b.FrontAt(circle, front))
                {
                    same = false;
                }
                if (a.FrontAt(circle, front) != other.FrontAt(circle, front))
                {
                    differ++;
                }
            }
        }
        Check("same seed, same weather; other seed, other weather", same && differ > 400, $"{differ}/500 fronts differ with another seed");
    }

    private void TestCircleCharacter(WeatherTable table)
    {
        WeatherForecast forecast = new(table, 42ul);
        (int circle, WeatherKind kind, WeatherKind never)[] expectations =
        {
            (0, WeatherKind.Fog, WeatherKind.Snow),
            (1, WeatherKind.Wind, WeatherKind.Heat),
            (2, WeatherKind.Rain, WeatherKind.Snow),
            (5, WeatherKind.Heat, WeatherKind.Snow),
            (8, WeatherKind.Snow, WeatherKind.Heat),
        };
        foreach ((int circle, WeatherKind kind, WeatherKind never) in expectations)
        {
            Dictionary<WeatherKind, int> counts = new();
            for (long front = 0; front < FrontSamples; front++)
            {
                WeatherKind seen = forecast.FrontAt(circle, front).Kind;
                counts[seen] = counts.GetValueOrDefault(seen) + 1;
            }
            WeatherKind top = WeatherKind.Clear;
            foreach (KeyValuePair<WeatherKind, int> pair in counts)
            {
                if (pair.Value > counts.GetValueOrDefault(top))
                {
                    top = pair.Key;
                }
            }
            string name = table.ProfileFor(circle).DisplayName;
            Check($"{name} is mostly {kind}", top == kind && counts.GetValueOrDefault(never) == 0,
                $"{kind} {counts.GetValueOrDefault(kind)}/{FrontSamples}, most common {top}, {never} {counts.GetValueOrDefault(never)}");
        }
    }

    private void TestBlending(WeatherTable table, CircleTable circles)
    {
        WeatherForecast forecast = new(table, 7ul);
        float worstTime = 0.0f;
        WeatherSnapshot previous = forecast.SampleCircle(2, 0.0, 0.0f);
        for (double hours = 0.05; hours < 60.0; hours += 0.05)
        {
            WeatherSnapshot next = forecast.SampleCircle(2, hours, 0.0f);
            worstTime = Mathf.Max(worstTime, Difference(previous, next));
            previous = next;
        }
        Check("fronts blend into each other", worstTime <= MaxStep, $"largest change per 3 in-game minutes {worstTime:0.000} (max {MaxStep})");

        float worstDepth = 0.0f;
        float start = circles.DepthPerCircle - table.CircleBlendDepth - 50.0f;
        previous = forecast.Sample(start, 3.0, 0.0f, circles);
        for (float depth = start + 5.0f; depth < circles.DepthPerCircle + 50.0f; depth += 5.0f)
        {
            WeatherSnapshot next = forecast.Sample(depth, 3.0, 0.0f, circles);
            worstDepth = Mathf.Max(worstDepth, Difference(previous, next));
            previous = next;
        }
        Check("circles blend into each other", worstDepth <= MaxStep, $"largest change per 5 m of depth {worstDepth:0.000} (max {MaxStep})");
    }

    private void TestNight(WeatherTable table)
    {
        WeatherForecast forecast = new(table, 1ul);
        WeatherProfile profile = table.ProfileFor(2);
        WeatherFront rain = new(WeatherKind.Rain, 0.6f, 0.0f);
        WeatherFront heat = new(WeatherKind.Heat, 0.8f, 0.0f);
        WeatherSnapshot rainDay = forecast.FromFront(rain, profile, 0.0f);
        WeatherSnapshot rainNight = forecast.FromFront(rain, profile, 1.0f);
        WeatherSnapshot heatDay = forecast.FromFront(heat, profile, 0.0f);
        WeatherSnapshot heatNight = forecast.FromFront(heat, profile, 1.0f);
        Check("night worsens rain and wind, cools heat",
            rainNight.Rain > rainDay.Rain && rainNight.WindSpeed > rainDay.WindSpeed && heatNight.Heat < heatDay.Heat,
            $"rain {rainDay.Rain:0.00} → {rainNight.Rain:0.00}, wind {rainDay.WindSpeed:0.0} → {rainNight.WindSpeed:0.0} m/s, heat {heatDay.Heat:0.00} → {heatNight.Heat:0.00}");
    }

    private async Task TestGripAndSpeed(PlayerController player, WeatherSystem weather)
    {
        weather.Forced = WeatherKind.Clear;
        await WaitSeconds(0.3f);
        float dryGrip = player.Grip;
        float drySpeed = player.WalkingSpeed(0.0f);

        weather.Forced = WeatherKind.Rain;
        await WaitSeconds(0.3f);
        float wetGrip = player.Grip;
        float expected = dryGrip * weather.Forecast!.GripFactor(1.0f, 0.0f);
        Check("rain makes the ground slippery", wetGrip < dryGrip && Mathf.Abs(wetGrip - expected) < 0.02f,
            $"grip {dryGrip:0.00} dry → {wetGrip:0.00} in rain (expected {expected:0.00})");

        weather.Forced = WeatherKind.Snow;
        await WaitSeconds(0.3f);
        float snowGrip = player.Grip;
        float snowSpeed = player.WalkingSpeed(0.0f);
        Check("snow is slipperier still and slows walking", snowGrip < wetGrip && snowSpeed < drySpeed * 0.8f,
            $"grip {snowGrip:0.00}, walking speed {drySpeed:0.00} → {snowSpeed:0.00} m/s");
        Check("weather changes are announced", _kindChanges >= 2, $"{_kindChanges} WeatherChanged events");
    }

    private async Task TestWind(WeatherSystem weather, BalanceController balance)
    {
        PublishLoad(5.0f);
        weather.Forced = WeatherKind.Clear;
        balance.ResetBalance();
        await WaitSeconds(1.0f);
        float calmRoll = balance.Snapshot.Tilt.X;

        // Wind blowing toward the carrier's right.
        Vector3 right = balance.Snapshot.Right;
        weather.ForcedWindAngle = Mathf.Atan2(right.X, right.Z);
        weather.Forced = WeatherKind.Wind;
        await WaitSeconds(2.0f);
        BalanceSnapshot light = balance.Snapshot;

        PublishLoad(25.0f);
        balance.ResetBalance();
        await WaitSeconds(2.0f);
        BalanceSnapshot heavy = balance.Snapshot;

        Check("wind pushes the carrier downwind", light.WindTorque.X > 0.0f && light.Tilt.X > calmRoll + Mathf.DegToRad(0.5f),
            $"wind torque roll {light.WindTorque.X:0.0} N·m, roll {Mathf.RadToDeg(calmRoll):0.0}° calm → {Mathf.RadToDeg(light.Tilt.X):0.0}° in {GameEvents.CurrentWeather.WindSpeed:0.0} m/s");
        Check("a big pack catches more wind", heavy.WindTorque.X > light.WindTorque.X * 1.3f && heavy.Tilt.X > light.Tilt.X,
            $"torque {light.WindTorque.X:0.0} → {heavy.WindTorque.X:0.0} N·m, roll {Mathf.RadToDeg(light.Tilt.X):0.0}° → {Mathf.RadToDeg(heavy.Tilt.X):0.0}°, state {heavy.State}");

        GameEvents.PublishLoad(LoadSnapshot.Empty);
        weather.Forced = WeatherKind.Clear;
        balance.ResetBalance();
        await WaitSeconds(0.5f);
    }

    private async Task TestNeeds(WeatherSystem weather, CarrierNeeds needs, WorldClock clock)
    {
        weather.Forced = WeatherKind.Clear;
        float clearLoss = await ThirstLoss(needs, clock);
        float clearCold = needs.Cold;
        weather.Forced = WeatherKind.Heat;
        float heatLoss = await ThirstLoss(needs, clock);
        Check("heat makes the carrier thirsty", heatLoss > clearLoss * 1.6f && clearCold <= 0.001f,
            $"thirst lost {clearLoss:0.00} clear vs {heatLoss:0.00} in heat over the same hours (clear-day cold {clearCold:0.00})");

        weather.Forced = WeatherKind.Rain;
        await WaitSeconds(0.2f);
        float rainCold = needs.Cold;
        GameEvents.PublishShelter(true, 0);
        await WaitSeconds(0.2f);
        float shelteredCold = needs.Cold;
        GameEvents.PublishShelter(false, -1);
        Check("rain chills by day, shelter keeps it off", rainCold > 0.15f && shelteredCold <= 0.001f,
            $"cold {rainCold:0.00} in the rain, {shelteredCold:0.00} under shelter");
        needs.Refill();
    }

    private async Task<float> ThirstLoss(CarrierNeeds needs, WorldClock clock)
    {
        needs.Refill();
        await WaitSeconds(0.1f);
        float before = needs.Thirst;
        clock.SetTimeOfDay(12.0f);
        clock.DebugTimeScale = FastClock;
        await WaitSeconds(1.0f);
        clock.DebugTimeScale = 1.0f;
        clock.SetTimeOfDay(12.0f);
        return before - needs.Thirst;
    }

    private async Task TestVisuals(WeatherSystem weather, DayNightCycle dayNight, WeatherParticles particles)
    {
        Godot.Environment? environment = dayNight.World?.Environment;
        weather.Forced = WeatherKind.Clear;
        await WaitSeconds(0.2f);
        float clearFog = environment?.FogDensity ?? 0.0f;
        float clearSun = dayNight.Sun?.LightEnergy ?? 0.0f;
        weather.Forced = WeatherKind.Fog;
        await WaitSeconds(0.2f);
        float fogDensity = environment?.FogDensity ?? 0.0f;
        float fogSun = dayNight.Sun?.LightEnergy ?? 0.0f;
        Check("fog closes in and dims the sun", fogDensity > clearFog * 3.0f && fogSun < clearSun * 0.6f,
            $"fog density {clearFog:0.000} → {fogDensity:0.000}, sun {clearSun:0.00} → {fogSun:0.00}");

        weather.Forced = WeatherKind.Rain;
        await WaitSeconds(0.2f);
        bool rainFalls = particles.Rain != null && particles.Rain.Emitting && particles.Snow != null && !particles.Snow.Emitting;
        weather.Forced = WeatherKind.Snow;
        await WaitSeconds(0.2f);
        bool snowFalls = particles.Snow != null && particles.Snow.Emitting && particles.Rain != null && !particles.Rain.Emitting;
        Check("rain and snow fall when they should", rainFalls && snowFalls, $"rain {rainFalls}, snow {snowFalls}");
        weather.Forced = WeatherKind.Clear;
    }

    private static void PublishLoad(float mass)
    {
        Vector3 center = new(0.0f, 1.3f, 0.0f);
        float inertia = mass * center.Y * center.Y;
        GameEvents.PublishLoad(new LoadSnapshot($"Test {mass:0} kg", mass, center, inertia, inertia,
            new[] { mass }, new[] { center }, new[] { new Vector3(0.4f, 0.6f, 0.3f) }));
    }

    private static float Difference(WeatherSnapshot a, WeatherSnapshot b)
    {
        float worst = Mathf.Abs(a.Fog - b.Fog);
        worst = Mathf.Max(worst, Mathf.Abs(a.Rain - b.Rain));
        worst = Mathf.Max(worst, Mathf.Abs(a.Snow - b.Snow));
        worst = Mathf.Max(worst, Mathf.Abs(a.Heat - b.Heat));
        worst = Mathf.Max(worst, Mathf.Abs(a.GripFactor - b.GripFactor));
        // Wind in units of 10 m/s, so a 1.5 m/s change counts like a 0.15 change in intensity.
        worst = Mathf.Max(worst, (a.Wind - b.Wind).Length() * 0.1f);
        return worst;
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
