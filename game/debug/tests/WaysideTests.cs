using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for points of interest between stations (the purpose patch):
/// - The shipped definitions are valid; broken and duplicate ones are skipped without a crash.
/// - Placement: one or two per stage over 20 stages, on the ground, clear of every path and away
///   from stations and set pieces, the same for the same seed and different for another.
/// - Near one, it is built in the world and published as the nearest (distance and side), and the
///   goals name it ("Off the path: …").
/// - E takes what it holds once (packed, WaysideUsed raised, the goals stop naming it); a second E
///   does nothing; streamed out and back in, it stays spent.
/// - With a full pack nothing is taken and the site keeps its supplies.
/// - A new run clears every site and what was taken.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/WaysideTests.tscn
/// </summary>
public partial class WaysideTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public WaysidePlacer? Placer { get; set; }

    private readonly List<string> _failures = new();
    private readonly List<WaysideNote> _used = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (PlayerBody == null || Streamer?.Generator == null || Placer == null || inventory == null || _run?.Route == null)
        {
            GD.PushError("WaysideTests: PlayerBody (with Inventory), Streamer, Placer and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.WaysideUsed += OnUsed;
        await WaitSeconds(0.5f);
        inventory.Clear();

        TestDefinitions();
        TestPlacement(Placer, _run.Route);
        WaysidePlacer.Placement? first = await TestNear(PlayerBody, Placer, _run.Route);
        if (first != null)
        {
            await TestTake(PlayerBody, Placer, inventory, _run.Route, first.Value);
        }
        await TestFullPack(PlayerBody, Placer, inventory, _run.Route);
        await TestNewRun(Placer);

        GameEvents.WaysideUsed -= OnUsed;
        if (_failures.Count == 0)
        {
            GD.Print("WAYSIDE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("WAYSIDE TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void OnUsed(WaysideNote note) => _used.Add(note);

    private void TestDefinitions()
    {
        int shipped = Placer!.ValidDefinitions.Count;
        bool items = Placer.ValidDefinitions.All(d => d.ItemPaths.All(p => ResourceLoader.Exists(p)));
        WaysidePlacer probe = new()
        {
            Definitions = new WaysideDefinition[] { Placer.ValidDefinitions[0], Placer.ValidDefinitions[0], new WaysideDefinition { Id = "EMPTY", Title = "Nothing" }, null! },
        };
        AddChild(probe);
        int kept = probe.ValidDefinitions.Count;
        probe.QueueFree();
        Check("the eight points of interest are valid and broken or duplicate ones are skipped", shipped == 8 && items && kept == 1,
            shipped + " valid, items exist " + items + ", probe kept " + kept + " of 4");
    }

    private void TestPlacement(WaysidePlacer placer, IRouteQuery route)
    {
        List<string> problems = new();
        if (placer.SetPieces == null)
        {
            problems.Add("no set pieces to keep clear of");
        }
        List<Vector3> first = new();
        int total = 0;
        for (int stage = 0; stage < 20; stage++)
        {
            IReadOnlyList<WaysidePlacer.Placement> plan = placer.PlanFor(route, stage);
            total += plan.Count;
            if (plan.Count < placer.PerStage.X || plan.Count > placer.PerStage.Y)
            {
                problems.Add("stage " + stage + " has " + plan.Count);
            }
            float top = route.StartZ;
            if (stage > 0)
            {
                top = route.Station(stage).Z;
            }
            float bottom = route.Station(stage + 1).Z;
            foreach (WaysidePlacer.Placement placement in plan)
            {
                Vector3 at = placement.Position;
                first.Add(at);
                float ground = route.GroundHeight(at.X, at.Z);
                if (Mathf.Abs(ground - placer.GroundSink - at.Y) > 0.01f)
                {
                    problems.Add(placement.Key + " not on the ground");
                }
                if (at.Z > top - placer.StationGap || at.Z < bottom + placer.StationGap)
                {
                    problems.Add(placement.Key + " near a station");
                }
                for (int path = 0; path < route.PathCount(stage); path++)
                {
                    if (Mathf.Abs(route.PathPoint(stage, path, at.Z).X - at.X) < placer.PathClearance - 0.01f)
                    {
                        problems.Add(placement.Key + " on path " + path);
                    }
                }
                if (placer.NearSetPiece(route, stage, at.X, at.Z))
                {
                    problems.Add(placement.Key + " inside a set piece");
                }
                float side = Mathf.Abs(route.RoutePoint(at.Z).X - at.X);
                if (side < placer.SideDistance.X - 0.01f || side > placer.SideDistance.Y + 0.01f)
                {
                    problems.Add(placement.Key + " " + side.ToString("0.0") + " m from the main path");
                }
            }
        }
        placer.Forget();
        List<Vector3> again = new();
        for (int stage = 0; stage < 20; stage++)
        {
            again.AddRange(placer.PlanFor(route, stage).Select(p => p.Position));
        }
        bool repeatable = again.Count == first.Count && again.Zip(first).All(pair => pair.First.IsEqualApprox(pair.Second));
        ulong seed = _run!.Seed;
        _run.Seed = seed ^ 0xABCDEFUL;
        placer.Forget();
        List<Vector3> other = new();
        for (int stage = 0; stage < 20; stage++)
        {
            other.AddRange(placer.PlanFor(route, stage).Select(p => p.Position));
        }
        _run.Seed = seed;
        placer.Forget();
        bool differs = other.Count != first.Count || other.Zip(first).Any(pair => !pair.First.IsEqualApprox(pair.Second));
        Check("one or two per stage, on the ground, clear of paths, stations and set pieces, repeatable per seed", problems.Count == 0 && repeatable && differs,
            total + " over 20 stages, repeatable " + repeatable + ", another seed differs " + differs + ", problems [" + string.Join(", ", problems.Take(8)) + "]");
    }

    private async Task<WaysidePlacer.Placement?> TestNear(PlayerController player, WaysidePlacer placer, IRouteQuery route)
    {
        WaysidePlacer.Placement? target = null;
        for (int stage = 0; stage < 6 && target == null; stage++)
        {
            foreach (WaysidePlacer.Placement placement in placer.PlanFor(route, stage))
            {
                target = placement;
                break;
            }
        }
        if (target == null)
        {
            Check("a point of interest nearby is built and named", false, "nothing planned in the first stages");
            return null;
        }
        Vector3 at = target.Value.Position;
        Vector3 path = route.RoutePoint(at.Z + 25.0f);
        await Place(player, path, 1.2f);
        bool built = placer.Sites.TryGetValue(target.Value.Key, out WaysideSite? site) && site.IsInsideTree();
        WaysideNote? near = GameEvents.NearestWayside;
        bool named = near != null && near.Value.Id == target.Value.Key && near.Value.Distance > 0.0f && near.Value.Distance <= placer.NearDistance && near.Value.Side.Length > 0;
        ObjectiveNote? goal = GameEvents.CurrentObjectives.FirstOrDefault(n => n.Kind == ObjectiveKind.Nearby);
        bool inGoals = goal != null && goal.Value.Title.Contains(target.Value.Definition.Title.ToLowerInvariant().Substring(2));
        Check("a point of interest nearby is built, published as the nearest and named in the goals", built && named && inGoals,
            $"built {built}, nearest '{near?.Title}' {near?.Distance} m {near?.Side}, goal '{goal?.Title}' ({goal?.Progress})");
        return target;
    }

    private async Task TestTake(PlayerController player, WaysidePlacer placer, CarrierInventory inventory, IRouteQuery route, WaysidePlacer.Placement target)
    {
        inventory.Clear();
        _used.Clear();
        placer.Sites.TryGetValue(target.Key, out WaysideSite? site);
        if (site == null)
        {
            Check("E takes what it holds once", false, "site not built");
            return;
        }
        List<string> expected = target.Definition.ItemPaths.Select(p => ((ItemDefinition)ResourceLoader.Load(p)).ItemId).ToList();
        bool took = site.Interact(player);
        bool packed = expected.All(id => GameEvents.CarriedItems.Contains(id)) && GameEvents.CarriedItems.Count == expected.Count;
        int carried = GameEvents.CarriedItems.Count;
        bool again = site.Interact(player);
        await WaitSeconds(0.5f);
        WaysideNote? near = GameEvents.NearestWayside;
        bool forgotten = near == null || near.Value.Id != target.Key;
        Check("E takes what it holds once, raises WaysideUsed, and the goals stop naming it",
            took && packed && site.Used && _used.Count == 1 && _used[0].Id == target.Key && !again && GameEvents.CarriedItems.Count == carried && forgotten,
            $"took {took}, packed [{string.Join(", ", GameEvents.CarriedItems)}] for [{string.Join(", ", expected)}], used {site.Used}, events {_used.Count}, second E {again}, nearest now '{near?.Title}'");

        // Stream it out (far down the mountain) and back in.
        await Place(player, route.Station(4), 1.0f);
        bool freed = !placer.Sites.ContainsKey(target.Key);
        await Place(player, route.RoutePoint(target.Position.Z + 25.0f), 1.0f);
        bool back = placer.Sites.TryGetValue(target.Key, out WaysideSite? rebuilt) && rebuilt.Used && rebuilt.Left.Count == 0;
        Check("streamed out and back in, a spent point of interest stays spent", freed && back, $"freed {freed}, rebuilt spent {back}");
    }

    private async Task TestFullPack(PlayerController player, WaysidePlacer placer, CarrierInventory inventory, IRouteQuery route)
    {
        WaysidePlacer.Placement? target = null;
        for (int stage = 0; stage < 12 && target == null; stage++)
        {
            foreach (WaysidePlacer.Placement placement in placer.PlanFor(route, stage))
            {
                if (!placer.IsUsed(placement.Key))
                {
                    target = placement;
                    break;
                }
            }
        }
        if (target == null)
        {
            Check("a full pack takes nothing", false, "no unused site");
            return;
        }
        await Place(player, route.RoutePoint(target.Value.Position.Z + 20.0f), 1.0f);
        placer.Sites.TryGetValue(target.Value.Key, out WaysideSite? site);
        if (site == null)
        {
            Check("a full pack takes nothing", false, "site not built");
            return;
        }
        inventory.Clear();
        ItemDefinition? ore = ResourceLoader.Load("res://inventory/items/ore_sample.tres") as ItemDefinition;
        int added = 0;
        while (ore != null && added < 200 && inventory.TryAdd(ore, -1.0f, false))
        {
            added++;
        }
        foreach (string path in target.Value.Definition.ItemPaths)
        {
            // Pockets too: fill them with the very items the site holds.
            while (ResourceLoader.Load(path) is ItemDefinition item && added < 400 && inventory.TryAdd(item, -1.0f, false))
            {
                added++;
            }
        }
        int before = GameEvents.CarriedItems.Count;
        int leftBefore = site.Left.Count;
        bool took = site.Interact(player);
        Check("with a full pack nothing is taken and the site keeps its supplies", !took && !site.Used && site.Left.Count == leftBefore && GameEvents.CarriedItems.Count == before,
            $"{added} items filled the pack, took {took}, used {site.Used}, left {site.Left.Count}/{leftBefore}");
        inventory.Clear();
    }

    private async Task TestNewRun(WaysidePlacer placer)
    {
        List<WaysideSite> before = placer.Sites.Values.ToList();
        GameEvents.PublishRespawnRequest();
        await WaitSeconds(0.2f);
        // The new run may already have planted its own first site by the carrier; none of the old may stay.
        int stayed = before.Count(site => IsInstanceValid(site) && !site.IsQueuedForDeletion() && placer.Sites.Values.Contains(site));
        bool cleared = stayed == 0 && !placer.IsUsed(_used.FirstOrDefault().Id ?? "");
        Check("a new run clears every site and what was taken", cleared, $"{stayed} of {before.Count} old sites stayed, {placer.Sites.Count} new");
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(settle);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print("PASS  " + name + ": " + detail);
        }
        else
        {
            GD.Print("FAIL  " + name + ": " + detail);
            _failures.Add(name);
        }
    }
}
