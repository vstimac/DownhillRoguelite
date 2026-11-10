using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Test probe for co-op runs (`-- --net-probe=seconds`, added by NetSession): after that many
/// seconds it prints one "[NetProbe]" line with what this machine sees (role, seed, players,
/// remote carriers, states received, supplies taken) and quits. With `--net-grab-at=unix seconds` it
/// also reaches, at that moment by the computer's clock, for the same shared supply every probe picks (the first by key), so a run
/// with several probes reaching at once shows that only one player ever gets it. With
/// `--net-die-at=unix seconds` its carrier falls then: it should watch a teammate, the others should
/// see it fallen, and it should come back. The harness reads the lines.
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
            + $"saw_fallen={_sawFallen} status=\"{session.Status}\"");
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
