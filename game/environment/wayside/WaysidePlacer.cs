using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Points of interest between stations (the purpose patch): one or two per stage, chosen and placed
/// by the mountain's seed (a stream of their own, so nothing else moves), beside the main path but
/// clear of it and of every other path, away from stations, in view from where people walk. Each is
/// a WaysideSite holding supplies (a spring's water, an abandoned pack, a carrier's grave, a
/// forager's cache, a mender's bundle). Sites near the carrier are built and the rest freed; what was
/// taken is remembered for the run. The closest unused one within NearDistance is published as
/// GameEvents.WaysideNear (how far, which side), so the goals can say "Off the path: a cold spring,
/// 35 m left". A new run clears everything.
/// </summary>
public partial class WaysidePlacer : Node3D
{
    [Export] public WaysideDefinition[] Definitions { get; set; } = System.Array.Empty<WaysideDefinition>();
    /// <summary>How many per stage (inclusive range).</summary>
    [Export] public Vector2I PerStage { get; set; } = new(1, 2);
    /// <summary>How far beside the main path's centre, in m.</summary>
    [Export] public Vector2 SideDistance { get; set; } = new(9.0f, 22.0f);
    /// <summary>Where along the stage (0 top, 1 bottom).</summary>
    [Export] public Vector2 StageSpan { get; set; } = new(0.12f, 0.88f);
    /// <summary>At least this far from every path's centre, in m.</summary>
    [Export] public float PathClearance { get; set; } = 6.5f;
    /// <summary>At least this far from a station, along Z, in m.</summary>
    [Export] public float StationGap { get; set; } = 35.0f;
    /// <summary>Two sites of a stage are at least this far apart along Z, in m.</summary>
    [Export] public float SiteGap { get; set; } = 60.0f;
    [Export] public int TriesPerSite { get; set; } = 8;
    [Export] public float LoadDistance { get; set; } = 260.0f;
    /// <summary>An unused site this close is worth mentioning, in m.</summary>
    [Export] public float NearDistance { get; set; } = 70.0f;
    [Export] public float NearInterval { get; set; } = 0.25f;
    [Export] public float GroundSink { get; set; } = 0.05f;

    public readonly record struct Placement(WaysideDefinition Definition, int Stage, int Index, Vector3 Position)
    {
        public string Key => Stage + ":" + Index;
    }

    public IReadOnlyDictionary<string, WaysideSite> Sites => _sites;
    public IReadOnlyList<WaysideDefinition> ValidDefinitions => _valid;

