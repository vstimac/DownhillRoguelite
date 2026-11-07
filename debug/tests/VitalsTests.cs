using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M4: sprint drains stamina into exhaustion and recovers, a load lowers the
/// jump, drops below the hard-landing height don't hurt, the same drop hurts much more on rock than
/// on grass (ragdoll impacts × surface hardness), and death collapses the carrier and respawns it.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/VitalsTests.tscn
/// </summary>
public partial class VitalsTests : Node3D
{
    private static readonly StringName ActionForward = "player_move_forward";
    private static readonly StringName ActionSprint = "player_sprint";
    private static readonly StringName ActionJump = "player_jump";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TestCourseBuilder? Course { get; set; }
    [Export] public LoadCycler? Loads { get; set; }

    [ExportGroup("Scenario")]
    [Export] public Vector3 FlatStart { get; set; } = new(0.0f, 0.0f, -60.0f);
    [Export] public int EmptyLoad { get; set; } = 0;
    [Export] public int HeavyLoad { get; set; } = 1;
    [Export] public float SettleTime { get; set; } = 0.8f;
    [Export] public float SprintCheckTime { get; set; } = 2.5f;
    [Export] public float ExhaustTimeout { get; set; } = 12.0f;
    [Export] public float RecoverTimeout { get; set; } = 6.0f;
    [Export] public float JumpWatchTime { get; set; } = 1.5f;
    [Export] public float DropWalkTime { get; set; } = 2.0f;
    [Export] public float DropWatchTime { get; set; } = 9.0f;
    [Export] public int LowDrop { get; set; } = 0;
    [Export] public int HighDrop { get; set; } = 2;

    [ExportGroup("Criteria")]
    [Export] public float MinSprintSpeedFraction { get; set; } = 0.9f;
    [Export] public float MinUnloadedJump { get; set; } = 0.4f;
    /// <summary>A loaded jump must reach at most this fraction of the unloaded height.</summary>
    [Export] public float MaxLoadedJumpFraction { get; set; } = 0.8f;
    /// <summary>Rock damage must be at least this multiple of grass damage for the same drop.</summary>
    [Export] public float MinRockToGrassRatio { get; set; } = 2.0f;
    [Export] public float MinHighDropDamage { get; set; } = 3.0f;

    private readonly System.Collections.Generic.List<string> _failures = new();
    private CarrierHealth? _health;
    private CarrierStamina? _stamina;
    private PlayerRagdoll? _ragdoll;
    private float _damageTaken;

