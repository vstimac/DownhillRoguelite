using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
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

    [ExportGroup("Foot slip")]
    /// <summary>Flat-ground start point with a clear run toward -Z.</summary>
    [Export] public Vector3 FootSlipStart { get; set; } = new(0.0f, 0.0f, -88.0f);
    [Export] public float FootSlipWarmup { get; set; } = 2.0f;
    [Export] public float FootSlipMeasureTime { get; set; } = 2.0f;
    /// <summary>A toe lower than this above the ground counts as planted, in m.</summary>
    [Export] public float PlantedToeHeight { get; set; } = 0.04f;
    /// <summary>Median planted-toe speed allowed, in m/s.</summary>
    [Export] public float MaxFootSlip { get; set; } = 0.35f;
    [Export] public string LeftToeBone { get; set; } = "DEF-toe.L";
    [Export] public string RightToeBone { get; set; } = "DEF-toe.R";

    [ExportGroup("Foot IK")]
    [Export] public float ContactRunTime { get; set; } = 4.0f;
    [Export] public int ContactStairIndex { get; set; } = 1;
    /// <summary>Median speed of a locked ankle after IK, in m/s.</summary>
    [Export] public float MaxPlantedSlip { get; set; } = 0.05f;
    /// <summary>Median distance between a planted ankle's height above the ground and its rest height, in m.</summary>
    [Export] public float MaxContactError { get; set; } = 0.03f;
    /// <summary>On the braked slope the hips must drop at least this much on average, in m.</summary>
    [Export] public float MinSlopeHipDrop { get; set; } = 0.03f;
    [Export] public float SlideBlendOutTime { get; set; } = 1.0f;
    [Export] public float MaxSlidingIkWeight { get; set; } = 0.05f;

    [ExportGroup("Surfaces")]
    [Export] public string GripSurface { get; set; } = "Rock";
    [Export] public string LooseSurface { get; set; } = "Scree";
    /// <summary>Min drift standing still on the loose surface lane, in m.</summary>
    [Export] public float MinLooseCreep { get; set; } = 0.4f;
    /// <summary>Stride time before releasing input in the stopping test, in s.</summary>
    [Export] public float StopTestStrideTime { get; set; } = 3.5f;
    [Export] public float StopTestMaxTime { get; set; } = 6.0f;
    /// <summary>Loose-surface stopping distance must be at least this multiple of the grip surface's.</summary>
    [Export] public float MinLooseStopRatio { get; set; } = 1.25f;

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
        await TestFootSlip(PlayerBody, false);
        await TestFootSlip(PlayerBody, true);

        await TestFootContact(PlayerBody, "flat stride", FootSlipStart, ContactRunTime, false);
        await TestFootContact(PlayerBody, $"{Course.LaneAngles[SpeedTestLane]:0}° downhill stride", Course.LanePoint(SpeedTestLane, 3.0f), ContactRunTime, false);
        await TestFootContact(PlayerBody, $"{Course.LaneAngles[CrestTestLane]:0}° downhill brake", Course.LanePoint(CrestTestLane, 3.0f), ContactRunTime, true);
        await TestFootContact(PlayerBody, $"stairs {Course.StepHeights[ContactStairIndex] * 100.0f:0} cm", Course.StairStart(ContactStairIndex), ContactRunTime, false);
        await TestFootContact(PlayerBody, "rock field", Course.RockFieldStart(), ContactRunTime, false);
        await TestIkBlendsOutWhenSliding(PlayerBody, Course);
        await TestSurfaces(PlayerBody, Course);

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

    /// <summary>
    /// Walks on flat ground and measures how fast each toe moves in world space while it is
    /// planted. Animation playback matched to body speed keeps this low; foot IK (M1.3) pins it.
    /// </summary>
    private async Task TestFootSlip(PlayerController player, bool brake)
    {
        string mode = "stride";
        if (brake)
        {
            mode = "brake";
        }

        Skeleton3D? skeleton = FindSkeleton(player);
        if (skeleton == null)
        {
            Check($"foot slip {mode}", false, "no Skeleton3D under the player");
            return;
        }
        int[] toes = { skeleton.FindBone(LeftToeBone), skeleton.FindBone(RightToeBone) };
        if (toes[0] < 0 || toes[1] < 0)
        {
            Check($"foot slip {mode}", false, "toe bones not found");
            return;
        }

        await Place(player, FootSlipStart);
        Input.ActionPress(ActionForward);
        if (brake)
        {
            Input.ActionPress(ActionBrake);
        }
        await WaitSeconds(FootSlipWarmup);

        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        float groundY = player.GlobalPosition.Y;
        Vector3[] previous = { ToeWorld(skeleton, toes[0]), ToeWorld(skeleton, toes[1]) };
        List<float> slips = new();

        for (float elapsed = 0.0f; elapsed < FootSlipMeasureTime; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (int i = 0; i < toes.Length; i++)
            {
                Vector3 toe = ToeWorld(skeleton, toes[i]);
                if (toe.Y - groundY < PlantedToeHeight)
                {
                    slips.Add(Flat(toe - previous[i]).Length() / dt);
                }
                previous[i] = toe;
            }
        }

        float bodySpeed = Flat(player.Velocity).Length();
        Input.ActionRelease(ActionForward);
        Input.ActionRelease(ActionBrake);

        if (slips.Count == 0)
        {
            Check($"foot slip {mode}", false, "no planted toe samples");
            return;
        }

        slips.Sort();
        float median = slips[slips.Count / 2];
        Check($"foot slip {mode}", median <= MaxFootSlip,
            $"median planted-toe speed {median:0.00} m/s ({slips.Count} samples, body {bodySpeed:0.00} m/s)");
    }

    /// <summary>
    /// Checks the final pose after foot IK: planted ankles stay put and sit at their rest height
    /// above the ground along its normal. Poses are read on SkeletonUpdated, after all modifiers.
    /// </summary>
    private async Task TestFootContact(PlayerController player, string name, Vector3 start, float seconds, bool brake)
    {
        FootIK? footIk = player.GetNodeOrNull<FootIK>("FootIK");
        Skeleton3D? skeleton = FindSkeleton(player);
        if (footIk == null || skeleton == null || footIk.Feet.Count < 2)
        {
            Check($"foot contact {name}", false, "FootIK or Skeleton3D missing");
            return;
        }

        await Place(player, start);

        PhysicsDirectSpaceState3D space = player.GetWorld3D().DirectSpaceState;
        Godot.Collections.Array<Rid> exclude = new() { player.GetRid() };
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        List<float> slips = new();
        List<float> errors = new();
        float hipOffsetSum = 0.0f;
        int hipSamples = 0;
        Vector3[] previous = new Vector3[footIk.Feet.Count];
        bool[] wasPlanted = new bool[footIk.Feet.Count];
        int[] toeIndices = { skeleton.FindBone(LeftToeBone), skeleton.FindBone(RightToeBone) };

        void OnSkeletonUpdated()
        {
            Transform3D skeletonWorld = skeleton.GetGlobalTransformInterpolated();
            for (int i = 0; i < footIk.Feet.Count; i++)
            {
                FootState foot = footIk.Feet[i];
                Vector3 ankle = (skeletonWorld * skeleton.GetBoneGlobalPose(foot.FootIndex)).Origin;
                bool planted = foot.Planted && foot.PlantWeight >= 1.0f && foot.GroundWeight >= 1.0f && footIk.IkWeight >= 1.0f;

                if (planted && wasPlanted[i])
                {
                    slips.Add(Flat(ankle - previous[i]).Length() / dt);
                }
                if (planted)
                {
                    // A planted sole touches through the heel or the toe, whichever is lower; a lifted
                    // heel during stance is correct motion, not a contact error.
                    Vector3 toe = (skeletonWorld * skeleton.GetBoneGlobalPose(toeIndices[i])).Origin;
                    float ankleGap = HeightAboveGround(space, exclude, ankle) - skeleton.GetBoneGlobalRest(foot.FootIndex).Origin.Y;
                    float toeGap = HeightAboveGround(space, exclude, toe) - skeleton.GetBoneGlobalRest(toeIndices[i]).Origin.Y;
                    float gap = Mathf.Min(ankleGap, toeGap);
                    if (!float.IsNaN(gap))
                    {
                        errors.Add(Mathf.Abs(gap));
                    }
                }

                wasPlanted[i] = planted;
                previous[i] = ankle;
            }
            hipOffsetSum += footIk.HipOffset;
            hipSamples++;
        }

        skeleton.SkeletonUpdated += OnSkeletonUpdated;
        await Run(player, seconds, true, brake);
        skeleton.SkeletonUpdated -= OnSkeletonUpdated;

        if (slips.Count == 0 || errors.Count == 0)
        {
            Check($"foot contact {name}", false, "no planted-foot samples");
            return;
        }

        slips.Sort();
        errors.Sort();
        float slip = slips[slips.Count / 2];
        float error = errors[errors.Count / 2];
        Check($"planted slip {name}", slip <= MaxPlantedSlip, $"median {slip:0.000} m/s ({slips.Count} samples)");
        Check($"contact error {name}", error <= MaxContactError, $"median {error * 100.0f:0.0} cm, 90th pct {errors[errors.Count * 9 / 10] * 100.0f:0.0} cm");

        if (brake)
        {
            float averageHip = hipOffsetSum / Mathf.Max(1, hipSamples);
            Check($"hips lower {name}", averageHip <= -MinSlopeHipDrop, $"average hip offset {averageHip * 100.0f:0.0} cm");
        }
    }

    private async Task TestIkBlendsOutWhenSliding(PlayerController player, TestCourseBuilder course)
    {
        FootIK? footIk = player.GetNodeOrNull<FootIK>("FootIK");
        if (footIk == null)
        {
            Check("IK blends out when sliding", false, "FootIK missing");
            return;
        }

        int steepLane = course.LaneAngles.Length - 1;
        await Place(player, course.LanePoint(steepLane, course.LaneLength(steepLane) * 0.3f));
        await WaitSeconds(SlideBlendOutTime);
        Check($"IK blends out sliding on {course.LaneAngles[steepLane]:0}°", footIk.IkWeight <= MaxSlidingIkWeight,
            $"IK weight {footIk.IkWeight:0.00} while {player.State}");
    }

    /// <summary>Same slope, different footing: the loose surface creeps and takes longer to stop.</summary>
    private async Task TestSurfaces(PlayerController player, TestCourseBuilder course)
    {
        int grip = course.SurfaceLaneIndex(GripSurface);
        int loose = course.SurfaceLaneIndex(LooseSurface);
        if (grip < 0 || loose < 0)
        {
            Check("surfaces", false, $"surface lanes '{GripSurface}' / '{LooseSurface}' not found");
            return;
        }
        string slope = $"{course.SurfaceLaneAngle:0}°";

        await Place(player, course.SurfaceLanePoint(grip, course.SurfaceLaneLength() * 0.5f));
        RunStats gripHold = await Run(player, SlopeHoldTime, false, false);
        float gripDrift = Flat(gripHold.End - gripHold.Start).Length();
        Check($"{GripSurface} {slope} holds still", gripDrift < MaxHoldDrift, $"drifted {gripDrift:0.00} m, grip {player.Grip:0.00}");

        await Place(player, course.SurfaceLanePoint(loose, course.SurfaceLaneLength() * 0.5f));
        RunStats looseHold = await Run(player, SlopeHoldTime, false, false);
        float looseDrift = Flat(looseHold.End - looseHold.Start).Length();
        Check($"{LooseSurface} {slope} holds still too: standing, the feet stay planted", looseDrift < MaxHoldDrift, $"crept {looseDrift:0.00} m, grip {player.Grip:0.00}");

        float gripStop = await StoppingDistance(player, course.SurfaceLanePoint(grip, 3.0f));
        float looseStop = await StoppingDistance(player, course.SurfaceLanePoint(loose, 3.0f));
        Check($"{LooseSurface} stops longer than {GripSurface} on {slope}", looseStop >= gripStop * MinLooseStopRatio,
            $"{LooseSurface} {looseStop:0.0} m vs {GripSurface} {gripStop:0.0} m");
    }

    /// <summary>Strides downhill, releases input, and measures the distance until the carrier is idle.</summary>
    private async Task<float> StoppingDistance(PlayerController player, Vector3 start)
    {
        await Place(player, start);
        await Run(player, StopTestStrideTime, true, false);

        Vector3 released = player.GlobalPosition;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < StopTestMaxTime && player.State != LocomotionState.Idle; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return Flat(player.GlobalPosition - released).Length();
    }

    /// <summary>Distance from a point to the ground below it, along the ground's normal; NaN if no ground.</summary>
    private static float HeightAboveGround(PhysicsDirectSpaceState3D space, Godot.Collections.Array<Rid> exclude, Vector3 point)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 0.5f, point + Vector3.Down * 0.5f, uint.MaxValue, exclude);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0)
        {
            return float.NaN;
        }
        return (point - hit["position"].AsVector3()).Dot(hit["normal"].AsVector3());
    }

    private static Skeleton3D? FindSkeleton(Node root)
    {
        foreach (Node node in root.FindChildren("*", "Skeleton3D", true, false))
        {
            if (node is Skeleton3D skeleton)
            {
                return skeleton;
            }
        }
        return null;
    }

    private static Vector3 ToeWorld(Skeleton3D skeleton, int bone)
    {
        return (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone)).Origin;
    }

    private async Task Place(PlayerController player, Vector3 position)
    {
        // Lift slightly so the capsule never starts inside the surface; it settles before measuring.
        player.Teleport(position + Vector3.Up * 0.05f, 0.0f);
        player.Health?.Refill();
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