    private readonly List<WaysideDefinition> _valid = new();
    private readonly Dictionary<int, List<Placement>> _plan = new();
    private readonly Dictionary<string, WaysideSite> _sites = new();
    private readonly Dictionary<string, List<string>> _left = new();
    private readonly HashSet<string> _used = new();
    private RunState? _run;
    private ulong _plannedSeed;
    private bool _planned;
    private Vector3 _focus;
    private bool _hasFocus;
    private float _nearTimer;
    private string _nearKey = "";
    private int _nearMetres = -1;
    private string _nearSide = "";

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        Reload();
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.RespawnRequested += OnRespawn;
        GameEvents.WaysideUsed += OnUsed;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.RespawnRequested -= OnRespawn;
        GameEvents.WaysideUsed -= OnUsed;
        if (GameEvents.NearestWayside != null)
        {
            GameEvents.PublishWaysideNear(null);
        }
    }

    /// <summary>Validates the definitions: broken or duplicate ones are skipped with a warning.</summary>
    public void Reload()
    {
        _valid.Clear();
        HashSet<string> ids = new();
        foreach (WaysideDefinition? definition in Definitions)
        {
            if (definition == null || !definition.IsValid)
            {
                GD.PushWarning("WaysidePlacer: skipped a point of interest without an id, title or items.");
                continue;
            }
            if (!ids.Add(definition.Id))
            {
                GD.PushWarning("WaysidePlacer: skipped a duplicate point of interest " + definition.Id + ".");
                continue;
            }
            _valid.Add(definition);
        }
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _focus = snapshot.Position;
        _hasFocus = true;
    }

    private void OnRespawn() => Forget();

    private void OnUsed(WaysideNote note)
    {
        _used.Add(note.Id);
        _left.Remove(note.Id);
    }

    /// <summary>Drops the plan, every site and what was taken (a new run).</summary>
    public void Forget()
    {
        _plan.Clear();
        _used.Clear();
        _left.Clear();
        _planned = false;
        foreach (WaysideSite site in _sites.Values)
        {
            site.QueueFree();
        }
        _sites.Clear();
        _nearKey = "";
        _nearMetres = -1;
        if (GameEvents.NearestWayside != null)
        {
            GameEvents.PublishWaysideNear(null);
        }
    }

    public bool IsUsed(string key) => _used.Contains(key);

    public override void _PhysicsProcess(double delta)
    {
        IRouteQuery? route = _run?.Route;
        if (!_hasFocus || route == null || _run == null)
        {
            return;
        }
        if (_planned && _plannedSeed != _run.Seed)
        {
            Forget();
        }
        Stream(route);
        _nearTimer -= (float)delta;
        if (_nearTimer <= 0.0f)
        {
            _nearTimer = NearInterval;
            UpdateNearest(route);
        }
    }

    /// <summary>The sites planned for a stage (the same for the same mountain).</summary>
    public IReadOnlyList<Placement> PlanFor(IRouteQuery route, int stage)
    {
        if (_run == null || stage < 0)
        {
            return System.Array.Empty<Placement>();
        }
        if (!_planned)
        {
            _planned = true;
            _plannedSeed = _run.Seed;
        }
        if (!_plan.TryGetValue(stage, out List<Placement>? placements))
        {
            placements = Plan(route, stage);
            _plan[stage] = placements;
        }
        return placements;
    }

    private List<Placement> Plan(IRouteQuery route, int stage)
    {
        List<Placement> placements = new();
        if (_run == null || _valid.Count == 0)
        {
            return placements;
        }
        SplitMix64 random = new(SplitMix64.Mix(_run.Seed ^ 0x57A1_51DEUL, (ulong)stage));
        float top = route.StartZ;
        if (stage > 0)
        {
            top = route.Station(stage).Z;
        }
        float bottom = route.Station(stage + 1).Z;
        int count = PerStage.X + random.NextInt(Mathf.Max(PerStage.Y - PerStage.X, 0) + 1);
        HashSet<string> chosen = new();
        for (int index = 0; index < count; index++)
        {
            for (int attempt = 0; attempt < TriesPerSite; attempt++)
            {
                float z = Mathf.Lerp(top, bottom, Mathf.Lerp(StageSpan.X, StageSpan.Y, random.NextFloat()));
                float side = 1.0f;
                if (random.NextFloat() < 0.5f)
                {
                    side = -1.0f;
                }
                float x = route.RoutePoint(z).X + side * random.Range(SideDistance.X, SideDistance.Y);
                if (!Clear(route, stage, x, z, top, bottom, placements))
                {
                    continue;
                }
                float depth = route.RoutePoint(route.StartZ).Y - route.GroundHeight(x, z);
                WaysideDefinition? definition = Choose(random, _run.Circles.CircleAt(depth), chosen);
                if (definition == null)
                {
                    break;
                }
                chosen.Add(definition.Id);
                placements.Add(new Placement(definition, stage, index, new Vector3(x, route.GroundHeight(x, z) - GroundSink, z)));
                break;
            }
        }
        return placements;
    }

    private bool Clear(IRouteQuery route, int stage, float x, float z, float top, float bottom, List<Placement> placed)
    {
        if (z > top - StationGap || z < bottom + StationGap)
        {
            return false;
        }
        for (int path = 0; path < route.PathCount(stage); path++)
        {
            if (Mathf.Abs(route.PathPoint(stage, path, z).X - x) < PathClearance)
            {
                return false;
            }
        }
        foreach (Placement other in placed)
        {
            if (Mathf.Abs(other.Position.Z - z) < SiteGap)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A definition allowed at this circle, by weight, preferring ones the stage doesn't have yet.</summary>
    private WaysideDefinition? Choose(SplitMix64 random, int circle, HashSet<string> chosen)
    {
        List<WaysideDefinition> pool = new();
        foreach (WaysideDefinition definition in _valid)
        {
            if (definition.MinCircle <= circle && !chosen.Contains(definition.Id))
            {
                pool.Add(definition);
            }
        }
        if (pool.Count == 0)
        {
            return null;
        }
        float total = 0.0f;
        foreach (WaysideDefinition definition in pool)
        {
            total += Mathf.Max(definition.Weight, 0.0f);
        }
        float pick = random.NextFloat() * total;
        foreach (WaysideDefinition definition in pool)
        {
            pick -= Mathf.Max(definition.Weight, 0.0f);
            if (pick <= 0.0f)
            {
                return definition;
            }
        }
        return pool[^1];
    }

    private void Stream(IRouteQuery route)
    {
        HashSet<string> wanted = new();
        int first = Mathf.Max(0, route.StageAt(_focus.Z + LoadDistance));
        int last = route.StageAt(_focus.Z - LoadDistance);
        for (int stage = first; stage <= last; stage++)
        {
            foreach (Placement placement in PlanFor(route, stage))
            {
                if (Mathf.Abs(placement.Position.Z - _focus.Z) > LoadDistance)
                {
                    continue;
                }
                string key = placement.Key;
                wanted.Add(key);
                if (_sites.ContainsKey(key))
                {
                    continue;
                }
                if (!_left.TryGetValue(key, out List<string>? left))
                {
                    left = new List<string>(placement.Definition.ItemPaths);
                    _left[key] = left;
                }
                WaysideSite site = new();
                site.Setup(placement.Definition, key, left, _used.Contains(key));
                AddChild(site);
                site.GlobalPosition = placement.Position;
                // Turn its face (+Z) toward the path.
                Vector3 path = route.RoutePoint(placement.Position.Z);
                site.Rotation = new Vector3(0.0f, Mathf.Atan2(path.X - placement.Position.X, 0.0f), 0.0f);
                _sites[key] = site;
            }
        }
        List<string> drop = new();
        foreach (KeyValuePair<string, WaysideSite> pair in _sites)
        {
            if (!wanted.Contains(pair.Key))
            {
                drop.Add(pair.Key);
            }
        }
        foreach (string key in drop)
        {
            // Remember what's still there for when it streams back in.
            _left[key] = new List<string>(_sites[key].Left);
            _sites[key].QueueFree();
            _sites.Remove(key);
        }
    }

    /// <summary>Finds the closest unused site within NearDistance and publishes it when it changes.</summary>
    public void UpdateNearest(IRouteQuery route)
    {
        Placement? best = null;
        float bestDistance = NearDistance;
        int stage = route.StageAt(_focus.Z);
        for (int s = Mathf.Max(0, stage - 1); s <= stage + 1; s++)
        {
            foreach (Placement placement in PlanFor(route, s))
            {
                if (_used.Contains(placement.Key))
                {
                    continue;
                }
                Vector3 offset = placement.Position - _focus;
                float distance = new Vector2(offset.X, offset.Z).Length();
                if (distance < bestDistance)
                {
                    best = placement;
                    bestDistance = distance;
                }
            }
        }
        if (best == null)
        {
            if (_nearKey.Length > 0)
            {
                _nearKey = "";
                _nearMetres = -1;
                GameEvents.PublishWaysideNear(null);
            }
            return;
        }
        Placement near = best.Value;
        string side = SideOf(near.Position - _focus);
        // Publish in 5 m steps, so the goals don't change every frame.
        int metres = Mathf.RoundToInt(bestDistance / 5.0f) * 5;
        if (near.Key == _nearKey && metres == _nearMetres && side == _nearSide)
        {
            return;
        }
        _nearKey = near.Key;
        _nearMetres = metres;
        _nearSide = side;
        string text = near.Definition.Description;
        GameEvents.PublishWaysideNear(new WaysideNote(near.Key, near.Definition.Title, text, near.Position, Mathf.Max(metres, 1), side));
    }

    /// <summary>Which way something is from the camera's view: ahead, behind, to the left or right.</summary>
    private string SideOf(Vector3 offset)
    {
        Vector3 forward = Vector3.Forward;
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            forward = -camera.GlobalBasis.Z;
        }
        forward.Y = 0.0f;
        offset.Y = 0.0f;
        if (forward.IsZeroApprox() || offset.IsZeroApprox())
        {
            return "here";
        }
        forward = forward.Normalized();
        Vector3 right = forward.Cross(Vector3.Up);
        float along = offset.Dot(forward);
        float across = offset.Dot(right);
        if (Mathf.Abs(across) < Mathf.Abs(along) * 0.6f)
        {
            if (along > 0.0f)
            {
                return "ahead";
            }
            return "behind";
        }
        if (across > 0.0f)
        {
            return "to your right";
        }
        return "to your left";
    }
}
