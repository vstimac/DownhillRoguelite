using System;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Walks the actual main scene, including terrain streaming, needs, balance, wildlife and HUD.</summary>
public partial class DescentPlaytestTests : Node3D
{
    [Export] public float WalkSeconds { get; set; } = 30.0f;
    [Export] public float MinDistance { get; set; } = 40.0f;
    [Export] public float StationWalkSeconds { get; set; } = 600.0f;
    /// <summary>Headroom for sliding, collision recovery and ordinary environmental pushes.</summary>
    [Export] public float UnforcedSpeedSafetyFactor { get; set; } = 2.0f;
    /// <summary>Extra metres permitted for a step/ground correction, and once per recorded fall.</summary>
    [Export] public float PositionCorrectionAllowance { get; set; } = 3.0f;
    [Export] public int MaxAnomalyReports { get; set; } = 6;

    public override async void _Ready()
    {
        PlayerController player = GetNode<PlayerController>("World/Player");
        RunState run = GetNode<RunState>("/root/RunState");
        BodyDynamics dynamics = player.GetNode<BodyDynamics>("Model/Rig/Skeleton3D/BodyDynamics");
        await Seconds(1.0f);
        if (run.Route == null)
        {
            GD.Print("FAIL  main scene did not provide a route");
            GetTree().Quit(1);
            return;
        }
        Vector3 start = player.GlobalPosition;
        bool firstStation = Array.IndexOf(OS.GetCmdlineUserArgs(), "--first-station") >= 0;
        float duration = WalkSeconds;
        if (firstStation)
        {
            duration = StationWalkSeconds;
        }
        Vector3 station = run.Route.Station(1);
        int teleports = 0;
        int falls = 0;
        void OnTeleport() { teleports++; }
        void OnDowned(bool downed) { if (downed) { falls++; } }
        player.Teleported += OnTeleport;
        GameEvents.DownedChanged += OnDowned;
        bool arrived = false;
        bool finite = true;
        float elapsed = 0.0f;
        float nextReport = 60.0f;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        float speedEnvelope = Mathf.Sqrt(player.Settings.MaxSpeed * player.Settings.MaxSpeed
            + player.Settings.MaxFallSpeed * player.Settings.MaxFallSpeed) * UnforcedSpeedSafetyFactor;
        Vector3 previous = start;
        Vector3 previousVelocity = player.Velocity;
        LocomotionState previousState = player.State;
        bool previousDowned = GameEvents.IsDowned;
        int previousTeleports = teleports;
        int tick = 0;
        int reports = 0;
        bool boundedMotion = true;
        float peakSpeed = 0.0f;
        float largestStep = 0.0f;
        float largestUnannouncedStep = 0.0f;
        float unannouncedPath = 0.0f;
        float teleportPath = 0.0f;
        string largestStepDetail = "none";
        string character = run.Character.Id;
        CarrierModel? look = player.GetNodeOrNull<CarrierModel>("CarrierModel");
        if (look != null && look.CharacterId.Length > 0) { character = look.CharacterId; }
        GD.Print($"PLAYTEST START seed {run.Seed}, character {character}, position {start}, station {station}, physics {Engine.PhysicsTicksPerSecond}Hz, duration {duration:0.0}s, speed envelope {speedEnvelope:0.00}m/s");
        for (; elapsed < duration; elapsed += dt)
        {
            float targetZ = player.GlobalPosition.Z - 6.0f;
            if (firstStation)
            {
                targetZ = Mathf.Max(targetZ, station.Z + 2.0f);
            }
            Vector3 target = run.Route.RoutePoint(targetZ);
            Vector3 direction = target - player.GlobalPosition;
            direction.Y = 0.0f;
            player.DirectionOverride = direction.Normalized();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            tick++;
            Vector3 position = player.GlobalPosition;
            float step = previous.DistanceTo(position);
            peakSpeed = Mathf.Max(peakSpeed, player.Velocity.Length());
            bool declaredTeleport = teleports != previousTeleports;
            float stepBudget = speedEnvelope * dt + PositionCorrectionAllowance;
            float pathBudget = speedEnvelope * (elapsed + dt) + PositionCorrectionAllowance * (falls + 1);
            bool plausible = declaredTeleport || (step <= stepBudget && unannouncedPath + step <= pathBudget);
            if (step > largestStep || declaredTeleport || (!plausible && reports < MaxAnomalyReports))
            {
                string stepDetail = $"tick {tick}, t {elapsed + dt:0.000}s, from {previous} to {position}, velocity {previousVelocity} -> {player.Velocity}, state {previousState} -> {player.State}, downed {previousDowned} -> {GameEvents.IsDowned}, teleport {declaredTeleport}, falls {falls}";
                if (step > largestStep)
                {
                    largestStep = step;
                    largestStepDetail = stepDetail;
                }
                if (declaredTeleport) { GD.Print($"PLAYTEST TELEPORT {step:0.000}m: {stepDetail}"); }
                if (!plausible && reports < MaxAnomalyReports)
                {
                    GD.Print($"PLAYTEST MOTION ANOMALY seed {run.Seed}, step {step:0.000}/{stepBudget:0.000}m, path {unannouncedPath + step:0.000}/{pathBudget:0.000}m: {stepDetail}");
                    reports++;
                }
            }
            if (declaredTeleport)
            {
                teleportPath += step;
            }
            else
            {
                unannouncedPath += step;
                largestUnannouncedStep = Mathf.Max(largestUnannouncedStep, step);
                boundedMotion &= plausible;
            }
            previous = position;
            previousVelocity = player.Velocity;
            previousState = player.State;
            previousDowned = GameEvents.IsDowned;
            previousTeleports = teleports;
            finite &= player.GlobalPosition.IsFinite() && player.Velocity.IsFinite()
                && float.IsFinite(dynamics.Pitch) && float.IsFinite(dynamics.Roll) && float.IsFinite(dynamics.Twist)
                && float.IsFinite(CarriedLoad.Mass) && CarriedLoad.Mass >= 0.0f
                && float.IsFinite(player.Health?.Current ?? float.NaN)
                && float.IsFinite(player.Stamina?.Current ?? float.NaN);
            Vector3 offset = player.GlobalPosition - station;
            arrived = GameEvents.InShelter && run.Stage >= 1 && new Vector2(offset.X, offset.Z).Length() <= 9.0f;
            if (firstStation && elapsed >= nextReport)
            {
                GD.Print($"PLAYTEST seed {run.Seed}, {elapsed:0}s, position {player.GlobalPosition}, speed {player.Velocity.Length():0.0}, health {player.Health?.Current:0.0}");
                nextReport += 60.0f;
            }
            if (firstStation && arrived)
            {
                break;
            }
            if (player.Health?.Dead == true || !finite)
            {
                break;
            }
        }
        player.Teleported -= OnTeleport;
        GameEvents.DownedChanged -= OnDowned;
        player.DirectionOverride = Vector3.Zero;
        float descended = start.Z - player.GlobalPosition.Z;
        bool alive = player.Health != null && !player.Health.Dead;
        bool passed = finite && alive && boundedMotion && descended >= MinDistance;
        GD.Print($"PLAYTEST END seed {run.Seed}, character {character}, start {start}, end {player.GlobalPosition}, ticks {tick}, teleports {teleports}, falls {falls}, peak velocity {peakSpeed:0.000}m/s, observed path {unannouncedPath:0.000}m, declared teleport path {teleportPath:0.000}m, largest step {largestStep:0.000}m, largest unannounced step {largestUnannouncedStep:0.000}m, bounded motion {boundedMotion}");
        GD.Print("PLAYTEST LARGEST STEP " + largestStepDetail);
        if (firstStation)
        {
            CarrierNeeds needs = player.GetNode<CarrierNeeds>("Needs");
            passed &= arrived && teleports == 0 && float.IsFinite(needs.Hunger) && float.IsFinite(needs.Thirst)
                && needs.Hunger >= 0.0f && needs.Hunger <= CarrierNeeds.Full
                && needs.Thirst >= 0.0f && needs.Thirst <= CarrierNeeds.Full;
            GD.Print($"FIRST STATION seed {run.Seed}, arrived {arrived}, teleports {teleports}, falls {falls}, hunger {needs.Hunger:0.0}, thirst {needs.Thirst:0.0}, load {CarriedLoad.Mass:0.0}kg");
        }
        string status = "FAIL";
        if (passed)
        {
            status = "PASS";
        }
        GD.Print($"{status}  main-scene descent: {descended:0.0} m downhill in {elapsed:0} s, alive {alive}, finite motion {finite}, bounded motion {boundedMotion}, health {player.Health?.Current:0.0}");
        await Seconds(0.5f);
        if (DisplayServer.GetName() != "headless")
        {
            foreach (string argument in OS.GetCmdlineUserArgs())
            {
                const string prefix = "--playtest-screenshot=";
                if (argument.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image screenshot = GetViewport().GetTexture().GetImage();
                    Error result = screenshot.SavePng(argument[prefix.Length..]);
                    passed &= result == Error.Ok;
                    GD.Print($"Playtest screenshot: {result}");
                }
            }
        }
        GetTree().Quit(Convert.ToInt32(!passed));
    }

    private async Task Seconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
