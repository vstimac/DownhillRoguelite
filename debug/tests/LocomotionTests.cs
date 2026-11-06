using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the M1 locomotion done-when criteria on the test course. Drives the player
/// with simulated input, prints PASS/FAIL per check and quits with exit code 1 on any failure.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/LocomotionTests.tscn
/// </summary>
public partial class LocomotionTests : Node3D
{
    private static readonly StringName ActionForward = "player_move_forward";
    private static readonly StringName ActionBrake = "player_brake";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TestCourseBuilder? Course { get; set; }

    [ExportGroup("Durations (s)")]
    [Export] public float SettleTime { get; set; } = 0.3f;
    [Export] public float StairWalkTime { get; set; } = 4.0f;
    [Export] public float SlopeHoldTime { get; set; } = 2.0f;
    [Export] public float SpeedTestTime { get; set; } = 3.0f;
    [Export] public float SpeedMeasureWindow { get; set; } = 1.0f;
    [Export] public float CrestWalkTime { get; set; } = 5.0f;
    [Export] public float CrossSlopeWalkTime { get; set; } = 4.0f;
    [Export] public float RockFieldWalkTime { get; set; } = 6.0f;

    [ExportGroup("Criteria")]
    /// <summary>Allowed shortfall when checking a staircase was fully climbed, in m.</summary>
    [Export] public float ClimbTolerance { get; set; } = 0.05f;
    /// <summary>Max drift on a walkable slope with no input, in m.</summary>
    [Export] public float MaxHoldDrift { get; set; } = 0.3f;
    /// <summary>Min slide distance on a too-steep slope with no input, in m.</summary>
    [Export] public float MinSlideDistance { get; set; } = 2.0f;
    /// <summary>Stride speed must be at least this multiple of brake speed on the speed-test lane.</summary>
    [Export] public float MinStrideToBrakeRatio { get; set; } = 2.5f;
    /// <summary>Allowed brake overshoot as a fraction of BrakeSpeed.</summary>
    [Export] public float BrakeTolerance { get; set; } = 0.15f;
    [Export] public int SpeedTestLane { get; set; } = 1;
    [Export] public int CrestTestLane { get; set; } = 2;
    /// <summary>Longest time off the ground allowed when walking over a crest or bumps, in s.</summary>
    [Export] public float MaxCrestAirTime { get; set; } = 0.2f;
    [Export] public float MaxRockAirTime { get; set; } = 0.5f;
    /// <summary>Max upward speed: anything above means the body was launched, in m/s.</summary>
    [Export] public float MaxCrestUpwardSpeed { get; set; } = 1.0f;
    [Export] public float MaxRockUpwardSpeed { get; set; } = 3.0f;
    [Export] public float MinCrossSlopeDistance { get; set; } = 8.0f;
    [Export] public float MinRockFieldDistance { get; set; } = 6.0f;

    private readonly List<string> _failures = new();

    private struct RunStats
    {
        public Vector3 Start;
        public Vector3 End;
        public float MaxY;
        public float MaxUpwardSpeed;
        public float MaxAirTime;
        public float WindowSpeed;
    }

