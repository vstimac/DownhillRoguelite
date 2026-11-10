using System.Collections.Generic;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Co-op creatures and kill loot (a child of NetSession).
///
/// Ownership: each machine's directors bring enemies and night beasts round its own carrier, and that
/// machine simulates them (their owner). Ten times a second it sends each one's state (kind, species,
/// position, facing, clip, health, down) through the host to everyone else. The others show a puppet
/// (made when the first state arrives, so late joiners see everything), and remove it when the owner
/// says it's gone or leaves.
///
/// Blows: a blow on a puppet goes to its owner, who applies it to the real one; the owner's next state
/// shows the result everywhere.
///
/// Loot: what a kill drops gets a shared key, and the others get the same items at the same spots.
/// Picking one up goes through the host like any shared supply, so only one player ever gets it.
///
/// A creature chases and attacks only its owner's carrier.
/// </summary>
public partial class NetMobs : Node
{
    /// <summary>States per second for each creature.</summary>
    [Export] public float SendRate { get; set; } = 10.0f;
    /// <summary>Puppets close this share of the gap to the latest state per 1/60 s.</summary>
    [Export] public float Follow { get; set; } = 12.0f;
    /// <summary>Further than this from its latest state (m), a puppet jumps there.</summary>
    [Export] public float SnapDistance { get; set; } = 8.0f;
    /// <summary>At most this many creatures per owner are shown (anything beyond is ignored).</summary>
    [Export] public int MaxPerOwner { get; set; } = 24;
    /// <summary>Enemy species come from this folder; beasts by name from this roster.</summary>
    [Export(PropertyHint.Dir)] public string EnemyFolder { get; set; } = "res://wildlife/enemies/";
    [Export(PropertyHint.File, "*.tres")] public string BeastRosterPath { get; set; } = "res://wildlife/default_beasts.tres";

    public NetSession? Session { get; set; }
    /// <summary>Creatures this machine owns and shares, by id (tests).</summary>
    public int OwnedCount => _owned.Count;
    /// <summary>Other machines' creatures shown here (tests).</summary>
    public int PuppetCount => _puppets.Count;
    /// <summary>Blows sent to other owners, and blows from others applied here (tests).</summary>
    public int HitsSent { get; private set; }
    public int HitsApplied { get; private set; }
    /// <summary>Blows this machine's creatures dealt other players, and blows from theirs taken here (tests).</summary>
    public int StrikesSent { get; private set; }
    public int StrikesTaken { get; private set; }
    public int LootShared { get; private set; }
    public int LootReceived { get; private set; }
    public IEnumerable<Node3D> Puppets
    {
        get
        {
            foreach (Puppet puppet in _puppets.Values)
            {
                yield return puppet.Body;
            }
        }
    }

    private sealed class Puppet
    {
        public Node3D Body = null!;
        public Vector3 Position;
        public float Yaw;
    }

