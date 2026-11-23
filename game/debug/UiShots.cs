using System;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: photographs the interface so its look can be judged and compared — the title screen
/// (`-- --ui-shots=start`), or (`-- --skip-start --ui-shots=game`) the HUD at the summit at noon in
/// clear weather and in fog, the HUD a while into the run and with notes, the pause menu, Options,
/// the pack (quick look, ring, open page, shouldering), journal and map. The carrier takes no input and the run is kept in its own save files,
/// never in the player's slots. Headless can't render: run it windowed (`--resolution 1920x1080`),
/// with `-- --shots-directory=C:/somewhere`.
/// </summary>
public partial class UiShots : Node3D
{
    [Export] public float MaxWaitSeconds { get; set; } = 25.0f;

    private string _directory = "";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _directory = ProjectSettings.GlobalizePath("res://../../ui-shots");
        string mode = "game";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { _directory = arg["--shots-directory=".Length..]; }
            if (arg.StartsWith("--ui-shots=", StringComparison.Ordinal)) { mode = arg["--ui-shots=".Length..]; }
        }
        Directory.CreateDirectory(_directory);
        RunSave.UseFiles("user://ui_shots_run.json", "user://ui_shots_run_pack.json");
        try
        {
            if (mode == "start") { await StartShots(); }
            else if (mode == "pack") { await GameShots(true); }
            else { await GameShots(false); }
            GD.Print("PASS  ui shots saved in " + _directory);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.Print("FAIL  ui shots: " + exception);
            GetTree().Quit(1);
        }
    }

    private async Task StartShots()
    {
        StartScreen? start = null;
        foreach (Node node in GetTree().Root.FindChildren("*", "CanvasLayer", true, false))
        {
            if (node is StartScreen screen) { start = screen; }
        }
        if (start == null) { throw new InvalidOperationException("no start screen (was --skip-start given?)"); }
        float waited = 0.0f;
        while (!start.Waiting && waited < MaxWaitSeconds) { await Seconds(0.25f); waited += 0.25f; }
        await Seconds(4.0f);
        await Shoot("start");
        start.ShowPage(StartScreen.Page.Options);
        await Seconds(1.0f);
        await Shoot("start_options");
        start.ShowPage(StartScreen.Page.Character);
        await Seconds(2.5f);
        await Shoot("start_character");
    }

    private async Task GameShots(bool packOnly)
    {
        PlayerController player = GetNode<PlayerController>("World/Player");
        TerrainStreamer streamer = GetNode<TerrainStreamer>("World/Terrain");
        WeatherSystem? weather = GetNodeOrNull<WeatherSystem>("World/Weather");
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Hud hud = GetNode<Hud>("World/Hud");
        PauseMenu pause = GetNode<PauseMenu>("World/PauseMenu");
        await Seconds(0.5f);
        // Nobody plays during the shots: no key, stick or button may move, lean or throw the carrier.
        player.InputEnabled = false;
        foreach (Node node in player.FindChildren("*", "", true, false))
        {
            if (node is Balance.BalanceController balance) { balance.InputEnabled = false; }
        }
        if (clock != null) { clock.SetTimeOfDay(12.0f); clock.Paused = true; }
        if (weather != null) { weather.Forced = WeatherKind.Clear; }
        int stable = 0;
        int lastLoaded = -1;
        for (float waited = 0.0f; waited < MaxWaitSeconds && stable < 6; waited += 0.25f)
        {
            await Seconds(0.25f);
            if (streamer.LoadedCount == lastLoaded && streamer.PlantsPendingCount == 0) { stable++; } else { stable = 0; }
            lastLoaded = streamer.LoadedCount;
        }
        if (packOnly)
        {
            // The opening cards out of the way first, as a player would have them.
            await Seconds(10.0f);
            await PackShots(hud);
            return;
        }
        await Shoot("hud_clear_start");
        await Seconds(8.0f);
        await Shoot("hud_clear");
        hud.Notes?.Note("Picked up: rope (2 m)");
        hud.Notes?.Note("Too cold to stand still", true);
        await Seconds(1.5f);
        await Shoot("hud_notes");
        if (weather != null) { weather.Forced = WeatherKind.Fog; }
        await Seconds(6.0f);
        await Shoot("hud_fog");
        if (weather != null) { weather.Forced = WeatherKind.Clear; }
        await Seconds(6.0f);

        pause.Open();
        await Seconds(1.0f);
        await Shoot("pause");
        pause.ShowOptions();
        await Seconds(1.0f);
        await Shoot("options");
        pause.Resume();
        await Seconds(1.0f);

        await PackShots(hud);
        hud.Toggle(hud.Journal);
        await Seconds(1.0f);
        await Shoot("journal");
        hud.Journal?.Close();
        await Seconds(0.8f);
        if (hud.MapPage?.Map != null)
        {
            hud.MapPage.Map.SetOpen(true);
            await Seconds(1.5f);
            await Shoot("map");
            hud.MapPage.Map.SetOpen(false);
            await Seconds(0.5f);
        }
    }

    /// <summary>
    /// The pack with its one key, pressed the way the keyboard does: a tap (the quick look), a hold
    /// half way (the ring), the open pack (nothing chosen, then a thing chosen and one in the hand),
    /// and the shouldering.
    /// </summary>
    private async Task PackShots(Hud hud)
    {
        Inventory.PackingMode? packing = Inventory.PackingMode.Current;
        if (packing == null)
        {
            GD.Print("FAIL  ui shots: no PackingMode in the scene");
            return;
        }
        PackKey(true);
        await Seconds(0.1f);
        PackKey(false);
        await Seconds(1.0f);
        await Shoot("pack_look");
        PackKey(true);
        ulong pressed = Time.GetTicksMsec();
        for (float waited = 0.0f; (hud.PackRing == null || hud.PackRing.Progress < 0.55f) && waited < 6.0f; waited += 0.02f)
        {
            await Seconds(0.02f);
        }
        GD.Print($"  ring {hud.PackRing?.Progress:0.00} after {(Time.GetTicksMsec() - pressed) / 1000.0f:0.00} s, phase {packing.Phase}, {Engine.GetFramesPerSecond():0} fps");
        await Shoot("pack_ring");
        for (float waited = 0.0f; packing.Phase != PackingPhase.Open && waited < 6.0f; waited += 0.05f)
        {
            await Seconds(0.05f);
        }
        GD.Print($"  open after {(Time.GetTicksMsec() - pressed) / 1000.0f:0.00} s, phase {packing.Phase}");
        PackKey(false);
        await Seconds(1.6f);
        await Shoot("pack_open");
        PackingScreen? page = hud.Packing;
        if (page != null)
        {
            string chosen = "";
            foreach (string id in GameEvents.CarriedItems)
            {
                if (ItemText.Find(id) is ItemDefinition item && (item.Category == ItemCategory.Water || item.Category == ItemCategory.Food))
                {
                    chosen = id;
                    break;
                }
            }
            page.SelectItem(chosen);
            await Seconds(0.6f);
            await Shoot("pack_item");
            foreach (PackLayoutEntry entry in GameEvents.CurrentPackLayout.Entries)
            {
                if (entry.Packed)
                {
                    page.Hold(entry.InstanceId, entry.Orientation);
                    break;
                }
            }
            await Seconds(0.6f);
            await Shoot("pack_hand");
            page.Hold("", GridOrientations.Identity);
        }
        GameEvents.RequestPackClose();
        await Seconds(packing.PutOnTime * 0.4f);
        await Shoot("pack_shoulder");
        await Seconds(packing.PutOnTime + 0.6f);
    }

    private static void PackKey(bool down)
    {
        Input.ParseInputEvent(new InputEventAction { Action = "inventory_toggle_packing", Pressed = down });
    }

    private async Task Shoot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        string file = Path.Combine(_directory, name + ".png");
        Error error = image.SavePng(file);
        GD.Print(error == Error.Ok ? "SHOT " + file : "FAIL  could not save " + file + ": " + error);
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--ui-dump") >= 0)
        {
            foreach (Node node in GetTree().Root.FindChildren("*", "CanvasLayer", true, false))
            {
                if (node is CanvasLayer layer && layer.Visible) { Dump(layer, 0); }
            }
        }
    }

    /// <summary>The visible pieces of a layer, with their place on screen (layout checks: margins, overlaps).</summary>
    private static void Dump(Node node, int depth)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control)
            {
                if (!control.IsVisibleInTree() || control.Modulate.A < 0.02f) { continue; }
                Rect2 rect = control.GetGlobalRect();
                if (rect.Size.X > 1.0f)
                {
                    GD.Print($"  UI {new string(' ', depth * 2)}{node.Name}/{control.Name} [{control.GetType().Name}] at {rect.Position.X:0},{rect.Position.Y:0} size {rect.Size.X:0}x{rect.Size.Y:0} a={control.Modulate.A:0.00}");
                }
                if (depth < 3) { Dump(control, depth + 1); }
            }
            else if (child is not CanvasLayer && depth < 3) { Dump(child, depth); }
        }
    }

    /// <summary>Real seconds, paused or not (the pause menu and the pages stop the world).</summary>
    private async Task Seconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }
}
