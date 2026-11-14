using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Co-op: up to four players descend the same mountain together, Minecraft-style. One plays and
/// hosts (a listen server over ENet); the others join by address and port, on a LAN or through a
/// forwarded port, at the start or in the middle of a run. The host is the authority:
/// - It hands out the mountain's seed, so every machine builds the same mountain.
/// - It decides who gets each shared supply: a client asks first, and only one player ever gets it.
/// - It checks every message's sender and relays each player's state to the others.
///
/// Each machine simulates its own carrier and sends where it is, which way it faces and what its
/// animator plays about fifteen times a second, plus its character and worn clothes when they
/// change. The others show it as a RemoteCarrier.
///
/// A fallen player watches a living teammate (Tab or a click switches) and comes back after
/// RespawnSeconds at the last station they reached. The mountain is not reset, and the pack starts
/// over as after any death.
///
/// If the host leaves, the session ends: the others keep playing alone on the same mountain.
/// Singleplayer never touches any of this (no session, no gate, no messages).
///
/// Everything it does goes to the log as "[Net] ...". Command line (after --):
/// --host[=port], --join=address[:port], --net-name=Name.
/// </summary>
public partial class NetSession : Node
{
    public enum Role
    {
        None,
        Host,
        Client,
    }

    /// <summary>Peers must speak the same protocol (a join from another version is turned away).</summary>
    public const string ProtocolVersion = "downhill-coop-6";

    [Export] public int DefaultPort { get; set; } = 7777;
    [Export] public int MaxPlayers { get; set; } = 4;
    /// <summary>The UDP port LanDiscovery answers on while hosting (the game itself uses the ENet port).</summary>
    [Export] public int DiscoveryPort { get; set; } = 7778;
    /// <summary>A `--join=lan` test client gives up looking for a host after this many seconds.</summary>
    [Export] public float DiscoverTimeout { get; set; } = 10.0f;
    /// <summary>States sent per second.</summary>
    [Export] public float SendRate { get; set; } = 15.0f;
    /// <summary>A fallen player is back this many seconds later.</summary>
    [Export] public float RespawnSeconds { get; set; } = 12.0f;
    /// <summary>Joining gives up after this many seconds without an answer.</summary>
    [Export] public float ConnectTimeout { get; set; } = 10.0f;
    /// <summary>A client that hasn't said hello this long after connecting is let go.</summary>
    [Export] public float HelloTimeout { get; set; } = 6.0f;
    /// <summary>A promised supply that isn't picked up within this many seconds is free again.</summary>
    [Export] public float ClaimSeconds { get; set; } = 5.0f;
    /// <summary>A respawned player comes back this far beside the last station's fire (m).</summary>
    [Export] public float RespawnBesideFire { get; set; } = 4.0f;
    /// <summary>States further than this from the world's centre are nonsense and dropped (m).</summary>
    [Export] public float WorldLimit { get; set; } = 200000.0f;
    [Export] public int MaxNameLength { get; set; } = 20;
    [Export] public int MaxWornPieces { get; set; } = 16;
    /// <summary>The host sends its clock this often (s); a client more than ClockDrift hours off jumps to it.</summary>
    [Export] public float ClockInterval { get; set; } = 2.0f;
    [Export] public float ClockDrift { get; set; } = 0.05f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    public static NetSession? Instance { get; private set; }

    public Role Mode { get; private set; } = Role.None;
    public bool Active => Mode != Role.None;
    /// <summary>A client is "in" once the host welcomed it (it has the seed); the host is in at once.</summary>
    public bool Joined { get; private set; }
    public string Status { get; private set; } = "Not connected";
    public string LocalName { get; private set; } = "Carrier";
    public long LocalId { get; private set; } = 1;
    public int Port { get; private set; }
    public ulong SessionSeed { get; private set; }
    /// <summary>The expedition's name in other players' server browsers (set before Host; empty: "nickname's Expedition").</summary>
    public string ServerName { get; set; } = "";
    /// <summary>Hosting answers LAN discovery (shows in the others' server browsers).</summary>
    public bool LanVisible { get; set; } = true;
    /// <summary>Room in this session (2 to MaxPlayers), chosen when hosting.</summary>
    public int SessionMax { get; private set; } = 4;
    /// <summary>Everyone in the session, this player included (0 when not in one).</summary>
    public int PlayerCount
    {
        get
        {
            if (!Active)
            {
                return 0;
            }
            return _peers.Count + 1;
        }
    }
    /// <summary>The descent is under way: the host has reached a station (before that, the expedition is "waiting" at the summit).</summary>
    public bool InProgress => _lastStation > 0;
    /// <summary>The port LanDiscovery listens on while hosting (0: not answering).</summary>
    public int DiscoveryBoundPort => _discovery?.BoundPort ?? 0;
    /// <summary>What a `--join=lan` client found ("name address:port players/max"), for the test probe.</summary>
    public string Discovered { get; private set; } = "";
    public IReadOnlyDictionary<long, NetPeer> Peers => _peers;
    public IReadOnlyDictionary<long, RemoteCarrier> Remotes => _remotes;
    /// <summary>Shared supplies taken this session (by anyone).</summary>
    public IReadOnlyCollection<string> TakenSupplies => _taken;
    private readonly Dictionary<string, long> _pickupOwners = new();
    public bool HoldsPickup(string key, long peer)
    {
        return (_pickupOwners.TryGetValue(key, out long owner) && owner == peer)
            || (_promised.TryGetValue(key, out var claim) && claim.Peer == peer && claim.Until >= _clock);
    }
    /// <summary>Who is being watched while fallen (null: alive, or nobody left to watch).</summary>
    public RemoteCarrier? Watching { get; private set; }
    public bool LocalDead { get; private set; }
    public float RespawnIn { get; private set; }
    /// <summary>Supplies this client was promised and picked up / was refused (tests).</summary>
    public int ClaimsGranted { get; private set; }
    public int ClaimsRefused { get; private set; }

    /// <summary>Something changed the UI should show (status, players).</summary>
    public event Action? Changed;
    /// <summary>A client got the host's seed and mountain: the run can begin.</summary>
    public event Action<ulong>? Welcomed;

