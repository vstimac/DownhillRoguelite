using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Cargo contracts (roguelite Phase 1, Courier only). The first time the carrier reaches a station
/// in a run, a board by its fire offers one job, picked by the mountain's seed and the station:
/// carry a real item down to a later station. Taking it (E) asks the inventory to pack the cargo,
/// a copy of the item of its own; if it doesn't fit, nothing changes and the offer stays. At the
/// destination a delivery post takes it back (E, then E again to confirm), and only once the
/// inventory has really removed it is the contract delivered and its score credited.
///
/// One contract at a time. Cargo lost from a torn pack fails the job; dying with it fails it too.
/// Phase 2: hard landings and falls knock the carried cargo (less when it's packed deep inside the
/// pack), so it may arrive damaged (paid in proportion), too damaged for its job (failed) or break
/// on the way (failed at once). A job may have a deadline on the world clock, and any board takes
/// back the open job if the carrier gives it up there (E, then E again).
/// A new run clears every offer, board and post. The only owner of contract state: everything else
/// reads ContractOffered / ContractAccepted / ContractResolved and changes nothing.
/// </summary>
public partial class ContractDirector : Node3D
{
    [Export] public ContractDefinition[] Contracts { get; set; } = System.Array.Empty<ContractDefinition>();
    /// <summary>Chance a station's board has a job at all (INITIAL TUNING: every station).</summary>
    [Export(PropertyHint.Range, "0,1")] public float OfferChance { get; set; } = 1.0f;
    /// <summary>Where the job board stands, from the station's fire (x across, z uphill), in m.</summary>
    [Export] public Vector3 BoardOffset { get; set; } = new(-5.0f, 0.0f, -1.0f);
    /// <summary>Where the delivery post stands at the destination, from its fire, in m.</summary>
    [Export] public Vector3 DeliveryOffset { get; set; } = new(-5.0f, 0.0f, 2.2f);
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Cargo condition (INITIAL TUNING)")]
    /// <summary>Landings faster than this knock the cargo, in m/s (the same as clothing wear).</summary>
    [Export] public float HardLanding { get; set; } = 6.0f;
    /// <summary>Condition lost per m/s above HardLanding, before fragility and packing.</summary>
    [Export] public float LandingDamagePerMs { get; set; } = 5.0f;
    /// <summary>Condition lost when the carrier goes down as a ragdoll, before fragility and packing.</summary>
    [Export] public float FallDamage { get; set; } = 12.0f;
    /// <summary>Share of a knock that reaches cargo packed deep inside the pack.</summary>
    [Export] public float ProtectedFactor { get; set; } = 0.4f;
    /// <summary>Share of a knock that reaches cargo against the pack's outside (or in a pocket).</summary>
    [Export] public float ExposedFactor { get; set; } = 1.4f;
    /// <summary>At or above this condition a delivery pays in full; below it, in proportion.</summary>
    [Export] public float IntactCondition { get; set; } = 90.0f;
    /// <summary>How often the deadline is checked, in s.</summary>
    [Export] public float DeadlineCheckInterval { get; set; } = 1.0f;

    /// <summary>Salt for the contract stream, so contracts never shift terrain, boons or supplies.</summary>
    private const ulong ContractSalt = 0xC0A7AC75ul;

    public IReadOnlyDictionary<int, ContractInstance> Offers => _offers;
    public IReadOnlyDictionary<int, ContractBoard> Boards => _boards;
    public ContractInstance? Active => _active;
    public DeliveryPost? Post => _post;
    public IReadOnlyList<ContractInstance> Finished => _finished;
    /// <summary>The definitions that passed validation (the rest are skipped with a warning).</summary>
    public IReadOnlyList<ContractDefinition> ValidContracts => _valid;

    private readonly Dictionary<int, ContractInstance> _offers = new();
    private readonly Dictionary<int, ContractBoard> _boards = new();
    private readonly List<ContractInstance> _finished = new();
    private readonly List<ContractDefinition> _valid = new();
    private ContractInstance? _active;
    private DeliveryPost? _post;
    private RunState? _run;
    private WorldClock? _clock;
    private float _deadlineTimer;

