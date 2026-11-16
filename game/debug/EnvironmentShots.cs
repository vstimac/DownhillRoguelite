using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: photographs the same spots of the same mountain, so the world's look can be compared
/// before and after a change. Stands the carrier on the route at fixed depths below the summit,
/// waits until the ground, plants and set pieces round it are in, and saves a view down the path and
/// a view across the slope from a camera of its own (HUD hidden, midday, clear sky).
/// `-- --seed=1 --shots-directory=C:/somewhere` (headless can't render: run it windowed).
/// </summary>
public partial class EnvironmentShots : Node3D
{
    /// <summary>Metres below the summit to photograph.</summary>
    [Export] public float[] Depths { get; set; } = { 25.0f, 120.0f, 260.0f, 1250.0f, 2350.0f, 3400.0f, 4450.0f, 7600.0f };
    [Export] public float SettleSeconds { get; set; } = 1.5f;
    [Export] public float MaxWaitSeconds { get; set; } = 25.0f;
    /// <summary>Frames timed per reading with `--frame-times` (vertical sync and the frame cap are off for it).</summary>
    [Export] public int TimedFrames { get; set; } = 150;

    private SubViewport? _view;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string directory = ProjectSettings.GlobalizePath("res://../../environment-shots");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { directory = arg["--shots-directory=".Length..]; }
        }
        bool frameTimes = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--frame-times") { frameTimes = true; }
        }
        Directory.CreateDirectory(directory);
        PlayerController player = GetNode<PlayerController>("World/Player");
        TerrainStreamer streamer = GetNode<TerrainStreamer>("World/Terrain");
        RunState run = GetNode<RunState>("/root/RunState");
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        await Seconds(2.0f);
        HideOverlays(GetTree().Root);
        DebugDraw3D.SetEnabled(false);
        // The shot camera renders the same world through a viewport of its own, so no HUD or debug
        // overlay (some re-show themselves every frame) ends up in the picture.
        Viewport root = GetViewport();
        SubViewport view = new()
        {
            Name = "ShotView",
            Size = (Vector2I)root.GetVisibleRect().Size,
            World3D = root.World3D,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = root.Msaa3D,
            ScreenSpaceAA = root.ScreenSpaceAA,
            UseTaa = root.UseTaa,
            UseDebanding = root.UseDebanding,
            Scaling3DMode = root.Scaling3DMode,
            Scaling3DScale = root.Scaling3DScale,
            PositionalShadowAtlasSize = 4096,
        };
        AddChild(view);
        _view = view;
        Camera3D camera = new() { Name = "ShotCamera", Fov = 70.0f, Far = 400.0f };
        view.AddChild(camera);
        if (run.Route == null || streamer.Generator == null)
        {
            GD.Print("FAIL  environment shots: no route");
            GetTree().Quit(1);
            return;
        }
        TerrainGenerator generator = streamer.Generator;
        IRouteQuery route = run.Route;
        int index = 0;
        foreach (float depth in Depths)
        {
            float z = route.StartZ - 5.0f;
            while (generator.DepthAt(route.RoutePoint(z).Y) < depth && z > -60000.0f) { z -= 10.0f; }
            Vector3 at = route.RoutePoint(z);
            player.Teleport(at + Vector3.Up * 0.3f, 0.0f);
            if (clock != null) { clock.SetTimeOfDay(12.0f); clock.Paused = true; }
            TerrainStreamer.Loading = true;
            float waited = 0.0f;
            int stable = 0;
            int lastLoaded = -1;
            while (waited < MaxWaitSeconds)
            {
                await Seconds(0.25f);
                waited += 0.25f;
                player.Teleport(at + Vector3.Up * 0.3f, 0.0f);
                if (streamer.LoadedCount == lastLoaded && streamer.PlantsPendingCount == 0) { stable++; } else { stable = 0; }
                lastLoaded = streamer.LoadedCount;
                if (stable >= 6) { break; }
            }
            TerrainStreamer.Loading = false;
            await Seconds(SettleSeconds);
            Vector3 ahead = route.RoutePoint(z - 30.0f);
            Vector3 eye = route.RoutePoint(z + 6.0f) + Vector3.Up * 2.2f;
            await Shoot(camera, eye, ahead + Vector3.Up * 0.8f, Path.Combine(directory, $"{index:D2}_depth{depth:0}_path.png"));
            Vector3 side = new(at.X + 30.0f, generator.Height(at.X + 30.0f, z - 12.0f) + 0.5f, z - 12.0f);
            Vector3 sideEye = new(at.X - 6.0f, at.Y + 2.4f, z + 4.0f);
            await Shoot(camera, sideEye, side, Path.Combine(directory, $"{index:D2}_depth{depth:0}_side.png"));
            GD.Print($"SHOT depth {depth:0} at {at}, loaded {streamer.LoadedCount}, waited {waited:0.0}s, biome {generator.BiomeAt(at.X, at.Z, depth)?.Name}");
            foreach (Node node in GetTree().Root.FindChildren("*", "Node3D", true, false))
            {
                if (node is DownhillRoguelite.Environment.Fog.GroundFog fog) { GD.Print("FOG depth " + depth.ToString("0", CultureInfo.InvariantCulture) + ": " + fog.Stats().Replace('\n', '|')); }
            }
            if (frameTimes)
            {
                double with = await FrameMilliseconds();
                SetCarpets(streamer, false);
                double without = await FrameMilliseconds();
                SetCarpets(streamer, true);
                GD.Print($"FRAME depth {depth:0}: {with:0.00} ms with the grass carpet, {without:0.00} ms without ({with - without:0.00} ms for the carpet)");
            }
            index++;
        }
        GD.Print("PASS  environment shots saved in " + directory);
        GetTree().Quit(0);
    }

    /// <summary>Average frame time over TimedFrames, uncapped, in ms.</summary>
    private async Task<double> FrameMilliseconds()
    {
        int cap = Engine.MaxFps;
        DisplayServer.VSyncMode sync = DisplayServer.WindowGetVsyncMode();
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        for (int i = 0; i < 20; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        ulong start = Time.GetTicksUsec();
        for (int i = 0; i < TimedFrames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        double average = (Time.GetTicksUsec() - start) / 1000.0 / TimedFrames;
        Engine.MaxFps = cap;
        DisplayServer.WindowSetVsyncMode(sync);
        return average;
    }

    private static void SetCarpets(Node node, bool shown)
    {
        if (node is Node3D spatial && node.Name == GrassCarpet.NodeName) { spatial.Visible = shown; }
        foreach (Node child in node.GetChildren()) { SetCarpets(child, shown); }
    }

    private static void HideOverlays(Node node)
    {
        if (node is CanvasLayer layer) { layer.Visible = false; }
        // The circle's name floats in the world as a 3D label: keep it out of the comparison.
        if (node is DownhillRoguelite.WorldUi.CircleTitle title) { title.Visible = false; title.ProcessMode = ProcessModeEnum.Disabled; }
        foreach (Node child in node.GetChildren()) { HideOverlays(child); }
    }

    private async Task Shoot(Camera3D camera, Vector3 eye, Vector3 target, string file)
    {
        camera.GlobalPosition = eye;
        camera.LookAt(target, Vector3.Up);
        camera.MakeCurrent();
        HideOverlays(GetTree().Root);
        await Seconds(0.4f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = (_view ?? GetViewport()).GetTexture().GetImage();
        Error error = image.SavePng(file);
        GD.Print("Saved " + file + ": " + error.ToString().ToLower(CultureInfo.InvariantCulture));
    }

    private async Task Seconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            // A stray key (Escape) must not pause the world under the camera.
            if (GetTree().Paused) { GetTree().Paused = false; }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
