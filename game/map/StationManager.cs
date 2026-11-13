using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// Places stations on the route's station terraces and stone cairns along the route between them,
/// keeping only those near the carrier. Tracks whether the carrier is inside a station's shelter
/// (GameEvents.ShelterChanged) and which stations it has reached this run (StationReached), and
/// shows the latest route estimate on the nearest cairn ahead. A respawn starts a new run.
///
/// Routes (M10): cairns stand along every path of a stage, a lookout sign at the top of each stage
/// lists its paths (RouteOptionsUpdated), and once the carrier is well into a stage's branch zone
/// on one path it publishes RouteChosen.
/// </summary>
public partial class StationManager : Node3D
{
    /// <summary>How far signs, cairns and the summit stone are set into the ground, so none floats on a slope.</summary>
    [Export] public float GroundSink { get; set; } = 0.08f;

    /// <summary>Within this horizontal distance of a station's centre the carrier is sheltered, in m.</summary>
    [Export] public float ShelterRadius { get; set; } = 9.0f;
    /// <summary>Stations this close along Z are kept loaded, in m.</summary>
    [Export] public float StationLoadDistance { get; set; } = 500.0f;
    /// <summary>Distance between cairns along Z, in m.</summary>
    [Export] public float CairnSpacing { get; set; } = 120.0f;
    [Export] public float CairnLoadDistance { get; set; } = 300.0f;
    /// <summary>No cairn this close to a station along Z, in m.</summary>
    [Export] public float CairnStationGap { get; set; } = 40.0f;
    /// <summary>The cairn ahead shows the estimate from this far away, in m.</summary>
    [Export] public float CairnLabelDistance { get; set; } = 45.0f;
    /// <summary>Cairns stand this far to the side of the route's centre, in m.</summary>
    [Export] public float CairnSideOffset { get; set; } = 2.5f;
    /// <summary>The lookout sign stands this far below the top of a stage (along Z), in m.</summary>
    [Export] public float SignDistance { get; set; } = 16.0f;
    /// <summary>The lookout sign stands this far to the side of the path, opposite the cairns, in m.</summary>
    [Export] public float SignSideOffset { get; set; } = 3.5f;
    /// <summary>The carrier has chosen a path once this far below the split (along Z), in m.</summary>
    [Export] public float CommitDistance { get; set; } = 60.0f;
    /// <summary>...and within this distance of that path's centre, in m.</summary>
    [Export] public float CommitWidth { get; set; } = 10.0f;

    /// <summary>A path this far to the side at mid-stage is signed "far left" / "far right", in m.</summary>
    [Export] public float FarPathOffset { get; set; } = 105.0f;
    /// <summary>The small per-path signposts (left of each path) as well as the junction signpost; off: the junction's arms say it all.</summary>
    [Export] public bool ShowPathMarkers { get; set; }
    /// <summary>Each path's marker stands this far below the split (along Z), in m.</summary>
    [Export] public float MarkerDistance { get; set; } = 30.0f;
    /// <summary>...this far to the side of the path's centre (negative: left, facing downhill), in m.</summary>
    [Export] public float MarkerSideOffset { get; set; } = -2.8f;
    /// <summary>The junction signpost stands this far above the split (along Z), in m.</summary>
    [Export] public float JunctionAbove { get; set; } = 3.0f;
    /// <summary>...this far to the side of the main path (negative: left, facing downhill), in m.</summary>
    [Export] public float JunctionSideOffset { get; set; } = -2.6f;
    /// <summary>Each arm points at its path where it has moved this far from the main path, in m...</summary>
    [Export] public float JunctionSeparation { get; set; } = 12.0f;
    /// <summary>...or this far below the split if it hasn't yet (and the safe arm points this far down the main path), in m.</summary>
    [Export] public float JunctionAimDistance { get; set; } = 90.0f;

    /// <summary>Step for measuring a path's length, in m along Z.</summary>
    [Export] public float MeasureStep { get; set; } = 6.0f;

    /// <summary>The last line on the summit stone: what to do first.</summary>
    [Export] public string SummitGoal { get; set; } = "Go downhill: reach the fire at Station 1 before dark.";

    public int ShelterStation { get; private set; }
    public IReadOnlyDictionary<int, StationSite> Stations => _stations;
    public IReadOnlyDictionary<int, WaypointCairn> Cairns => _cairns;
    public WaypointCairn? LabelledCairn { get; private set; }
    public IReadOnlyDictionary<int, RouteSign> Signs => _signs;
    public IReadOnlyDictionary<int, PathMarker> Markers => _markers;
    public IReadOnlyDictionary<int, JunctionSignpost> Junctions => _junctions;

