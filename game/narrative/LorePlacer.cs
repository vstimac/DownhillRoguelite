using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// Leaves found texts along the descent: at most one per stage, chosen by the run's seed (the same
/// mountain has the same texts in the same places). Where a stage has side paths, a text usually
/// stands on one of them, halfway along, as a reason to take the detour; otherwise it stands beside
/// the main path. The first stage always has one; the texts of Act I come first. Texts are kept
/// loaded only near the carrier. A new run plans afresh.
/// </summary>
public partial class LorePlacer : Node3D
{
    /// <summary>Chance that a stage after the first has a text.</summary>
    [Export] public float StageChance { get; set; } = 0.65f;
    /// <summary>Chance that a stage with side paths has its text on one of them.</summary>
    [Export] public float BranchChance { get; set; } = 0.75f;
    /// <summary>Where along the stage (0 top, 1 bottom) a text on the main path stands.</summary>
    [Export] public Vector2 MainPathSpan { get; set; } = new(0.3f, 0.7f);
    /// <summary>Where along a side path (0 split, 1 join) its text stands.</summary>
    [Export] public Vector2 BranchSpan { get; set; } = new(0.35f, 0.65f);
    /// <summary>Distance beside the path's centre, in m.</summary>
    [Export] public float SideOffset { get; set; } = 3.2f;
    [Export] public float LoadDistance { get; set; } = 260.0f;
    [Export] public float GroundSink { get; set; } = 0.05f;
    /// <summary>A text read in an earlier run keeps this share of its chance.</summary>
    [Export] public float ReadWeight { get; set; } = 0.35f;

    public readonly record struct Placement(LoreEntry Entry, int Stage, int Path, Vector3 Position);

    public LoreLibrary Library => _library ??= LoreLibrary.Load();
    public IReadOnlyDictionary<int, LoreMark> Marks => _marks;

    private LoreLibrary? _library;
    private readonly Dictionary<int, Placement?> _plan = new();
    private readonly HashSet<string> _used = new();
    private readonly Dictionary<int, LoreMark> _marks = new();
    private int _planned = -1;
    private ulong _plannedSeed;
    private RunState? _run;
    private Vector3 _focus;
    private bool _hasFocus;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.RespawnRequested += OnRespawn;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.RespawnRequested -= OnRespawn;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _focus = snapshot.Position;
        _hasFocus = true;
    }

    private void OnRespawn()
    {
        Forget();
    }

    /// <summary>Drops the plan and every placed text (a new run, or a new mountain).</summary>
    public void Forget()
    {
        _plan.Clear();
        _used.Clear();
        _planned = -1;
        foreach (LoreMark mark in _marks.Values)
        {
            mark.QueueFree();
        }
        _marks.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        IRouteQuery? route = _run?.Route;
        if (!_hasFocus || route == null || _run == null)
        {
            return;
        }
        if (_planned >= 0 && _plannedSeed != _run.Seed)
        {
            Forget();
        }
        Stream(route);
    }

    /// <summary>The text planned for a stage (planning every stage above it first), or null for none.</summary>
    public Placement? PlanFor(IRouteQuery route, int stage)
    {
        if (_run == null || stage < 0)
        {
            return null;
        }
        if (_planned < 0)
        {
            _plannedSeed = _run.Seed;
        }
        while (_planned < stage)
        {
            _planned++;
            _plan[_planned] = Plan(route, _planned);
        }
        return _plan[stage];
    }

    private Placement? Plan(IRouteQuery route, int stage)
    {
        if (_run == null)
        {
            return null;
        }
        SplitMix64 random = new(SplitMix64.Mix(_run.Seed ^ 0x504C4143UL, (ulong)stage));
        if (stage > 0 && random.NextFloat() >= StageChance)
        {
            return null;
        }
        float top = route.StartZ;
        if (stage > 0)
        {
            top = route.Station(stage).Z;
        }
        float bottom = route.Station(stage + 1).Z;
        int paths = route.PathCount(stage);
        int path = 0;
        float z;
        if (paths > 1 && random.NextFloat() < BranchChance)
        {
            path = 1 + (int)(random.NextFloat() * (paths - 1));
            path = Mathf.Min(path, paths - 1);
            Vector2 zone = route.BranchZone(stage);
            z = Mathf.Lerp(zone.X, zone.Y, Mathf.Lerp(BranchSpan.X, BranchSpan.Y, random.NextFloat()));
        }
        else
        {
            z = Mathf.Lerp(top, bottom, Mathf.Lerp(MainPathSpan.X, MainPathSpan.Y, random.NextFloat()));
        }
        Vector3 centre = route.PathPoint(stage, path, z);
        float side = 1.0f;
        if (random.NextFloat() < 0.5f)
        {
            side = -1.0f;
        }
        float x = centre.X + side * SideOffset;
        Vector3 at = new(x, route.GroundHeight(x, z) - GroundSink, z);
        float depth = route.RoutePoint(route.StartZ).Y - at.Y;
        int circle = _run.Circles.CircleAt(depth);
        LoreEntry? entry = Library.Choose(_run.Seed, stage, circle, _used, _run.Lore.HasEverRead, ReadWeight);
        if (entry == null)
        {
            return null;
        }
        LoreLibrary.MarkUsed(entry, _used);
        return new Placement(entry, stage, path, at);
    }

    private void Stream(IRouteQuery route)
    {
        List<int> wanted = new();
        int first = Mathf.Max(0, route.StageAt(_focus.Z + LoadDistance));
        int last = route.StageAt(_focus.Z - LoadDistance);
        for (int stage = first; stage <= last; stage++)
        {
            Placement? placement = PlanFor(route, stage);
            if (placement == null)
            {
                continue;
            }
            Vector3 at = placement.Value.Position;
            if (Mathf.Abs(at.Z - _focus.Z) > LoadDistance)
            {
                continue;
            }
            wanted.Add(stage);
            if (_marks.ContainsKey(stage))
            {
                continue;
            }
            LoreMark mark = new();
            mark.Setup(placement.Value.Entry);
            AddChild(mark);
            mark.GlobalPosition = at;
            // Face the path, so the text is read from where people walk.
            mark.Rotation = new Vector3(0.0f, Mathf.Atan2(route.PathPoint(stage, placement.Value.Path, at.Z).X - at.X, 0.0f), 0.0f);
            _marks[stage] = mark;
        }
        List<int> drop = new();
        foreach (int stage in _marks.Keys)
        {
            if (!wanted.Contains(stage))
            {
                drop.Add(stage);
            }
        }
        foreach (int stage in drop)
        {
            _marks[stage].QueueFree();
            _marks.Remove(stage);
        }
    }
}
