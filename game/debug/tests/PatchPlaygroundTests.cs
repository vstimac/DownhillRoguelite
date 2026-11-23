using System;
using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The patch playground (debug/PatchPlayground.tscn): every entry has a unique id, a loadable scene,
/// a description and a way to try it, opens, and sets itself up (it stands where it says, the beast
/// is on its way, the item is in the pack).
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/PatchPlaygroundTests.tscn
/// </summary>
public partial class PatchPlaygroundTests : Node
{
    private int _failed;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        try
        {
            Node blank = new() { Name = "TestTransition" };
            GetTree().Root.AddChild(blank);
            GetTree().CurrentScene = blank;
            DevPlayground config = GD.Load<PackedScene>("res://debug/PatchPlayground.tscn").Instantiate<DevPlayground>();
            DevPlaygroundConsole console = new() { Labs = config.Labs, Seed = config.Seed, HomeLab = config.HomeLab, Heading = config.Heading, Intro = config.Intro };
            config.Free();
            GetTree().Root.AddChild(console);

            HashSet<string> ids = new(StringComparer.Ordinal);
            List<string> bad = new();
            foreach (DevPlaygroundEntry entry in console.Labs)
            {
                if (!ids.Add(entry.Id) || ResourceLoader.Load(entry.ScenePath) is not PackedScene
                    || !entry.Description.Contains("STO JE NOVO", StringComparison.Ordinal)
                    || !entry.Description.Contains("KAKO ISPROBATI", StringComparison.Ordinal)
                    || entry.Category.Length == 0 || entry.Controls.Length == 0)
                {
                    bad.Add(entry.Id);
                }
            }
            Check("every entry has a unique id, a scene, what changed and how to try it", bad.Count == 0 && console.Labs.Count >= 10,
                $"{console.Labs.Count} entries; wrong: {string.Join(", ", bad)}");
            Check("the first section is the next patch", console.Labs[0].Category == "Sljedeca zakrpa" && console.HomeLab == console.Labs[0].Id);

            foreach (DevPlaygroundEntry entry in console.Labs)
            {
                bool opened = await console.OpenLab(entry.Id);
                for (int i = 0; i < 20; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
                string where = console.Player != null && IsInstanceValid(console.Player) ? console.Player.GlobalPosition.ToString("0.0") : "no carrier";
                Check($"{entry.Category} / {entry.Title} opens and sets up", opened && console.LastSetupMissed.Length == 0
                        && (console.Player == null || console.Player.GlobalPosition.IsFinite()),
                    $"setup '{entry.Setup}', missed '{string.Join(", ", console.LastSetupMissed)}', carrier at {where}");
            }
            Check("it returns to the first entry", await console.OpenLab(console.HomeLab));
        }
        catch (Exception problem) { _failed++; GD.Print("FAIL  patch playground: " + problem); }
        GD.Print("PATCH PLAYGROUND TESTS: " + _failed + " failures");
        GetTree().Quit(_failed);
    }

    private void Check(string title, bool result, string detail = "")
    {
        if (result) { GD.Print("PASS  " + title + ": " + detail); }
        else { _failed++; GD.Print("FAIL  " + title + ": " + detail); }
    }
}
