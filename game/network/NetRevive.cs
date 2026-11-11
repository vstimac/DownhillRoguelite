using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Co-op: down, not dead (a child of NetSession). A blow that would kill a carrier while a teammate
/// still stands knocks them down instead: they bleed out for BleedSeconds, unable to move. A friend
/// beside them holding E for ReviveSeconds helps them up with part of their health back, pack and all;
/// another blow, or the time running out, and they fall for real (the usual co-op fall and respawn).
///
/// Network: the downed machine tells the host, which tells everyone (so their creatures leave the
/// downed alone and their name tag asks for help). A helper's machine asks the host to revive them,
/// and the host passes it on to the downed player's machine, which gets up.
/// </summary>
public partial class NetRevive : Node
{
    [Export] public float BleedSeconds { get; set; } = 30.0f;
    [Export] public float ReviveSeconds { get; set; } = 3.0f;
    /// <summary>A helper must stand this close to the downed, in m.</summary>
    [Export] public float Reach { get; set; } = 2.5f;
    /// <summary>The share of health a helped-up carrier gets back.</summary>
    [Export] public float ReviveHealth { get; set; } = 0.3f;
    [Export] public string DownHint { get; set; } = "You're down and bleeding! A friend can help you up: they hold E beside you within {0} s.";
    [Export] public string HelpHint { get; set; } = "Helping {0} up… keep holding E.";
    [Export] public string HelpedHint { get; set; } = "You helped {0} up.";
    [Export] public string UpHint { get; set; } = "{0} helped you up. Your pack is still on your back.";
    [Export] public string FriendDownHint { get; set; } = "{0} is down! Hold E beside them to help them up.";

    public NetSession? Session { get; set; }
    /// <summary>This carrier is down and bleeding out.</summary>
    public bool Bleeding { get; private set; }
    public float BleedLeft { get; private set; }
    /// <summary>How far the help for a downed friend has come (0..1), and whom.</summary>
    public float Helping { get; private set; }
    public string HelpingName { get; private set; } = "";
    /// <summary>Times this carrier went down, was helped up, and helped someone else up (tests).</summary>
    public int Downs { get; private set; }
    public int Revived { get; private set; }
    public int Helped { get; private set; }

    private readonly HashSet<long> _downPeers = new();
    private bool _finishing;
    private long _helpTarget;
    private float _helpTimer;
    private static readonly StringName ActionInteract = "interact";

    private bool Live => Session != null && Session.Active && Session.Joined;

    /// <summary>Whether that player is down and bleeding (as their machine says).</summary>
    public bool IsDown(long peer) => _downPeers.Contains(peer);

    public override void _Ready()
    {
        GameEvents.LastChanceRequested += OnLastChance;
        GameEvents.CarrierDied += OnDied;
    }

    public override void _ExitTree()
    {
        GameEvents.LastChanceRequested -= OnLastChance;
        GameEvents.CarrierDied -= OnDied;
    }

    /// <summary>The session ended: nobody is down any more (and this carrier can move again).</summary>
    public void Clear()
    {
        if (Bleeding)
        {
            Bleeding = false;
            GameEvents.PublishBleeding(false);
            Lock(false);
        }
        _downPeers.Clear();
        Helping = 0.0f;
        _helpTimer = 0.0f;
    }

    /// <summary>The carrier's health asks before a death: down instead, while a teammate stands.</summary>
    private float OnLastChance(string cause)
    {
        if (!Live || Bleeding || _finishing || Session!.LocalDead || !AnyoneStanding())
        {
            return 0.0f;
        }
        Bleeding = true;
        GameEvents.PublishBleeding(true);
        BleedLeft = BleedSeconds;
        Downs++;
        Lock(true);
        GameEvents.RequestKnockdown(Vector3.Down, cause);
        GameEvents.PublishHint(string.Format(DownHint, Mathf.RoundToInt(BleedSeconds)));
        GD.Print($"[Net] down and bleeding ({cause})");
        Tell(true);
        // One point of health: the next blow finishes it.
        return 0.01f;
    }

    private void OnDied(string cause)
    {
        if (!Bleeding)
        {
            return;
        }
        Bleeding = false;
        GameEvents.PublishBleeding(false);
        Lock(false);
        Tell(false);
    }