    private ENetMultiplayerPeer? _peer;
    private readonly Dictionary<long, NetPeer> _peers = new();
    private readonly Dictionary<long, RemoteCarrier> _remotes = new();
    private readonly Dictionary<long, double> _waitingHello = new();
    private readonly HashSet<string> _taken = new();
    private readonly Dictionary<string, (long Peer, double Until)> _promised = new();
    private readonly HashSet<string> _granted = new();
    private readonly HashSet<string> _asking = new();
    private readonly Dictionary<string, IInteractable> _claimTargets = new();
    private double _sendTimer;
    private double _connectTimer;
    private double _clock;
    private int _lastStation;
    private bool _savedNewMountain = true;
    private float _savedSummaryDelay = 7.0f;
    private bool _teleportNext;
    private double _clockTimer;
    private Difficulty _savedDifficulty = Difficulty.Normal;
    private bool _difficultySaved;
    private NetHud? _hud;
    private LanDiscovery? _discovery;
    private LanScanner? _finder;
    private double _findTimer;
    private NetMobs? _mobs;
    private NetPings? _pings;
    public NetPings? Pings => _pings;
    private NetChat? _chat;
    public NetChat? Chat => _chat;
    private NetRevive? _revive;
    public NetRevive? Revive => _revive;
    private NetWebs? _webs;
    public NetWebs? Webs => _webs;
    /// <summary>Shared creatures and kill loot.</summary>
    public NetMobs? Mobs => _mobs;
    public NetPlayerPoses? Poses { get; private set; }
    public NetWorldActors? WorldActors { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        GameEvents.GearChanged += OnGearChanged;
        GameEvents.CarrierDied += OnCarrierDied;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.StationReached += OnStationReached;
        GameEvents.SupplyPickedUp += OnSupplyPickedUp;
        GameEvents.SharedPickupAttempted += OnSharedPickupAttempted;
        GameEvents.Slept += OnSlept;
        GameOptions.Changed += OnOptionsChanged;
        _hud = new NetHud { Name = "NetHud", Session = this };
        AddChild(_hud);
        // The same path on every machine (/root/NetSession/NetMobs), so its messages find it.
        _mobs = new NetMobs { Name = "NetMobs", Session = this };
        AddChild(_mobs);
        Poses = new NetPlayerPoses { Name = "NetPlayerPoses", Session = this };
        AddChild(Poses);
        WorldActors = new NetWorldActors { Name = "NetWorldActors", Session = this };
        AddChild(WorldActors);
        _pings = new NetPings { Name = "NetPings", Session = this };
        AddChild(_pings);
        _chat = new NetChat { Name = "NetChat", Session = this };
        AddChild(_chat);
        _revive = new NetRevive { Name = "NetRevive", Session = this };
        AddChild(_revive);
        _webs = new NetWebs { Name = "NetWebs", Session = this };
        AddChild(_webs);
        CallDeferred(MethodName.ReadCommandLine);
    }