    public override async void _Ready()
    {
        if (PlayerBody == null || Course == null)
        {
            GD.PushError("LocomotionTests: PlayerBody and Course must be assigned.");
            GetTree().Quit(1);
            return;
        }

        await WaitSeconds(SettleTime);

        await TestStairs(PlayerBody, Course);
        await TestSlopeHold(PlayerBody, Course);
        await TestBrakeVersusStride(PlayerBody, Course);
        await TestCrest(PlayerBody, Course);
        await TestCrossSlope(PlayerBody, Course);
        await TestRockField(PlayerBody, Course);

        if (_failures.Count == 0)
        {
            GD.Print("LOCOMOTION TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"LOCOMOTION TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private async Task TestStairs(PlayerController player, TestCourseBuilder course)
    {
        for (int stair = 0; stair < course.StepHeights.Length; stair++)
        {
            float rise = course.StepHeights[stair];
            await Place(player, course.StairStart(stair));
            RunStats stats = await Run(player, StairWalkTime, true, false);
            float climbed = stats.MaxY - stats.Start.Y;
            string name = $"stairs {rise * 100.0f:0} cm";

            if (rise <= player.Settings.StepHeight)
            {
                float expected = rise * course.StepCount;
                Check(name + " climbed", climbed >= expected - ClimbTolerance, $"climbed {climbed:0.00} m of {expected:0.00} m");
            }
            else
            {
                Check(name + " blocked", climbed < rise * 0.5f, $"climbed {climbed:0.00} m");
            }
        }
    }

    private async Task TestSlopeHold(PlayerController player, TestCourseBuilder course)
    {
        for (int lane = 0; lane < course.LaneAngles.Length; lane++)
        {
            float angle = course.LaneAngles[lane];
            await Place(player, course.LanePoint(lane, course.LaneLength(lane) * 0.5f));
            RunStats stats = await Run(player, SlopeHoldTime, false, false);
            float drift = Flat(stats.End - stats.Start).Length();

            if (angle <= player.Settings.MaxWalkableAngle)
            {
                Check($"{angle:0}° holds still", drift < MaxHoldDrift, $"drifted {drift:0.00} m");
            }
            else
            {
                Check($"{angle:0}° slides", drift > MinSlideDistance, $"slid {drift:0.00} m");
            }
        }
    }

    private async Task TestBrakeVersusStride(PlayerController player, TestCourseBuilder course)
    {
        float angle = course.LaneAngles[SpeedTestLane];

        await Place(player, course.LanePoint(SpeedTestLane, 3.0f));
        RunStats brake = await Run(player, SpeedTestTime, true, true);

        await Place(player, course.LanePoint(SpeedTestLane, 3.0f));
        RunStats stride = await Run(player, SpeedTestTime, true, false);

        float brakeLimit = player.Settings.BrakeSpeed * (1.0f + BrakeTolerance);
        Check($"brake on {angle:0}°", brake.WindowSpeed <= brakeLimit, $"{brake.WindowSpeed:0.00} m/s (limit {brakeLimit:0.00})");
        Check($"stride on {angle:0}°", stride.WindowSpeed >= player.Settings.StrideSpeed, $"{stride.WindowSpeed:0.00} m/s");
        Check($"stride vs brake on {angle:0}°", stride.WindowSpeed >= brake.WindowSpeed * MinStrideToBrakeRatio,
            $"ratio {stride.WindowSpeed / Mathf.Max(brake.WindowSpeed, Mathf.Epsilon):0.0}");
    }

    private async Task TestCrest(PlayerController player, TestCourseBuilder course)
    {
        float angle = course.LaneAngles[CrestTestLane];
        Vector3 start = new(course.LaneX(CrestTestLane), course.Drop, course.StartDepth * 0.25f);
        await Place(player, start);
        RunStats stats = await Run(player, CrestWalkTime, true, false);

        Check($"crest onto {angle:0}° stays grounded", stats.MaxAirTime <= MaxCrestAirTime, $"longest air time {stats.MaxAirTime:0.00} s");
        Check($"crest onto {angle:0}° no launch", stats.MaxUpwardSpeed <= MaxCrestUpwardSpeed, $"max upward {stats.MaxUpwardSpeed:0.00} m/s");
        Check($"crest onto {angle:0}° descends", stats.End.Y < stats.Start.Y - 1.0f, $"descended {stats.Start.Y - stats.End.Y:0.0} m");
    }

    private async Task TestCrossSlope(PlayerController player, TestCourseBuilder course)
    {
        await Place(player, course.CrossSlopeStart());
        RunStats stats = await Run(player, CrossSlopeWalkTime, true, false);
        float distance = Flat(stats.End - stats.Start).Length();
        float sideways = Mathf.Abs(stats.End.X - stats.Start.X);

        Check("cross slope walkable", distance >= MinCrossSlopeDistance, $"walked {distance:0.0} m");
        Check("cross slope no sideways drift", sideways < MaxHoldDrift, $"drifted {sideways:0.00} m sideways");
    }

    private async Task TestRockField(PlayerController player, TestCourseBuilder course)
    {
        await Place(player, course.RockFieldStart());
        RunStats stats = await Run(player, RockFieldWalkTime, true, false);
        float distance = Flat(stats.End - stats.Start).Length();

        Check("rock field not stuck", distance >= MinRockFieldDistance, $"walked {distance:0.0} m");
        Check("rock field no launch", stats.MaxUpwardSpeed <= MaxRockUpwardSpeed, $"max upward {stats.MaxUpwardSpeed:0.00} m/s");
        Check("rock field stays grounded", stats.MaxAirTime <= MaxRockAirTime, $"longest air time {stats.MaxAirTime:0.00} s");
    }

    private async Task Place(PlayerController player, Vector3 position)
    {
        // Lift slightly so the capsule never starts inside the surface; it settles before measuring.
        player.Teleport(position + Vector3.Up * 0.05f, 0.0f);
        await WaitSeconds(SettleTime);
    }

    private async Task<RunStats> Run(PlayerController player, float seconds, bool forward, bool brake)
    {
        if (forward)
        {
            Input.ActionPress(ActionForward);
        }
        if (brake)
        {
            Input.ActionPress(ActionBrake);
        }

        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        RunStats stats = new() { Start = player.GlobalPosition, MaxY = player.GlobalPosition.Y };
        float airTime = 0.0f;
        float windowDistance = 0.0f;
        float windowTime = 0.0f;
        Vector3 previous = player.GlobalPosition;

        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Vector3 position = player.GlobalPosition;

            stats.MaxY = Mathf.Max(stats.MaxY, position.Y);
            stats.MaxUpwardSpeed = Mathf.Max(stats.MaxUpwardSpeed, player.GetRealVelocity().Y);

            if (player.IsOnFloor())
            {
                airTime = 0.0f;
            }
            else
            {
                airTime += dt;
                stats.MaxAirTime = Mathf.Max(stats.MaxAirTime, airTime);
            }

            if (elapsed >= seconds - SpeedMeasureWindow)
            {
                windowDistance += Flat(position - previous).Length();
                windowTime += dt;
            }
            previous = position;
        }

        Input.ActionRelease(ActionForward);
        Input.ActionRelease(ActionBrake);

        stats.End = player.GlobalPosition;
        if (windowTime > 0.0f)
        {
            stats.WindowSpeed = windowDistance / windowTime;
        }
        return stats;
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

    private static Vector3 Flat(Vector3 vector)
    {
        return new Vector3(vector.X, 0.0f, vector.Z);
    }
}
