using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: a scripted walk down the route through each circle. The carrier is set a few metres
/// further down the path every second (streaming runs as in play, at midday), and a third-person
/// camera behind and above him saves a frame each time: for looking along the way for anything that
/// floats, is sunk, is far too big or is off the mountain's style.
/// `-- --shots-directory=C:/somewhere [--circles=0,1,2] [--steps=20] [--stride=5]` (windowed).
/// </summary>
public partial class WalkShots : EnvironmentShots
{
    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string directory = ProjectSettings.GlobalizePath("res://../../walk-shots");
        List<int> circles = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        int steps = 20;
        float stride = 5.0f;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { directory = arg["--shots-directory=".Length..]; }
            if (arg.StartsWith("--steps=", StringComparison.Ordinal)) { steps = int.Parse(arg["--steps=".Length..], CultureInfo.InvariantCulture); }
            if (arg.StartsWith("--stride=", StringComparison.Ordinal)) { stride = float.Parse(arg["--stride=".Length..], CultureInfo.InvariantCulture); }
            if (arg.StartsWith("--circles=", StringComparison.Ordinal))
            {
                circles.Clear();
                foreach (string part in arg["--circles=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    circles.Add(int.Parse(part, CultureInfo.InvariantCulture));
                }
            }
        }
        Directory.CreateDirectory(directory);
        PlayerController player = GetNode<PlayerController>("World/Player");
        TerrainStreamer streamer = GetNode<TerrainStreamer>("World/Terrain");
        RunState run = GetNode<RunState>("/root/RunState");
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        await Seconds(2.0f);
        Camera3D camera = MakeShotCamera(player);
        if (run.Route == null || streamer.Generator == null)
        {
            GD.Print("FAIL  walk shots: no route");
            GetTree().Quit(1);
            return;
        }
        TerrainGenerator generator = streamer.Generator;
        IRouteQuery route = run.Route;
        const float CircleDepth = 1050.0f;
        foreach (int circle in circles)
        {
            float depth = circle == 0 ? 40.0f : circle * CircleDepth + 350.0f;
            float z = route.StartZ - 5.0f;
            while (generator.DepthAt(route.RoutePoint(z).Y) < depth && z > -80000.0f) { z -= 10.0f; }
            await StandAndLoad(player, streamer, clock, route.RoutePoint(z));
            string biome = generator.BiomeAt(route.RoutePoint(z).X, z, depth)?.Name ?? "?";
            for (int step = 0; step < steps; step++)
            {
                Vector3 at = route.RoutePoint(z);
                player.Teleport(at + Vector3.Up * 0.2f, 0.0f);
                // A second's walk: streaming and the plants keep up as they would in play.
                await Seconds(1.0f);
                player.Teleport(at + Vector3.Up * 0.2f, 0.0f);
                Vector3 back = route.RoutePoint(z + 6.0f);
                Vector3 eye = back + Vector3.Up * 2.6f;
                Vector3 look = route.RoutePoint(z - 14.0f) + Vector3.Up * 0.6f;
                await Shoot(camera, eye, look, Path.Combine(directory, $"c{circle}_{step:D2}.png"));
                z -= stride;
            }
            GD.Print($"WALK circle {circle} ({biome}) from {depth:0} m: {steps} frames, {steps * stride:0} m of path");
        }
        GD.Print("PASS  walk shots saved in " + directory);
        GetTree().Quit(0);
    }
}
