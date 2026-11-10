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
/// (the blow goes to its owner). At the grab moment it notes the clock, so the harness can compare
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
    private int _sawPuppets;
    private double _clockAtGrab = -1.0;
    private bool _advanced;

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
        foreach (RemoteCarrier remote in session.Remotes.Values)
        {
            fewest = Mathf.Min(fewest, remote.States);
        }
        if (session.Remotes.Count == 0)
        {
            fewest = 0;
        }
        ulong seed = GetNodeOrNull<RunState>("/root/RunState")?.Seed ?? 0;
        GD.Print($"[NetProbe] role={session.Mode} name={session.LocalName} joined={session.Joined} seed={seed} session_seed={session.SessionSeed} "
            + $"peers={session.Peers.Count} remotes={session.Remotes.Count} min_states={fewest} target={_target} got={_got} "
            + $"taken={session.TakenSupplies.Count} granted={session.ClaimsGranted} refused={session.ClaimsRefused} fell={_fell} watched={_watched} back={_back} "
            + $"saw_fallen={_sawFallen} looks={Looks(session)} owned={session.Mobs?.OwnedCount ?? 0} saw_puppets={_sawPuppets} "
            + $"hits_sent={session.Mobs?.HitsSent ?? 0} hits_applied={session.Mobs?.HitsApplied ?? 0} loot_shared={session.Mobs?.LootShared ?? 0} "
            + $"loot_received={session.Mobs?.LootReceived ?? 0} clock_at_grab={_clockAtGrab:0.000} status=\"{session.Status}\"");
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
