using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>First expedition slice: exploration → real fragile cargo → delivery → useful station service.</summary>
public partial class RecoveryExpedition : Node3D
{
    [Export] public ContractDirector? Director { get; set; }
    [Export] public ContractDefinition? Job { get; set; }
    [Export] public Vector2 StageSpan { get; set; } = new(0.12f, 0.34f);
    [Export] public Vector2 TrailOffsets { get; set; } = new(10.0f, 18.0f);
    [Export] public float MaxApproachSlope { get; set; } = 30.0f;
    [Export] public float MinPathGap { get; set; } = 7.0f;
    [Export] public Vector3 ReserveOffset { get; set; } = new(-7, 0, 2);
    [Export] public float RefreshSeconds { get; set; } = 0.25f;
    public RecoveryWorkshop? Workshop { get; private set; }
    public StationWaterReserve? Reserve { get; private set; }
    public ContractInstance? Contract { get; private set; }
    private RunState? _run;
    private ulong _seed;
    private float _timer;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.RespawnRequested += Reset;
        GameEvents.ContractResolved += OnResolved;
    }
    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= Reset;
        GameEvents.ContractResolved -= OnResolved;
        GameEvents.PublishExpedition(null);
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_run?.Route == null || Director == null || Job == null) { return; }
        if (Contract != null && _seed != _run.Seed) { Reset(); }
        if (Contract == null && !Build(_run.Route)) { return; }
        _timer -= (float)delta;
        if (_timer > 0) { return; }
        _timer = RefreshSeconds;
        Refresh();
    }
    private bool Build(IRouteQuery route)
    {
        if (_run == null || Job == null || Director == null
            || !RecoveryPlacement.TryFind(route, _run.Seed, StageSpan, TrailOffsets, MaxApproachSlope, MinPathGap, out Vector3 at)) { return false; }
        Contract = Director.OfferRecovery(Job, 0);
        if (Contract == null) { return false; }
        _seed = _run.Seed;
        Workshop = new RecoveryWorkshop { Contract = Contract, Director = Director };
        AddChild(Workshop);
        Workshop.GlobalPosition = at;
        Reserve = new StationWaterReserve();
        AddChild(Reserve);
        Vector3 reserveAt = route.Station(1) + ReserveOffset;
        Reserve.GlobalPosition = new Vector3(reserveAt.X, route.GroundHeight(reserveAt.X, reserveAt.Z), reserveAt.Z);
        Refresh();
        return true;
    }
    private void OnResolved(ContractNote note)
    {
        if (note.InstanceId != Contract?.InstanceId) { return; }
        if (note.Status == ContractStatus.Delivered && Reserve?.Restore() == true)
        {
            GameEvents.PublishHint("Pump restored: Station 1 now holds three water flasks. A boon awaits by the fire.");
        }
        Refresh();
    }
    private void Refresh()
    {
        if (Contract == null || Workshop == null || Reserve == null) { return; }
        string title = "Recover the pump";
        string text = "Find the orange-flagged workshop off the main trail. Deliver the pump to Station 1: water reserve + one boon.";
        string progress = "6.5 kg · 3 × 4 × 3 cells · 150 score";
        Vector3 target = Workshop.GlobalPosition;
        if (Contract.Status == ContractStatus.Accepted)
        {
            title = "Deliver the pump to Station 1";
            text = "Use the delivery table twice to confirm. Protect the cargo from hard landings; it must arrive at 60% or better.";
            progress = "Condition " + ContractResolver.ConditionText(Contract.Condition) + " · pack low, close to your back";
            if (Director?.Post != null) { target = Director.Post.GlobalPosition; }
        }
        else if (Contract.Status == ContractStatus.Delivered)
        {
            title = "Water restored";
            text = "Collect water from the blue reserve beside Station 1, choose your boon, and prepare for the next leg.";
            progress = Reserve.Remaining + " flasks left · delivery +" + Contract.ScoreAwarded;
            target = Reserve.GlobalPosition;
        }
        else if (!Contract.IsOpen)
        {
            title = "Pump recovery ended";
            text = "The water reserve remains broken. Shelter and rest still work; other station jobs can earn a boon.";
            progress = Contract.Reason;
            target = Reserve.GlobalPosition;
        }
        GameEvents.PublishExpedition(new ExpeditionNote(title, text, target, progress));
    }
    private void Reset()
    {
        Workshop?.QueueFree();
        Reserve?.QueueFree();
        Workshop = null;
        Reserve = null;
        Contract = null;
        _timer = 0;
        GameEvents.PublishExpedition(null);
    }
}
