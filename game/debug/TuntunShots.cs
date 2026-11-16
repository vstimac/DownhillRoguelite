using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Photographs Tuntun Sahur's hard mode: the first station with its three offerings and the hard-mode
/// stone beside the hand cart, the stone up close, then (after the carrier takes it) the Tuntun Sahurs
/// gathering round the carrier. Writes report.txt with what was counted.
/// Run it windowed (headless can't render): `-- --shots-directory=C:/somewhere`.
/// </summary>
public partial class TuntunShots : Node3D
{
    [Export] public float MaxWaitSeconds { get; set; } = 25.0f;

    private PlayerController? _player;
    private TerrainStreamer? _streamer;
    private IRouteQuery? _route;
    private Camera3D? _camera;
    private string _directory = "";
    private readonly List<string> _report = new();
    private int _shots;
    private Vector3 _spot;

    public override async void _Ready()
    {
        _directory = ProjectSettings.GlobalizePath("res://../../tuntun-shots");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { _directory = arg["--shots-directory=".Length..]; }
        }
        Directory.CreateDirectory(_directory);
        await Seconds(3.0f);
        GetTree().Paused = false;
        _camera = new Camera3D { Name = "ShotCamera", Fov = 60.0f, Far = 300.0f };
        AddChild(_camera);
        _player = GetNodeOrNull<PlayerController>("World/Player");
        _streamer = GetNodeOrNull<TerrainStreamer>("World/Terrain");
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        _route = run?.Route;
        BoonOfferings? boons = Find<BoonOfferings>(GetTree().Root);
        TuntunSpawner? spawner = Find<TuntunSpawner>(GetTree().Root);
        if (_player == null || _streamer == null || _route == null || run == null || boons == null || spawner == null)
        {
            GD.Print("FAIL  tuntun shots: no carrier, terrain, route, offerings or Tuntun spawner");
            GetTree().Quit(1);
            return;
        }

        // 1. The first station: three offerings downhill of the fire, the hard-mode stone beside the hand cart.
        int station = _route.NextStationIndex(_route.StartZ);
        Vector3 centre = _route.Station(station);
        await Visit(centre + new Vector3(0.0f, 0.0f, 2.0f));
        _report.Add($"station {station}: {boons.Stones.Count} offerings, {boons.HardmodeStones.Count} hard-mode stone(s)");
        foreach (BoonStone stone in boons.Stones) { _report.Add($"  offering {stone.Boon?.DisplayName} at {Fmt(stone.GlobalPosition)}"); }
        BoonStone? hardmode = boons.HardmodeStones.Count > 0 ? boons.HardmodeStones[0] : null;
        if (hardmode == null)
        {
            GD.Print("FAIL  tuntun shots: no hard-mode stone at the station");
            Finish(1);
            return;
        }
        _report.Add($"  hard-mode stone at {Fmt(hardmode.GlobalPosition)}");
        Node3D? cart = NearestCart(hardmode.GlobalPosition);
        if (cart != null) { _report.Add($"  hand cart at {Fmt(cart.GlobalPosition)}, {cart.GlobalPosition.DistanceTo(hardmode.GlobalPosition):0.0} m from the stone"); }

        hardmode.SetFocused(true);
        foreach (BoonStone stone in boons.Stones) { stone.SetFocused(true); }
        Vector3 middle = _player.GlobalPosition + hardmode.GlobalPosition;
        foreach (BoonStone stone in boons.Stones) { middle += stone.GlobalPosition; }
        middle /= boons.Stones.Count + 2;
        await Shoot(middle + new Vector3(0.0f, 6.5f, 11.0f), middle + Vector3.Up * 0.5f, "station_offerings_and_hardmode_stone");
        foreach (BoonStone stone in boons.Stones) { stone.SetFocused(false); }
        Vector3 at = hardmode.GlobalPosition;
        await Shoot(at + new Vector3(2.4f, 1.6f, 2.8f), at + Vector3.Up * 0.8f, "hardmode_stone_close");
        hardmode.SetFocused(false);

