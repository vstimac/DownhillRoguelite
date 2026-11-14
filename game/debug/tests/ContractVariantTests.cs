using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for roguelite Phase 2 (contract reliability and variants):
/// - The five first jobs (CT_001–CT_005) are valid and cover every kind.
/// - Two seeds give repeatable offers, and early stations offer only early jobs.
/// - The rules hold: intact pays in full, damaged pays in proportion, too damaged fails with
///   nothing, a stone can't be hurt, and deadlines pass.
/// - Cargo packed deep in the pack takes smaller knocks than cargo against its outside.
/// - Hard landings and falls knock it, and breaking it fails the job.
/// - A too-damaged delivery and a damaged-but-fine delivery end as they should.
/// - Any board takes back the open job on a second E.
/// - A deadline fails the job when the clock passes it.
/// - The HUD line and the companion's note mention the job.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ContractVariantTests.tscn
/// </summary>
public partial class ContractVariantTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public ContractDirector? Director { get; set; }

    private readonly List<string> _failures = new();
    private readonly List<ContractNote> _resolved = new();
    private RunState? _run;
    private WorldClock? _clock;
    private ContractDefinition[] _shipped = System.Array.Empty<ContractDefinition>();

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (PlayerBody == null || Streamer?.Generator == null || Director == null || inventory == null || _run?.Route == null || _clock == null)
        {
            GD.PushError("ContractVariantTests: PlayerBody (with Inventory), Streamer, Director, RunState.Route and WorldClock are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.ContractResolved += OnResolved;
        _shipped = Director.Contracts;
        await Place(PlayerBody, _run.Route.Station(1) + new Vector3(0.0f, 0.0f, 2.0f), 1.0f);

        TestDefinitions(Director);
        TestRepeatableOffers(Director);
        TestRules();
        if (System.Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--stress-resource-gc"))
        {
            for (int sample = 0; sample < 60; sample++)
            {
                System.GC.Collect();
                foreach (ContractDefinition definition in _shipped)
                {
                    string problem = definition.Problem();
                    if (problem.Length > 0)
                    {
                        Check("cargo remains valid during concurrent finalization", false, problem);
                    }
                }
                System.GC.WaitForPendingFinalizers();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        TestPacking(Director, inventory);
        TestLandingsAndBreaking(Director);
        TestDeliveries(Director, inventory);
        TestAbandon(Director, inventory);
        TestDeadline(Director);
        TestJobWords();
        TestNewJobsAndCargoUse(Director, inventory);
        TestWeather(Director, inventory);

        GameEvents.ContractResolved -= OnResolved;
        if (_failures.Count == 0)
        {
            GD.Print("CONTRACT VARIANT TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("CONTRACT VARIANT TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void OnResolved(ContractNote note)
    {
        _resolved.Add(note);
    }

    private ContractDefinition? Shipped(string id)
    {
        foreach (ContractDefinition definition in _shipped)
        {
            if (definition.Id == id)
            {
                return definition;
            }
        }
        return null;
    }

    /// <summary>Beta 3.1's and 3.2's jobs are valid, and a job's cargo (a med kit here) can't be used up by the carrier.</summary>
    private void TestNewJobsAndCargoUse(ContractDirector director, CarrierInventory inventory)
    {
        string[] paths =
        {
            "res://progression/contracts/resources/ct_006_fuel_for_the_fire.tres",
            "res://progression/contracts/resources/ct_007_rope_two_fires_down.tres",
            "res://progression/contracts/resources/ct_008_medicine_whole.tres",
            "res://progression/contracts/resources/ct_009_ore_for_the_smith.tres",
            "res://progression/contracts/resources/ct_010_a_stove_for_the_camp.tres",
        };
        List<string> problems = new();
        ContractDefinition? medicine = null;
        foreach (string path in paths)
        {
            ContractDefinition? definition = GD.Load<ContractDefinition>(path);
            if (definition == null)
            {
                problems.Add(path + " missing");
                continue;
            }
            string problem = definition.Problem();
            if (problem.Length > 0)
            {
                problems.Add(definition.Id + ": " + problem);
            }
            if (definition.Id == "CT_008")
            {
                medicine = definition;
            }
        }
        inventory.Clear();
        bool kept = false;
        string stage = "no medicine job";
        if (medicine != null)
        {
            ContractInstance? job = OfferOnly(director, medicine, 10);
            if (job == null)
            {
                stage = "not offered at Station 10";
            }
            else
            {
                ContractResult accepted = director.Accept(job);
                stage = "accept " + accepted;
                if (accepted == ContractResult.Ok)
                {
                    bool packed = inventory.OwnsCargo(job.InstanceId);
                    GameEvents.RequestItemUse("med_kit");
                    GameEvents.RequestUseItem("med_kit");
                    kept = inventory.OwnsCargo(job.InstanceId);
                    stage += ", packed " + packed + ", kept " + kept;
                    director.Abandon();
                }
            }
        }
        inventory.Clear();
        director.Contracts = _shipped;
        director.Reload();
        Check("the fuel, rope, medicine, ore and stove jobs are valid, and a job's med kit can't be used up",
            problems.Count == 0 && medicine != null && kept, string.Join("; ", problems) + " " + stage);
    }

    /// <summary>Rain, snow and heat wear a Preservation job's cargo only while it's exposed; other jobs and light weather are left alone.</summary>
    private void TestWeather(ContractDirector director, CarrierInventory inventory)
    {
        const string name = "rain wears exposed glass, not glass packed deep, light weather or a sealed parcel; a long storm ruins it";
        WeatherSnapshot rain = WeatherSnapshot.Calm with { Kind = WeatherKind.Rain, Rain = 1.0f };
        WeatherSnapshot drizzle = WeatherSnapshot.Calm with { Kind = WeatherKind.Rain, Rain = 0.1f };
        inventory.Clear();
        ContractInstance? glass = OfferOnly(director, Shipped("CT_003")!, 11);
        if (glass == null || director.Accept(glass) != ContractResult.Ok || CargoIn(inventory, glass.InstanceId) is not PackedItem packed)
        {
            Check(name, false, "couldn't take and pack the glass job");
            return;
        }
        ItemDefinition item = packed.Definition;
        int orientation = packed.Orientation;
        Vector3I size = packed.Size;
        Vector3I grid = inventory.Grid!.Size;
        inventory.Unpack(packed);
        Vector3I centre = new((grid.X - size.X) / 2, (grid.Y - size.Y) / 2, (grid.Z - size.Z) / 2);
        PackedItem? deep = inventory.Pack(item, centre, orientation);
        bool sheltered = GameEvents.InShelter;
        GameEvents.PublishShelter(false, -1);
        float deepLoss = director.Weather(rain, 60.0f);
        if (deep != null)
        {
            inventory.Unpack(deep);
        }
        PackedItem? edge = inventory.Pack(item, Vector3I.Zero, orientation);
        float drizzleLoss = director.Weather(drizzle, 60.0f);
        float edgeLoss = director.Weather(rain, 60.0f);
        float expected = director.WeatherWearPerMinute * glass.Definition.Fragility;
        director.Weather(rain, 3600.0f);
        bool ruined = glass.Status == ContractStatus.Failed && director.Active == null;
        inventory.Clear();
        ContractInstance? parcel = OfferOnly(director, Shipped("CT_courier_parcel")!, 12);
        float parcelLoss = -1.0f;
        if (parcel != null && director.Accept(parcel) == ContractResult.Ok)
        {
            parcelLoss = director.Weather(rain, 600.0f);
            director.Abandon();
        }
        inventory.Clear();
        if (sheltered)
        {
            GameEvents.PublishShelter(true, -1);
        }
        Check(name, deep != null && edge != null && deepLoss == 0.0f && drizzleLoss == 0.0f && Mathf.Abs(edgeLoss - expected) < 0.01f && ruined && parcelLoss == 0.0f,
            "a minute of rain took " + deepLoss.ToString("0.00") + " deep, " + edgeLoss.ToString("0.00") + " at the edge (expected " + expected.ToString("0.00")
            + "); drizzle " + drizzleLoss.ToString("0.00") + "; ruined after a long storm " + ruined + " (" + glass.Reason + "); parcel " + parcelLoss.ToString("0.00") + "; sheltered at the start " + sheltered);
    }

    /// <summary>Makes the director offer only <paramref name="definition"/>, and pins it at <paramref name="station"/>.</summary>
    private ContractInstance? OfferOnly(ContractDirector director, ContractDefinition definition, int station)
    {
        director.Contracts = new[] { definition };
        director.Reload();
        return director.Offer(station);
    }

    private void TestDefinitions(ContractDirector director)
    {
        HashSet<ContractKind> kinds = new();
        int valid = 0;
        foreach (ContractDefinition definition in _shipped)
        {
            if (definition.Problem().Length == 0)
            {
                valid++;
            }
            kinds.Add(definition.Kind);
        }
        ContractDefinition? letter = Shipped("CT_001");
        ContractDefinition? stone = Shipped("CT_002");
        ContractDefinition? glass = Shipped("CT_003");
        ContractDefinition? map = Shipped("CT_005");
        bool shaped = letter != null && letter.MinCondition > 0.0f && stone != null && stone.Fragility <= 0.0f
            && glass != null && glass.MinCondition >= 70.0f && glass.Fragility > 1.0f && map != null && map.StationsAhead == 2 && map.DeadlineDays > 0.0f
            && Shipped("CT_004") != null;
        Check("the first five jobs are valid and cover every kind", valid == _shipped.Length && _shipped.Length >= 6 && kinds.Count == 4 && shaped,
            valid + " of " + _shipped.Length + " valid, " + kinds.Count + " kinds, shaped as planned " + shaped);
    }

    private void TestRepeatableOffers(ContractDirector director)
    {
        director.Contracts = _shipped;
        director.Reload();
        ulong original = _run!.Seed;
        _run.Seed = 1234567;
        string a1 = Picks(director);
        string a2 = Picks(director);
        _run.Seed = 7654321;
        string b1 = Picks(director);
        string b2 = Picks(director);
        _run.Seed = original;
        bool earlyOnly = true;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            _run.Seed = seed;
            ContractDefinition? first = director.Pick(1);
            if (first != null && first.MinStation > 1)
            {
                earlyOnly = false;
            }
        }
        _run.Seed = original;
        Check("two seeds give repeatable offers; station 1 offers only early jobs", a1 == a2 && b1 == b2 && a1 != b1 && earlyOnly,
            "seed A: " + a1 + "\n      seed B: " + b1);
    }

    private static string Picks(ContractDirector director)
    {
        List<string> ids = new();
        for (int station = 1; station <= 10; station++)
        {
            ContractDefinition? picked = director.Pick(station);
            if (picked == null)
            {
                ids.Add("-");
            }
            else
            {
                ids.Add(picked.Id);
            }
        }
        return string.Join(" ", ids);
    }

    private void TestRules()
    {
        ContractDefinition glass = Shipped("CT_003")!;
        ContractDefinition stone = Shipped("CT_002")!;
        ContractDefinition parcel = Shipped("CT_courier_parcel")!;

        ContractInstance intact = new(parcel, 1, "Sealed parcel", 3.5f);
        ContractResolver.Accept(intact, 0.0);
        ContractResolver.Deliver(intact, 90.0f);

        ContractInstance damaged = new(parcel, 2, "Sealed parcel", 3.5f);
        ContractResolver.Accept(damaged, 0.0);
        float lost = ContractResolver.Damage(damaged, 40.0f);
        ContractResolver.Deliver(damaged, 90.0f);

        ContractInstance fragile = new(glass, 3, "Glass reliquary", 1.8f);
        ContractResolver.Accept(fragile, 0.0);
        float glassLost = ContractResolver.Damage(fragile, 20.0f);
        ContractResolver.Deliver(fragile, 90.0f);

        ContractInstance rock = new(stone, 4, "Carved stone block", 7.0f);
        ContractResolver.Accept(rock, 0.0);
        float rockLost = ContractResolver.Damage(rock, 500.0f);

        ContractInstance late = new(Shipped("CT_005")!, 5, "Rolled map tube", 0.8f);
        ContractResolver.Accept(late, 100.0);
        bool onTime = !ContractResolver.IsLate(late, 100.0 + 71.0);
        bool tooLate = ContractResolver.IsLate(late, 100.0 + 73.0);

        bool ok = intact.Status == ContractStatus.Delivered && intact.ScoreAwarded == parcel.Score
            && Mathf.IsEqualApprox(lost, 40.0f) && damaged.Status == ContractStatus.Delivered && damaged.ScoreAwarded == Mathf.RoundToInt(parcel.Score * 0.6f)
            && Mathf.IsEqualApprox(glassLost, 40.0f) && fragile.Status == ContractStatus.Failed && fragile.ScoreAwarded == 0
            && rockLost == 0.0f && Mathf.IsEqualApprox(rock.Condition, 100.0f) && onTime && tooLate;
        Check("intact pays in full, damaged in proportion, too damaged fails, stone can't break, deadlines pass", ok,
            "intact +" + intact.ScoreAwarded + ", damaged (60%) +" + damaged.ScoreAwarded + ", glass after a 20 knock: " + fragile.Status + " (" + fragile.Reason
            + "), stone lost " + rockLost + ", deadline at 72 h: on time at 71 " + onTime + ", late at 73 " + tooLate);
    }

    private PackedItem? CargoIn(CarrierInventory inventory, string instanceId)
    {
        foreach (PackedItem packed in inventory.Grid!.Items)
        {
            if (packed.Definition.CargoInstanceId == instanceId)
            {
                return packed;
            }
        }
        return null;
    }

    private void TestPacking(ContractDirector director, CarrierInventory inventory)
    {
        inventory.Clear();
        ContractInstance? job = OfferOnly(director, Shipped("CT_003")!, 2);
        if (job == null || director.Accept(job) != ContractResult.Ok)
        {
            Check("cargo packed deep takes smaller knocks than cargo at the edge", false, "couldn't take the glass job");
            return;
        }
        PackedItem? packed = CargoIn(inventory, job.InstanceId);
        if (packed == null)
        {
            Check("cargo packed deep takes smaller knocks than cargo at the edge", false, "the reliquary isn't in the pack");
            return;
        }
        ItemDefinition item = packed.Definition;
        int orientation = packed.Orientation;
        Vector3I size = packed.Size;
        Vector3I grid = inventory.Grid!.Size;
        inventory.Unpack(packed);
        Vector3I centre = new((grid.X - size.X) / 2, (grid.Y - size.Y) / 2, (grid.Z - size.Z) / 2);
        PackedItem? deep = inventory.Pack(item, centre, orientation);
        float deepExposure = -1.0f;
        GameEvents.CargoExposure.TryGetValue(job.InstanceId, out deepExposure);
        float deepLoss = director.Knock(10.0f);
        if (deep != null)
        {
            inventory.Unpack(deep);
        }
        PackedItem? edge = inventory.Pack(item, Vector3I.Zero, orientation);
        float edgeExposure = -1.0f;
        GameEvents.CargoExposure.TryGetValue(job.InstanceId, out edgeExposure);
        float edgeLoss = director.Knock(10.0f);
        bool updated = GameEvents.ActiveContract != null && Mathf.IsEqualApprox(GameEvents.ActiveContract.Value.Condition, job.Condition);
        Check("cargo packed deep takes smaller knocks than cargo at the edge",
            deep != null && edge != null && deepExposure < 0.01f && edgeExposure > 0.99f && edgeLoss > deepLoss * 3.0f && updated,
            "exposure " + deepExposure.ToString("0.00") + " deep, " + edgeExposure.ToString("0.00") + " at the edge; a knock of 10 took "
            + deepLoss.ToString("0.0") + " vs " + edgeLoss.ToString("0.0") + "; HUD knows the condition " + updated);
        director.Abandon();
        inventory.Clear();
    }

    private void TestLandingsAndBreaking(ContractDirector director)
    {
        ContractInstance? job = OfferOnly(director, Shipped("CT_003")!, 3);
        if (job == null || director.Accept(job) != ContractResult.Ok)
        {
            Check("hard landings and falls knock the cargo; breaking it fails the job", false, "couldn't take the glass job");
            return;
        }
        GameEvents.PublishLanded(director.HardLanding - 1.0f);
        float afterSoft = job.Condition;
        GameEvents.PublishLanded(director.HardLanding + 1.0f);
        float afterHard = job.Condition;
        GameEvents.PublishDowned(true);
        GameEvents.PublishDowned(false);
        float afterFall = job.Condition;
        int failedBefore = _run!.Telemetry.ContractsFailed;
        for (int i = 0; i < 10 && job.Status == ContractStatus.Accepted; i++)
        {
            GameEvents.PublishDowned(true);
            GameEvents.PublishDowned(false);
        }
        bool broke = job.Status == ContractStatus.Failed && job.Reason.Contains("broke") && director.Active == null
            && _run.Telemetry.ContractsFailed == failedBefore + 1 && job.ScoreAwarded == 0;
        Check("hard landings and falls knock the cargo; breaking it fails the job",
            Mathf.IsEqualApprox(afterSoft, 100.0f) && afterHard < 100.0f && afterFall < afterHard && broke,
            "soft landing " + afterSoft.ToString("0") + "%, hard " + afterHard.ToString("0") + "%, fall " + afterFall.ToString("0") + "%, then " + job.Status + " (" + job.Reason + ")");
    }

    private void TestDeliveries(ContractDirector director, CarrierInventory inventory)
    {
        inventory.Clear();
        ContractInstance? glass = OfferOnly(director, Shipped("CT_003")!, 4);
        bool taken = glass != null && director.Accept(glass) == ContractResult.Ok;
        if (glass == null || !taken)
        {
            Check("a too-damaged delivery fails with nothing; a damaged one pays in part", false, "couldn't take the glass job");
            return;
        }
        int scoreBefore = _run!.Telemetry.CargoScore;
        director.Knock(15.0f);
        ContractResult handed = director.Deliver(glass.ToStation);
        bool gone = CargoIn(inventory, glass.InstanceId) == null;
        bool tooDamaged = handed == ContractResult.Ok && glass.Status == ContractStatus.Failed && glass.Reason.Contains("too damaged") && gone
            && _run.Telemetry.CargoScore == scoreBefore;

        ContractInstance? parcel = OfferOnly(director, Shipped("CT_courier_parcel")!, 5);
        bool parcelTaken = parcel != null && director.Accept(parcel) == ContractResult.Ok;
        if (parcel == null || !parcelTaken)
        {
            Check("a too-damaged delivery fails with nothing; a damaged one pays in part", false, "couldn't take the parcel job");
            return;
        }
        director.Knock(20.0f);
        float condition = parcel.Condition;
        director.Deliver(parcel.ToStation);
        int expected = Mathf.RoundToInt(parcel.Definition.Score * condition / 100.0f);
        bool partPaid = parcel.Status == ContractStatus.Delivered && condition < 90.0f && parcel.ScoreAwarded == expected
            && _run.Telemetry.CargoScore == scoreBefore + expected;
        Check("a too-damaged delivery fails with nothing; a damaged one pays in part", tooDamaged && partPaid,
            "glass: " + glass.Status + " (" + glass.Reason + "), cargo gone " + gone + "; parcel at " + condition.ToString("0") + "%: "
            + parcel.Status + " +" + parcel.ScoreAwarded + " of " + parcel.Definition.Score);
    }

    private void TestAbandon(ContractDirector director, CarrierInventory inventory)
    {
        inventory.Clear();
        ContractInstance? job = OfferOnly(director, Shipped("CT_004")!, 6);
        bool taken = job != null && director.Accept(job) == ContractResult.Ok;
        ContractInstance? next = director.Offer(7);
        director.Boards.TryGetValue(7, out ContractBoard? board);
        if (job == null || !taken || next == null || board == null)
        {
            Check("any board takes back the open job on a second E", false, "couldn't set up the job and a second board");
            return;
        }
        string promptBefore = board.Prompt;
        bool first = board.Interact(PlayerBody!);
        bool armed = board.ArmedToAbandon && job.Status == ContractStatus.Accepted;
        bool second = board.Interact(PlayerBody!);
        bool given = job.Status == ContractStatus.Abandoned && director.Active == null && CargoIn(inventory, job.InstanceId) == null
            && job.ScoreAwarded == 0 && board.Prompt.Contains("Take");
        bool takeNext = director.Accept(next) == ContractResult.Ok;
        Check("any board takes back the open job on a second E", first && armed && second && given && takeNext && promptBefore.Contains("Give up"),
            "prompt '" + promptBefore + "', first E armed " + armed + ", then " + job.Status + ", cargo left at the board, next board: '" + board.Prompt
            + "', next job taken " + takeNext);
        director.Abandon();
        inventory.Clear();
    }

    private void TestDeadline(ContractDirector director)
    {
        ContractInstance? job = OfferOnly(director, Shipped("CT_005")!, 8);
        bool taken = job != null && director.Accept(job) == ContractResult.Ok;
        if (job == null || !taken)
        {
            Check("a deadline fails the job when the clock passes it", false, "couldn't take the map job");
            return;
        }
        double left = job.DeadlineHours - _clock!.TotalHours;
        bool early = !director.CheckDeadline();
        _clock.AdvanceHours((float)left - 1.0f);
        bool stillOpen = !director.CheckDeadline() && job.Status == ContractStatus.Accepted;
        _clock.AdvanceHours(2.0f);
        bool failed = director.CheckDeadline() && job.Status == ContractStatus.Failed && job.Reason.Contains("time") && director.Active == null;
        Check("a deadline fails the job when the clock passes it", Mathf.IsEqualApprox((float)left, job.Definition.DeadlineDays * 24.0f) && early && stillOpen && failed,
            left.ToString("0") + " h to deliver, open an hour before " + stillOpen + ", then " + job.Status + " (" + job.Reason + ")");
    }

    private void TestJobWords()
    {
        ContractNote job = new("CT_003@2", "The Glass Truth", "Glass reliquary", 1.8f, 2, 3, ContractStatus.Accepted, 0, "", 82.0f, 70.0f, 30.0, true);
        string line = DescentPanel.JobLine(job, 10.0, 0.05f);
        string note = CompanionChat.WithJob("[circle 1, Limbo; carrying nothing]", job, 10.0);
        string none = CompanionChat.WithJob("[circle 1]", null, 0.0);
        Check("the HUD line and the companion's note mention the job",
            line.Contains("The Glass Truth") && line.Contains("Station 3") && line.Contains("82%") && line.Contains("70%") && line.Contains("left")
            && note.Contains("on a job") && note.Contains("82%") && note.Contains("20 hours") && note.EndsWith("]") && none == "[circle 1]",
            "HUD: " + line + "\n      companion: " + note);
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
