using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the M2 balance done-when criteria, including the A-vs-B test: the same
/// 20 kg packed high and to one side (A) or low and close (B) must make a clear difference.
/// Prints PASS/FAIL per check and quits with exit code 1 on any failure.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/BalanceTests.tscn
/// </summary>
public partial class BalanceTests : Node3D
{
    private static readonly StringName ActionForward = "player_move_forward";
    private static readonly StringName ActionBrake = "player_brake";
    private static readonly StringName ActionLeanLeft = "balance_lean_left";
    private static readonly StringName ActionLeanRight = "balance_lean_right";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TestCourseBuilder? Course { get; set; }
    [Export] public LoadCycler? Loads { get; set; }

    [ExportGroup("Loads (indices into LoadCycler.LoadPaths)")]
    [Export] public int EmptyLoad { get; set; } = 0;
    [Export] public int LoadA { get; set; } = 1;
    [Export] public int LoadB { get; set; } = 2;

    [ExportGroup("Scenario")]
    /// <summary>Flat-ground point with a clear run toward -Z.</summary>
    [Export] public Vector3 FlatStart { get; set; } = new(0.0f, 0.0f, -88.0f);
    [Export] public int DescentLane { get; set; } = 2;
    [Export] public float SettleTime { get; set; } = 1.0f;
    [Export] public float HoldTime { get; set; } = 3.0f;
    [Export] public float MeasureWindow { get; set; } = 1.0f;
    [Export] public float DescentStrideTime { get; set; } = 4.0f;
    [Export] public float DescentBrakeTime { get; set; } = 2.0f;
    [Export] public float FlatStrideTime { get; set; } = 2.5f;
    [Export] public float StopObserveTime { get; set; } = 1.5f;

    [ExportGroup("Criteria")]
    [Export] public float MaxEmptyImbalance { get; set; } = 0.3f;
    [Export] public float MaxLowLoadImbalance { get; set; } = 0.35f;
    /// <summary>Load A at rest must lean toward its heavy side at least this much without input.</summary>
    [Export] public float MinHighLoadDrift { get; set; } = 0.5f;
    /// <summary>With a counter-lean held, load A's sideways imbalance must stay under this.</summary>
    [Export] public float MaxCounterLeanImbalance { get; set; } = 0.35f;
    /// <summary>Peak forward imbalance on stopping: A must exceed B by this factor.</summary>
    [Export] public float MinStopPitchRatio { get; set; } = 1.3f;
    /// <summary>Sideways knock used to force a stumble, in rad/s.</summary>
    [Export] public float StumbleKnock { get; set; } = 0.6f;
    /// <summary>Smaller knock used to compare bracing, in rad/s.</summary>
    [Export] public float BraceKnock { get; set; } = 0.5f;
    [Export] public float MaxBracedRatio { get; set; } = 0.75f;
    [Export] public float MaxFallenSpeed { get; set; } = 0.3f;

    private readonly List<string> _failures = new();
    private readonly List<BalanceSnapshot> _samples = new();
    private BalanceController? _balance;

