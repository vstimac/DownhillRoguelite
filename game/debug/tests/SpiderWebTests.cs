using System.Collections.Generic;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Spider webs: strung between two trees standing close together in the deeper circles, always the
/// same pairs; walking through one tears it and slows the carrier's stride for a while; a torn web
/// stays torn until a new run; a burning torch burns through one; none in the upper circles.
/// </summary>
public partial class SpiderWebTests : Node3D
{
    [Export] public PlayerController? Player { get; set; }

    private readonly List<string> _failures = new();
    private int _snares;
    private float _snareSpeed;

    public override async void _Ready()
    {
        if (Player == null)
        {
            GD.PushError("SpiderWebTests: Player is needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.Snared += OnSnared;
        // Two trunks 3.5 m apart (a web fits between), and one standing alone.
        StaticBody3D trees = new() { Name = "Trees" };
        trees.AddToGroup(TerrainChunk.TreeGroup);
        foreach (Vector3 at in new[] { new Vector3(-1.75f, 1.5f, -5.0f), new Vector3(1.75f, 1.5f, -5.0f), new Vector3(10.0f, 1.5f, -5.0f) })
        {
            trees.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.25f, Height = 3.0f }, Position = at });
        }
        AddChild(trees);
        SpiderWebs webs = new() { Name = "Webs", Chance = 1.0f, CircleOverride = 4 };
        AddChild(webs);
        await Frames(20);
        webs.Refresh();
        await Frames(2);
        SpiderWeb? web = webs.Webs.FirstOrDefault();
        Vector3 centre = Vector3.Zero;
        if (web != null)
        {
            centre = web.GlobalPosition;
        }
        Check("a web hangs between the two trees close together, none to the lone one", webs.Webs.Count == 1 && web != null
            && centre.DistanceTo(new Vector3(0.0f, 1.15f, -5.0f)) < 0.05f && web.Radius > 0.8f,
            $"{webs.Webs.Count} webs, centre {centre:0.00}, radius {web?.Radius ?? 0:0.00}");
        string key = web?.Key ?? "";

        // Walked through: it tears, and the stride is slow for a while.
        Player.Teleport(new Vector3(0.0f, 0.1f, -4.4f), 0.0f);
        await Frames(3);
        Player.Teleport(new Vector3(0.0f, 0.1f, -5.6f), 0.0f);
        await Frames(3);
        Check("walking through a web tears it and it clings to the legs", _snares == 1 && webs.Webs.Count == 0 && webs.Torn == 1
            && Player.Snared && Mathf.IsEqualApprox(_snareSpeed, webs.SnareSpeed),
            $"snared {_snares} times at {_snareSpeed:0.00} of the stride, {webs.Webs.Count} webs left, player slowed {Player.Snared}");
        await Frames(Mathf.CeilToInt(webs.SnareSeconds * 60.0f) + 30);
        Check("the web lets go after a few seconds", !Player.Snared, "");

        webs.Refresh();
        await Frames(2);
        Check("a torn web stays torn", webs.Webs.Count == 0, $"{webs.Webs.Count} webs");
        webs.Forget();
        webs.Refresh();
        await Frames(2);
        Check("a new run: the same web hangs in the same place again", webs.Webs.Count == 1 && webs.Webs.First().Key == key, key);

        // With a burning torch the web shrivels: it's gone, but nothing clings.
        GameEvents.PublishTorch(true);
        int snares = _snares;
        Player.Teleport(new Vector3(0.0f, 0.1f, -5.6f), 0.0f);
        await Frames(3);
        Player.Teleport(new Vector3(0.0f, 0.1f, -4.4f), 0.0f);
        await Frames(3);
        GameEvents.PublishTorch(false);
        Check("a burning torch burns through a web without being caught", webs.Burned == 1 && webs.Webs.Count == 0 && _snares == snares,
            $"burned {webs.Burned}, snared {_snares - snares} more times");

        webs.CircleOverride = 1;
        await Frames(Mathf.CeilToInt(webs.CheckEvery * 60.0f) + 10);
        Check("no webs in the upper circles", webs.Webs.Count == 0, $"{webs.Webs.Count} webs");

        GameEvents.Snared -= OnSnared;
        if (_failures.Count == 0)
        {
            GD.Print("SPIDER WEB TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"SPIDER WEB TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnSnared(float seconds, float speed)
    {
        _snares++;
        _snareSpeed = speed;
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
}
