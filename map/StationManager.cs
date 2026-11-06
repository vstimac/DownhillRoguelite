using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// Places stations on the route's station terraces and stone cairns along the route between them,
/// keeping only those near the carrier. Tracks whether the carrier is inside a station's shelter
/// (GameEvents.ShelterChanged) and which stations it has reached this run (StationReached), and
/// shows the latest route estimate on the nearest cairn ahead. A respawn starts a new run.
/// </summary>
public partial class StationManager : Node3D
{
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

    public int ShelterStation { get; private set; }
    public IReadOnlyDictionary<int, StationSite> Stations => _stations;
    public IReadOnlyDictionary<int, WaypointCairn> Cairns => _cairns;
    public WaypointCairn? LabelledCairn { get; private set; }

    private readonly Dictionary<int, StationSite> _stations = new();
    private readonly Dictionary<int, WaypointCairn> _cairns = new();
    private readonly HashSet<int> _reached = new();
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
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.RespawnRequested -= OnRespawnRequested;
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
    }

    public override void _PhysicsProcess(double delta)
    {
        IRouteQuery? route = _run?.Route;
        if (!_hasFocus || route == null)
        {
            return;
        }

        StreamStations(route);
        StreamCairns(route);
        UpdateShelter();
        UpdateCairnLabel();
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
            wanted.Add(k);
            if (!_cairns.ContainsKey(k))
            {
                Vector3 point = route.RoutePoint(z);
                WaypointCairn cairn = new() { Name = $"Cairn{-k}" };
                AddChild(cairn);
                Vector3 side = new(point.X + CairnSideOffset, 0.0f, z);
                side.Y = route.RoutePoint(z).Y;
                cairn.GlobalPosition = side;
                _cairns[k] = cairn;
            }
        }
        RemoveExcept(_cairns, wanted);
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