    public override async void _Ready()
    {
        // These tests check the raw balance model: no automatic steadying while standing.
        BalanceController? raw = PlayerBody?.GetNodeOrNull<BalanceController>("Balance");
        if (raw != null)
        {
            raw.SteadyWhenStanding = false;
        }
        if (PlayerBody == null || Course == null || Loads == null)
        {
            GD.PushError("BalanceTests: PlayerBody, Course and Loads must be assigned.");
            GetTree().Quit(1);
            return;
        }
        _balance = PlayerBody.GetNodeOrNull<BalanceController>("Balance");
        if (_balance == null)
        {
            GD.PushError("BalanceTests: the player has no Balance node.");
            GetTree().Quit(1);
            return;
        }

        GameEvents.BalanceUpdated += OnBalanceUpdated;
        await WaitSeconds(SettleTime);

        await TestRestingLoads(PlayerBody, Loads);
        await TestCounterLean(PlayerBody, Loads);
        await TestStoppingPitch(PlayerBody, Loads);
        await TestDescent(PlayerBody, Course, Loads);
        await TestBrace(PlayerBody, Loads, _balance);
        await TestStumble(PlayerBody, Loads, _balance);
        await TestStandingSteady(PlayerBody, Loads, _balance);

        GameEvents.BalanceUpdated -= OnBalanceUpdated;
        if (_failures.Count == 0)
        {
            GD.Print("BALANCE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"BALANCE TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        _samples.Add(snapshot);
    }

    /// <summary>At rest on flat ground: empty and B stay stable, A leans toward its heavy side.</summary>
    private async Task TestRestingLoads(PlayerController player, LoadCycler loads)
    {
        await Place(player, loads, EmptyLoad, FlatStart);
        await Hold(HoldTime);
        float empty = PeakMagnitude(_samples);
        Check("empty load stable at rest", empty <= MaxEmptyImbalance, $"peak imbalance {empty:0.00}");

        await Place(player, loads, LoadB, FlatStart);
        await Hold(HoldTime);
        float low = PeakMagnitude(_samples);
        Check("load B stable at rest", low <= MaxLowLoadImbalance, $"peak imbalance {low:0.00}");

        await Place(player, loads, LoadA, FlatStart);
        await Hold(HoldTime);
        float drift = AverageRoll(LastWindow());
        Check("load A drifts to its heavy side without input", drift >= MinHighLoadDrift,
            $"average roll imbalance {drift:+0.00;-0.00}, state {_samples[^1].State}");
    }

    /// <summary>Load A on flat ground: holding the counter-lean keeps it steady.</summary>
    private async Task TestCounterLean(PlayerController player, LoadCycler loads)
    {
        await Place(player, loads, LoadA, FlatStart);
        Input.ActionPress(ActionLeanLeft);
        await Hold(HoldTime);
        Input.ActionRelease(ActionLeanLeft);

        float roll = Mathf.Abs(AverageRoll(LastWindow()));
        Check("load A steadied by counter-lean", roll <= MaxCounterLeanImbalance, $"average |roll imbalance| {roll:0.00}");
    }

    /// <summary>Stopping from a stride pitches the load forward, more for the high load.</summary>
    private async Task TestStoppingPitch(PlayerController player, LoadCycler loads)
    {
        // Load A also needs its counter-lean while walking, or it tips sideways before it stops.
        float high = await StopPitch(player, loads, LoadA, true);
        float low = await StopPitch(player, loads, LoadB, false);
        Check("stopping pitches load A forward more than B", high >= low * MinStopPitchRatio,
            $"peak forward imbalance A {high:0.00} vs B {low:0.00}");
    }

    /// <summary>Peak forward swing of the pitch imbalance after releasing input, above the striding baseline.</summary>
    private async Task<float> StopPitch(PlayerController player, LoadCycler loads, int load, bool counterLean)
    {
        await Place(player, loads, load, FlatStart);
        if (counterLean)
        {
            Input.ActionPress(ActionLeanLeft);
        }
        Input.ActionPress(ActionForward);
        await WaitSeconds(FlatStrideTime);
        float baseline = _samples[^1].Imbalance.Y;
        Input.ActionRelease(ActionForward);
        _samples.Clear();
        await WaitSeconds(StopObserveTime);
        Input.ActionRelease(ActionLeanLeft);

        float peak = 0.0f;
        foreach (BalanceSnapshot sample in _samples)
        {
            peak = Mathf.Max(peak, sample.Imbalance.Y - baseline);
        }
        return peak;
    }

    /// <summary>The A-vs-B test: a 30° descent with no lean input. A loses balance, B doesn't.</summary>
    private async Task TestDescent(PlayerController player, TestCourseBuilder course, LoadCycler loads)
    {
        string lane = $"{course.LaneAngles[DescentLane]:0}°";
        BalanceState high = await DescentWorstState(player, course, loads, LoadA);
        BalanceState low = await DescentWorstState(player, course, loads, LoadB);
        Check($"A-vs-B: load A loses balance on {lane} descent", high >= BalanceState.Stumbling, $"worst state {high}");
        Check($"A-vs-B: load B keeps balance on {lane} descent", low <= BalanceState.Wobbling, $"worst state {low}");
    }

    private async Task<BalanceState> DescentWorstState(PlayerController player, TestCourseBuilder course, LoadCycler loads, int load)
    {
        await Place(player, loads, load, course.LanePoint(DescentLane, 3.0f));
        Input.ActionPress(ActionForward);
        await WaitSeconds(DescentStrideTime);
        Input.ActionPress(ActionBrake);
        await WaitSeconds(DescentBrakeTime);
        Input.ActionRelease(ActionForward);
        Input.ActionRelease(ActionBrake);

        BalanceState worst = BalanceState.Stable;
        float peak = 0.0f;
        foreach (BalanceSnapshot sample in _samples)
        {
            if (sample.State > worst)
            {
                worst = sample.State;
            }
            peak = Mathf.Max(peak, sample.ImbalanceMagnitude);
        }
        GD.Print($"      load {load}: peak imbalance {peak:0.00}, worst {worst}");
        return worst;
    }

    /// <summary>The same knock sways a braced carrier less.</summary>
    private async Task TestBrace(PlayerController player, LoadCycler loads, BalanceController balance)
    {
        await Place(player, loads, LoadA, FlatStart);
        balance.ApplyImpulse(new Vector2(-BraceKnock, 0.0f));
        await Hold(HoldTime);
        float free = PeakMagnitude(_samples);

        await Place(player, loads, LoadA, FlatStart);
        Input.ActionPress(ActionLeanLeft);
        Input.ActionPress(ActionLeanRight);
        await WaitSeconds(0.2f);
        balance.ApplyImpulse(new Vector2(-BraceKnock, 0.0f));
        await Hold(HoldTime);
        Input.ActionRelease(ActionLeanLeft);
        Input.ActionRelease(ActionLeanRight);
        float braced = PeakMagnitude(_samples);

        Check("bracing reduces sway from a knock", braced <= free * MaxBracedRatio, $"peak imbalance braced {braced:0.00} vs free {free:0.00}");
    }

    /// <summary>Standing still with the lopsided pack and no push, the carrier steadies stumbles himself and never falls.</summary>
    private async Task TestStandingSteady(PlayerController player, LoadCycler loads, BalanceController balance)
    {
        balance.SteadyWhenStanding = true;
        await Place(player, loads, LoadA, FlatStart);
        bool fell = await WaitForState(balance, BalanceState.Fallen, 8.0f);
        balance.SteadyWhenStanding = false;
        Check("standing still with a lopsided pack and no push, he never falls", !fell, $"fell {fell} within 8 s of standing");
    }

    /// <summary>A stumble recovers with a quick counter-lean and becomes a fall without one.</summary>
    private async Task TestStumble(PlayerController player, LoadCycler loads, BalanceController balance)
    {
        // Ignored: knock toward the heavy side and do nothing.
        await Place(player, loads, LoadA, FlatStart);
        balance.ApplyImpulse(new Vector2(StumbleKnock, 0.0f));
        bool stumbled = await WaitForState(balance, BalanceState.Stumbling, 1.0f);
        bool fell = await WaitForState(balance, BalanceState.Fallen, balance.Settings.StumbleWindow + 0.5f);
        Check("ignored stumble becomes a fall", stumbled && fell, $"stumbled {stumbled}, fell {fell}");

        if (fell)
        {
            Input.ActionPress(ActionForward);
            await WaitSeconds(1.0f);
            float speed = new Vector2(player.Velocity.X, player.Velocity.Z).Length();
            Input.ActionRelease(ActionForward);
            Check("no movement while fallen", speed <= MaxFallenSpeed && player.State == LocomotionState.Fallen,
                $"speed {speed:0.00} m/s, locomotion {player.State}");

            // Falls are ragdolls now: the carrier is up once the body settles, at most MaxDownTime.
            float getUpTimeout = balance.Settings.FallDuration + 0.5f;
            PlayerRagdoll? ragdoll = player.GetNodeOrNull<PlayerRagdoll>("Ragdoll");
            if (ragdoll != null)
            {
                getUpTimeout = ragdoll.MaxDownTime + ragdoll.GetUpBlendTime + 0.5f;
            }
            bool up = await WaitForState(balance, BalanceState.Stable, getUpTimeout);
            await WaitSeconds(0.1f);
            Check("gets back up after a fall", up && player.State != LocomotionState.Fallen, $"balance {balance.State}, locomotion {player.State}");
        }

        // Answered: the same knock, countered as soon as the stumble starts.
        await Place(player, loads, LoadA, FlatStart);
        balance.ApplyImpulse(new Vector2(StumbleKnock, 0.0f));
        bool stumbledAgain = await WaitForState(balance, BalanceState.Stumbling, 1.0f);
        Input.ActionPress(ActionLeanLeft);
        bool fellAnyway = await WaitForState(balance, BalanceState.Fallen, balance.Settings.StumbleWindow + 1.0f);
        Input.ActionRelease(ActionLeanLeft);
        Check("counter-leaned stumble recovers", stumbledAgain && !fellAnyway, $"stumbled {stumbledAgain}, fell {fellAnyway}, now {balance.State}");
    }

    private async Task Place(PlayerController player, LoadCycler loads, int load, Vector3 position)
    {
        loads.Publish(load);
        player.Teleport(position + Vector3.Up * 0.05f, 0.0f);
        player.Health?.Refill();
        _balance?.ResetBalance();
        await WaitSeconds(SettleTime);
        _samples.Clear();
    }

    private async Task Hold(float seconds)
    {
        _samples.Clear();
        await WaitSeconds(seconds);
    }

    private async Task<bool> WaitForState(BalanceController balance, BalanceState state, float timeout)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < timeout; elapsed += dt)
        {
            if (balance.State == state)
            {
                return true;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return balance.State == state;
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private List<BalanceSnapshot> LastWindow()
    {
        int count = Mathf.Min(_samples.Count, Mathf.RoundToInt(MeasureWindow * Engine.PhysicsTicksPerSecond));
        return _samples.GetRange(_samples.Count - count, count);
    }

    private static float PeakMagnitude(List<BalanceSnapshot> samples)
    {
        float peak = 0.0f;
        foreach (BalanceSnapshot sample in samples)
        {
            peak = Mathf.Max(peak, sample.ImbalanceMagnitude);
        }
        return peak;
    }

    private static float AverageRoll(List<BalanceSnapshot> samples)
    {
        if (samples.Count == 0)
        {
            return 0.0f;
        }
        float sum = 0.0f;
        foreach (BalanceSnapshot sample in samples)
        {
            sum += sample.Imbalance.X;
        }
        return sum / samples.Count;
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
