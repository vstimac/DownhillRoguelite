using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Test probe for co-op runs (`-- --net-probe=seconds`, added by NetSession): after that many
/// seconds it prints one "[NetProbe]" line with what this machine sees (role, seed, players,
/// remote carriers, states received, supplies taken) and quits. With `--net-grab-at=unix seconds` it
/// also reaches, at that moment by the computer's clock, for the same shared supply every probe picks (the first by key), so a run
/// with several probes reaching at once shows that only one player ever gets it. With
/// `--net-die-at=unix seconds` its carrier falls then: it should watch a teammate, the others should
/// see it fallen, and it should come back. `--net-spawn-at=unix seconds` brings an enemy beside this
/// carrier then; `--net-strike-at=unix seconds` strikes every other machine's creature shown here
/// (the blow goes to its owner). A client started with `--join=lan` finds the host by LAN discovery
/// first; its report names what it found (discovered="name address:port players/max"). At the grab moment it notes the clock, so the harness can compare
/// everyone's hour. The harness reads the lines.
/// </summary>
public partial class NetProbe : Node
{
    [Export] public float Seconds { get; set; } = 20.0f;
    /// <summary>When it reaches for the shared supply, by the computer's clock (Unix seconds; negative: never), so probes started apart reach at once.</summary>
    [Export] public double GrabAtUnix { get; set; } = -1.0;
    /// <summary>It stays this long after reporting before it quits (a host stays while its clients report).</summary>
    [Export] public float Linger { get; set; }
    [Export] public float GrabFor { get; set; } = 3.0f;
    /// <summary>When its carrier falls, by the computer's clock (Unix seconds; negative: never).</summary>
    [Export] public double DieAtUnix { get; set; } = -1.0;
    [Export] public double SpawnAtUnix { get; set; } = -1.0;
    [Export] public double StrikeAtUnix { get; set; } = -1.0;
    /// <summary>`--net-drop-at=unix seconds`: drops the first thing in the pack (the others should see it).</summary>
    [Export] public double DropAtUnix { get; set; } = -1.0;
    /// <summary>`--net-ping-at=unix seconds`: marks a spot a few metres ahead (the others should see the mark).</summary>
    [Export] public double PingAtUnix { get; set; } = -1.0;
    /// <summary>`--net-web-at=unix seconds`: tears a spider web (the others should take it down too).</summary>
    [Export] public double WebAtUnix { get; set; } = -1.0;
    /// <summary>`--net-say-at=unix seconds`: says a line in the chat (the others should read it).</summary>
    [Export] public double SayAtUnix { get; set; } = -1.0;
    /// <summary>`--net-shot-at=unix seconds --net-shot=file.png`: saves what this (rendered) machine shows then.</summary>
    [Export] public double ShotAtUnix { get; set; } = -1.0;
    [Export] public string ShotPath { get; set; } = "";
    /// <summary>`--net-help-at=unix seconds`: helps up a downed friend, wherever they are.</summary>
    [Export] public double HelpAtUnix { get; set; } = -1.0;
    /// <summary>`--net-step-away-at=unix seconds`: this carrier walks off (is moved StepAway metres), leaving its enemy to the others.</summary>
    [Export] public double StepAwayAtUnix { get; set; } = -1.0;
    [Export] public Vector3 StepAway { get; set; } = new(-35.0f, 4.0f, 0.0f);
    [Export] public float StrikeDamage { get; set; } = 500.0f;
    /// <summary>The host moves its clock on this many hours early in the run (`--net-advance=hours`), so the clients must follow.</summary>
    [Export] public float AdvanceHours { get; set; }

    public NetSession? Session { get; set; }

