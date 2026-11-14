using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Progression;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>Shared station presence and NPC presentation. One participant at a time talks to a figure.</summary>
public partial class NetWorldActors : Node
{
    [Export] public float SendRate { get; set; } = 10;
    [Export] public float TalkDistance { get; set; } = 12;
    [Export] public float LeaseSeconds { get; set; } = 5;
    public NetSession? Session { get; set; }
    public int Received { get; private set; }
    private readonly Dictionary<string, (long Peer, double Until)> _leases = new();
    private readonly HashSet<string> _local = new();
    private readonly Dictionary<string, Action> _pending = new();
    private readonly HashSet<int> _stations = new();
    private Node? _world;
    private StationVisitors? _visitors;
    private PresidentDirector? _president;
    private ContractDirector? _contracts;
    private double _clock;
    private double _timer;
    private double _worldTimer;
    private bool _bridge;
    private int _victories;
    private readonly Dictionary<int, (long Owner, ContractStatus Status, float Condition, double Deadline)> _jobs = new();
    private readonly Dictionary<int, DeliveryPost> _posts = new();

    public override void _Ready()
    {
        GameEvents.SharedNpcUseGate = Gate; GameEvents.StationReached += OnStation;
        GameEvents.ContractAccepted += OnContract; GameEvents.ContractResolved += OnContract;
    }
    public override void _ExitTree()
    {
        GameEvents.SharedNpcUseGate = null; GameEvents.StationReached -= OnStation;
        GameEvents.ContractAccepted -= OnContract; GameEvents.ContractResolved -= OnContract;
    }
    public void Clear()
    {
        if (IsInsideTree())
        { foreach (Node node in GetTree().GetNodesInGroup(ISharedNpc.Group)) { if (node is ISharedNpc npc) { npc.SharedNpcProxy = false; } } }
        _leases.Clear(); _pending.Clear(); _local.Clear(); _stations.Clear(); _bridge = false; _victories = 0;
        _world = null; _visitors = null; _president = null; _contracts = null;
        foreach (DeliveryPost post in _posts.Values) { if (IsInstanceValid(post)) { post.QueueFree(); } }
        _posts.Clear(); _jobs.Clear();
    }
    private bool Live => Session != null && Session.Active && Session.Joined;
    private bool Host => Session?.Mode == NetSession.Role.Host;
    private ISharedNpc? Find(string key)
    {
        foreach (Node node in GetTree().GetNodesInGroup(ISharedNpc.Group))
        { if (node is ISharedNpc npc && npc.SharedNpcKey == key && !node.IsQueuedForDeletion()) { return npc; } }
        return null;
    }
    private bool Gate(string key, Action retry)
    {
        if (!Live) { return true; }
        if (Host)
        {
            if (!Acquire(key, 1)) { GameEvents.PublishHint("A teammate is talking here."); return false; }
            _local.Add(key); if (Find(key) is ISharedNpc npc) { npc.SharedNpcProxy = false; } return true;
        }
        if (_local.Contains(key)) { return true; }
        if (!_pending.ContainsKey(key)) { _pending[key] = retry; RpcId(1, MethodName.RequestUse, key); }
        return false;
    }
    private bool Acquire(string key, long peer)
    {
        if (key.Length > 64 || Find(key) is not Node3D npc) { return false; }
        Vector3 feet = Session!.LocalPlayer()?.GlobalPosition ?? Vector3.Zero;
        if (peer != 1)
        {
            if (!Session.Peers.TryGetValue(peer, out NetPeer? player)) { return false; }
            feet = player.Position;
        }
        if (feet.DistanceTo(npc.GlobalPosition) > TalkDistance) { return false; }
        if (_leases.TryGetValue(key, out var lease) && lease.Peer != peer && lease.Until > _clock) { return false; }
        _leases[key] = (peer, _clock + LeaseSeconds);
        return true;
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestUse(string key)
    {
        long peer = Multiplayer.GetRemoteSenderId();
        if (!Live || !Host || !Session!.Peers.ContainsKey(peer)) { return; }
        bool granted = Acquire(key, peer);
        RpcId(peer, MethodName.UseAnswer, key, granted);
        if (granted && Find(key) is ISharedNpc npc) { npc.SharedNpcProxy = true; _local.Remove(key); }
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UseAnswer(string key, bool granted)
    {
        if (!Live || Host || !_pending.Remove(key, out Action? retry)) { return; }
        if (!granted) { GameEvents.PublishHint("A teammate is talking here."); return; }
        _local.Add(key);
        if (Find(key) is ISharedNpc npc) { npc.SharedNpcProxy = false; retry(); }
    }
    private void BindWorld()
    {
        Node? world = GetTree().CurrentScene;
        if (_world == world) { return; }
        Clear(); _world = world;
        if (world == null) { return; }
        foreach (Node node in world.FindChildren("*", "", true, false))
        {
            if (node is StationVisitors visitors) { _visitors = visitors; }
            if (node is PresidentDirector president) { _president = president; }
            if (node is ContractDirector contracts) { _contracts = contracts; }
        }
    }
    private void OnStation(int station)
    {
        if (!Live || !Host) { return; }
        if (station > 0 && station < 10000) { _stations.Add(station); }
    }
    private void Seat(int station, bool reached)
    {
        _visitors?.SeatAhead(station);
        if (_president != null) { _president.Seat(_president.RestsiteFrom(station)); }
        if (reached) { _contracts?.Offer(station); }
    }
    public override void _Process(double delta)
    {
        if (!Live) { return; }
        BindWorld(); _clock += delta; _timer += delta; _worldTimer += delta;
        if (_worldTimer >= 1)
        {
            _worldTimer = 0;
            if (_contracts?.Active is ContractInstance active) { OnContract(active.Note()); }
            if (Host)
            {
                IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
                if (route != null)
                {
                    Vector3 local = Session!.LocalPlayer()?.GlobalPosition ?? GameEvents.CarrierPosition;
                    int upcoming = route.NextStationIndex(local.Z);
                    Seat(upcoming, false);
                    foreach (NetCarrier player in GameEvents.OtherCarriers)
                    {
                        int next = route.NextStationIndex(player.Feet.Z);
                        if (next > 0 && next < 10000) { Seat(next, false); }
                        int previous = next - 1;
                        if (previous > 0 && previous < 10000 && player.Feet.DistanceTo(route.Station(previous)) < 50) { _stations.Add(previous); }
                    }
                }
                foreach (int station in _stations) { Seat(station, true); Seat(station + 1, false); }
                SharePresence();
                foreach (int station in new List<int>(_jobs.Keys))
                {
                    var job = _jobs[station];
                    if (job.Owner != 1 && !Session!.Peers.ContainsKey(job.Owner) && job.Status == ContractStatus.Accepted)
                    { job.Status = ContractStatus.Failed; _jobs[station] = job; }
                    foreach (long peer in Session!.Peers.Keys) { RpcId(peer, MethodName.PeerJob, station, job.Owner, (int)job.Status, job.Condition, job.Deadline); }
                    ShowJob(station, job.Owner, job.Status, job.Condition, job.Deadline);
                }
            }
            if (_president != null)
            {
                if (Host) { _bridge |= _president.Save.BridgeUnlocked; _victories = Math.Max(_victories, _president.Save.Victories); }
                else { RpcId(1, MethodName.PublicProgress, _president.Save.BridgeUnlocked, _president.Save.Victories); }
            }
        }
        if (_timer < 1.0 / Mathf.Max(SendRate, 1)) { return; }
        _timer = 0;
        foreach (Node node in GetTree().GetNodesInGroup(ISharedNpc.Group))
        {
            if (node is not ISharedNpc npc || node.IsQueuedForDeletion()) { continue; }
            string key = npc.SharedNpcKey;
            long owner = 1;
            if (Host)
            {
                if (_leases.TryGetValue(key, out var lease))
                {
                    if (lease.Peer != 1 && (!Session!.Peers.ContainsKey(lease.Peer) || lease.Until < _clock)) { _leases.Remove(key); }
                    else { owner = lease.Peer; }
                }
                npc.SharedNpcProxy = owner != 1;
                if (owner != 1) { continue; }
                SharedNpcFrame frame = npc.CaptureSharedNpc();
                if (frame.Busy && _local.Contains(key)) { _leases[key] = (1, _clock + LeaseSeconds); }
                Broadcast(key, 1, frame);
            }
            else if (_local.Contains(key)) { SendState(1, MethodName.NpcState, key, Session!.LocalId, npc.CaptureSharedNpc()); }
            else { npc.SharedNpcProxy = true; }
        }
    }
    private void SharePresence()
    {
        HashSet<int> visitors = new(); HashSet<int> presidents = new();
        if (_visitors != null) { foreach (NpcFigure figure in _visitors.Figures) { visitors.Add(figure.Station); } }
        if (_president != null) { foreach (int station in _president.Sets.Keys) { presidents.Add(station); } }
        int[] seats = new int[visitors.Count]; visitors.CopyTo(seats);
        int[] rests = new int[presidents.Count]; presidents.CopyTo(rests);
        int[] stations = new int[_stations.Count]; _stations.CopyTo(stations);
        foreach (long peer in Session!.Peers.Keys) { RpcId(peer, MethodName.Presence, seats, rests, stations, _bridge, _victories); }
        ApplyPublicProgress();
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PublicProgress(bool bridge, int victories)
    {
        if (!Live || !Host || !Session!.Peers.ContainsKey(Multiplayer.GetRemoteSenderId()) || victories < 0 || victories > 1000000) { return; }
        _bridge |= bridge; _victories = Math.Max(_victories, victories);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Presence(int[] seats, int[] rests, int[] stations, bool bridge, int victories)
    {
        if (!Live || Host || seats.Length + rests.Length + stations.Length > 4096) { return; }
        BindWorld();
        foreach (int station in seats) { if (station > 0 && station < 10000) { _visitors?.SeatAhead(station); } }
        foreach (int station in rests) { if (station > 0 && station < 10000) { _president?.Seat(station); } }
        foreach (int station in stations) { if (station > 0 && station < 10000) { _contracts?.Offer(station); } }
        _bridge = bridge; _victories = victories; ApplyPublicProgress();
    }
    private void ApplyPublicProgress()
    {
        if (_president == null) { return; }
        foreach (RestsiteSet set in _president.Sets.Values) { set.SetBridgeBuilt(_bridge); set.SetVictories(_victories); }
    }
    private void OnContract(ContractNote note)
    {
        if (!Live) { return; }
        if (Host) { _jobs[note.FromStation] = (1, note.Status, note.Condition, note.DeadlineHours); }
        else { RpcId(1, MethodName.Job, note.FromStation, (int)note.Status, note.Condition, note.DeadlineHours); }
    }
    private static bool ValidJob(int station, int status, float condition, double deadline)
    {
        return station > 0 && station < 10000 && Enum.IsDefined((ContractStatus)status)
            && status != (int)ContractStatus.Offered && float.IsFinite(condition) && condition >= 0 && condition <= 100 && double.IsFinite(deadline);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Job(int station, int status, float condition, double deadline)
    {
        long peer = Multiplayer.GetRemoteSenderId();
        if (!Live || !Host || !Session!.Peers.ContainsKey(peer) || !ValidJob(station, status, condition, deadline)
            || !Session.HoldsPickup("contract:" + station, peer)) { return; }
        _jobs[station] = (peer, (ContractStatus)status, condition, deadline);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerJob(int station, long owner, int status, float condition, double deadline)
    {
        if (!Live || Host || !ValidJob(station, status, condition, deadline)) { return; }
        BindWorld(); ShowJob(station, owner, (ContractStatus)status, condition, deadline);
    }
    private void ShowJob(int station, long owner, ContractStatus status, float condition, double deadline)
    {
        ContractInstance? offer = _contracts?.Offer(station);
        if (offer == null) { return; }
        if (_contracts!.Boards.TryGetValue(station, out ContractBoard? board)) { board.SharedStatus = status; board.Refresh(); }
        if (owner == Session!.LocalId || status != ContractStatus.Accepted)
        {
            if (_posts.Remove(station, out DeliveryPost? old) && IsInstanceValid(old)) { old.QueueFree(); }
            return;
        }
        if (!_posts.TryGetValue(station, out DeliveryPost? post) || !IsInstanceValid(post))
        {
            IRouteQuery? route = GetNode<RunState>("/root/RunState").Route;
            if (route == null || _world == null) { return; }
            ContractInstance display = new(offer.Definition, station, offer.CargoName, offer.CargoMassKg)
            { Status = ContractStatus.Accepted, Condition = condition, DeadlineHours = deadline };
            post = new DeliveryPost { SharedProxy = true };
            post.Setup(display, _contracts); _world.AddChild(post);
            Vector3 at = route.Station(display.ToStation) + _contracts.DeliveryOffset;
            at.Y = route.GroundHeight(at.X, at.Z); post.GlobalPosition = at;
            _posts[station] = post;
        }
        post.ShowSharedCondition(condition);
    }
    private static bool Valid(SharedNpcFrame f)
    {
        return f.Position.IsFinite() && Mathf.IsFinite(f.Yaw) && Mathf.IsFinite(f.Clock) && f.Clock >= 0
            && f.Clip.Length <= 64 && f.Line.Length <= 128 && f.LineSequence >= 0
            && Mathf.IsFinite(f.ClipTime) && f.ClipTime >= 0 && f.ClipTime < 100000
            && Mathf.IsFinite(f.LineElapsed) && f.LineElapsed >= 0 && f.LineElapsed < 10000;
    }
    private void SendState(long peer, StringName method, string key, long author, SharedNpcFrame f)
    {
        RpcId(peer, method, key, author, f.Position, f.Yaw, f.Busy, f.Speaking, f.Clock, f.Clip, f.ClipTime, f.Line, f.LineSequence, f.LineElapsed);
    }
    private void Broadcast(string key, long author, SharedNpcFrame f)
    { foreach (long peer in Session!.Peers.Keys) { SendState(peer, MethodName.PeerNpcState, key, author, f); } }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void NpcState(string key, long author, Vector3 at, float yaw, bool busy, bool speaking, float clock, string clip, float time, string line, int sequence, float elapsed)
    {
        long peer = Multiplayer.GetRemoteSenderId();
        if (!Live || !Host || author != peer || !_leases.TryGetValue(key, out var lease) || lease.Peer != peer || Find(key) is not ISharedNpc npc) { return; }
        SharedNpcFrame frame = new(at, yaw, busy, speaking, clock, clip, time, line, sequence, elapsed);
        if (!Valid(frame) || ((Node3D)npc).GlobalPosition.DistanceTo(at) > 2) { return; }
        if (busy) { _leases[key] = (peer, _clock + LeaseSeconds); }
        npc.SharedNpcProxy = true; npc.ApplySharedNpc(frame, peer); Broadcast(key, peer, frame);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void PeerNpcState(string key, long author, Vector3 at, float yaw, bool busy, bool speaking, float clock, string clip, float time, string line, int sequence, float elapsed)
    {
        if (!Live || Host || Find(key) is not ISharedNpc npc) { return; }
        SharedNpcFrame frame = new(at, yaw, busy, speaking, clock, clip, time, line, sequence, elapsed);
        if (!Valid(frame)) { return; }
        if (author == Session!.LocalId && _local.Contains(key)) { return; }
        _local.Remove(key); npc.SharedNpcProxy = true; npc.ApplySharedNpc(frame, author); Received++;
    }
}