    public override async void _Ready()
    {
        if (PlayerBody == null || Course == null || Loads == null)
        {
            GD.PushError("VitalsTests: PlayerBody, Course and Loads must be assigned.");
            GetTree().Quit(1);
            return;
        }
        _health = PlayerBody.Health;
        _stamina = PlayerBody.Stamina;
        _ragdoll = PlayerBody.GetNodeOrNull<PlayerRagdoll>("Ragdoll");
        if (_health == null || _stamina == null || _ragdoll == null)
        {
            GD.PushError("VitalsTests: the player needs Health, Stamina and Ragdoll.");
            GetTree().Quit(1);
            return;
        }

        GameEvents.Damaged += OnDamaged;
        await WaitSeconds(SettleTime);

        await TestSprint(PlayerBody, _stamina);
        await TestJump(PlayerBody, Loads);
        await TestDrops(PlayerBody, Course, Loads);
        await TestDeath(PlayerBody, _health);

        GameEvents.Damaged -= OnDamaged;
        if (_failures.Count == 0)
        {
            GD.Print("VITALS TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"VITALS TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnDamaged(float amount)
    {
        _damageTaken += amount;
    }

    private async Task TestSprint(PlayerController player, CarrierStamina stamina)
    {
        await Place(player, EmptyLoad, FlatStart);
        Input.ActionPress(ActionForward);
        Input.ActionPress(ActionSprint);
        await WaitSeconds(SprintCheckTime);
        float speed = FlatSpeed(player);
        float drained = stamina.Max - stamina.Current;
        Check("sprint is faster and drains stamina",
            speed >= player.Settings.SprintSpeed * MinSprintSpeedFraction && drained > 0.0f,
            $"{speed:0.00} m/s, stamina {stamina.Current:0}/{stamina.Max:0}");

        float elapsed = await WaitUntil(() => stamina.Exhausted, ExhaustTimeout);
        await WaitSeconds(0.5f);
        float tiredSpeed = FlatSpeed(player);
        Check("sprinting until empty exhausts and drops to a stride",
            stamina.Exhausted && tiredSpeed < player.Settings.SprintSpeed * MinSprintSpeedFraction,
            $"exhausted after {SprintCheckTime + elapsed:0.0} s of sprinting, then {tiredSpeed:0.00} m/s");

        Input.ActionRelease(ActionSprint);
        Input.ActionRelease(ActionForward);
        float recovered = await WaitUntil(() => !stamina.Exhausted, RecoverTimeout);
        Check("stamina recovers after resting", !stamina.Exhausted, $"usable again after {recovered:0.0} s, stamina {stamina.Current:0}");
    }

    private async Task TestJump(PlayerController player, LoadCycler loads)
    {
        float unloaded = await JumpHeight(player, loads, EmptyLoad);
        float loaded = await JumpHeight(player, loads, HeavyLoad);
        Check("unloaded jump clears a meaningful height", unloaded >= MinUnloadedJump, $"{unloaded:0.00} m");
        Check("a load lowers the jump", loaded <= unloaded * MaxLoadedJumpFraction && loaded > 0.0f,
            $"{loaded:0.00} m with {GameEvents.CurrentLoad.Mass:0} kg vs {unloaded:0.00} m empty");
    }

    private async Task<float> JumpHeight(PlayerController player, LoadCycler loads, int load)
    {
        await Place(player, load, FlatStart);
        float start = player.GlobalPosition.Y;
        Input.ActionPress(ActionJump);
        await WaitSeconds(0.1f);
        Input.ActionRelease(ActionJump);

        float peak = start;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float t = 0.0f; t < JumpWatchTime; t += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            peak = Mathf.Max(peak, player.GlobalPosition.Y);
        }
        return peak - start;
    }

    private async Task TestDrops(PlayerController player, TestCourseBuilder course, LoadCycler loads)
    {
        int grass = course.DropSurfaceIndex("Grass");
        int rock = course.DropSurfaceIndex("Rock");
        if (grass < 0 || rock < 0)
        {
            Check("drop zone", false, "grass/rock drop rows not found");
            return;
        }

        (float lowDamage, bool lowDowned) = await Drop(player, loads, course, rock, LowDrop);
        Check($"{course.DropHeights[LowDrop]:0} m drop onto rock: no ragdoll, no damage", !lowDowned && lowDamage <= 0.0f,
            $"downed {lowDowned}, damage {lowDamage:0.0}");

        (float grassDamage, bool grassDowned) = await Drop(player, loads, course, grass, HighDrop);
        (float rockDamage, bool rockDowned) = await Drop(player, loads, course, rock, HighDrop);
        string height = $"{course.DropHeights[HighDrop]:0} m";
        Check($"{height} drop goes down as a ragdoll", grassDowned && rockDowned, $"grass {grassDowned}, rock {rockDowned}");
        Check($"{height} drop onto rock hurts", rockDamage >= MinHighDropDamage, $"{rockDamage:0.0} damage");
        Check($"{height} drop: rock hurts much more than grass", rockDamage >= grassDamage * MinRockToGrassRatio,
            $"rock {rockDamage:0.0} vs grass {grassDamage:0.0}");

        // Drop() waits until the carrier is back up (or DropWatchTime runs out).
        bool up = !GameEvents.IsDowned;
        Check("gets back up after a survivable fall", up && _health != null && !_health.Dead, $"downed {GameEvents.IsDowned}, health {_health?.Current:0}");
    }

    /// <summary>Walks off a tower; returns damage taken and whether the carrier went down.</summary>
    private async Task<(float Damage, bool Downed)> Drop(PlayerController player, LoadCycler loads, TestCourseBuilder course, int surface, int height)
    {
        await Place(player, EmptyLoad, course.DropStart(surface, height));
        _damageTaken = 0.0f;
        bool downed = false;

        Input.ActionPress(ActionForward);
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float t = 0.0f; t < DropWatchTime; t += dt)
        {
            if (t >= DropWalkTime)
            {
                Input.ActionRelease(ActionForward);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            downed = downed || GameEvents.IsDowned;
            if (downed && !GameEvents.IsDowned)
            {
                break;
            }
        }
        Input.ActionRelease(ActionForward);
        return (_damageTaken, downed);
    }

    private async Task TestDeath(PlayerController player, CarrierHealth health)
    {
        await Place(player, EmptyLoad, FlatStart);
        health.TakeDamage(health.Max * 2.0f, "test");
        await WaitSeconds(0.2f);
        Check("zero health collapses the carrier", health.Dead && GameEvents.IsDowned, $"dead {health.Dead}, downed {GameEvents.IsDowned}");

        await WaitUntil(() => !health.Dead, GetNode<RunState>("/root/RunState").SummaryDelay + 1.0f);
        await WaitSeconds(0.2f);
        Check("dead carrier respawns healed and standing", !health.Dead && !GameEvents.IsDowned && health.Current >= health.Max,
            $"health {health.Current:0}, downed {GameEvents.IsDowned}");
    }

    private async Task Place(PlayerController player, int load, Vector3 position)
    {
        Loads!.Publish(load);
        player.Teleport(position + Vector3.Up * 0.05f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(SettleTime);
    }

    /// <summary>Waits until the condition holds or the timeout passes; returns the time waited.</summary>
    private async Task<float> WaitUntil(System.Func<bool> condition, float timeout)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        float elapsed = 0.0f;
        while (elapsed < timeout && !condition())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            elapsed += dt;
        }
        return elapsed;
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private static float FlatSpeed(PlayerController player)
    {
        return new Vector2(player.Velocity.X, player.Velocity.Z).Length();
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