    private readonly Dictionary<int, Node3D> _owned = new();
    private readonly Dictionary<ulong, int> _ids = new();
    private readonly Dictionary<(long Owner, int Id), Puppet> _puppets = new();
    private readonly Dictionary<Node3D, (long Owner, int Id)> _puppetKeys = new();
    private int _nextId = 1;
    private int _nextLoot = 1;
    private double _sendTimer;
    private Node3D? _holder;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameEvents.NetMobHit += OnPuppetHit;
        GameEvents.LootDropped += OnLootDropped;
        GameEvents.NetCarrierStruck += OnStrike;
    }

    public override void _ExitTree()
    {
        GameEvents.NetMobHit -= OnPuppetHit;
        GameEvents.LootDropped -= OnLootDropped;
        GameEvents.NetCarrierStruck -= OnStrike;
    }

    private bool Live => Session != null && Session.Active && Session.Joined;

    /// <summary>The session ended: every puppet goes, and nothing is shared any more.</summary>
    public void Clear()
    {
        foreach (Puppet puppet in _puppets.Values)
        {
            if (IsInstanceValid(puppet.Body))
            {
                puppet.Body.QueueFree();
            }
        }
        _puppets.Clear();
        _puppetKeys.Clear();
        _owned.Clear();
        _ids.Clear();
    }

    /// <summary>A player left: their creatures go with them.</summary>
    public void DropOwner(long owner)
    {
        List<(long, int)> gone = new();
        foreach ((long Owner, int Id) key in _puppets.Keys)
        {
            if (key.Owner == owner)
            {
                gone.Add(key);
            }
        }
        foreach ((long, int) key in gone)
        {
            RemovePuppet(key);
        }
    }

    public override void _Process(double delta)
    {
        if (!Live)
        {
            return;
        }
        MovePuppets((float)delta);
        _sendTimer += delta;
        if (_sendTimer < 1.0 / Mathf.Max(SendRate, 1.0f))
        {
            return;
        }
        _sendTimer = 0.0;
        ShareOwned();
    }

    // --- Owner: share what this machine's directors brought ---

    private void ShareOwned()
    {
        // New ones (any non-puppet creature that can still be hit), then a state for each.
        foreach (Node node in GetTree().GetNodesInGroup(IMeleeTarget.Group))
        {
            if (node is Node3D body && node is INetMob mob && !mob.NetPuppet && mob.NetSpecies.Length > 0 && !_ids.ContainsKey(body.GetInstanceId()))
            {
                int id = _nextId++;
                _ids[body.GetInstanceId()] = id;
                _owned[id] = body;
            }
        }
        List<int> gone = new();
        foreach (KeyValuePair<int, Node3D> pair in _owned)
        {
            Node3D body = pair.Value;
            if (!IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion())
            {
                gone.Add(pair.Key);
                continue;
            }
            INetMob mob = (INetMob)body;
            Send(pair.Key, mob.NetKind, mob.NetSpecies, body.GlobalPosition, body.Rotation.Y, mob.NetClip, mob.NetClipPosition, mob.NetClipSpeed, mob.NetHealth, mob.NetDown);
        }
        foreach (int id in gone)
        {
            _owned.Remove(id);
            if (Session!.Mode == NetSession.Role.Host)
            {
                Rpc(MethodName.PeerMobGone, 1L, id);
            }
            else
            {
                RpcId(1, MethodName.MobGone, id);
            }
        }
    }

    private void Send(int id, int kind, string species, Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, float health, bool down)
    {
        if (Session!.Mode == NetSession.Role.Host)
        {
            foreach (long peer in Session.Peers.Keys)
            {
                RpcId(peer, MethodName.PeerMobState, 1L, id, kind, species, position, yaw, clip, clipPosition, clipSpeed, health, down);
            }
        }
        else
        {
            RpcId(1, MethodName.MobState, id, kind, species, position, yaw, clip, clipPosition, clipSpeed, health, down);
        }
    }

    // --- Client -> host (the host relays to everyone else) ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void MobState(int id, int kind, string species, Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, float health, bool down)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!FromPeer(sender) || !Sane(species, position, yaw, clip))
        {
            return;
        }
        Show(sender, id, kind, species, position, yaw, clip, clipPosition, clipSpeed, health, down);
        foreach (long peer in Session!.Peers.Keys)
        {
            if (peer != sender)
            {
                RpcId(peer, MethodName.PeerMobState, sender, id, kind, species, position, yaw, clip, clipPosition, clipSpeed, health, down);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MobGone(int id)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!FromPeer(sender))
        {
            return;
        }
        RemovePuppet((sender, id));
        foreach (long peer in Session!.Peers.Keys)
        {
            if (peer != sender)
            {
                RpcId(peer, MethodName.PeerMobGone, sender, id);
            }
        }
    }

    /// <summary>A client struck another machine's creature: the host passes the blow to its owner (or takes it, if it's the host's).</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MobHit(long owner, int id, float damage, Vector3 push, string source)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!FromPeer(sender) || !Mathf.IsFinite(damage) || damage < 0.0f || damage > 1000.0f || !push.IsFinite() || push.Length() > 100.0f || source.Length > 32)
        {
            return;
        }
        if (owner == 1)
        {
            ApplyHit(id, damage, push, source);
        }
        else if (Session!.Peers.ContainsKey(owner))
        {
            RpcId(owner, MethodName.TakeMobHit, id, damage, push, source);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LootDrop(string key, string itemId, Vector3 at, float durability)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!FromPeer(sender) || !key.StartsWith($"L{sender}.", System.StringComparison.Ordinal) || key.Length > 40 || !at.IsFinite() || !Mathf.IsFinite(durability))
        {
            return;
        }
        PlaceLoot(key, itemId, at, durability);
        foreach (long peer in Session!.Peers.Keys)
        {
            if (peer != sender)
            {
                RpcId(peer, MethodName.PeerLootDrop, key, itemId, at, durability);
            }
        }
    }

    // --- Host -> clients ---

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void PeerMobState(long owner, int id, int kind, string species, Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, float health, bool down)
    {
        if (Session?.Mode != NetSession.Role.Client || owner == Session.LocalId)
        {
            return;
        }
        Show(owner, id, kind, species, position, yaw, clip, clipPosition, clipSpeed, health, down);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerMobGone(long owner, int id)
    {
        if (Session?.Mode == NetSession.Role.Client)
        {
            RemovePuppet((owner, id));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TakeMobHit(int id, float damage, Vector3 push, string source)
    {
        if (Session?.Mode == NetSession.Role.Client)
        {
            ApplyHit(id, damage, push, source);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerLootDrop(string key, string itemId, Vector3 at, float durability)
    {
        if (Session?.Mode == NetSession.Role.Client && Mathf.IsFinite(durability))
        {
            PlaceLoot(key, itemId, at, durability);
        }
    }

    // --- Puppets ---

    private void Show(long owner, int id, int kind, string species, Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, float health, bool down)
    {
        if (!_puppets.TryGetValue((owner, id), out Puppet? puppet))
        {
            if (down || CountOf(owner) >= MaxPerOwner)
            {
                return;
            }
            Node3D? body = MakePuppet(kind, species, position, yaw);
            if (body == null)
            {
                return;
            }
            puppet = new Puppet { Body = body, Position = position, Yaw = yaw };
            _puppets[(owner, id)] = puppet;
            _puppetKeys[body] = (owner, id);
        }
        if (!IsInstanceValid(puppet.Body))
        {
            _puppets.Remove((owner, id));
            return;
        }
        puppet.Position = position;
        puppet.Yaw = yaw;
        ((INetMob)puppet.Body).NetShow(clip, clipPosition, clipSpeed, health, down);
    }

    private Node3D? MakePuppet(int kind, string species, Vector3 position, float yaw)
    {
        Node? world = GetTree().CurrentScene;
        if (world == null)
        {
            return null;
        }
        if (_holder == null || !IsInstanceValid(_holder) || !_holder.IsInsideTree())
        {
            _holder = new Node3D { Name = "NetCreatures" };
            world.AddChild(_holder);
        }
        Node3D? body = null;
        if (kind == 0 && ResourceLoader.Load(species) is EnemySpecies enemySpecies)
        {
            HumanoidEnemy enemy = HumanoidEnemy.Create(enemySpecies);
            enemy.Puppet = true;
            body = enemy;
        }
        else if (kind == 1 && Beast(species) is BeastSpecies beastSpecies)
        {
            NightBeast beast = new() { Puppet = true };
            beast.Setup(beastSpecies, (ulong)GD.Randi(), 0, 0);
            body = beast;
        }
        if (body == null)
        {
            return null;
        }
        _holder.AddChild(body);
        body.GlobalPosition = position;
        body.Rotation = new Vector3(0.0f, yaw, 0.0f);
        return body;
    }

    private BeastSpecies? Beast(string species)
    {
        if (ResourceLoader.Load(BeastRosterPath) is not BeastRoster roster)
        {
            return null;
        }
        foreach (BeastSpecies beast in roster.Beasts)
        {
            if ("beast:" + beast.DisplayName == species)
            {
                return beast;
            }
        }
        return null;
    }

    private void MovePuppets(float dt)
    {
        float share = 1.0f - Mathf.Exp(-Follow * dt);
        foreach (Puppet puppet in _puppets.Values)
        {
            if (!IsInstanceValid(puppet.Body))
            {
                continue;
            }
            // A dead enemy lies where it fell (its ragdoll); a fleeing beast keeps running.
            if (puppet.Body is HumanoidEnemy enemy && enemy.State == HumanoidEnemy.Phase.Dead)
            {
                continue;
            }
            Vector3 at = puppet.Body.GlobalPosition;
            if (at.DistanceTo(puppet.Position) > SnapDistance)
            {
                puppet.Body.GlobalPosition = puppet.Position;
            }
            else
            {
                puppet.Body.GlobalPosition = at.Lerp(puppet.Position, share);
            }
            float yaw = Mathf.LerpAngle(puppet.Body.Rotation.Y, puppet.Yaw, share);
            puppet.Body.Rotation = new Vector3(0.0f, yaw, 0.0f);
        }
    }

    private void RemovePuppet((long Owner, int Id) key)
    {
        if (!_puppets.Remove(key, out Puppet? puppet))
        {
            return;
        }
        _puppetKeys.Remove(puppet.Body);
        if (IsInstanceValid(puppet.Body))
        {
            puppet.Body.QueueFree();
        }
    }

    private int CountOf(long owner)
    {
        int count = 0;
        foreach ((long Owner, int Id) key in _puppets.Keys)
        {
            if (key.Owner == owner)
            {
                count++;
            }
        }
        return count;
    }

    // --- Blows ---

    /// <summary>This player struck a puppet: the blow goes to its owner.</summary>
    private void OnPuppetHit(INetMob mob, float damage, Vector3 push, string source)
    {
        if (!Live || mob is not Node3D body || !_puppetKeys.TryGetValue(body, out (long Owner, int Id) key))
        {
            return;
        }
        HitsSent++;
        if (Session!.Mode == NetSession.Role.Host)
        {
            RpcId(key.Owner, MethodName.TakeMobHit, key.Id, damage, push, source);
        }
        else
        {
            RpcId(1, MethodName.MobHit, key.Owner, key.Id, damage, push, source);
        }
    }

    /// <summary>Someone else struck one of this machine's creatures.</summary>
    private void ApplyHit(int id, float damage, Vector3 push, string source)
    {
        if (!_owned.TryGetValue(id, out Node3D? body) || !IsInstanceValid(body) || body is not IMeleeTarget target || !target.CanBeHit)
        {
            return;
        }
        HitsApplied++;
        if (source == "bleeding")
        {
            target.Bleed(damage);
        }
        else
        {
            target.TakeHit(damage, push, source);
        }
    }

    // --- Creatures striking other players ---

    /// <summary>One of this machine's creatures struck another player's carrier: the blow goes to them (through the host).</summary>
    private void OnStrike(NetStrike strike)
    {
        if (!Live || strike.Peer == Session!.LocalId)
        {
            return;
        }
        StrikesSent++;
        if (Session.Mode == NetSession.Role.Host)
        {
            RpcId(strike.Peer, MethodName.StruckYou, strike.Damage, strike.Away, strike.Push, strike.Knockdown, strike.Lift, strike.StaggerHeight, strike.Cause);
        }
        else
        {
            RpcId(1, MethodName.Strike, strike.Peer, strike.Damage, strike.Away, strike.Push, strike.Knockdown, strike.Lift, strike.StaggerHeight, strike.Cause);
        }
    }

    private static bool SaneStrike(float damage, Vector3 away, float push, float lift, float stagger, string cause)
    {
        return Mathf.IsFinite(damage) && damage >= 0.0f && damage <= 200.0f && away.IsFinite() && away.Length() <= 1.5f
            && Mathf.IsFinite(push) && push >= 0.0f && push <= 40.0f && Mathf.IsFinite(lift) && Mathf.Abs(lift) <= 4.0f
            && Mathf.IsFinite(stagger) && stagger >= 0.0f && stagger <= 4.0f && cause.Length <= 48;
    }

    /// <summary>Host: a client's creature struck someone: to them, or here if it's the host.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Strike(long target, float damage, Vector3 away, float push, bool knockdown, float lift, float stagger, string cause)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        if (!FromPeer(sender) || target == sender || !SaneStrike(damage, away, push, lift, stagger, cause))
        {
            return;
        }
        if (target == 1)
        {
            TakeStrike(damage, away, push, knockdown, lift, stagger, cause);
        }
        else if (Session!.Peers.ContainsKey(target))
        {
            RpcId(target, MethodName.StruckYou, damage, away, push, knockdown, lift, stagger, cause);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StruckYou(float damage, Vector3 away, float push, bool knockdown, float lift, float stagger, string cause)
    {
        if (Session?.Mode == NetSession.Role.Client && SaneStrike(damage, away, push, lift, stagger, cause))
        {
            TakeStrike(damage, away, push, knockdown, lift, stagger, cause);
        }
    }

    private void TakeStrike(float damage, Vector3 away, float push, bool knockdown, float lift, float stagger, string cause)
    {
        if (Session == null || Session.LocalDead)
        {
            return;
        }
        StrikesTaken++;
        GameEvents.TakeNetStrike(new NetStrike(Session.LocalId, damage, away, push, knockdown, lift, stagger, cause));
    }

    // --- Kill loot ---

    /// <summary>A kill here dropped an item: it gets a shared key, and everyone else gets it too.</summary>
    private void OnLootDropped(Node3D node, string itemId, Vector3 landing)
    {
        if (!Live || node is not WorldItem item)
        {
            return;
        }
        string key = $"L{Session!.LocalId}.{_nextLoot++}";
        item.SlotKey = key;
        LootShared++;
        // Its wear goes with it (a worn shirt handed over stays worn).
        float durability = item.Durability;
        if (Session.Mode == NetSession.Role.Host)
        {
            foreach (long peer in Session.Peers.Keys)
            {
                RpcId(peer, MethodName.PeerLootDrop, key, itemId, landing, durability);
            }
        }
        else
        {
            RpcId(1, MethodName.LootDrop, key, itemId, landing, durability);
        }
    }

    private void PlaceLoot(string key, string itemId, Vector3 at, float durability)
    {
        ItemDefinition? definition = ItemText.Find(itemId);
        Node? world = GetTree().CurrentScene;
        if (definition == null || world == null || Session!.TakenSupplies.Contains(key))
        {
            return;
        }
        WorldItem loot = new() { Definition = definition, SlotKey = key, Name = $"Loot_{itemId}" };
        if (durability >= 0.0f)
        {
            loot.Durability = Mathf.Min(durability, definition.MaxDurability);
        }
        world.AddChild(loot);
        loot.GlobalPosition = at;
        LootReceived++;
    }

    // --- Checks ---

    private bool FromPeer(long sender)
    {
        return Session != null && Session.Mode == NetSession.Role.Host && Session.Peers.ContainsKey(sender);
    }

    private bool Sane(string species, Vector3 position, float yaw, string clip)
    {
        bool enemy = species.StartsWith(EnemyFolder, System.StringComparison.Ordinal) && species.EndsWith(".tres", System.StringComparison.Ordinal) && !species.Contains("..");
        bool beast = species.StartsWith("beast:", System.StringComparison.Ordinal);
        return (enemy || beast) && species.Length < 200
            && position.IsFinite() && position.Length() < Session!.WorldLimit && Mathf.IsFinite(yaw) && clip.Length < 64;
    }
}
