using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for roguelite Phase 3 (relics):
/// - The shipped relics are valid. Broken, duplicate or missing definitions are skipped without a crash.
/// - Placement is repeatable for a seed, differs between seeds, and lies on the ground beside a path
///   (a side path when the stage has one) within each relic's stages.
/// - Coming close finds a relic once per run (journal event, "seen" mark).
/// - Picking the counterweight up adds its 8 kg to the load.
/// - Knocks hurt the fragile letter and never the stone.
/// - Dropping a relic and taking it again keeps the very same relic and its condition.
/// - A station altar takes a carried relic on E then E: it leaves the pack, scores (in proportion
///   when damaged), is counted, and is marked studied.
/// - A new run clears every site, altar and carried state; "seen" and "studied" marks stay.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RelicTests.tscn
/// </summary>
public partial class RelicTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public RelicDirector? Director { get; set; }

    private const string LetterId = "RL_001";
    private const string WeightId = "RL_004";

    private readonly List<string> _failures = new();
    private readonly List<RelicNote> _found = new();
    private readonly List<RelicNote> _delivered = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (PlayerBody == null || Streamer?.Generator == null || Director == null || inventory == null || _run?.Route == null)
        {
            GD.PushError("RelicTests: PlayerBody (with Inventory), Streamer, Director and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.RelicDiscovered += OnFound;
        GameEvents.RelicDelivered += OnDelivered;
        await Place(PlayerBody, _run.Route.Station(1) + new Vector3(0.0f, 0.0f, 2.0f), 1.0f);
        inventory.Clear();

        TestDefinitions(Director);
        TestPlacement(Director, _run.Route);
        await TestDiscoverAndCarry(PlayerBody, Director, inventory, _run.Route);
        TestKnocks(Director);
        await TestDropAndPickUp(PlayerBody, Director, inventory);
        await TestAltar(PlayerBody, Director, inventory, _run.Route);
        await TestNewRun(Director);

        GameEvents.RelicDiscovered -= OnFound;
        GameEvents.RelicDelivered -= OnDelivered;
        if (_failures.Count == 0)
        {
            GD.Print("RELIC TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("RELIC TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void OnFound(RelicNote note)
    {
        _found.Add(note);
    }

    private void OnDelivered(RelicNote note)
    {
        _delivered.Add(note);
    }

    private void TestDefinitions(RelicDirector director)
    {
        RelicDefinition[] shipped = director.Relics;
        int valid = 0;
        foreach (RelicDefinition relic in shipped)
        {
            if (relic.Problem().Length == 0)
            {
                valid++;
            }
        }
        RelicDefinition? letter = director.Find(LetterId);
        RelicDefinition? weight = director.Find(WeightId);
        bool shaped = letter != null && letter.Fragility > 0.0f && letter.Interpretations.Length >= 2
            && weight != null && weight.Fragility <= 0.0f && weight.BaseScore > letter.BaseScore && weight.Interpretations.Length >= 2;

        RelicDefinition missing = new() { Id = "RL_X", DisplayName = "Nothing", SeenText = "Nothing.", Interpretations = new[] { "None." }, ItemPath = "res://nowhere.tres" };
        RelicDefinition duplicate = (RelicDefinition)shipped[0].Duplicate();
        director.Relics = new[] { shipped[0], null!, missing, duplicate, shipped[1] };
        director.Reload();
        int kept = director.ValidRelics.Count;
        director.Relics = shipped;
        director.Reload();
        Check("the shipped relics are valid; broken ones are skipped", valid == shipped.Length && shipped.Length >= 2 && shaped && kept == 2
            && director.ValidRelics.Count == shipped.Length,
            valid + " of " + shipped.Length + " valid, shaped as planned " + shaped + "; with a null, a missing item and a duplicate added, " + kept + " kept");
    }

    private void TestPlacement(RelicDirector director, IRouteQuery route)
    {
        ulong original = _run!.Seed;
        string a1 = Describe(director, route, 1234567);
        string a2 = Describe(director, route, 1234567);
        string b1 = Describe(director, route, 7654321);
        _run.Seed = original;
        director.Forget();

        bool grounded = true;
        bool inRange = true;
        bool beside = true;
        int sidePaths = 0;
        int placed = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            _run.Seed = seed;
            director.Forget();
            foreach (RelicDirector.Placement placement in director.Plan(route).Values)
            {
                placed++;
                Vector3 at = placement.Position;
                grounded = grounded && Mathf.Abs(at.Y - route.GroundHeight(at.X, at.Z)) < 0.2f;
                inRange = inRange && placement.Stage >= placement.Relic.StageRange.X && placement.Stage <= placement.Relic.StageRange.Y;
                Vector3 centre = route.PathPoint(placement.Stage, placement.Path, at.Z);
                beside = beside && Mathf.Abs(Mathf.Abs(at.X - centre.X) - director.SideOffset) < 0.05f;
                if (route.PathCount(placement.Stage) > 1)
                {
                    if (placement.Path >= 1)
                    {
                        sidePaths++;
                    }
                    else
                    {
                        beside = false;
                    }
                }
            }
        }
        _run.Seed = original;
        director.Forget();
        Check("relics lie where the seed says, on the ground beside a path", a1 == a2 && a1 != b1 && a1.Length > 0 && grounded && inRange && beside && placed == 40,
            "seed A: " + a1 + "\n      seed B: " + b1 + "\n      20 seeds: " + placed + " placed, " + sidePaths + " on side paths, grounded " + grounded
            + ", in their stages " + inRange + ", beside the path " + beside);
    }

    private string Describe(RelicDirector director, IRouteQuery route, ulong seed)
    {
        _run!.Seed = seed;
        director.Forget();
        List<string> parts = new();
        foreach (RelicDirector.Placement placement in director.Plan(route).Values)
        {
            parts.Add(placement.Relic.Id + "@" + placement.Stage + "/" + placement.Path + " (" + placement.Position.X.ToString("0.0") + ", " + placement.Position.Z.ToString("0.0") + ")");
        }
        parts.Sort();
        return string.Join(" ", parts);
    }

    private async Task TestDiscoverAndCarry(PlayerController player, RelicDirector director, CarrierInventory inventory, IRouteQuery route)
    {
        director.Forget();
        IReadOnlyDictionary<string, RelicDirector.Placement> plan = director.Plan(route);
        if (!plan.TryGetValue(WeightId, out RelicDirector.Placement weight) || !plan.TryGetValue(LetterId, out RelicDirector.Placement letter))
        {
            Check("coming close finds a relic; the counterweight adds 8 kg", false, "a relic wasn't placed this run");
            return;
        }
        _found.Clear();
        await Place(player, weight.Position + new Vector3(0.0f, 0.0f, -3.0f), 1.0f);
        bool seen = _run!.RelicsSeen.ReadThisRun(WeightId) && _found.Count == 1 && _found[0].Id == WeightId && _found[0].Name == "The Counterweight";
        await WaitFrames(5);
        bool stillOnce = _found.Count == 1;
        WorldItem? item = SiteItem(director, WeightId);
        float before = GameEvents.CurrentLoad.Mass;
        bool picked = item != null && item.Interact(player);
        await WaitFrames(3);
        float added = GameEvents.CurrentLoad.Mass - before;
        bool carried = Carried("relic:" + WeightId) && director.CarriedRelics().Count == 1 && !director.Sites.ContainsKey(WeightId);
        Check("coming close finds a relic once; the counterweight adds 8 kg", seen && stillOnce && picked && Mathf.Abs(added - 8.0f) < 0.05f && carried,
            "found " + seen + " (" + _found.Count + " note), picked up " + picked + ", load +" + added.ToString("0.00") + " kg, carried and site cleared " + carried);

        await Place(player, letter.Position + new Vector3(0.0f, 0.0f, -2.0f), 1.0f);
        WorldItem? note = SiteItem(director, LetterId);
        bool letterTaken = note != null && note.Interact(player);
        await WaitFrames(3);
        Check("the letter is found and taken too", letterTaken && _found.Count == 2 && Carried("relic:" + LetterId) && director.CarriedRelics().Count == 2
            && inventory.AllItems.Count == 2,
            "taken " + letterTaken + ", " + _found.Count + " found, " + director.CarriedRelics().Count + " relics carried");
    }

    private static WorldItem? SiteItem(RelicDirector director, string id)
    {
        if (!director.Sites.TryGetValue(id, out Node3D? site))
        {
            return null;
        }
        return site.GetNodeOrNull<WorldItem>("Relic");
    }

    private static bool Carried(string cargoId)
    {
        foreach (string id in GameEvents.CarriedCargo)
        {
            if (id == cargoId)
            {
                return true;
            }
        }
        return false;
    }

    private void TestKnocks(RelicDirector director)
    {
        float letterBefore = director.ConditionOf(LetterId);
        GameEvents.PublishLanded(director.HardLanding - 1.0f);
        float afterSoft = director.ConditionOf(LetterId);
        GameEvents.PublishLanded(director.HardLanding + 2.0f);
        float afterHard = director.ConditionOf(LetterId);
        GameEvents.PublishDowned(true);
        GameEvents.PublishDowned(false);
        float afterFall = director.ConditionOf(LetterId);
        float weight = director.ConditionOf(WeightId);
        Check("knocks hurt the fragile letter, never the stone",
            Mathf.IsEqualApprox(letterBefore, afterSoft) && afterHard < afterSoft && afterFall < afterHard && afterFall > 0.0f && Mathf.IsEqualApprox(weight, 100.0f),
            "letter " + letterBefore.ToString("0") + "% → soft landing " + afterSoft.ToString("0") + "% → hard " + afterHard.ToString("0") + "% → fall "
            + afterFall.ToString("0") + "%; counterweight " + weight.ToString("0") + "%");
    }

    private async Task TestDropAndPickUp(PlayerController player, RelicDirector director, CarrierInventory inventory)
    {
        float condition = director.ConditionOf(LetterId);
        int foundBefore = _found.Count;
        GameEvents.RequestDrop("relic_unsent_letter");
        await WaitFrames(12);
        WorldItem? dropped = inventory.LastDropped;
        ItemDefinition? kind = dropped?.Definition;
        bool onGround = kind != null && kind.CargoInstanceId == "relic:" + LetterId && !Carried("relic:" + LetterId) && director.CarriedRelics().Count == 1;
        bool again = dropped != null && dropped.Interact(player);
        await WaitFrames(3);
        bool same = false;
        foreach (ItemDefinition item in inventory.AllItems)
        {
            same = same || ReferenceEquals(item, kind);
        }
        Check("dropping a relic and taking it again keeps the very same relic", onGround && again && same && Carried("relic:" + LetterId)
            && Mathf.IsEqualApprox(director.ConditionOf(LetterId), condition) && _found.Count == foundBefore,
            "on the ground " + onGround + ", picked up " + again + ", same item " + same + ", condition " + director.ConditionOf(LetterId).ToString("0") + "% (was "
            + condition.ToString("0") + "%), new finds " + (_found.Count - foundBefore));
    }

    private async Task TestAltar(PlayerController player, RelicDirector director, CarrierInventory inventory, IRouteQuery route)
    {
        await Place(player, route.Station(1) + new Vector3(0.0f, 0.0f, 2.0f), 1.0f);
        if (!director.Altars.TryGetValue(1, out RelicAltar? altar))
        {
            Check("an altar takes carried relics on E then E", false, "no altar at Station 1");
            return;
        }
        float letterCondition = director.ConditionOf(LetterId);
        int expectedLetter = Mathf.RoundToInt(director.Find(LetterId)!.BaseScore * letterCondition / 100.0f);
        int expectedWeight = director.Find(WeightId)!.BaseScore;
        int scoreBefore = _run!.Telemetry.CargoScore;
        float massBefore = GameEvents.CurrentLoad.Mass;
        string prompt = altar.Prompt;
        bool first = altar.Interact(player);
        bool armedOnly = altar.Armed && _delivered.Count == 0 && director.CarriedRelics().Count == 2;
        bool second = altar.Interact(player);
        bool third = altar.Interact(player);
        bool fourth = altar.Interact(player);
        await WaitFrames(3);
        int scored = _run.Telemetry.CargoScore - scoreBefore;
        bool gone = director.CarriedRelics().Count == 0 && inventory.AllItems.Count == 0 && massBefore - GameEvents.CurrentLoad.Mass > 8.1f;
        bool notes = _delivered.Count == 2 && _delivered[0].Station == 1 && _delivered[0].Text.Length > 0;
        bool studied = _run.RelicsStudied.HasEverRead(LetterId) && _run.RelicsStudied.HasEverRead(WeightId);
        bool counted = _run.Telemetry.RelicsDelivered == 2;
        bool empty = !altar.Interact(player) && altar.Prompt.Length == 0;
        bool again = !director.Deliver(director.Find(WeightId)!, 1);
        Check("an altar takes carried relics on E then E; they score and are studied",
            prompt.Contains("Leave") && first && armedOnly && second && third && fourth && gone && notes && studied && counted && empty && again
            && scored == expectedLetter + expectedWeight,
            "prompt '" + prompt + "', first E armed " + armedOnly + ", left both " + gone + ", +" + scored + " (expected " + expectedWeight + " + " + expectedLetter
            + " for the letter at " + letterCondition.ToString("0") + "%), studied " + studied + ", counted " + counted + "; reading: \"" + FirstReading() + "\"");
    }

    private string FirstReading()
    {
        if (_delivered.Count == 0)
        {
            return "";
        }
        return _delivered[0].Text;
    }

    private async Task TestNewRun(RelicDirector director)
    {
        GameEvents.PublishRespawnRequest();
        await WaitFrames(3);
        bool cleared = director.Sites.Count == 0 && !director.WasDelivered(WeightId) && Mathf.IsEqualApprox(director.ConditionOf(LetterId), 100.0f);
        bool remembered = _run!.RelicsSeen.HasEverRead(WeightId) && !_run.RelicsSeen.ReadThisRun(WeightId) && _run.RelicsStudied.HasEverRead(LetterId)
            && _run.Telemetry.RelicsDelivered == 0;
        Check("a new run clears every relic; seen and studied marks stay", cleared && remembered,
            "cleared " + cleared + ", remembered " + remembered);
    }

    private async Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < settle; elapsed += dt)
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
