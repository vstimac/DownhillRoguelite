using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Exercises actual world replacement, pause ownership and inventory subscription cleanup.</summary>
public partial class DevPlaygroundTests : Node
{
    private int _failed;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        try
        {
            // Remain a test driver while the console changes the current world normally.
            Node blank = new() { Name = "TestTransition" };
            GetTree().Root.AddChild(blank);
            GetTree().CurrentScene = blank;
            DevPlayground config = GD.Load<PackedScene>("res://debug/DevPlayground.tscn").Instantiate<DevPlayground>();
            DevPlaygroundConsole console = new() { Labs = config.Labs, Seed = config.Seed };
            config.Free();
            GetTree().Root.AddChild(console);
            HashSet<string> ids = new(StringComparer.Ordinal);
            bool valid = console.Labs.Count >= 16;
            foreach (DevPlaygroundEntry entry in console.Labs)
            {
                valid &= ids.Add(entry.Id) && !entry.ScenePath.Contains("/tests/", StringComparison.Ordinal)
                    && ResourceLoader.Load(entry.ScenePath) is PackedScene;
            }
            Check("all manual laboratories have unique ids and loadable scenes", valid);
            Check("sandbox opens with player, course, and developer menu", await console.OpenLab("sandbox", showMenu: true)
                && console.Player != null && GetTree().CurrentScene.GetNodeOrNull<TestCourseBuilder>("Course") != null && console.MenuOpen);
            Check("Start selected laboratory enters gameplay instead of reopening the menu", await console.LaunchSelectedLab()
                && !console.MenuOpen && !GetTree().Paused && !GameEvents.IsScreenOpen);
            Vector3 walkStart = console.Player!.GlobalPosition;
            Input.ActionPress("player_move_forward");
            for (int i = 0; i < 60; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
            Input.ActionRelease("player_move_forward");
            float walked = new Vector2(console.Player.GlobalPosition.X - walkStart.X, console.Player.GlobalPosition.Z - walkStart.Z).Length();
            Check("player actually walks after Start selected laboratory", walked > 1, walked.ToString("0.00") + " m");
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--sandbox-only") >= 0)
            {
                GD.Print("DEV SANDBOX START TESTS: " + _failed + " failures");
                GetTree().Quit(_failed);
                return;
            }
            Check("closing the developer menu resumes an unpaused world", !GetTree().Paused && !GameEvents.IsScreenOpen);
            console.SetMenuOpen(true);
            Check("developer menu freezes gameplay and owns the mouse", GetTree().Paused && GameEvents.IsScreenOpen);
            console.SetMenuOpen(false);
            GetTree().Paused = true;
            console.SetMenuOpen(true);
            console.SetMenuOpen(false);
            Check("closing the menu preserves a pre-existing pause", GetTree().Paused);
            GetTree().Paused = false;
            CarrierInventory old = GetTree().CurrentScene.GetNode<CarrierInventory>("Player/Inventory");
            old.Clear();
            Check("developer item grant reaches the active real inventory once", console.GrantItem("res://inventory/items/water_flask.tres") && old.AllItems.Count == 1);
            Check("player stat tools use the active player", console.RefillPlayer() && console.Player!.Health!.Current > 0);
            Check("developer reset restores the live player without ending the run", console.ResetPlayer() && !GetNode<RunState>("/root/RunState").RunOver);
            Check("clothing laboratory opens and releases the previous inventory", await console.OpenLab("clothing") && !IsInstanceValid(old));
            CarrierInventory clothing = GetTree().CurrentScene.GetNode<CarrierInventory>("World/Player/Inventory");
            clothing.Clear();
            Check("only the replacement inventory receives item grants", console.GrantItem("res://inventory/items/water_flask.tres") && clothing.AllItems.Count == 1);
            Check("ability lab opens without leaking the old inventory", await console.OpenLab("abilities") && !IsInstanceValid(clothing));
            Check("grant safely fails in a lab without inventory", !console.GrantItem("res://inventory/items/water_flask.tres"));
            Node before = GetTree().CurrentScene;
            Check("an unknown lab leaves the current world intact", !await console.OpenLab("missing-lab") && GetTree().CurrentScene == before);
            Check("forest integration scene opens with its real terrain", await console.OpenLab("forest")
                && GetTree().CurrentScene.GetNodeOrNull<TerrainStreamer>("Terrain")?.Generator != null);
            Check("returning from the world restores a clean sandbox", await console.OpenLab("sandbox") && console.ActiveLabId == "sandbox"
                && !console.MenuOpen && !GetTree().Paused && !GameEvents.IsPacking && !GameEvents.IsDowned && GetNode<RunState>("/root/RunState").Route == null);
            console.SetMenuOpen(false);
            for (int i = 0; i < 30; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
            Check("sandbox remains live after repeated scene changes", console.Player != null && IsInstanceValid(console.Player) && console.Player.GlobalPosition.IsFinite());
            foreach (string id in new[] { "animation", "locomotion", "balance", "packing", "recovery", "purpose", "npcs", "president", "beasts", "wildlife", "game", "learning" })
            {
                Check("manual laboratory starts: " + id, await console.OpenLab(id));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (id == "game")
                {
                    GetTree().Paused = false;
                    Vector3 station = GetNode<RunState>("/root/RunState").Route!.Station(1);
                    // A prop can exist before the streamed terrain. It must not release the guard.
                    StaticBody3D prop = new() { Name = "NonTerrainProbeFixture", Position = station + Vector3.Down * 0.8f, CollisionLayer = 1, CollisionMask = 0 };
                    prop.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2, 0.1f, 2) } });
                    GetTree().CurrentScene.AddChild(prop);
                    console.MovePlayer(station);
                    bool held = true;
                    float lowest = float.PositiveInfinity;
                    for (int i = 0; i < 120; i++)
                    {
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                        lowest = Mathf.Min(lowest, console.Player!.GlobalPosition.Y - station.Y);
                        held &= console.Player!.GlobalPosition.Y >= station.Y - 0.2f;
                    }
                    Check("teleport waits for streamed ground instead of falling through the world", held,
                        $"target {station}, final {console.Player!.GlobalPosition}, lowest delta {lowest:0.000}, floor {console.Player.IsOnFloor()}");
                }
            }
            Check("all laboratories return to the sandbox in the same window", await console.OpenLab("sandbox"));
        }
        catch (Exception problem) { _failed++; GD.Print("FAIL  playground integration: " + problem); }
        GD.Print("DEV PLAYGROUND TESTS: " + _failed + " failures");
        GetTree().Quit(_failed);
    }
    private void Check(string title, bool result, string detail = "")
    {
        if (result) { GD.Print("PASS  " + title + ": " + detail); }
        else { _failed++; GD.Print("FAIL  " + title + ": " + detail); }
    }
}
