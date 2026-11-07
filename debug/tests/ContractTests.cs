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
/// Headless checks for cargo contracts (roguelite Phase 1, Courier): definitions are validated;
/// offers are the same for the same mountain and station; reaching station 1 pins a job on a
/// board; a full pack can't take it and nothing changes; taking it packs exactly one real copy of
/// the cargo, which changes the load's mass and centre of mass (higher when packed higher); taking
/// it twice does nothing; it can't be handed over at the wrong station or when it's on the ground;
/// dropping and picking it up keeps the very same parcel; at station 2 the delivery post needs a
/// second E, then removes that parcel and credits the job once; cargo lost from a torn pack fails
/// its job with no credit; dying with a job fails it, the run summary counts the delivery, and a
/// new run has no offers, no job and no cargo.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ContractTests.tscn
/// </summary>
public partial class ContractTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public ContractDirector? Director { get; set; }

    private const string ParcelPath = "res://inventory/items/sealed_parcel.tres";
    private const string StonePath = "res://inventory/items/ore_sample.tres";

    private readonly List<string> _failures = new();
    private RunState? _run;
    private RunSummary? _summary;
    private int _offered;
    private int _accepted;
    private int _resolved;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (PlayerBody == null || Streamer == null || Director == null || _run?.Route == null || inventory == null)
        {
            GD.PushError("ContractTests: PlayerBody (with Inventory), Streamer, Director and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        _run.SummaryDelay = 1.0f;
        GameEvents.ContractOffered += OnOffered;
        GameEvents.ContractAccepted += OnAccepted;
        GameEvents.ContractResolved += OnResolved;
        GameEvents.RunEnded += OnRunEnded;
        await WaitSeconds(1.0f);

        TestDefinitions(Director);
        TestPicks(Director);
        ContractInstance? offer = await TestOfferAtStation(PlayerBody, Director, _run.Route);
        if (offer != null)
        {
            TestNoRoom(Director, inventory, offer);
            ItemDefinition? parcel = TestAccept(Director, inventory, offer);
            TestRepackHigher(inventory, parcel);
            TestNoDoubles(Director, inventory, offer);
            await TestDropAndPickUp(PlayerBody, Director, inventory, parcel);
            await TestDelivery(PlayerBody, Director, inventory, _run.Route, offer);
            TestLostCargo(Director, inventory);
            await TestDeathAndNewRun(Director, inventory);
        }

        GameEvents.ContractOffered -= OnOffered;
        GameEvents.ContractAccepted -= OnAccepted;
        GameEvents.ContractResolved -= OnResolved;
        GameEvents.RunEnded -= OnRunEnded;
        if (_failures.Count == 0)
        {
            GD.Print("CONTRACT TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("CONTRACT TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void OnOffered(ContractNote note) => _offered++;

    private void OnAccepted(ContractNote note) => _accepted++;

    private void OnResolved(ContractNote note) => _resolved++;

    private void OnRunEnded(RunSummary summary) => _summary = summary;

    private void TestDefinitions(ContractDirector director)
    {
        ContractDefinition noId = new() { DisplayName = "Nameless", CargoItemPath = ParcelPath };
        ContractDefinition noCargo = new() { Id = "CT_test_missing", DisplayName = "Missing", CargoItemPath = "res://inventory/items/no_such_item.tres" };
        ContractDefinition backwards = new() { Id = "CT_test_back", DisplayName = "Uphill", CargoItemPath = ParcelPath, StationsAhead = 0 };
        bool shipped = director.ValidContracts.Count == director.Contracts.Length && director.ValidContracts.Count > 0;
        foreach (ContractDefinition definition in director.ValidContracts)
        {
            shipped = shipped && definition.Problem().Length == 0;
        }
        Check("contract definitions are checked: the shipped ones are valid, broken ones are refused",
            shipped && noId.Problem().Length > 0 && noCargo.Problem().Length > 0 && backwards.Problem().Length > 0,
            director.ValidContracts.Count + " valid; no id: '" + noId.Problem() + "', missing cargo: '" + noCargo.Problem() + "', uphill: '" + backwards.Problem() + "'");
    }

    private void TestPicks(ContractDirector director)
    {
        ContractDefinition[] shipped = director.Contracts;
        ContractDefinition stone = new() { Id = "CT_test_stone", DisplayName = "A Stone", CargoItemPath = StonePath, Score = 10 };
        director.Contracts = new[] { shipped[0], stone };
        director.Reload();
        List<string> first = PickSequence(director);
        List<string> again = PickSequence(director);
        HashSet<string> kinds = new(first);
        bool same = first.Count == again.Count;
        for (int i = 0; i < first.Count && same; i++)
        {
            same = first[i] == again[i];
        }
        director.Contracts = shipped;
        director.Reload();
        Check("the same mountain and station offer the same job", same && kinds.Count == 2 && director.Pick(0) == null,
            "stations 1-12: " + string.Join(" ", first) + "; station 0 offers nothing");
    }

    private static List<string> PickSequence(ContractDirector director)
    {
        List<string> ids = new();
        for (int station = 1; station <= 12; station++)
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
        return ids;
    }

    private async Task<ContractInstance?> TestOfferAtStation(PlayerController player, ContractDirector director, IRouteQuery route)
    {
        Vector3 station = route.Station(1);
        await Place(player, station + new Vector3(0.0f, 0.0f, 2.0f), 1.5f);
        director.Offers.TryGetValue(1, out ContractInstance? offer);
        director.Boards.TryGetValue(1, out ContractBoard? board);
        float distance = 99.0f;
        float height = 99.0f;
        if (board != null)
        {
            distance = board.GlobalPosition.DistanceTo(station);
            height = Mathf.Abs(board.GlobalPosition.Y - route.GroundHeight(board.GlobalPosition.X, board.GlobalPosition.Z));
        }
        bool fine = offer != null && board != null && offer.Status == ContractStatus.Offered && offer.FromStation == 1 && offer.ToStation == 2
            && distance < 7.0f && height < 0.6f && _offered == 1 && board.Prompt.Length > 0;
        string detail = "no offer";
        if (offer != null)
        {
            detail = offer.Definition.DisplayName + " (" + offer.InstanceId + "), station " + offer.FromStation + " to " + offer.ToStation
                + ", board " + distance.ToString("0.0") + " m from the fire, " + height.ToString("0.00") + " m off the ground";
        }
        Check("reaching station 1 pins one job for station 2 on a board by the fire", fine, detail);
        return offer;
    }

    private void TestNoRoom(ContractDirector director, CarrierInventory inventory, ContractInstance offer)
    {
        ItemDefinition? stone = ResourceLoader.Load(StonePath) as ItemDefinition;
        int packed = 0;
        while (stone != null && packed < 200 && inventory.TryAdd(stone, -1.0f, false))
        {
            packed++;
        }
        int owned = inventory.AllItems.Count;
        float mass = GameEvents.CurrentLoad.Mass;
        ContractResult result = director.Accept(offer);
        bool unchanged = result == ContractResult.NoRoom && offer.Status == ContractStatus.Offered && director.Active == null
            && GameEvents.CarriedCargo.Count == 0 && inventory.AllItems.Count == owned && Mathf.IsEqualApprox(GameEvents.CurrentLoad.Mass, mass)
            && _accepted == 0 && director.Offers.ContainsKey(1);
        Check("a full pack can't take the job, and nothing changes", unchanged,
            packed + " stones fill the pack; taking the job: " + result + ", offer " + offer.Status + ", cargo carried " + GameEvents.CarriedCargo.Count);
        inventory.Clear();
    }

    private ItemDefinition? TestAccept(ContractDirector director, CarrierInventory inventory, ContractInstance offer)
    {
        LoadSnapshot before = GameEvents.CurrentLoad;
        ContractResult result = director.Accept(offer);
        LoadSnapshot after = GameEvents.CurrentLoad;
        ItemDefinition? parcel = null;
        foreach (ItemDefinition item in inventory.AllItems)
        {
            if (item.CargoInstanceId == offer.InstanceId)
            {
                parcel = item;
            }
        }
        ItemDefinition? shared = ResourceLoader.Load(ParcelPath) as ItemDefinition;
        bool ownCopy = parcel != null && parcel != shared && shared != null && shared.CargoInstanceId.Length == 0 && parcel.ItemId == shared.ItemId;
        float added = after.Mass - before.Mass;
        float moved = after.CenterOfMass.DistanceTo(before.CenterOfMass);
        bool fine = result == ContractResult.Ok && offer.Status == ContractStatus.Accepted && director.Active == offer && ownCopy
            && inventory.AllItems.Count == 1 && GameEvents.CarriedCargo.Count == 1 && GameEvents.CarriedCargo[0] == offer.InstanceId
            && Mathf.Abs(added - offer.CargoMassKg) < 0.01f && moved > 0.001f && _accepted == 1
            && GameEvents.ActiveContract != null && GameEvents.ActiveContract.Value.InstanceId == offer.InstanceId && director.Post != null;
        Check("taking the job packs exactly one parcel of its own, and the load gets heavier and shifts", fine,
            result + ", " + inventory.AllItems.Count + " item(s), own copy " + ownCopy + ", load " + before.Mass.ToString("0.00") + " → " + after.Mass.ToString("0.00")
            + " kg, centre of mass moved " + (moved * 100.0f).ToString("0.0") + " cm, delivery post " + (director.Post != null));
        return parcel;
    }

    private void TestRepackHigher(CarrierInventory inventory, ItemDefinition? parcel)
    {
        if (inventory.Grid == null || parcel == null)
        {
            Check("packing the parcel higher raises the load's centre of mass", false, "no parcel");
            return;
        }
        PackedItem? packed = null;
        foreach (PackedItem item in inventory.Grid.Items)
        {
            if (item.Definition == parcel)
            {
                packed = item;
            }
        }
        if (packed == null)
        {
            Check("packing the parcel higher raises the load's centre of mass", false, "the parcel isn't in the grid");
            return;
        }
        float low = GameEvents.CurrentLoad.CenterOfMass.Y;
        inventory.Unpack(packed);
        Vector3I top = new(packed.Origin.X, inventory.Grid.Size.Y - packed.Size.Y, packed.Origin.Z);
        PackedItem? high = inventory.Pack(parcel, top, packed.Orientation);
        float raised = GameEvents.CurrentLoad.CenterOfMass.Y;
        Check("packing the parcel higher raises the load's centre of mass", high != null && raised > low + 0.05f && GameEvents.CarriedCargo.Count == 1,
            "centre of mass " + low.ToString("0.000") + " m packed low, " + raised.ToString("0.000") + " m packed at the top");
    }

    private void TestNoDoubles(ContractDirector director, CarrierInventory inventory, ContractInstance offer)
    {
        ContractResult again = director.Accept(offer);
        ContractResult wrong = director.Deliver(1);
        bool boardRefuses = director.Boards.TryGetValue(1, out ContractBoard? board) && board.Prompt.Length == 0;
        bool grantedTwice = inventory.GrantCargo(ParcelPath, offer.InstanceId);
        Check("taking the job twice does nothing; it can't be handed over at the wrong station",
            again == ContractResult.AlreadyAccepted && wrong == ContractResult.WrongStation && boardRefuses && !grantedTwice
            && inventory.AllItems.Count == 1 && GameEvents.CarriedCargo.Count == 1 && _accepted == 1 && offer.Status == ContractStatus.Accepted,
            "again: " + again + ", at station 1: " + wrong + ", board refuses " + boardRefuses + ", second grant " + grantedTwice + ", " + inventory.AllItems.Count + " item(s)");
    }

    private async Task TestDropAndPickUp(PlayerController player, ContractDirector director, CarrierInventory inventory, ItemDefinition? parcel)
    {
        GameEvents.RequestDrop("sealed_parcel");
        await WaitSeconds(0.2f);
        WorldItem? dropped = inventory.LastDropped;
        bool onGround = dropped != null && dropped.Definition == parcel && GameEvents.CarriedCargo.Count == 0 && inventory.AllItems.Count == 0;
        ContractResult missing = director.CanDeliverAt(2);
        bool again = dropped != null && dropped.Interact(player);
        await WaitSeconds(0.1f);
        bool same = false;
        foreach (ItemDefinition item in inventory.AllItems)
        {
            same = same || item == parcel;
        }
        Check("dropping the parcel and picking it up keeps the very same parcel", onGround && missing == ContractResult.CargoMissing && again && same
            && inventory.AllItems.Count == 1 && GameEvents.CarriedCargo.Count == 1 && director.Active != null,
            "on the ground " + onGround + ", delivery while dropped: " + missing + ", picked up " + again + ", same parcel " + same);
    }

    private async Task TestDelivery(PlayerController player, ContractDirector director, CarrierInventory inventory, IRouteQuery route, ContractInstance offer)
    {
        float before = GameEvents.CurrentLoad.Mass;
        Vector3 station = route.Station(2);
        await Place(player, station + new Vector3(0.0f, 0.0f, 2.0f), 1.5f);
        DeliveryPost? post = director.Post;
        float height = 99.0f;
        if (post != null)
        {
            height = Mathf.Abs(post.GlobalPosition.Y - route.GroundHeight(post.GlobalPosition.X, post.GlobalPosition.Z));
        }
        bool first = post != null && post.Interact(player);
        bool armedOnly = post != null && post.Armed && offer.Status == ContractStatus.Accepted && GameEvents.CarriedCargo.Count == 1;
        bool second = post != null && post.Interact(player);
        await WaitSeconds(0.1f);
        float after = GameEvents.CurrentLoad.Mass;
        RunTelemetry telemetry = _run!.Telemetry;
        bool delivered = first && armedOnly && second && offer.Status == ContractStatus.Delivered && offer.ScoreAwarded == offer.Definition.Score
            && GameEvents.CarriedCargo.Count == 0 && inventory.AllItems.Count == 0 && Mathf.Abs(before - after - offer.CargoMassKg) < 0.01f
            && telemetry.ContractsDelivered == 1 && telemetry.CargoScore == offer.Definition.Score && _resolved == 1
            && director.Active == null && director.Post == null && GameEvents.ActiveContract == null && height < 0.6f;
        Check("at station 2 the post asks once more, then takes that parcel and credits the job", delivered,
            "first E " + first + " (armed, still carried: " + armedOnly + "), second E " + second + ", " + offer.Status + " +" + offer.ScoreAwarded
            + ", load " + before.ToString("0.00") + " → " + after.ToString("0.00") + " kg, run: " + telemetry.ContractsDelivered + " delivered, score " + telemetry.CargoScore
            + ", post " + height.ToString("0.00") + " m off the ground");

        ContractResult again = director.Deliver(2);
        GameEvents.PublishContractResolved(offer.Note());
        bool once = again == ContractResult.Invalid && telemetry.ContractsDelivered == 1 && telemetry.CargoScore == offer.Definition.Score;
        bool nextOffer = director.Offers.TryGetValue(2, out ContractInstance? next) && next.ToStation == 3;
        Check("a delivery is credited once, and station 2 offers the next job", once && nextOffer,
            "delivering again: " + again + ", repeated ending ignored: " + once + ", next job to station 3: " + nextOffer);
    }

    private void TestLostCargo(ContractDirector director, CarrierInventory inventory)
    {
        if (!director.Offers.TryGetValue(2, out ContractInstance? job) || director.Accept(job) != ContractResult.Ok || inventory.Grid == null)
        {
            Check("cargo lost from a torn pack fails its job, with no credit", false, "couldn't take station 2's job");
            return;
        }
        PackedItem? packed = null;
        foreach (PackedItem item in inventory.Grid.Items)
        {
            if (item.Definition.CargoInstanceId == job.InstanceId)
            {
                packed = item;
            }
        }
        if (packed != null)
        {
            // What a torn pack does at a fall (CarrierEquipment): the item is gone, and ItemLost says so.
            inventory.Consume(packed);
            GameEvents.PublishItemLost(packed.Definition.ItemId);
        }
        RunTelemetry telemetry = _run!.Telemetry;
        Check("cargo lost from a torn pack fails its job, with no credit",
            packed != null && job.Status == ContractStatus.Failed && job.ScoreAwarded == 0 && director.Active == null && director.Post == null
            && telemetry.ContractsFailed == 1 && telemetry.ContractsDelivered == 1 && telemetry.CargoScore == 30,
            job.Status + " (" + job.Reason + "), run: " + telemetry.ContractsDelivered + " delivered, " + telemetry.ContractsFailed + " failed, score " + telemetry.CargoScore);
    }

    private async Task TestDeathAndNewRun(ContractDirector director, CarrierInventory inventory)
    {
        ContractInstance? job = director.Offer(3);
        ContractResult taken = ContractResult.Invalid;
        if (job != null)
        {
            taken = director.Accept(job);
        }
        _summary = null;
        GameEvents.PublishCarrierDied("a test");
        await WaitSeconds(0.1f);
        bool failedOnDeath = job != null && job.Status == ContractStatus.Failed;
        RunSummary summary = _summary ?? default;
        bool counted = _summary != null && summary.ContractsDelivered == 1 && summary.CargoScore == 30 && summary.Score >= 30 + summary.Station * _run!.ScorePerStation;
        await WaitSeconds(1.6f);
        bool clean = director.Offers.Count == 0 && director.Boards.Count == 0 && director.Active == null && director.Post == null
            && GameEvents.ActiveContract == null && GameEvents.CarriedCargo.Count == 0 && inventory.AllItems.Count == 0
            && _run!.Telemetry.ContractsDelivered == 0 && _run.Telemetry.ContractsAccepted == 0 && _run.Telemetry.CargoScore == 0 && !_run.RunOver;
        Check("dying with a job fails it; the summary counts the delivery; a new run starts with no jobs and no cargo",
            taken == ContractResult.Ok && failedOnDeath && counted && clean,
            "took station 3's job: " + taken + ", failed on death " + failedOnDeath + ", summary: " + summary.ContractsDelivered + " delivered, cargo "
            + summary.CargoScore + ", score " + summary.Score + "; new run clean " + clean + " (" + director.Offers.Count + " offers, " + inventory.AllItems.Count + " items)");
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
