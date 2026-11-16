using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: photographs the far trees and the sky behind them from the same spots of the same
/// mountain, at several hours and in several weathers, so the distance fog can be compared before
/// and after a change. Each spot gets a view down the path and a raised view far downhill (HUD
/// hidden, clock stopped). Run it windowed (headless can't render):
/// `-- --skip-start --seed=1 --shots-directory=C:/somewhere --hours=12,19.5,23 --weathers=clear,fog`.
/// </summary>
public partial class FogShots : Node3D
{
    /// <summary>Metres below the summit to photograph.</summary>
    [Export] public float[] Depths { get; set; } = { 25.0f, 260.0f, 1250.0f };
    [Export] public float[] Hours { get; set; } = { 12.0f, 19.5f, 23.0f };
    [Export] public string[] Weathers { get; set; } = { "clear", "fog" };
    [Export] public float SettleSeconds { get; set; } = 1.5f;
    [Export] public float MaxWaitSeconds { get; set; } = 25.0f;

    public override async void _Ready()
    {
        string directory = ProjectSettings.GlobalizePath("res://../../fog-shots");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { directory = arg["--shots-directory=".Length..]; }
            if (arg.StartsWith("--hours=", StringComparison.Ordinal)) { Hours = Array.ConvertAll(arg["--hours=".Length..].Split(','), text => float.Parse(text, CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--depths=", StringComparison.Ordinal)) { Depths = Array.ConvertAll(arg["--depths=".Length..].Split(','), text => float.Parse(text, CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--weathers=", StringComparison.Ordinal)) { Weathers = arg["--weathers=".Length..].Split(','); }
        }
        Directory.CreateDirectory(directory);
        PlayerController player = GetNode<PlayerController>("World/Player");
        TerrainStreamer streamer = GetNode<TerrainStreamer>("World/Terrain");
        WeatherSystem? weather = GetNodeOrNull<WeatherSystem>("World/Weather");
        RunState run = GetNode<RunState>("/root/RunState");
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        await Seconds(2.0f);
        // Nobody plays during the shots: no key, stick or button may move, lean or throw the carrier.
        player.InputEnabled = false;
        foreach (Node node in player.FindChildren("*", "", true, false))
        {
            if (node is DownhillRoguelite.Balance.BalanceController balance) { balance.InputEnabled = false; }
        }
        HideOverlays(GetTree().Root);
        Camera3D camera = new() { Name = "ShotCamera", Fov = 70.0f, Far = 1000.0f };
        AddChild(camera);
        if (run.Route == null || streamer.Generator == null)
        {
            GD.Print("FAIL  fog shots: no route");
            GetTree().Quit(1);
            return;
        }
        GD.Print("Menu fog scale " + DayNightCycle.MenuFogScale.ToString(CultureInfo.InvariantCulture));
        TerrainGenerator generator = streamer.Generator;
        IRouteQuery route = run.Route;
        foreach (float depth in Depths)
        {
            float z = route.StartZ - 5.0f;
            while (generator.DepthAt(route.RoutePoint(z).Y) < depth && z > -60000.0f) { z -= 10.0f; }
            Vector3 at = route.RoutePoint(z);
            if (clock != null) { clock.SetTimeOfDay(12.0f); clock.Paused = true; }
            TerrainStreamer.Loading = true;
            float waited = 0.0f;
            int stable = 0;
            int lastLoaded = -1;
            while (waited < MaxWaitSeconds)
            {
                player.Teleport(at + Vector3.Up * 0.3f, 0.0f);
                await Seconds(0.25f);
                waited += 0.25f;
                if (streamer.LoadedCount == lastLoaded && streamer.PlantsPendingCount == 0) { stable++; } else { stable = 0; }
                lastLoaded = streamer.LoadedCount;
                if (stable >= 6) { break; }
            }
            TerrainStreamer.Loading = false;
            Vector3 pathEye = route.RoutePoint(z + 6.0f) + Vector3.Up * 2.2f;
            Vector3 pathTarget = route.RoutePoint(z - 30.0f) + Vector3.Up * 0.8f;
            Vector3 farEye = at + Vector3.Up * 7.0f;
            Vector3 farTarget = route.RoutePoint(z - 140.0f) + Vector3.Up * 6.0f;
            foreach (string weatherName in Weathers)
            {
                if (weather != null && Enum.TryParse(weatherName, true, out WeatherKind kind)) { weather.Forced = kind; }
                foreach (float hour in Hours)
                {
                    clock?.SetTimeOfDay(hour);
                    player.Teleport(at + Vector3.Up * 0.3f, 0.0f);
                    await Seconds(SettleSeconds);
                    string name = string.Create(CultureInfo.InvariantCulture, $"d{depth:0000}_{weatherName}_h{hour:00.0}");
                    await Shoot(camera, pathEye, pathTarget, Path.Combine(directory, name + "_path.png"));
                    await Shoot(camera, farEye, farTarget, Path.Combine(directory, name + "_far.png"));
                    Godot.Environment? environment = GetViewport().World3D.Environment;
                    GD.Print(string.Create(CultureInfo.InvariantCulture, $"SHOT {name}: fog density {environment?.FogDensity:0.0000}, colour {environment?.FogLightColor}, sky affect {environment?.FogSkyAffect:0.00}, aerial {environment?.FogAerialPerspective:0.00}"));
                }
            }
        }
        GD.Print("PASS  fog shots saved in " + directory);
        GetTree().Quit(0);
    }

    /// <summary>Every HUD layer, wherever it hangs in the tree (the ability chip is not under World).</summary>
    private static void HideOverlays(Node node)
    {
        if (node is CanvasLayer layer) { layer.Visible = false; }
        foreach (Node child in node.GetChildren()) { HideOverlays(child); }
    }

    private async Task Shoot(Camera3D camera, Vector3 eye, Vector3 target, string file)
    {
        camera.GlobalPosition = eye;
        camera.LookAt(target, Vector3.Up);
        camera.MakeCurrent();
        await Seconds(0.5f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(file);
        if (error != Error.Ok) { GD.Print("FAIL  could not save " + file + ": " + error); }
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