    private bool AnyoneStanding()
    {
        foreach (RemoteCarrier remote in Session!.Remotes.Values)
        {
            if (IsInstanceValid(remote) && remote.States > 0 && !remote.Dead && !_downPeers.Contains(remote.PeerId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Down, the carrier can't walk, lean or swing (the letters and keys do nothing).</summary>
    private void Lock(bool locked)
    {
        Session?.LockControls("down", locked);
    }

    public override void _Process(double delta)
    {
        if (!Live)
        {
            return;
        }
        float dt = (float)delta;
        if (Bleeding)
        {
            BleedLeft = Mathf.Max(0.0f, BleedLeft - dt);
            if (BleedLeft <= 0.0f)
            {
                // Nobody came: the fall is real now.
                _finishing = true;
                GameEvents.PublishKill("bleeding out");
                _finishing = false;
            }
            return;
        }
        // Friends who left are no longer down here.
        _downPeers.RemoveWhere(peer => !Session!.Remotes.ContainsKey(peer));
        Help(dt);
    }

    /// <summary>Holding E beside a downed friend helps them up after ReviveSeconds.</summary>
    private void Help(float dt)
    {
        RemoteCarrier? target = null;
        PlayerController? player = Session!.LocalPlayer();
        if (player != null && !Session.LocalDead && _downPeers.Count > 0 && Input.IsActionPressed(ActionInteract))
        {
            float best = Reach;
            foreach (long peer in _downPeers)
            {
                if (Session.Remotes.TryGetValue(peer, out RemoteCarrier? remote) && IsInstanceValid(remote) && !remote.Dead)
                {
                    float distance = remote.GlobalPosition.DistanceTo(player.GlobalPosition);
                    if (distance <= best)
                    {
                        best = distance;
                        target = remote;
                    }
                }
            }
        }
        if (target == null)
        {
            _helpTimer = 0.0f;
            Helping = 0.0f;
            _helpTarget = 0;
            return;
        }
        if (target.PeerId != _helpTarget)
        {
            _helpTarget = target.PeerId;
            _helpTimer = 0.0f;
            HelpingName = target.PlayerName;
            GameEvents.PublishHint(string.Format(HelpHint, target.PlayerName));
        }
        _helpTimer += dt;
        Helping = Mathf.Clamp(_helpTimer / Mathf.Max(ReviveSeconds, 0.01f), 0.0f, 1.0f);
        if (_helpTimer >= ReviveSeconds)
        {
            HelpUp(target.PeerId, target.PlayerName);
            _helpTimer = 0.0f;
            Helping = 0.0f;
            _helpTarget = 0;
        }
    }

    /// <summary>Helps that downed player up (holding E, or the co-op probe).</summary>
    public void HelpUp(long peer, string name)
    {
        if (!Live || !_downPeers.Contains(peer))
        {
            return;
        }
        Helped++;
        _downPeers.Remove(peer);
        GameEvents.PublishHint(string.Format(HelpedHint, name));
        GD.Print($"[Net] helped {name} up");
        if (Session!.Mode == NetSession.Role.Host)
        {
            RpcId(peer, MethodName.YouAreUp, Session.LocalName);
        }
        else
        {
            RpcId(1, MethodName.Revive, peer);
        }
    }

    /// <summary>Helps up any downed friend, wherever they are (the co-op probe).</summary>
    public void HelpAnyone()
    {
        foreach (long peer in _downPeers)
        {
            string name = $"peer {peer}";
            if (Session!.Remotes.TryGetValue(peer, out RemoteCarrier? remote))
            {
                name = remote.PlayerName;
            }
            HelpUp(peer, name);
            return;
        }
    }

    // --- Network ---

    /// <summary>This carrier went down or got up (or fell for real): everyone is told.</summary>
    private void Tell(bool down)
    {
        NetSession session = Session!;
        if (session.Mode == NetSession.Role.Host)
        {
            foreach (long id in session.Peers.Keys)
            {
                RpcId(id, MethodName.PeerDown, 1L, session.LocalName, down);
            }
        }
        else
        {
            RpcId(1, MethodName.Down, down);
        }
    }

    /// <summary>Host: a client went down or got up.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Down(bool down)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        NetSession? session = Session;
        if (session == null || session.Mode != NetSession.Role.Host || !session.Peers.TryGetValue(sender, out NetPeer? peer))
        {
            return;
        }
        Mark(sender, peer.Name, down);
        foreach (long id in session.Peers.Keys)
        {
            if (id != sender)
            {
                RpcId(id, MethodName.PeerDown, sender, peer.Name, down);
            }
        }
    }

    /// <summary>Client: someone went down or got up, from the host.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerDown(long who, string name, bool down)
    {
        if (Session?.Mode != NetSession.Role.Client || who == Session.LocalId)
        {
            return;
        }
        if (name.Length > 24)
        {
            name = name.Substring(0, 24);
        }
        Mark(who, name, down);
    }

    private void Mark(long who, string name, bool down)
    {
        bool was = _downPeers.Contains(who);
        if (down)
        {
            _downPeers.Add(who);
            if (!was && !Bleeding && !(Session?.LocalDead ?? true))
            {
                GameEvents.PublishHint(string.Format(FriendDownHint, name));
            }
        }
        else
        {
            _downPeers.Remove(who);
        }
        if (Session != null && Session.Remotes.TryGetValue(who, out RemoteCarrier? remote) && IsInstanceValid(remote))
        {
            remote.Bleeding = down;
        }
    }

    /// <summary>Host: a client helps someone up; here if it's the host, else passed to them.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Revive(long target)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        NetSession? session = Session;
        if (session == null || session.Mode != NetSession.Role.Host || target == sender || !session.Peers.TryGetValue(sender, out NetPeer? helper))
        {
            return;
        }
        if (target == 1)
        {
            GotUp(helper.Name);
        }
        else if (session.Peers.ContainsKey(target))
        {
            RpcId(target, MethodName.YouAreUp, helper.Name);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void YouAreUp(string helper)
    {
        if (Session?.Mode != NetSession.Role.Client)
        {
            return;
        }
        if (helper.Length > 24)
        {
            helper = helper.Substring(0, 24);
        }
        GotUp(helper);
    }

    private void GotUp(string helper)
    {
        if (!Bleeding)
        {
            return;
        }
        Bleeding = false;
        GameEvents.PublishBleeding(false);
        Revived++;
        Lock(false);
        CarrierHealth? health = Session?.LocalPlayer()?.Health;
        if (health != null)
        {
            health.Heal(health.EffectiveMax * ReviveHealth);
        }
        GameEvents.PublishHint(string.Format(UpHint, helper));
        GD.Print($"[Net] {helper} helped us up");
        Tell(false);
    }
}