    private readonly Dictionary<int, StationSite> _stations = new();
    private readonly Dictionary<int, WaypointCairn> _cairns = new();
    private readonly HashSet<int> _reached = new();
    private readonly Dictionary<int, RouteSign> _signs = new();
    private readonly Dictionary<int, PathMarker> _markers = new();
    private readonly Dictionary<int, JunctionSignpost> _junctions = new();
    private int _chosenStage = -1;
    private Node3D? _summit;
    private Label3D? _summitLabel;
    private Vector3 _focus;
    private bool _hasFocus;
    private RunState? _run;
    private WorldClock? _clock;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.RouteOptionsUpdated += OnRouteOptions;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.RouteOptionsUpdated -= OnRouteOptions;
        if (GameEvents.InShelter)
        {
            GameEvents.PublishShelter(false, 0);
        }
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _focus = snapshot.Position;
        _hasFocus = true;
    }

    private void OnRespawnRequested()
    {
        _reached.Clear();
        _chosenStage = -1;
    }

    private void OnRouteOptions(int stage, RouteEstimate[] options)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null || !_signs.TryGetValue(stage, out RouteSign? sign))
        {
            return;
        }
        string[] directions = new string[options.Length];
        Vector2 zone = route.BranchZone(stage);
        float middle = (zone.X + zone.Y) * 0.5f;
        float main = route.RoutePoint(middle).X;
        for (int path = 0; path < options.Length; path++)
        {
            float offset = route.PathPoint(stage, path, middle).X - main;
            directions[path] = "ahead";
            // Facing downhill (-Z), +X is on the right; a second path on the same side is the far one.
            if (offset > 5.0f)
            {
                directions[path] = "right";
                if (offset > FarPathOffset)
                {
                    directions[path] = "far right";
                }
            }
            else if (offset < -5.0f)
            {
                directions[path] = "left";
                if (offset < -FarPathOffset)
                {
                    directions[path] = "far left";
                }
            }
        }
        sign.ShowOptions(options, directions, _clock);
    }

    public override void _PhysicsProcess(double delta)
    {
        IRouteQuery? route = _run?.Route;
        if (!_hasFocus || route == null)
        {
            return;
        }

        PlaceSummit(route);
        StreamStations(route);
        StreamCairns(route);
        StreamSigns(route);
        if (ShowPathMarkers)
        {
            StreamMarkers(route);
        }
        StreamJunctions(route);
        UpdateShelter();
        UpdateCairnLabel();
        UpdateChoice(route);
    }

    private void StreamStations(IRouteQuery route)
    {
        int first = route.NextStationIndex(_focus.Z + StationLoadDistance);
        List<int> wanted = new();
        for (int index = first; ; index++)
        {
            Vector3 centre = route.Station(index);
            if (centre.Z < _focus.Z - StationLoadDistance)
            {
                break;
            }
            wanted.Add(index);
            if (!_stations.ContainsKey(index))
            {
                StationSite site = new();
                site.Build(index);
                AddChild(site);
                site.GlobalPosition = centre;
                _stations[index] = site;
            }
        }
        RemoveExcept(_stations, wanted);
    }

    private void StreamCairns(IRouteQuery route)
    {
        int nearest = Mathf.RoundToInt(_focus.Z / CairnSpacing);
        int reach = Mathf.CeilToInt(CairnLoadDistance / CairnSpacing);
        List<int> wanted = new();
        for (int k = nearest - reach; k <= nearest + reach; k++)
        {
            float z = k * CairnSpacing;
            if (z >= route.StartZ || NearStation(route, z))
            {
                continue;
            }
            int stage = route.StageAt(z);
            Vector2 zone = route.BranchZone(stage);
            int paths = 1;
            if (z < zone.X && z > zone.Y)
            {
                paths = route.PathCount(stage);
            }
            for (int path = 0; path < paths; path++)
            {
                // Keys: path in the low bits (up to eight paths a stage).
                int key = k * 8 + path;
                wanted.Add(key);
                if (_cairns.ContainsKey(key))
                {
                    continue;
                }
                Vector3 point = route.PathPoint(stage, path, z);
                WaypointCairn cairn = new() { Name = $"Cairn{-k}_{path}" };
                AddChild(cairn);
                float x = point.X + CairnSideOffset;
                // On the ground where it stands (beside the path), set a little into it.
                cairn.GlobalPosition = new Vector3(x, route.GroundHeight(x, z) - GroundSink, z);
                _cairns[key] = cairn;
            }
        }
        RemoveExcept(_cairns, wanted);
    }

    /// <summary>A stone on the summit naming the run and the mountain (its seed, to replay it).</summary>
    private void PlaceSummit(IRouteQuery route)
    {
        if (_summit == null)
        {
            _summit = new Node3D { Name = "SummitStone" };
            AddChild(_summit);
            _summit.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.9f, 1.4f, 0.5f) },
                Position = new Vector3(0.0f, 0.6f, 0.0f),
                RotationDegrees = new Vector3(0.0f, 15.0f, 4.0f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.44f, 0.42f), Roughness = 1.0f },
            });
            _summitLabel = new Label3D
            {
                FontSize = 30,
                PixelSize = 0.005f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 8,
                Position = new Vector3(0.0f, 1.9f, 0.0f),
            };
            _summit.AddChild(WorldLabel.Readable(_summitLabel));
        }
        Vector3 top = route.RoutePoint(route.StartZ + 3.0f);
        Vector3 at = new(top.X + 3.0f, route.GroundHeight(top.X + 3.0f, top.Z) - GroundSink, top.Z);
        if (!_summit.GlobalPosition.IsEqualApprox(at))
        {
            _summit.GlobalPosition = at;
        }
        if (_summitLabel != null && _run != null)
        {
            string kit = "nothing yet";
            if (_run.StartingKit.Count > 0)
            {
                kit = string.Join(", ", _run.StartingKit);
            }
            // The run number and the mountain's seed are in the journal and the run summary, not on the sign.
            _summitLabel.Text = $"SUMMIT\n{SummitGoal}\nIn your pack: {kit}";
        }
    }

    /// <summary>A lookout sign at the top of every stage with a choice of paths, near the carrier.</summary>
    private void StreamSigns(IRouteQuery route)
    {
        List<int> wanted = new();
        int first = Mathf.Max(0, route.StageAt(_focus.Z + StationLoadDistance));
        for (int stage = first; ; stage++)
        {
            float top = route.StartZ;
            if (stage > 0)
            {
                top = route.Station(stage).Z;
            }
            if (top < _focus.Z - StationLoadDistance)
            {
                break;
            }
            if (route.PathCount(stage) < 2)
            {
                continue;
            }
            wanted.Add(stage);
            if (_signs.ContainsKey(stage))
            {
                continue;
            }
            float z = top - SignDistance;
            Vector3 point = route.RoutePoint(z);
            RouteSign sign = new() { Name = $"RouteSign{stage}", Stage = stage };
            AddChild(sign);
            float signX = point.X - SignSideOffset;
            sign.GlobalPosition = new Vector3(signX, route.GroundHeight(signX, z) - GroundSink, z);
            _signs[stage] = sign;
        }
        RemoveExcept(_signs, wanted);
    }

    /// <summary>A marker a little way down every path of each stage with a choice of paths, near the carrier.</summary>
    private void StreamMarkers(IRouteQuery route)
    {
        List<int> wanted = new();
        int first = Mathf.Max(0, route.StageAt(_focus.Z + StationLoadDistance));
        int last = route.StageAt(_focus.Z - StationLoadDistance);
        for (int stage = first; stage <= last; stage++)
        {
            int paths = route.PathCount(stage);
            if (paths < 2)
            {
                continue;
            }
            Vector2 zone = route.BranchZone(stage);
            float z = zone.X - MarkerDistance;
            if (Mathf.Abs(z - _focus.Z) > CairnLoadDistance)
            {
                continue;
            }
            for (int path = 0; path < paths; path++)
            {
                int key = stage * 8 + path;
                wanted.Add(key);
                if (_markers.ContainsKey(key))
                {
                    continue;
                }
                Vector3 point = route.PathPoint(stage, path, z);
                PathMarker marker = new() { Name = "PathMarker" + stage + "_" + path };
                AddChild(marker);
                float x = point.X + MarkerSideOffset;
                marker.GlobalPosition = new Vector3(x, route.GroundHeight(x, z) - GroundSink, z);
                float drop = route.PathPoint(stage, path, zone.X).Y - route.PathPoint(stage, path, zone.Y).Y;
                marker.Show(route.PathKind(stage, path), PathLength(route, stage, path, zone.X, zone.Y), drop, true);
                _markers[key] = marker;
            }
        }
        RemoveExcept(_markers, wanted);
    }

    /// <summary>A signpost where each stage's paths split, near the carrier, one arm aimed down each path.</summary>
    private void StreamJunctions(IRouteQuery route)
    {
        List<int> wanted = new();
        int first = Mathf.Max(0, route.StageAt(_focus.Z + StationLoadDistance));
        int last = route.StageAt(_focus.Z - StationLoadDistance);
        for (int stage = first; stage <= last; stage++)
        {
            int paths = route.PathCount(stage);
            if (paths < 2)
            {
                continue;
            }
            Vector2 zone = route.BranchZone(stage);
            float z = zone.X + JunctionAbove;
            if (Mathf.Abs(z - _focus.Z) > CairnLoadDistance)
            {
                continue;
            }
            wanted.Add(stage);
            if (_junctions.ContainsKey(stage))
            {
                continue;
            }
            Vector3 split = route.RoutePoint(z);
            float x = split.X + JunctionSideOffset;
            Vector3 post = new(x, route.GroundHeight(x, z) - GroundSink, z);
            RouteKind[] kinds = new RouteKind[paths];
            Vector2[] directions = new Vector2[paths];
            for (int path = 0; path < paths; path++)
            {
                kinds[path] = route.PathKind(stage, path);
                Vector3 target = AimPoint(route, stage, path, zone.X);
                directions[path] = new Vector2(target.X - post.X, target.Z - post.Z);
            }
            JunctionSignpost sign = new() { Name = "JunctionSignpost" + stage, Stage = stage };
            AddChild(sign);
            sign.GlobalPosition = post;
            sign.Build(kinds, directions);
            _junctions[stage] = sign;
        }
        RemoveExcept(_junctions, wanted);
    }

    /// <summary>
    /// Where a junction arm points for a path: the first point below the split where the path has
    /// clearly left the main path, so each arm shows the way into its own path.
    /// </summary>
    private Vector3 AimPoint(IRouteQuery route, int stage, int path, float splitZ)
    {
        float step = Mathf.Max(MeasureStep, 0.5f);
        if (path > 0)
        {
            for (float down = step; down <= JunctionAimDistance; down += step)
            {
                float z = splitZ - down;
                Vector3 point = route.PathPoint(stage, path, z);
                if (Mathf.Abs(point.X - route.RoutePoint(z).X) >= JunctionSeparation)
                {
                    return point;
                }
            }
        }
        return route.PathPoint(stage, path, splitZ - JunctionAimDistance);
    }

    /// <summary>A path's length on the ground between two z, in m (sampled every MeasureStep).</summary>
    public float PathLength(IRouteQuery route, int stage, int path, float fromZ, float toZ)
    {
        float step = Mathf.Max(MeasureStep, 0.5f);
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(fromZ - toZ) / step));
        float length = 0.0f;
        Vector3 previous = route.PathPoint(stage, path, fromZ);
        for (int i = 1; i <= count; i++)
        {
            Vector3 point = route.PathPoint(stage, path, Mathf.Lerp(fromZ, toZ, (float)i / count));
            length += point.DistanceTo(previous);
            previous = point;
        }
        return length;
    }

    /// <summary>Once the carrier is well past a stage's split, the path it is on is its choice.</summary>
    private void UpdateChoice(IRouteQuery route)
    {
        int stage = route.StageAt(_focus.Z);
        if (stage == _chosenStage || route.PathCount(stage) < 2)
        {
            return;
        }
        Vector2 zone = route.BranchZone(stage);
        if (_focus.Z > zone.X - CommitDistance || _focus.Z < zone.Y)
        {
            return;
        }
        int path = route.NearestPath(_focus, out float distance);
        if (distance > CommitWidth)
        {
            return;
        }
        _chosenStage = stage;
        GameEvents.PublishRouteChosen(stage, route.PathKind(stage, path));
    }

    private bool NearStation(IRouteQuery route, float z)
    {
        int next = route.NextStationIndex(z + CairnStationGap);
        return Mathf.Abs(route.Station(next).Z - z) < CairnStationGap;
    }

    private void UpdateShelter()
    {
        int inside = 0;
        foreach ((int index, StationSite site) in _stations)
        {
            Vector3 offset = site.GlobalPosition - _focus;
            if (new Vector2(offset.X, offset.Z).Length() <= ShelterRadius)
            {
                inside = index;
            }
        }

        if (inside != ShelterStation)
        {
            ShelterStation = inside;
            GameEvents.PublishShelter(inside != 0, inside);
            if (inside != 0 && _reached.Add(inside))
            {
                GameEvents.PublishStationReached(inside);
            }
        }
    }

    private void UpdateCairnLabel()
    {
        WaypointCairn? best = null;
        float bestDistance = CairnLabelDistance;
        foreach (WaypointCairn cairn in _cairns.Values)
        {
            Vector3 offset = cairn.GlobalPosition - _focus;
            float distance = new Vector2(offset.X, offset.Z).Length();
            // Ahead means downhill of the carrier, or right beside it.
            if (offset.Z < 2.0f && distance < bestDistance)
            {
                best = cairn;
                bestDistance = distance;
            }
        }

        if (best != LabelledCairn)
        {
            LabelledCairn?.HideEstimate();
            LabelledCairn = best;
        }
        LabelledCairn?.ShowEstimate(GameEvents.LatestEstimate, _clock);
    }

    private static void RemoveExcept<T>(Dictionary<int, T> loaded, List<int> wanted) where T : Node
    {
        List<int> drop = new();
        foreach (int key in loaded.Keys)
        {
            if (!wanted.Contains(key))
            {
                drop.Add(key);
            }
        }
        foreach (int key in drop)
        {
            loaded[key].QueueFree();
            loaded.Remove(key);
        }
    }
}