    private double _clock;
    private string _target = "";
    private string _got = "none";
    private bool _grabbing;
    private bool _done;
    private double _nextTry;
    private double _grabEnd;
    private bool _killed;
    private bool _fell;
    private string _watched = "none";
    private bool _back;
    private bool _sawFallen;
    private bool _spawned;
    private bool _struck;
    private bool _steppedAway;
    private bool _dropped;
    private bool _pinged;
    private bool _webbed;
    private double _nextWhere;
    private bool _said;
    private bool _shot;
    private bool _helped;
    private string _droppedId = "";
    private double _nextEnemyNote;
    private int _sawPuppets;
    private double _clockAtGrab = -1.0;
    private bool _advanced;
    private bool _sawPhysicalFall;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameEvents.SupplyPickedUp += OnPicked;
    }

    public override void _ExitTree()
    {
        GameEvents.SupplyPickedUp -= OnPicked;
    }

    private void OnPicked(string key)
    {
        if (key == _target && _target.Length > 0)
        {
            _got = key;
        }
    }

    public override void _Process(double delta)
    {
        if (Session == null)
        {
            return;
        }
        _clock += delta;
        // Where this carrier is, every 2 s (a fall through the ground shows as y running away).
        if (_clock >= _nextWhere)
        {
            _nextWhere = _clock + 2.0;
            Player.PlayerController? here = Session.LocalPlayer();
            if (here != null)
            {
                GD.Print($"[NetProbe] at {here.GlobalPosition:0.0} moving {here.Velocity:0.0} after {_clock:0.00} s");
            }
        }
        if (_done)
        {
            if (_clock >= Seconds + Linger)
            {
                GetTree().Quit(0);
            }
            return;
        }
        if (GrabAtUnix >= 0.0 && !_grabbing && Time.GetUnixTimeFromSystem() >= GrabAtUnix)
        {
            _grabbing = true;
            _grabEnd = _clock + GrabFor;
            _clockAtGrab = GetNodeOrNull<WorldClock>("/root/WorldClock")?.TotalHours ?? -1.0;
            _target = FirstSupply()?.SlotKey ?? "";
            GD.Print($"[NetProbe] reaching for {_target}");
        }
        if (_grabbing && _clock < _grabEnd && _clock >= _nextTry && _target.Length > 0 && _got == "none")
        {
            _nextTry = _clock + 0.25;
            WorldItem? item = Find(_target);
            Node3D? player = Session.LocalPlayer();
            if (item != null && player != null)
            {
                item.Interact(player);
            }
        }
        if (DieAtUnix >= 0.0 && !_killed && Session.Joined && Time.GetUnixTimeFromSystem() >= DieAtUnix)
        {
            _killed = true;
            GD.Print("[NetProbe] falling");
            GameEvents.PublishKill("a test");
        }
        double now = Time.GetUnixTimeFromSystem();
        if (AdvanceHours > 0.0f && !_advanced && _clock > 3.0)
        {
            _advanced = true;
            GetNodeOrNull<WorldClock>("/root/WorldClock")?.AdvanceHours(AdvanceHours);
            GD.Print($"[NetProbe] clock moved on {AdvanceHours:0} h");
        }
        if (SpawnAtUnix >= 0.0 && !_spawned && Session.Joined && now >= SpawnAtUnix)
        {
            _spawned = true;
            SpawnEnemy();
        }
        if (DropAtUnix >= 0.0 && !_dropped && Session.Joined && now >= DropAtUnix)
        {
            _dropped = true;
            CarrierInventory? pack = Session.LocalPlayer()?.GetNodeOrNull<CarrierInventory>("Inventory");
            if (pack?.Grid != null && pack.Grid.Items.Count > 0)
            {
                _droppedId = pack.Grid.Items[0].Definition.ItemId;
                GameEvents.RequestDrop(_droppedId);
                GD.Print($"[NetProbe] dropped {_droppedId}");
            }
        }
        if (WebAtUnix >= 0.0 && !_webbed && Session.Joined && now >= WebAtUnix)
        {
            _webbed = true;
            GameEvents.PublishWebTorn("12:-345:56:-345");
            GD.Print("[NetProbe] tore a web");
        }
        if (PingAtUnix >= 0.0 && !_pinged && Session.Joined && now >= PingAtUnix)
        {
            _pinged = true;
            Node3D? me = Session.LocalPlayer();
            if (me != null && Session.Pings != null)
            {
                Session.Pings.MarkAt(me.GlobalPosition + new Vector3(0.0f, 0.0f, -6.0f));
                GD.Print("[NetProbe] marked a spot");
            }
        }
        if (SayAtUnix >= 0.0 && !_said && Session.Joined && now >= SayAtUnix)
        {
            _said = true;
            Session.Chat?.Say("Supplies by the big rock, come [here]!");
            GD.Print("[NetProbe] said a line");
        }
        if (HelpAtUnix >= 0.0 && !_helped && Session.Joined && now >= HelpAtUnix)
        {
            _helped = true;
            Session.Revive?.HelpAnyone();
            GD.Print("[NetProbe] helped");
        }
        if (ShotAtUnix >= 0.0 && !_shot && ShotPath.Length > 0 && now >= ShotAtUnix)
        {
            _shot = true;
            Image? frame = GetViewport().GetTexture()?.GetImage();
            if (frame != null)
            {
                frame.SavePng(ShotPath);
                GD.Print($"[NetProbe] saved {ShotPath}");
            }
        }
        if (_steppedAway && _clock >= _nextEnemyNote)
        {
            _nextEnemyNote = _clock + 1.0;
            foreach (Node node in GetTree().GetNodesInGroup(IMeleeTarget.Group))
            {
                if (node is HumanoidEnemy enemy && !enemy.Puppet)
                {
                    GD.Print($"[NetProbe] enemy {enemy.State} aware={enemy.Aware} target={enemy.TargetPeer} at {enemy.GlobalPosition:0.0} feet {enemy.TargetFeet:0.0} hits={enemy.HitsLanded} others={GameEvents.OtherCarriers.Length}");
                }
            }
        }
        if (StepAwayAtUnix >= 0.0 && !_steppedAway && Session.Joined && now >= StepAwayAtUnix)
        {
            _steppedAway = true;
            Node3D? me = Session.LocalPlayer();
            if (me != null)
            {
                me.GlobalPosition += StepAway;
                GD.Print("[NetProbe] stepped away");
            }
        }
        if (StrikeAtUnix >= 0.0 && !_struck && Session.Joined && now >= StrikeAtUnix)
        {
            _struck = true;
            Strike();
        }
        if (Session.Mobs != null)
        {
            _sawPuppets = Mathf.Max(_sawPuppets, Session.Mobs.PuppetCount);
        }
        if (Session.LocalDead)
        {
            _fell = true;
            if (Session.Watching != null)
            {
                _watched = Session.Watching.PlayerName;
            }
        }
        else if (_fell)
        {
            _back = true;
        }
        foreach (RemoteCarrier remote in Session.Remotes.Values)
        {
            _sawPhysicalFall |= remote.Pose?.PhysicalFall == true;
            if (remote.Dead)
            {
                _sawFallen = true;
            }
        }
        if (_clock >= Seconds)
        {
            _done = true;
            Report();
        }
    }

    private void Report()
    {
        NetSession session = Session!;
        int fewest = int.MaxValue;
        int fewestPoses = int.MaxValue;
        foreach (RemoteCarrier remote in session.Remotes.Values)
        {
            fewest = Mathf.Min(fewest, remote.States);
            fewestPoses = Mathf.Min(fewestPoses, remote.Pose?.Received ?? 0);
        }
        if (session.Remotes.Count == 0)
        {
            fewest = 0;
            fewestPoses = 0;
        }
        ulong seed = GetNodeOrNull<RunState>("/root/RunState")?.Seed ?? 0;
        GD.Print($"[NetProbe] role={session.Mode} name={session.LocalName} joined={session.Joined} seed={seed} session_seed={session.SessionSeed} "
            + $"peers={session.Peers.Count} remotes={session.Remotes.Count} min_states={fewest} min_poses={fewestPoses} poses_sent={session.Poses?.Sent ?? 0} saw_physical_fall={_sawPhysicalFall} npc_states={session.WorldActors?.Received ?? 0} target={_target} got={_got} "
            + $"taken={session.TakenSupplies.Count} granted={session.ClaimsGranted} refused={session.ClaimsRefused} fell={_fell} watched={_watched} back={_back} "
            + $"saw_fallen={_sawFallen} looks={Looks(session)} owned={session.Mobs?.OwnedCount ?? 0} saw_puppets={_sawPuppets} "
            + $"hits_sent={session.Mobs?.HitsSent ?? 0} hits_applied={session.Mobs?.HitsApplied ?? 0} loot_shared={session.Mobs?.LootShared ?? 0} "
            + $"loot_received={session.Mobs?.LootReceived ?? 0} clock_at_grab={_clockAtGrab:0.000} strikes_sent={session.Mobs?.StrikesSent ?? 0} dropped={_droppedId.Length > 0} strikes_taken={session.Mobs?.StrikesTaken ?? 0} pings_sent={session.Pings?.Sent ?? 0} pings_seen={session.Pings?.Seen ?? 0} webs_sent={session.Webs?.Sent ?? 0} webs_received={session.Webs?.Received ?? 0} chat_sent={session.Chat?.Sent ?? 0} chat_seen={session.Chat?.Seen ?? 0} downs={session.Revive?.Downs ?? 0} revived={session.Revive?.Revived ?? 0} helped_up={session.Revive?.Helped ?? 0} discovered=\"{session.Discovered}\" lan_port={session.DiscoveryBoundPort} status=\"{session.Status}\"");
    }

    /// <summary>Each remote carrier's name, character and shirt colour, as shown here.</summary>
    private static string Looks(NetSession session)
    {
        List<string> looks = new();
        foreach (RemoteCarrier remote in session.Remotes.Values)
        {
            looks.Add($"{remote.PlayerName}:{remote.Character}:{remote.Shirt.ToHtml(false)}");
        }
        looks.Sort(System.StringComparer.Ordinal);
        return string.Join(",", looks);
    }

    private void SpawnEnemy()
    {
        EnemyDirector? director = null;
        foreach (Node node in GetTree().CurrentScene.FindChildren("*", "", true, false))
        {
            if (node is EnemyDirector found)
            {
                director = found;
                break;
            }
        }
        Node3D? player = Session!.LocalPlayer();
        if (director == null || player == null || director.Species.Length == 0)
        {
            GD.Print("[NetProbe] no enemy director to spawn with");
            return;
        }
        director.Bring(director.Species[0], player.GlobalPosition + new Vector3(5.0f, 0.5f, 0.0f));
        GD.Print("[NetProbe] brought an enemy");
    }

    private void Strike()
    {
        int struck = 0;
        if (Session!.Mobs == null)
        {
            return;
        }
        foreach (Node3D body in Session.Mobs.Puppets)
        {
            if (IsInstanceValid(body) && body is IMeleeTarget target && target.CanBeHit)
            {
                target.TakeHit(StrikeDamage, Vector3.Right, "sword");
                struck++;
            }
        }
        GD.Print($"[NetProbe] struck {struck} creatures");
    }

    private WorldItem? FirstSupply()
    {
        WorldItem? first = null;
        foreach (Node node in GetTree().GetNodesInGroup(IInteractable.Group))
        {
            if (node is WorldItem item && item.SlotKey.Length > 0 && !item.IsQueuedForDeletion()
                && (first == null || string.CompareOrdinal(item.SlotKey, first.SlotKey) < 0))
            {
                first = item;
            }
        }
        return first;
    }

    private WorldItem? Find(string key)
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
}
