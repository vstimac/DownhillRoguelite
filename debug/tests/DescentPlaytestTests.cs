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
        bool finite = true;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < WalkSeconds; elapsed += dt)
        {
            Vector3 target = run.Route.RoutePoint(player.GlobalPosition.Z - 6.0f);
            Vector3 direction = target - player.GlobalPosition;
            direction.Y = 0.0f;
            player.DirectionOverride = direction.Normalized();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            finite &= player.GlobalPosition.IsFinite() && player.Velocity.IsFinite()
                && float.IsFinite(dynamics.Pitch) && float.IsFinite(dynamics.Roll) && float.IsFinite(dynamics.Twist);
            if (player.Health?.Dead == true || !finite)
            {
                break;
            }
        }
        player.DirectionOverride = Vector3.Zero;
        float descended = start.Z - player.GlobalPosition.Z;
        bool alive = player.Health != null && !player.Health.Dead;
        bool passed = finite && alive && descended >= MinDistance;
        string status = "FAIL";
        if (passed)
        {
            status = "PASS";
        }
        GD.Print($"{status}  main-scene descent: {descended:0.0} m downhill in {WalkSeconds:0} s, alive {alive}, finite motion {finite}, health {player.Health?.Current:0.0}");
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
