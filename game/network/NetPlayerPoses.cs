using DownhillRoguelite.Player;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>One bounded pose stream, relayed by the host. Remote drivers show the owner's actual body result.</summary>
public partial class NetPlayerPoses : Node
{
    [Export] public float SendRate { get; set; } = 15;
    public NetSession? Session { get; set; }
    public int Sent { get; private set; }
    private PlayerController? _body;
    private Skeleton3D? _skeleton;
    private Node3D? _model;
    private MeleeCombat? _melee;
    private PlayerRagdoll? _ragdoll;
    private int[] _indices = System.Array.Empty<int>();
    private readonly float[] _data = new float[NetPoseCodec.Size];
    private double _timer;
    private bool _due;
    private int _sequence;
    private LocomotionSnapshot _locomotion;
    private bool _sprinting;
    public override void _Ready() { GameEvents.LocomotionUpdated += OnLocomotion; GameEvents.VitalsUpdated += OnVitals; }
    private void OnLocomotion(LocomotionSnapshot snapshot) { _locomotion = snapshot; }
    private void OnVitals(VitalsSnapshot snapshot) { _sprinting = snapshot.Sprinting; }

    private bool Live => Session != null && Session.Active && Session.Joined;

    public override void _Process(double delta)
    {
        if (!Live) { Detach(); return; }
        if (_body == null || !IsInstanceValid(_body) || !_body.IsInsideTree())
        {
            Detach();
            _body = Session!.LocalPlayer();
            _model = _body?.GetNodeOrNull<Node3D>("Model");
            _skeleton = _body?.GetNodeOrNull<Skeleton3D>("Model/Rig/Skeleton3D");
            _melee = _body?.GetNodeOrNull<MeleeCombat>("MeleeCombat");
            _ragdoll = _body?.GetNodeOrNull<PlayerRagdoll>("Ragdoll");
            if (_skeleton != null) { _indices = NetPoseCodec.Bind(_skeleton); _skeleton.SkeletonUpdated += Capture; }
        }
        _timer += delta;
        if (_timer >= 1.0 / Mathf.Max(1, SendRate)) { _timer = 0; _due = true; }
    }

    public override void _ExitTree() { Detach(); GameEvents.LocomotionUpdated -= OnLocomotion; GameEvents.VitalsUpdated -= OnVitals; }
    private void Detach()
    {
        if (_skeleton != null && IsInstanceValid(_skeleton)) { _skeleton.SkeletonUpdated -= Capture; }
        _body = null; _skeleton = null; _model = null; _melee = null; _ragdoll = null; _due = false;
    }
    private void Capture()
    {
        if (!_due || !Live || _skeleton == null || _model == null || !NetPoseCodec.Capture(_skeleton, _model, _melee, _indices, _data,
            _locomotion.InputDirection.LimitLength(1), _sprinting)) { return; }
        _due = false;
        _sequence++;
        int flags = 0;
        if (_ragdoll?.Downed == true) { flags |= NetPoseCodec.Falling; }
        if (_melee?.Sword?.IsVisibleInTree() == true) { flags |= NetPoseCodec.SwordVisible; }
        if (_melee?.Shield?.IsVisibleInTree() == true) { flags |= NetPoseCodec.ShieldVisible; }
        if (Session!.Mode == NetSession.Role.Host)
        {
            foreach (long peer in Session.Peers.Keys) { RpcId(peer, MethodName.PeerPose, 1L, _sequence, flags, _data); }
        }
        else { RpcId(1, MethodName.Pose, _sequence, flags, _data); }
        Sent++;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 3)]
    private void Pose(int sequence, int flags, float[] data)
    {
        if (!Live || Session!.Mode != NetSession.Role.Host) { return; }
        long sender = Multiplayer.GetRemoteSenderId();
        if (!Session.Peers.ContainsKey(sender) || !Session.Remotes.TryGetValue(sender, out RemoteCarrier? remote) || remote.Pose == null) { return; }
        int previousFlags = remote.Pose.LastReceivedFlags;
        if (!remote.Pose.Receive(sequence, flags, data)) { return; }
        if ((flags & NetPoseCodec.Falling) != 0 && (previousFlags & NetPoseCodec.Falling) == 0)
        { DownhillRoguelite.Core.GameEvents.PublishSharedCarrierFell(remote.GlobalPosition); }
        foreach (long peer in Session.Peers.Keys)
        { if (peer != sender) { RpcId(peer, MethodName.PeerPose, sender, sequence, flags, data); } }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 3)]
    private void PeerPose(long owner, int sequence, int flags, float[] data)
    {
        if (!Live || Session!.Mode != NetSession.Role.Client || owner == Session.LocalId || !Session.Peers.ContainsKey(owner)) { return; }
        if (Session.Remotes.TryGetValue(owner, out RemoteCarrier? remote)) { remote.Pose?.Receive(sequence, flags, data); }
    }
}