    // Answers from the inventory to a request made a moment ago (C# events are synchronous).
    private string _awaitedCargo = "";
    private bool _answer;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Reload();
        GameEvents.StationReached += OnStationReached;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.RunEnded += OnRunEnded;
        GameEvents.ItemLost += OnItemLost;
        GameEvents.CargoGranted += OnCargoAnswer;
        GameEvents.CargoHandedOver += OnCargoAnswer;
        GameEvents.Landed += OnLanded;
        GameEvents.DownedChanged += OnDowned;
    }

    public override void _ExitTree()
    {
        GameEvents.StationReached -= OnStationReached;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.RunEnded -= OnRunEnded;
        GameEvents.ItemLost -= OnItemLost;
        GameEvents.CargoGranted -= OnCargoAnswer;
        GameEvents.CargoHandedOver -= OnCargoAnswer;
        GameEvents.Landed -= OnLanded;
        GameEvents.DownedChanged -= OnDowned;
        GameEvents.ClearActiveContract();
    }

    /// <summary>Reads Contracts again, keeping only definitions that make sense; broken ones are skipped with a warning.</summary>
    public void Reload()
    {
        _valid.Clear();
        HashSet<string> ids = new();
        foreach (ContractDefinition? definition in Contracts)
        {
            if (definition == null)
            {
                continue;
            }
            string problem = definition.Problem();
            if (problem.Length == 0 && !ids.Add(definition.Id))
            {
                problem = definition.Id + " is defined twice";
            }
            if (problem.Length > 0)
            {
                GD.PushWarning("ContractDirector: skipping a contract: " + problem);
                continue;
            }
            _valid.Add(definition);
        }
    }

    /// <summary>
    /// The job a station's board offers in this run, or null: the same for the same mountain and
    /// station, whatever else happened (its own random stream).
    /// </summary>
    public ContractDefinition? Pick(int station)
    {
        ulong seed = 0;
        if (_run != null)
        {
            seed = _run.Seed;
        }
        SplitMix64 random = new(SplitMix64.Mix(seed ^ ContractSalt, (ulong)station));
        if (random.NextFloat() >= OfferChance)
        {
            return null;
        }
        int circle = CircleOf(station);
        float total = 0.0f;
        foreach (ContractDefinition definition in _valid)
        {
            if (definition.EligibleAt(station, circle))
            {
                total += definition.Weight;
            }
        }
        if (total <= 0.0f)
        {
            return null;
        }
        float roll = random.NextFloat() * total;
        ContractDefinition? last = null;
        foreach (ContractDefinition definition in _valid)
        {
            if (!definition.EligibleAt(station, circle))
            {
                continue;
            }
            last = definition;
            roll -= definition.Weight;
            if (roll < 0.0f)
            {
                return definition;
            }
        }
        return last;
    }

    /// <summary>The circle a station lies in (0 = Limbo), from its depth below the start.</summary>
    public int CircleOf(int station)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null || _run == null || station < 1)
        {
            return 0;
        }
        float depth = route.RoutePoint(route.StartZ).Y - route.Station(station).Y;
        return _run.Circles.CircleAt(depth);
    }

    /// <summary>The world clock's hour count now (0 without a clock).</summary>
    public double Now
    {
        get
        {
            if (_clock == null)
            {
                return 0.0;
            }
            return _clock.TotalHours;
        }
    }

    /// <summary>Pins a station's job on its board (once per station per run); the offer, or null if none.</summary>
    public ContractInstance? Offer(int station)
    {
        if (_offers.TryGetValue(station, out ContractInstance? existing))
        {
            return existing;
        }
        IRouteQuery? route = _run?.Route;
        ContractDefinition? definition = Pick(station);
        if (route == null || definition == null)
        {
            return null;
        }
        if (definition.CargoItem is not ItemDefinition cargo)
        {
            return null;
        }
        ContractInstance contract = new(definition, station, cargo.DisplayName, cargo.MassKg);
        _offers[station] = contract;
        ContractBoard board = new();
        board.Setup(contract, cargo, this);
        AddChild(board);
        board.GlobalPosition = OnGround(route, route.Station(station) + BoardOffset);
        _boards[station] = board;
        GameEvents.PublishContractOffered(contract.Note());
        return contract;
    }

    /// <summary>Registers an authored recovery job. Its world site, rather than a station board, supplies the cargo.</summary>
    public ContractInstance? OfferRecovery(ContractDefinition definition, int fromStation)
    {
        if (fromStation < 0 || definition.Problem().Length > 0 || definition.CargoItem is not ItemDefinition cargo) { return null; }
        if (_offers.TryGetValue(fromStation, out ContractInstance? existing))
        {
            if (existing.Definition.Id == definition.Id) { return existing; }
            return null;
        }
        ContractInstance contract = new(definition, fromStation, cargo.DisplayName, cargo.MassKg);
        _offers.Add(fromStation, contract);
        // The recovery site announces the location. Publishing a station-board offer here would
        // incorrectly tell the existing guide to take a job at Station 0.
        return contract;
    }

    /// <summary>
    /// Takes a job: its cargo is packed (or nothing happens and the offer stays). Taking it twice,
    /// or a second job while one is open, does nothing.
    /// </summary>
    public ContractResult Accept(ContractInstance contract)
    {
        ContractResult result = ContractResolver.CanAccept(contract, _active);
        if (result != ContractResult.Ok)
        {
            return result;
        }
        if (!Ask(contract.InstanceId, true, contract.Definition.CargoItemPath))
        {
            return ContractResult.NoRoom;
        }
        ContractResolver.Accept(contract, Now);
        _active = contract;
        ContractNote note = contract.Note();
        GameEvents.PublishContractAccepted(note);
        string care = "";
        if (contract.Definition.MinCondition > 0.0f)
        {
            care = " It's fragile: pack it deep inside the pack (I), away from the sides.";
        }
        GameEvents.PublishHint("Took the job: carry the " + note.CargoName.ToLowerInvariant() + " to Station " + note.ToStation + "." + care);
        PlaceDeliveryPost(contract);
        if (_boards.TryGetValue(contract.FromStation, out ContractBoard? board))
        {
            board.Refresh();
        }
        return ContractResult.Ok;
    }

    /// <summary>Whether the open job could be handed over at this station right now.</summary>
    public ContractResult CanDeliverAt(int station)
    {
        if (_active != null && ContractResolver.IsLate(_active, Now))
        {
            return ContractResult.Expired;
        }
        bool carried = _active != null && Carried(_active.InstanceId);
        return ContractResolver.CanDeliver(_active, station, carried);
    }

    /// <summary>
    /// Hands the open job's cargo over at <paramref name="station"/>: the inventory removes that very
    /// item, then the contract is delivered and credited, once.
    /// </summary>
    public ContractResult Deliver(int station)
    {
        if (CheckDeadline())
        {
            return ContractResult.Expired;
        }
        ContractResult result = CanDeliverAt(station);
        if (result != ContractResult.Ok || _active == null)
        {
            return result;
        }
        ContractInstance contract = _active;
        if (!Ask(contract.InstanceId, false, ""))
        {
            return ContractResult.CargoMissing;
        }
        ContractResolver.Deliver(contract, IntactCondition);
        Finish(contract);
        if (contract.Status == ContractStatus.Delivered)
        {
            string state = "";
            if (contract.Condition < IntactCondition)
            {
                state = " in " + ContractResolver.ConditionText(contract.Condition) + " condition";
            }
            GameEvents.PublishHint("Delivered the " + contract.CargoName.ToLowerInvariant() + state + ". The job is done (+" + contract.ScoreAwarded + ").");
        }
        else
        {
            GameEvents.PublishHint("Handed over the " + contract.CargoName.ToLowerInvariant() + ", but it " + contract.Reason + ". No pay.");
        }
        return ContractResult.Ok;
    }

    /// <summary>Sends a request to the inventory and returns its answer (false if nothing answered).</summary>
    private bool Ask(string cargoId, bool grant, string itemPath)
    {
        _awaitedCargo = cargoId;
        _answer = false;
        if (grant)
        {
            GameEvents.RequestCargoGrant(itemPath, cargoId);
        }
        else
        {
            GameEvents.RequestCargoHandOver(cargoId);
        }
        _awaitedCargo = "";
        return _answer;
    }

    private void OnCargoAnswer(string cargoId, bool success)
    {
        if (_awaitedCargo.Length > 0 && cargoId == _awaitedCargo)
        {
            _answer = success;
        }
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

    /// <summary>
    /// Gives up the open job at a board: its cargo, if carried, is left there (removed from the pack)
    /// and nothing is credited. Invalid when there is no open job.
    /// </summary>
    public ContractResult Abandon()
    {
        if (_active == null || _active.Status != ContractStatus.Accepted)
        {
            return ContractResult.Invalid;
        }
        ContractInstance contract = _active;
        bool left = false;
        if (Carried(contract.InstanceId))
        {
            left = Ask(contract.InstanceId, false, "");
        }
        if (!ContractResolver.End(contract, ContractStatus.Abandoned, "given up"))
        {
            return ContractResult.AlreadyResolved;
        }
        Finish(contract);
        string cargo = "";
        if (left)
        {
            cargo = " The " + contract.CargoName.ToLowerInvariant() + " stays here.";
        }
        GameEvents.PublishHint("Gave up the job \"" + contract.Definition.DisplayName + "\"." + cargo);
        return ContractResult.Ok;
    }

    /// <summary>
    /// A knock to the carried cargo of <paramref name="amount"/> before fragility and packing: deep in
    /// the pack it takes ProtectedFactor of it, against the outside ExposedFactor. Cargo that isn't
    /// carried (dropped) is not touched. Breaking it fails the job. Returns the condition lost.
    /// </summary>
    public float Knock(float amount)
    {
        if (_active == null || _active.Status != ContractStatus.Accepted || !Carried(_active.InstanceId))
        {
            return 0.0f;
        }
        float exposure = 1.0f;
        if (GameEvents.CargoExposure.TryGetValue(_active.InstanceId, out float packed))
        {
            exposure = Mathf.Clamp(packed, 0.0f, 1.0f);
        }
        float lost = ContractResolver.Damage(_active, amount * Mathf.Lerp(ProtectedFactor, ExposedFactor, exposure));
        if (lost <= 0.0f)
        {
            return 0.0f;
        }
        ContractInstance contract = _active;
        if (contract.Condition <= 0.0f)
        {
            Fail("broke on the way");
            GameEvents.PublishHint("The " + contract.CargoName.ToLowerInvariant() + " broke. The job has failed.");
            return lost;
        }
        GameEvents.PublishContractUpdated(contract.Note());
        if (lost >= 1.0f)
        {
            string warning = "";
            if (contract.Condition < contract.Definition.MinCondition)
            {
                warning = " Too damaged for the job now.";
            }
            GameEvents.PublishHint("The " + contract.CargoName.ToLowerInvariant() + " took a knock: " + ContractResolver.ConditionText(contract.Condition) + "." + warning);
        }
        return lost;
    }

    private void OnLanded(float speed)
    {
        if (speed > HardLanding)
        {
            Knock((speed - HardLanding) * LandingDamagePerMs);
        }
    }

    private void OnDowned(bool downed)
    {
        if (downed)
        {
            Knock(FallDamage);
        }
    }

    public override void _Process(double delta)
    {
        _deadlineTimer -= (float)delta;
        if (_deadlineTimer > 0.0f)
        {
            return;
        }
        _deadlineTimer = DeadlineCheckInterval;
        CheckDeadline();
    }

    /// <summary>Fails the open job if its deadline has passed; true if it did.</summary>
    public bool CheckDeadline()
    {
        if (_active == null || !ContractResolver.IsLate(_active, Now))
        {
            return false;
        }
        ContractInstance contract = _active;
        Fail("ran out of time");
        GameEvents.PublishHint("Too late: the job \"" + contract.Definition.DisplayName + "\" has failed.");
        return true;
    }

    /// <summary>Ends the open job badly (lost cargo, death); nothing is credited.</summary>
    public bool Fail(string reason)
    {
        if (_active == null || !ContractResolver.End(_active, ContractStatus.Failed, reason))
        {
            return false;
        }
        Finish(_active);
        return true;
    }

    private void Finish(ContractInstance contract)
    {
        if (_active == contract)
        {
            _active = null;
        }
        _finished.Add(contract);
        if (_post != null)
        {
            _post.QueueFree();
            _post = null;
        }
        GameEvents.PublishContractResolved(contract.Note());
    }

    private void PlaceDeliveryPost(ContractInstance contract)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return;
        }
        _post?.QueueFree();
        _post = new DeliveryPost();
        _post.Setup(contract, this);
        AddChild(_post);
        _post.GlobalPosition = OnGround(route, route.Station(contract.ToStation) + DeliveryOffset);
    }

    /// <summary>On the ground at <paramref name="at"/>: the physics ground where it's loaded, the terrain's height elsewhere.</summary>
    private Vector3 OnGround(IRouteQuery route, Vector3 at)
    {
        Vector3 ground = new(at.X, route.GroundHeight(at.X, at.Z), at.Z);
        if (!IsInsideTree())
        {
            return ground;
        }
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(ground + Vector3.Up * 6.0f, ground + Vector3.Down * 6.0f, GroundMask));
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return ground;
    }

    private void OnStationReached(int station)
    {
        Offer(station);
        // The destination's ground has loaded now: settle the post onto it.
        IRouteQuery? route = _run?.Route;
        if (_post != null && _active != null && _active.ToStation == station && route != null)
        {
            _post.GlobalPosition = OnGround(route, route.Station(station) + DeliveryOffset);
        }
    }

    /// <summary>Cargo that fell out of a torn pack is gone: the job fails.</summary>
    private void OnItemLost(string itemId)
    {
        if (_active == null || _active.Status != ContractStatus.Accepted || Carried(_active.InstanceId))
        {
            return;
        }
        if (ItemText.AtPath(_active.Definition.CargoItemPath) is ItemDefinition cargo && cargo.ItemId == itemId)
        {
            Fail("lost from a torn pack");
        }
    }

    private void OnRunEnded(RunSummary summary)
    {
        if (_active != null && _active.Status == ContractStatus.Accepted)
        {
            Fail("never arrived: the carrier died on the way");
        }
    }

    /// <summary>A new run: no offers, no boards, no job, no cargo claims from the old mountain.</summary>
    private void OnRespawnRequested()
    {
        foreach (ContractBoard board in _boards.Values)
        {
            board.QueueFree();
        }
        _boards.Clear();
        _offers.Clear();
        _finished.Clear();
        _active = null;
        if (_post != null)
        {
            _post.QueueFree();
            _post = null;
        }
        _awaitedCargo = "";
        GameEvents.ClearActiveContract();
    }
}