    public override void _ExitTree()
    {
        GameEvents.GearChanged -= OnGearChanged;
        GameEvents.CarrierDied -= OnCarrierDied;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.StationReached -= OnStationReached;
        GameEvents.SupplyPickedUp -= OnSupplyPickedUp;
        GameEvents.SharedPickupAttempted -= OnSharedPickupAttempted;
        GameEvents.Slept -= OnSlept;
        GameOptions.Changed -= OnOptionsChanged;
        if (Active)
        {
            Leave();
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void ReadCommandLine()
    {
        string name = "";
        string join = "";
        int hostPort = -1;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--net-name=", StringComparison.Ordinal))
            {
                name = arg["--net-name=".Length..];
            }
            else if (arg == "--host")
            {
                hostPort = DefaultPort;
            }
            else if (arg.StartsWith("--host=", StringComparison.Ordinal) && int.TryParse(arg["--host=".Length..], out int port))
            {
                hostPort = port;
            }
            else if (arg.StartsWith("--join=", StringComparison.Ordinal))
            {
                join = arg["--join=".Length..];
            }
            else if (arg.StartsWith("--net-server-name=", StringComparison.Ordinal))
            {
                ServerName = arg["--net-server-name=".Length..];
            }
            else if (arg.StartsWith("--net-discovery-port=", StringComparison.Ordinal) && int.TryParse(arg["--net-discovery-port=".Length..], out int discovery))
            {
                DiscoveryPort = discovery;
            }
            else if (arg.StartsWith("--net-probe=", StringComparison.Ordinal) && float.TryParse(arg["--net-probe=".Length..],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            {
                AddChild(new NetProbe { Name = "NetProbe", Session = this, Seconds = seconds, GrabAtUnix = ProbeNumber("--net-grab-at="), DieAtUnix = ProbeNumber("--net-die-at="),
                    SpawnAtUnix = ProbeNumber("--net-spawn-at="), StrikeAtUnix = ProbeNumber("--net-strike-at="),
                    StepAwayAtUnix = ProbeNumber("--net-step-away-at="), DropAtUnix = ProbeNumber("--net-drop-at="), PingAtUnix = ProbeNumber("--net-ping-at="), WebAtUnix = ProbeNumber("--net-web-at="), SayAtUnix = ProbeNumber("--net-say-at="), ShotAtUnix = ProbeNumber("--net-shot-at="), ShotPath = ProbeText("--net-shot="), HelpAtUnix = ProbeNumber("--net-help-at="),
                    AdvanceHours = Mathf.Max((float)ProbeNumber("--net-advance="), 0.0f), Linger = (float)ProbeNumber("--net-linger=") });
                double respawn = ProbeNumber("--net-respawn=");
                if (respawn > 0.0)
                {
                    RespawnSeconds = (float)respawn;
                }
                double bleed = ProbeNumber("--net-bleed=");
                if (bleed > 0.0 && _revive != null)
                {
                    _revive.BleedSeconds = (float)bleed;
                }
            }
        }
        double worldProbe = ProbeNumber("--net-world-probe=");
        if (worldProbe >= 0) { AddChild(new NetWorldProbe { Name = "NetWorldProbe", Session = this, StartAtUnix = worldProbe }); }
        if (hostPort > 0)
        {
            Host(name, hostPort);
        }
        else if (join == "lan")
        {
            FindAndJoin(name);
        }
        else if (join.Length > 0)
        {
            string address = join;
            int port = DefaultPort;
            int colon = join.LastIndexOf(':');
            if (colon > 0 && int.TryParse(join[(colon + 1)..], out int given))
            {
                address = join[..colon];
                port = given;
            }
            Join(name, address, port);
        }
    }

    /// <summary>A number after a probe argument (`--net-grab-at=unix seconds`, `--net-linger=seconds`), or -1.</summary>
    private static string ProbeText(string prefix)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }
        return "";
    }

    private static double ProbeNumber(string prefix)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal) && double.TryParse(arg[prefix.Length..],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }
        }
        return -1.0;
    }

    // --- Starting and ending a session ---

    /// <summary>Hosts a session on this port; false (and Status says why) if the port can't be opened.</summary>
    public bool Host(string name, int port)
    {
        return Host(name, port, MaxPlayers);
    }

    /// <summary>Hosts with room for this many (2 to MaxPlayers); answers LAN discovery while LanVisible.</summary>
    public bool Host(string name, int port, int maxPlayers)
    {
        if (Active)
        {
            Leave();
        }
        LocalName = CleanName(name);
        SessionMax = Mathf.Clamp(maxPlayers, 2, Mathf.Max(MaxPlayers, 2));
        _peer = new ENetMultiplayerPeer();
        Error error = _peer.CreateServer(port, Mathf.Max(SessionMax - 1, 1), 4);
        if (error != Error.Ok)
        {
            SetStatus($"Couldn't host on port {port} ({error}). Is another game using it?");
            Log($"host failed on port {port}: {error}");
            _peer = null;
            return false;
        }
        Multiplayer.MultiplayerPeer = _peer;
        Mode = Role.Host;
        GameEvents.SetWorldAuthority(true, true);
        Port = port;
        LocalId = 1;
        Joined = true;
        RunState? run = Run();
        SessionSeed = run?.Seed ?? 0;
        BeginSession();
        string addresses = LocalAddresses();
        if (addresses.Length > 0)
        {
            SetStatus($"Hosting on port {port}. Others join with your address: {addresses} (over the internet: your public address, with the port forwarded).");
        }
        else
        {
            SetStatus($"Hosting on port {port}. Others join with this computer's address.");
        }
        Log($"hosting on port {port}, seed {SessionSeed}, up to {SessionMax} players");
        if (LanVisible)
        {
            if (ServerName.Trim().Length == 0)
            {
                ServerName = $"{LocalName}'s Expedition";
            }
            _discovery = new LanDiscovery { Name = "LanDiscovery", Session = this, DiscoveryPort = DiscoveryPort };
            AddChild(_discovery);
        }
        return true;
    }

    /// <summary>Joins a host; Status follows the attempt and Welcomed fires once the host lets this player in.</summary>
    public bool Join(string name, string address, int port)
    {
        if (Active)
        {
            Leave();
        }
        LocalName = CleanName(name);
        string host = address.Trim();
        if (host.Length == 0)
        {
            host = "127.0.0.1";
        }
        _peer = new ENetMultiplayerPeer();
        Error error = _peer.CreateClient(host, port, 4);
        if (error != Error.Ok)
        {
            SetStatus($"Couldn't reach {host}:{port} ({error}).");
            Log($"join failed to start for {host}:{port}: {error}");
            _peer = null;
            return false;
        }
        Multiplayer.MultiplayerPeer = _peer;
        Mode = Role.Client;
        GameEvents.SetWorldAuthority(true, false);
        Port = port;
        Joined = false;
        _connectTimer = 0.0;
        SetStatus($"Connecting to {host}:{port}...");
        Log($"connecting to {host}:{port} as {LocalName}");
        return true;
    }

    /// <summary>Leaves the session (a host ends it for everyone); this player keeps playing alone.</summary>
    public void Leave()
    {
        if (!Active)
        {
            return;
        }
        Log("leaving the session");
        EndSession("You left the co-op session. The mountain is yours alone again.");
    }

    private void BeginSession()
    {
        RunState? run = Run();
        if (run != null)
        {
            // Together, a fall isn't the end of the mountain: you come back at the last station.
            _savedNewMountain = run.NewMountainOnDeath;
            _savedSummaryDelay = run.SummaryDelay;
            run.NewMountainOnDeath = false;
            run.SummaryDelay = RespawnSeconds;
        }
        GameEvents.PickupGate = Gate;
        GameEvents.PickupWasTaken = key => _taken.Contains(key);
        _lastStation = 0;
        LocalDead = false;
        Changed?.Invoke();
    }

    private void EndSession(string hint)
    {
        bool wasActive = Active;
        if (_discovery != null)
        {
            _discovery.Stop();
            _discovery.QueueFree();
            _discovery = null;
        }
        if (_peer != null)
        {
            _peer.Close();
        }
        _peer = null;
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        foreach (RemoteCarrier remote in _remotes.Values)
        {
            if (IsInstanceValid(remote))
            {
                remote.QueueFree();
            }
        }
        _remotes.Clear();
        _peers.Clear();
        _mobs?.Clear();
        WorldActors?.Clear();
        _pings?.Clear();
        _chat?.Clear();
        _revive?.Clear();
        GameEvents.PublishOtherCarriers(System.Array.Empty<NetCarrier>());
        _waitingHello.Clear();
        _taken.Clear();
        _pickupOwners.Clear();
        _promised.Clear();
        _granted.Clear();
        _asking.Clear();
        _claimTargets.Clear();
        GameEvents.PickupGate = null;
        GameEvents.PickupWasTaken = null;
        RunState? run = Run();
        if (run != null && wasActive)
        {
            run.NewMountainOnDeath = _savedNewMountain;
            run.SummaryDelay = _savedSummaryDelay;
            if (_difficultySaved)
            {
                run.Difficulty = _savedDifficulty;
                _difficultySaved = false;
            }
        }
        StopWatching();
        Mode = Role.None;
        GameEvents.SetWorldAuthority(false, false);
        Joined = false;
        SetStatus("Not connected");
        if (wasActive && hint.Length > 0)
        {
            GameEvents.PublishHint(hint);
        }
    }

    // --- Connection events ---

    private void OnPeerConnected(long id)
    {
        if (Mode == Role.Host)
        {
            _waitingHello[id] = _clock;
            Log($"peer {id} connected, waiting for hello");
        }
    }

    private void OnPeerDisconnected(long id)
    {
        _waitingHello.Remove(id);
        if (!_peers.Remove(id, out NetPeer? peer))
        {
            return;
        }
        RemoveRemote(id);
        _mobs?.DropOwner(id);
        Log($"{peer.Name} ({id}) left");
        GameEvents.PublishHint($"{peer.Name} left the descent.");
        if (Mode == Role.Host)
        {
            // Supplies promised to them and never taken are free again.
            List<string> free = new();
            foreach (KeyValuePair<string, (long Peer, double Until)> promise in _promised)
            {
                if (promise.Value.Peer == id)
                {
                    free.Add(promise.Key);
                }
            }
            foreach (string key in free)
            {
                _promised.Remove(key);
            }
            Rpc(MethodName.PeerLeft, id);
        }
        Changed?.Invoke();
    }

    private void OnConnected()
    {
        LocalId = Multiplayer.GetUniqueId();
        SetStatus("Connected. Waiting for the host to let you in...");
        Log($"connected as peer {LocalId}, saying hello");
        RpcId(1, MethodName.Hello, LocalName, LocalCharacter(), LocalWorn(), GameOptions.BaseShirtColour, GameOptions.BasePackColour, ProtocolVersion);
    }

    private void OnConnectionFailed()
    {
        Log("connection failed");
        EndSession("");
        SetStatus("The host refused the connection or couldn't be reached: it may be full or closed. Check the address, the port, and that the host is running.");
    }

    private void OnServerDisconnected()
    {
        Log("the host closed the session");
        EndSession("The host has left. You carry on alone on the same mountain.");
    }

    // --- Messages: client -> host ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(string name, string character, string[] worn, Color shirt, Color pack, string version)
    {
        if (Mode != Role.Host)
        {
            return;
        }
        long sender = Multiplayer.GetRemoteSenderId();
        if (_peers.ContainsKey(sender))
        {
            return;
        }
        _waitingHello.Remove(sender);
        if (version != ProtocolVersion)
        {
            Log($"turned away peer {sender}: version {version}, ours {ProtocolVersion}");
            RpcId(sender, MethodName.Rejected, $"The host runs another version of the game ({ProtocolVersion}). Update both to the same version.");
            DisconnectLater(sender);
            return;
        }
        if (_peers.Count + 1 >= SessionMax)
        {
            Log($"turned away peer {sender}: the session is full");
            RpcId(sender, MethodName.Rejected, $"The expedition is full ({SessionMax} players).");
            DisconnectLater(sender);
            return;
        }
        NetPeer peer = new() { Id = sender, Name = UniqueName(CleanName(name)), Character = CleanCharacter(character), Worn = CleanWorn(worn), Shirt = CleanColour(shirt), Pack = CleanColour(pack) };
        _peers[sender] = peer;
        Log($"{peer.Name} ({sender}) joined as {peer.Character}, wearing {peer.Worn.Length} pieces");
        // The newcomer gets the mountain, what's been taken, the host, and everyone already here.
        string[] taken = new string[_taken.Count];
        _taken.CopyTo(taken);
        RpcId(sender, MethodName.Welcome, (long)SessionSeed, taken, LocalName, LocalCharacter(), LocalWorn(), GameOptions.BaseShirtColour, GameOptions.BasePackColour, peer.Name,
            (int)(Run()?.Difficulty ?? Difficulty.Normal));
        SendClock(sender);
        foreach (NetPeer other in _peers.Values)
        {
            if (other.Id != sender)
            {
                RpcId(sender, MethodName.PeerJoined, other.Id, other.Name, other.Character, other.Worn, other.Shirt, other.Pack);
                RpcId(other.Id, MethodName.PeerJoined, sender, peer.Name, peer.Character, peer.Worn, peer.Shirt, peer.Pack);
            }
        }
        SpawnRemote(peer);
        GameEvents.PublishHint($"{peer.Name} joined the descent.");
        Changed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void State(Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, bool dead, int torch)
    {
        if (Mode != Role.Host)
        {
            return;
        }
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_peers.TryGetValue(sender, out NetPeer? peer) || !Sane(position, yaw, clipPosition, clipSpeed))
        {
            return;
        }
        string safeClip = Clip(clip);
        peer.Position = position;
        peer.Silent = 0.0;
        if (peer.Dead != dead)
        {
            peer.Dead = dead;
            Changed?.Invoke();
        }
        if (_remotes.TryGetValue(sender, out RemoteCarrier? remote))
        {
            remote.Receive(position, yaw, safeClip, clipPosition, clipSpeed, dead, torch);
        }
        foreach (long other in _peers.Keys)
        {
            if (other != sender)
            {
                RpcId(other, MethodName.PeerState, sender, position, yaw, safeClip, clipPosition, clipSpeed, dead, torch);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Look(string character, string[] worn, Color shirt, Color pack)
    {
        if (Mode != Role.Host)
        {
            return;
        }
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_peers.TryGetValue(sender, out NetPeer? peer))
        {
            return;
        }
        peer.Character = CleanCharacter(character);
        peer.Worn = CleanWorn(worn);
        peer.Shirt = CleanColour(shirt);
        peer.Pack = CleanColour(pack);
        if (_remotes.TryGetValue(sender, out RemoteCarrier? remote))
        {
            remote.ShowLook(peer.Character, peer.Worn, peer.Shirt, peer.Pack);
        }
        foreach (long other in _peers.Keys)
        {
            if (other != sender)
            {
                RpcId(other, MethodName.PeerLook, sender, peer.Character, peer.Worn, peer.Shirt, peer.Pack);
            }
        }
    }

    /// <summary>A client asks for a shared supply: the first to ask gets it; it is theirs for ClaimSeconds.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Claim(string key)
    {
        if (Mode != Role.Host)
        {
            return;
        }
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_peers.ContainsKey(sender) || key.Length == 0 || key.Length > 64)
        {
            return;
        }
        bool free = !_taken.Contains(key) && (!_promised.TryGetValue(key, out (long Peer, double Until) promise) || promise.Peer == sender);
        if (free)
        {
            _promised[key] = (sender, _clock + ClaimSeconds);
        }
        Log($"{NameOf(sender)} asks for {key}: {(free ? "yes" : "already taken")}");
        RpcId(sender, MethodName.ClaimAnswer, key, free);
    }

    /// <summary>A client picked up what it was promised: it's gone for everyone.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Took(string key)
    {
        if (Mode != Role.Host)
        {
            return;
        }
        long sender = Multiplayer.GetRemoteSenderId();
        if (!_promised.TryGetValue(key, out (long Peer, double Until) promise) || promise.Peer != sender)
        {
            Log($"{NameOf(sender)} says it took {key} without a promise; ignored");
            return;
        }
        _promised.Remove(key);
        MarkTaken(key, sender);
    }

    // --- Messages: host -> clients (only the host may send these) ---

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Welcome(long seed, string[] taken, string hostName, string hostCharacter, string[] hostWorn, Color hostShirt, Color hostPack, string yourName, int difficulty)
    {
        if (Mode != Role.Client)
        {
            return;
        }
        SessionSeed = (ulong)seed;
        LocalName = yourName;
        Joined = true;
        BeginSession();
        foreach (string key in taken)
        {
            _taken.Add(key);
        }
        NetPeer host = new() { Id = 1, Name = hostName, Character = hostCharacter, Worn = hostWorn, Shirt = hostShirt, Pack = hostPack };
        _peers[1] = host;
        SpawnRemote(host);
        // The host's mountain, at the host's difficulty (it sets how many supplies there are): built from its seed here too.
        RunState? run = Run();
        if (run != null && Enum.IsDefined(typeof(Difficulty), difficulty))
        {
            _savedDifficulty = run.Difficulty;
            _difficultySaved = true;
            run.Difficulty = (Difficulty)difficulty;
        }
        run?.StartNewRun(SessionSeed);
        CallDeferred(MethodName.RemoveTakenSupplies);
        SetStatus($"In {hostName}'s descent.");
        Log($"welcomed by {hostName}: seed {SessionSeed}, {taken.Length} supplies already taken, named {yourName}");
        Welcomed?.Invoke(SessionSeed);
        SendLook();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Rejected(string reason)
    {
        Log($"the host turned us away: {reason}");
        EndSession("");
        SetStatus(reason);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerJoined(long id, string name, string character, string[] worn, Color shirt, Color pack)
    {
        if (Mode != Role.Client || id == LocalId)
        {
            return;
        }
        NetPeer peer = new() { Id = id, Name = name, Character = character, Worn = worn, Shirt = shirt, Pack = pack };
        _peers[id] = peer;
        SpawnRemote(peer);
        Log($"{name} ({id}) is here");
        GameEvents.PublishHint($"{name} joined the descent.");
        Changed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerLeft(long id)
    {
        if (Mode != Role.Client || !_peers.Remove(id, out NetPeer? peer))
        {
            return;
        }
        RemoveRemote(id);
        _mobs?.DropOwner(id);
        Log($"{peer.Name} ({id}) left");
        GameEvents.PublishHint($"{peer.Name} left the descent.");
        Changed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void PeerState(long id, Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, bool dead, int torch)
    {
        if (Mode != Role.Client || id == LocalId || !_peers.TryGetValue(id, out NetPeer? peer))
        {
            return;
        }
        peer.Position = position;
        if (peer.Dead != dead)
        {
            peer.Dead = dead;
            Changed?.Invoke();
        }
        if (_remotes.TryGetValue(id, out RemoteCarrier? remote))
        {
            remote.Receive(position, yaw, clip, clipPosition, clipSpeed, dead, torch);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerLook(long id, string character, string[] worn, Color shirt, Color pack)
    {
        if (Mode != Role.Client || !_peers.TryGetValue(id, out NetPeer? peer))
        {
            return;
        }
        peer.Character = character;
        peer.Worn = worn;
        peer.Shirt = shirt;
        peer.Pack = pack;
        if (_remotes.TryGetValue(id, out RemoteCarrier? remote))
        {
            remote.ShowLook(character, worn, shirt, pack);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClaimAnswer(string key, bool granted)
    {
        if (Mode != Role.Client)
        {
            return;
        }
        _asking.Remove(key);
        if (!granted)
        {
            _claimTargets.Remove(key);
            ClaimsRefused++;
            Log($"{key}: someone else got it first");
            RemoveSupply(key);
            return;
        }
        ClaimsGranted++;
        _granted.Add(key);
        // Now take it for real (the gate lets it through once).
        IInteractable? item = null;
        if (_claimTargets.Remove(key, out IInteractable? preferred) && preferred is Node target && IsInstanceValid(target) && !target.IsQueuedForDeletion())
        { item = preferred; }
        item ??= FindSupply(key);
        PlayerController? player = LocalPlayer();
        if (item == null || player == null || !item.Interact(player))
        {
            _granted.Remove(key);
            Log($"{key}: promised, but not picked up (no room, or gone)");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Taken(string key, long by)
    {
        if (Mode != Role.Client)
        {
            return;
        }
        _taken.Add(key);
        GameEvents.PublishSharedPickupTaken(key);
        if (by != LocalId)
        {
            RemoveSupply(key);
        }
    }

    // --- Shared supplies ---
    private void OnSharedPickupAttempted(string key, IInteractable target)
    {
        if (Active && Joined && Mode == Role.Client && !_asking.Contains(key)) { _claimTargets[key] = target; }
    }

    /// <summary>The pickup gate (GameEvents.PickupGate): may this machine take this supply now?</summary>
    private bool Gate(string key)
    {
        if (!Joined)
        {
            return true;
        }
        if (Mode == Role.Host)
        {
            if (_taken.Contains(key))
            {
                return false;
            }
            return !_promised.ContainsKey(key);
        }
        if (_granted.Remove(key))
        {
            return true;
        }
        if (_taken.Contains(key))
        {
            RemoveSupply(key);
            return false;
        }
        if (_asking.Add(key))
        {
            RpcId(1, MethodName.Claim, key);
        }
        return false;
    }

    private void OnSupplyPickedUp(string key)
    {
        if (!Active || !Joined)
        {
            return;
        }
        if (Mode == Role.Host)
        {
            MarkTaken(key, 1);
            return;
        }
        _taken.Add(key);
        RpcId(1, MethodName.Took, key);
    }

    /// <summary>Host: a supply is gone; everyone removes it.</summary>
    private void MarkTaken(string key, long by)
    {
        _taken.Add(key);
        _pickupOwners[key] = by;
        GameEvents.PublishSharedPickupTaken(key);
        if (by != 1)
        {
            RemoveSupply(key);
        }
        Log($"{key} taken by {NameOf(by)}");
        Rpc(MethodName.Taken, key, by);
    }

    private void RemoveTakenSupplies()
    {
        SupplySpawner? spawner = Spawner();
        if (spawner != null && _taken.Count > 0)
        {
            spawner.MarkTaken(_taken);
        }
        foreach (string key in _taken) { RemoveSharedChoice(key); }
    }

    private void RemoveSupply(string key)
    {
        Spawner()?.MarkTaken(new[] { key });
        // Kill loot isn't the spawner's: it goes by its key.
        FindSupply(key)?.QueueFree();
        RemoveSharedChoice(key);
    }

    private void RemoveSharedChoice(string key)
    {
        foreach (Node node in GetTree().GetNodesInGroup(IInteractable.Group))
        {
            if (node is ISharedWorldPickup shared && shared.SharedPickupKey == key)
            {
                shared.SharedTaken();
                if (shared.RemoveWhenTaken) { node.QueueFree(); }
            }
        }
        GameEvents.PublishSharedPickupTaken(key);
    }

    private WorldItem? FindSupply(string key)
    {
        foreach (Node node in GetTree().GetNodesInGroup(IInteractable.Group))
        {
            if (node is WorldItem item && item.SlotKey == key && !item.IsQueuedForDeletion())
            {
                return item;
            }
        }
        return null;
    }

    // --- The local carrier: state out, death, respawn ---

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_finder != null)
        {
            _findTimer += delta;
            if (_findTimer > DiscoverTimeout)
            {
                Log("found no expedition on the home network");
                StopFinding();
                SetStatus("No expedition found on the home network.");
            }
        }
        if (!Active)
        {
            return;
        }
        if (Mode == Role.Client && !Joined)
        {
            _connectTimer += delta;
            if (_connectTimer > ConnectTimeout)
            {
                Log("no answer from the host; giving up");
                EndSession("");
                SetStatus("No answer from the host. Check the address and port, and the host's firewall.");
            }
            return;
        }
        if (Mode == Role.Host)
        {
            Housekeeping(delta);
            _clockTimer += delta;
            if (_clockTimer >= ClockInterval)
            {
                _clockTimer = 0.0;
                SendClock(0);
            }
        }
        _sendTimer += delta;
        if (_sendTimer >= 1.0 / Mathf.Max(SendRate, 1.0f))
        {
            _sendTimer = 0.0;
            SendState();
        }
        ShareCarriers();
        if (LocalDead)
        {
            RespawnIn = Mathf.Max(0.0f, RespawnIn - (float)delta);
            if (Watching == null || !IsInstanceValid(Watching) || Watching.Dead)
            {
                WatchNext();
            }
        }
    }

    /// <summary>Where the other players' carriers stand here, so this machine's creatures can go for them too.</summary>
    private void ShareCarriers()
    {
        List<NetCarrier> carriers = new(_remotes.Count);
        foreach (RemoteCarrier remote in _remotes.Values)
        {
            if (IsInstanceValid(remote) && remote.States > 0)
            {
                Vector3 input = Vector3.Zero; bool sprinting = false;
                if (remote.Pose?.Received > 0) { input = NetPoseCodec.Vector(remote.Pose.CurrentPose, 3); sprinting = remote.Pose.CurrentPose[6] > 0.5f; }
                carriers.Add(new NetCarrier(remote.PeerId, remote.GlobalPosition, remote.Dead || remote.Bleeding || remote.Pose?.PhysicalFall == true, remote.TorchLit, input, sprinting));
            }
        }
        GameEvents.PublishOtherCarriers(carriers.ToArray());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!LocalDead || !Active)
        {
            return;
        }
        bool next = @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Tab;
        if (@event is InputEventMouseButton button && button.Pressed && button.ButtonIndex == MouseButton.Left)
        {
            next = true;
        }
        if (next)
        {
            WatchNext();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Housekeeping(double delta)
    {
        // Hellos that never came, and promises nobody kept.
        List<long> silent = new();
        foreach (KeyValuePair<long, double> waiting in _waitingHello)
        {
            if (_clock - waiting.Value > HelloTimeout)
            {
                silent.Add(waiting.Key);
            }
        }
        foreach (long id in silent)
        {
            Log($"peer {id} never said hello; let go");
            _waitingHello.Remove(id);
            _peer?.DisconnectPeer((int)id);
        }
        List<string> expired = new();
        foreach (KeyValuePair<string, (long Peer, double Until)> promise in _promised)
        {
            if (_clock > promise.Value.Until)
            {
                expired.Add(promise.Key);
            }
        }
        foreach (string key in expired)
        {
            _promised.Remove(key);
        }
    }

    /// <summary>This computer's home-network (IPv4) addresses, for friends to type in: "192.168.1.5", or two joined by "or".</summary>
    public static string LocalAddresses()
    {
        List<string> found = new();
        foreach (string address in IP.GetLocalAddresses())
        {
            bool lan = address.StartsWith("192.168.", StringComparison.Ordinal) || address.StartsWith("10.", StringComparison.Ordinal);
            if (address.StartsWith("172.", StringComparison.Ordinal))
            {
                string[] parts = address.Split('.');
                if (parts.Length == 4 && int.TryParse(parts[1], out int second) && second >= 16 && second <= 31)
                {
                    lan = true;
                }
            }
            if (lan && !found.Contains(address))
            {
                found.Add(address);
            }
        }
        if (found.Count > 2)
        {
            found.RemoveRange(2, found.Count - 2);
        }
        return string.Join(" or ", found);
    }

    // --- One clock for everyone (the weather follows it) ---

    private WorldClock? Clock()
    {
        return GetNodeOrNull<WorldClock>("/root/WorldClock");
    }

    /// <summary>Host: its day and hour to one client (or everyone, 0).</summary>
    private void SendClock(long to)
    {
        WorldClock? clock = Clock();
        if (clock == null || Mode != Role.Host)
        {
            return;
        }
        if (to == 0)
        {
            foreach (long id in _peers.Keys)
            {
                RpcId(id, MethodName.ClockSync, clock.Day, clock.TimeOfDay);
            }
        }
        else
        {
            RpcId(to, MethodName.ClockSync, clock.Day, clock.TimeOfDay);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClockSync(int day, float hour)
    {
        WorldClock? clock = Clock();
        if (Mode != Role.Client || clock == null || day < 1 || !Mathf.IsFinite(hour))
        {
            return;
        }
        double theirs = (day - 1) * WorldClock.HoursPerDay + Mathf.PosMod(hour, WorldClock.HoursPerDay) - clock.StartHour;
        double off = Math.Abs(theirs - clock.TotalHours);
        if (off > ClockDrift)
        {
            if (off > 1.0)
            {
                Log($"clock set to the host's: day {day}, {hour:0.0} h");
            }
            clock.Restore(day, hour);
        }
    }

    /// <summary>Someone slept by a fire: the night passes for everyone (the host's clock jumps, and everyone follows it).</summary>
    private void OnSlept(float hours)
    {
        if (!Active || !Joined)
        {
            return;
        }
        if (Mode == Role.Host)
        {
            SendClock(0);
            Rpc(MethodName.NightPassed, LocalName);
            return;
        }
        RpcId(1, MethodName.Slept, hours);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Slept(float hours)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        WorldClock? clock = Clock();
        if (Mode != Role.Host || clock == null || !_peers.ContainsKey(sender) || !Mathf.IsFinite(hours) || hours <= 0.0f || hours > WorldClock.HoursPerDay)
        {
            return;
        }
        clock.AdvanceHours(hours);
        Log($"{NameOf(sender)} slept {hours:0.0} h; the night passes for everyone");
        GameEvents.PublishHint($"{NameOf(sender)} slept by a fire. The night passes.");
        SendClock(0);
        foreach (long id in _peers.Keys)
        {
            if (id != sender)
            {
                RpcId(id, MethodName.NightPassed, NameOf(sender));
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void NightPassed(string who)
    {
        if (Mode == Role.Client)
        {
            GameEvents.PublishHint($"{who} slept by a fire. The night passes.");
        }
    }

    /// <summary>The local torch for the others: 1 burning in the hand, 2 put away at the belt, 0 none.</summary>
    private static int LocalTorch()
    {
        if (GameEvents.CarrierTorchLit)
        {
            return 1;
        }
        if (GameEvents.CarrierTorchStowed)
        {
            return 2;
        }
        return 0;
    }

    private void SendState()
    {
        PlayerController? player = LocalPlayer();
        if (player == null)
        {
            return;
        }
        Vector3 position = player.GlobalPosition;
        float yaw = player.GlobalRotation.Y;
        Node3D? model = player.GetNodeOrNull<Node3D>("Model");
        if (model != null)
        {
            yaw = model.GlobalRotation.Y;
        }
        string clip = "";
        float clipPosition = 0.0f;
        float clipSpeed = 1.0f;
        AnimationPlayer? animation = player.GetNodeOrNull<AnimationPlayer>("Model/AnimationPlayer");
        if (animation != null && animation.IsPlaying())
        {
            clip = animation.CurrentAnimation;
            clipPosition = (float)animation.CurrentAnimationPosition;
            clipSpeed = animation.GetPlayingSpeed();
        }
        if (Mode == Role.Host)
        {
            foreach (long id in _peers.Keys)
            {
                RpcId(id, MethodName.PeerState, 1L, position, yaw, clip, clipPosition, clipSpeed, LocalDead, LocalTorch());
            }
        }
        else
        {
            RpcId(1, MethodName.State, position, yaw, clip, clipPosition, clipSpeed, LocalDead, LocalTorch());
        }
    }

    private void OnGearChanged(GearSnapshot gear)
    {
        if (Active && Joined)
        {
            SendLook();
        }
    }

    /// <summary>A new character or colours in the creator during a session: the others see it too.</summary>
    private void OnOptionsChanged()
    {
        string look = $"{LocalCharacter()}|{GameOptions.BaseShirtColour}|{GameOptions.BasePackColour}";
        if (look == _sentLook)
        {
            return;
        }
        if (Active && Joined)
        {
            SendLook();
        }
    }

    private string _sentLook = "";

    private void SendLook()
    {
        _sentLook = $"{LocalCharacter()}|{GameOptions.BaseShirtColour}|{GameOptions.BasePackColour}";
        if (Mode == Role.Host)
        {
            foreach (long id in _peers.Keys)
            {
                RpcId(id, MethodName.PeerLook, 1L, LocalCharacter(), LocalWorn(), GameOptions.BaseShirtColour, GameOptions.BasePackColour);
            }
        }
        else if (Mode == Role.Client && Joined)
        {
            RpcId(1, MethodName.Look, LocalCharacter(), LocalWorn(), GameOptions.BaseShirtColour, GameOptions.BasePackColour);
        }
    }

    private void OnStationReached(int station)
    {
        _lastStation = Mathf.Max(_lastStation, station);
    }

    private void OnCarrierDied(string cause)
    {
        if (!Active || !Joined)
        {
            return;
        }
        LocalDead = true;
        RespawnIn = RespawnSeconds;
        Log($"we fell ({cause}); back at station {_lastStation} in {RespawnSeconds:0} s");
        WatchNext();
        SendState();
        Changed?.Invoke();
    }

    private void OnRespawnRequested()
    {
        if (!Active || !Joined)
        {
            return;
        }
        // A new run (joining) builds the mountain again: what's been taken stays taken.
        CallDeferred(MethodName.RemoveTakenSupplies);
        if (!LocalDead)
        {
            return;
        }
        LocalDead = false;
        StopWatching();
        _teleportNext = true;
        // After every system has put the carrier back on the summit, bring them to the last station.
        GetTree().CreateTimer(0.3, true).Timeout += BackAtStation;
        Changed?.Invoke();
    }

    private void BackAtStation()
    {
        if (!_teleportNext)
        {
            return;
        }
        _teleportNext = false;
        RunState? run = Run();
        PlayerController? player = LocalPlayer();
        if (run?.Route == null || player == null || _lastStation <= 0)
        {
            Log("back at the summit (no station reached yet)");
            return;
        }
        Vector3 fire = run.Route.Station(_lastStation);
        Vector3 at = fire + Vector3.Right * RespawnBesideFire;
        PhysicsDirectSpaceState3D space = player.GetWorld3D().DirectSpaceState;
        Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 50.0f, at + Vector3.Down * 100.0f, GroundMask));
        if (hit.Count > 0)
        {
            at = (Vector3)hit["position"];
        }
        player.Teleport(at + Vector3.Up * 0.2f, player.GlobalRotation.Y);
        GameEvents.PublishHint($"You're back at the fire of Station {_lastStation}.");
        Log($"back at station {_lastStation} ({at})");
    }

    // --- Watching a teammate while fallen ---

    private void WatchNext()
    {
        List<RemoteCarrier> living = new();
        foreach (RemoteCarrier remote in _remotes.Values)
        {
            if (IsInstanceValid(remote) && !remote.Dead)
            {
                living.Add(remote);
            }
        }
        ChaseCamera? camera = Camera();
        if (living.Count == 0 || camera == null)
        {
            StopWatching();
            return;
        }
        int index = 0;
        if (Watching != null)
        {
            index = (living.IndexOf(Watching) + 1) % living.Count;
        }
        Watching = living[index];
        camera.Target = Watching;
        Changed?.Invoke();
    }

    private void StopWatching()
    {
        Watching = null;
        ChaseCamera? camera = Camera();
        PlayerController? player = LocalPlayer();
        if (camera != null && player != null && camera.Target != player)
        {
            camera.Target = player;
        }
    }

    // --- Remote carriers ---

    private void SpawnRemote(NetPeer peer)
    {
        RemoveRemote(peer.Id);
        Node? world = GetTree().CurrentScene;
        if (world == null)
        {
            return;
        }
        RemoteCarrier remote = new() { Name = $"Remote_{peer.Id}", PeerId = peer.Id, PlayerName = peer.Name, Character = peer.Character };
        Node3D? model = LocalPlayer()?.GetNodeOrNull<Node3D>("Model");
        Node3D? look = LocalPlayer()?.GetNodeOrNull<Node3D>("CarrierModel");
        if (model != null)
        {
            remote.UseModelPlacement(model.Transform, look?.Transform ?? model.Transform);
        }
        world.AddChild(remote);
        remote.ShowLook(peer.Character, peer.Worn, peer.Shirt, peer.Pack);
        _remotes[peer.Id] = remote;
    }

    private void RemoveRemote(long id)
    {
        if (_remotes.Remove(id, out RemoteCarrier? remote) && IsInstanceValid(remote))
        {
            if (Watching == remote)
            {
                Watching = null;
                if (LocalDead)
                {
                    WatchNext();
                }
                else
                {
                    StopWatching();
                }
            }
            remote.QueueFree();
        }
    }

    // --- Helpers ---

    private RunState? Run()
    {
        return GetNodeOrNull<RunState>("/root/RunState");
    }

    private PlayerController? _player;
    private ChaseCamera? _camera;
    private SupplySpawner? _spawner;

    public PlayerController? LocalPlayer()
    {
        if (_player != null && IsInstanceValid(_player) && _player.IsInsideTree())
        {
            return _player;
        }
        _player = Find<PlayerController>(GetTree().CurrentScene);
        return _player;
    }

    private ChaseCamera? Camera()
    {
        if (_camera != null && IsInstanceValid(_camera) && _camera.IsInsideTree())
        {
            return _camera;
        }
        _camera = Find<ChaseCamera>(GetTree().CurrentScene);
        return _camera;
    }

    private SupplySpawner? Spawner()
    {
        if (_spawner != null && IsInstanceValid(_spawner) && _spawner.IsInsideTree())
        {
            return _spawner;
        }
        _spawner = Find<SupplySpawner>(GetTree().CurrentScene);
        return _spawner;
    }

    private readonly HashSet<string> _controlLocks = new();

    /// <summary>
    /// Stops (or frees) the local carrier's walking and leaning for a reason (the chat line, being
    /// down); it moves again only when no reason is left.
    /// </summary>
    public void LockControls(string reason, bool locked)
    {
        if (locked)
        {
            _controlLocks.Add(reason);
        }
        else
        {
            _controlLocks.Remove(reason);
        }
        PlayerController? player = LocalPlayer();
        if (player == null)
        {
            return;
        }
        bool free = _controlLocks.Count == 0;
        player.InputEnabled = free;
        DownhillRoguelite.Balance.BalanceController? balance = Find<DownhillRoguelite.Balance.BalanceController>(player);
        if (balance != null)
        {
            balance.InputEnabled = free;
        }
    }

    internal static T? Find<T>(Node? root) where T : class
    {
        if (root == null)
        {
            return null;
        }
        if (root is T found && root is not RemoteCarrier)
        {
            return found;
        }
        foreach (Node child in root.GetChildren())
        {
            if (child is RemoteCarrier)
            {
                continue;
            }
            T? inner = Find<T>(child);
            if (inner != null)
            {
                return inner;
            }
        }
        return null;
    }

    private string LocalCharacter()
    {
        string? id = Run()?.Character.Definition?.Id;
        if (id != null && id.Length > 0)
        {
            return id;
        }
        return GameOptions.Character;
    }

    private static string[] LocalWorn()
    {
        List<string> worn = new();
        GearSnapshot gear = GameEvents.CurrentGear;
        if (gear.Slots != null)
        {
            foreach (WornPiece piece in gear.Slots)
            {
                if (!piece.Empty && !worn.Contains(piece.ItemId))
                {
                    worn.Add(piece.ItemId);
                }
            }
        }
        return worn.ToArray();
    }

    private string NameOf(long id)
    {
        if (id == 1 && Mode == Role.Host)
        {
            return LocalName;
        }
        if (_peers.TryGetValue(id, out NetPeer? peer))
        {
            return peer.Name;
        }
        return $"peer {id}";
    }

    private string CleanName(string name)
    {
        string clean = name.Trim().Replace("\n", " ").Replace("[", "(").Replace("]", ")");
        if (clean.Length == 0)
        {
            clean = GameOptions.NetName.Trim();
        }
        if (clean.Length == 0)
        {
            clean = "Carrier";
        }
        if (clean.Length > MaxNameLength)
        {
            clean = clean[..MaxNameLength];
        }
        return clean;
    }

    private string UniqueName(string name)
    {
        string unique = name;
        int n = 2;
        while (unique == LocalName || Taken(unique))
        {
            unique = $"{name} {n}";
            n++;
        }
        return unique;
    }

    private bool Taken(string name)
    {
        foreach (NetPeer peer in _peers.Values)
        {
            if (peer.Name == name)
            {
                return true;
            }
        }
        return false;
    }

    private static string CleanCharacter(string character)
    {
        if (CarrierRoster.Load()?.Pick(character) is CarrierCharacter picked)
        {
            return picked.Id;
        }
        return "";
    }

    private string[] CleanWorn(string[] worn)
    {
        List<string> clean = new();
        foreach (string id in worn)
        {
            if (clean.Count < MaxWornPieces && ItemText.Find(id) != null && !clean.Contains(id))
            {
                clean.Add(id);
            }
        }
        return clean.ToArray();
    }

    /// <summary>A colour as sent: opaque, each channel 0–1 (anything else is turned into the default grey).</summary>
    private static Color CleanColour(Color colour)
    {
        if (!Mathf.IsFinite(colour.R) || !Mathf.IsFinite(colour.G) || !Mathf.IsFinite(colour.B))
        {
            return new Color(0.5f, 0.5f, 0.5f);
        }
        return new Color(Mathf.Clamp(colour.R, 0.0f, 1.0f), Mathf.Clamp(colour.G, 0.0f, 1.0f), Mathf.Clamp(colour.B, 0.0f, 1.0f));
    }

    private static string Clip(string clip)
    {
        if (clip.Length > 64)
        {
            return "";
        }
        return clip;
    }

    private bool Sane(Vector3 position, float yaw, float clipPosition, float clipSpeed)
    {
        return position.IsFinite() && position.Length() < WorldLimit && Mathf.IsFinite(yaw) && Mathf.IsFinite(clipPosition)
            && Mathf.IsFinite(clipSpeed) && Mathf.Abs(clipSpeed) < 10.0f;
    }

    // --- Joining the first expedition found on the home network (`--join=lan`) ---

    private void FindAndJoin(string name)
    {
        _finder = new LanScanner { Name = "LanScanner", DiscoveryPort = DiscoveryPort };
        AddChild(_finder);
        _findTimer = 0.0;
        _finder.Changed += () => OnFound(name);
        _finder.Start();
        Log($"looking for an expedition on the home network (UDP {DiscoveryPort})");
    }

    private void OnFound(string name)
    {
        if (_finder == null)
        {
            return;
        }
        foreach (LanServerInfo server in _finder.Servers)
        {
            if (server.Joinable)
            {
                Discovered = $"{server.Name.Replace(' ', '_')} {server.Endpoint} {server.Players}/{server.MaxPlayers}";
                Log($"discovered {server}");
                StopFinding();
                Join(name, server.Address, server.Port);
                return;
            }
        }
    }

    private void StopFinding()
    {
        if (_finder != null)
        {
            _finder.Stop();
            _finder.QueueFree();
            _finder = null;
        }
    }

    private void DisconnectLater(long id)
    {
        GetTree().CreateTimer(0.5, true).Timeout += () => _peer?.DisconnectPeer((int)id);
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private static void Log(string text)
    {
        GD.Print($"[Net] {text}");
    }
}