        // 2. The carrier takes it: Tuntun Sahurs come in round him.
        bool taken = hardmode.Interact(_player);
        _report.Add($"hard-mode stone taken {taken}: hard mode {run.TuntunHardmode}, offerings left {boons.Stones.Count}");
        await Hold(1.5f);
        await ShootAround(spawner, "tuntuns_arriving");
        await Hold(2.5f);
        await ShootAround(spawner, "tuntuns_around_carrier");
        Finish(run.TuntunHardmode && spawner.HardmodeTuntuns.Count >= spawner.HardmodeStart ? 0 : 1);
    }

    private void Finish(int code)
    {
        File.WriteAllText(Path.Combine(_directory, "report.txt"), string.Join("\n", _report) + "\n");
        foreach (string line in _report) { GD.Print("REPORT " + line); }
        GD.Print($"TUNTUN SHOTS: {_shots} photographs, directory {_directory}");
        GetTree().Quit(code);
    }

    /// <summary>A wide shot from above and behind the carrier, with every hard-mode Tuntun Sahur in it.</summary>
    private async Task ShootAround(TuntunSpawner spawner, string name)
    {
        Vector3 carrier = _player!.GlobalPosition;
        Vector3 middle = carrier;
        float reach = 6.0f;
        int count = 0;
        foreach (TuntunSahur tuntun in spawner.HardmodeTuntuns)
        {
            if (!IsInstanceValid(tuntun)) { continue; }
            middle += tuntun.GlobalPosition;
            reach = Mathf.Max(reach, tuntun.GlobalPosition.DistanceTo(carrier));
            count++;
            _report.Add($"  {name}: Tuntun at {Fmt(tuntun.GlobalPosition)}, {tuntun.GlobalPosition.DistanceTo(carrier):0.0} m from the carrier");
        }
        middle /= count + 1;
        _report.Add($"{name}: {count} hard-mode Tuntun Sahurs, carrier at {Fmt(carrier)}");
        await Shoot(middle + new Vector3(0.0f, reach * 0.9f + 3.0f, reach * 1.1f + 4.0f), middle + Vector3.Up * 0.8f, name);
    }

    /// <summary>Puts the carrier on a spot and waits for the terrain round it to load (as SignShots does).</summary>
    private async Task Visit(Vector3 at)
    {
        _spot = new Vector3(at.X, _route!.GroundHeight(at.X, at.Z) + 0.3f, at.Z);
        TerrainStreamer.Loading = true;
        _player!.Teleport(_spot, 0.0f);
        Report(_spot);
        int stable = 0;
        int last = -1;
        float waited = 0.0f;
        while (waited < MaxWaitSeconds)
        {
            await Seconds(0.25f);
            waited += 0.25f;
            _player.Teleport(_spot, 0.0f);
            Report(_spot);
            if (_streamer!.LoadedCount == last && _streamer.PlantsPendingCount == 0) { stable++; } else { stable = 0; }
            last = _streamer.LoadedCount;
            if (stable >= 6) { break; }
        }
        TerrainStreamer.Loading = false;
        await Seconds(2.0f);
    }

    /// <summary>Lets the world run, the carrier reporting where it is (it does so only while it moves).</summary>
    private async Task Hold(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            Report(_player!.GlobalPosition);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private static void Report(Vector3 spot)
    {
        GameEvents.PublishLocomotion(new LocomotionSnapshot(spot, Vector3.Zero, Vector3.Zero, Vector3.Up, 0.0f, Vector3.Zero,
            Vector3.Zero, LocomotionState.Idle, "", 1.0f, Vector3.Forward));
    }

    private Node3D? NearestCart(Vector3 from)
    {
        Node3D? best = null;
        float bestDistance = float.MaxValue;
        foreach (Node node in GetTree().Root.FindChildren("HandCart", "", true, false))
        {
            if (node is not Inventory.HandCart cart) { continue; }
            float distance = cart.GlobalPosition.DistanceTo(from);
            if (distance < bestDistance) { best = cart; bestDistance = distance; }
        }
        return best;
    }

    private static T? Find<T>(Node node) where T : class
    {
        if (node is T found) { return found; }
        foreach (Node child in node.GetChildren())
        {
            T? hit = Find<T>(child);
            if (hit != null) { return hit; }
        }
        return null;
    }

    private static string Fmt(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})");

    private async Task Shoot(Vector3 eye, Vector3 target, string name)
    {
        _camera!.GlobalPosition = eye;
        _camera.LookAt(target, Vector3.Up);
        _camera.MakeCurrent();
        HideHud(GetTree().Root);
        await Seconds(0.5f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string file = Path.Combine(_directory, $"{_shots:D2}_{name}.png");
        using Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(file);
        _shots++;
        _report.Add($"photo {Path.GetFileName(file)}: {error}");
        GD.Print("Saved " + file + ": " + error.ToString().ToLower(CultureInfo.InvariantCulture));
    }

    private static void HideHud(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is CanvasLayer layer) { layer.Visible = false; }
            HideHud(child);
        }
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
