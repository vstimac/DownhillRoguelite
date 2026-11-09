using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Dresses the mountain with set pieces (environment/set_pieces/*.tres), chosen and placed by the
/// run's seed so the same mountain looks the same: a few wayside pieces per stage beside its paths
/// (ruins, wagons, banners, shrines), kept clear of every path's walking band and of the stations,
/// and now and then a landmark far off the paths (a windmill, a tower) to tell one stretch from
/// another. Pieces favour their circles. Pieces on ground too uneven for them are left out. Only
/// pieces near the carrier exist; landmarks load from further away. A new run plans afresh.
/// </summary>
public partial class SetPiecePlacer : Node3D
{
    public const string Folder = "res://environment/set_pieces";

    /// <summary>Wayside pieces tried per stage (inclusive range).</summary>
    [Export] public Vector2I WaysidePerStage { get; set; } = new(2, 4);
    [Export] public float LandmarkChance { get; set; } = 0.55f;
    /// <summary>A wayside piece's near edge keeps this far from the centre of every path, in m.</summary>
    [Export] public float PathClearance { get; set; } = 6.5f;
    /// <summary>...and its centre stands up to this much further out, in m.</summary>
    [Export] public float WaysideSpread { get; set; } = 6.0f;
    /// <summary>A landmark stands this far from the main path (range), in m.</summary>
    [Export] public Vector2 LandmarkDistance { get; set; } = new(45.0f, 85.0f);
    /// <summary>...and keeps at least this far from every path, in m.</summary>
    [Export] public float LandmarkClearance { get; set; } = 28.0f;
    /// <summary>No piece within this distance (along Z) of a station, in m.</summary>
    [Export] public float StationGap { get; set; } = 30.0f;
    /// <summary>Pieces keep this much ground between them, in m (on top of their radii).</summary>
    [Export] public float PieceGap { get; set; } = 4.0f;
    /// <summary>Most the ground may rise or fall across a piece's footprint, in m.</summary>
    [Export] public float MaxRise { get; set; } = 2.5f;
    [Export] public int TriesPerPiece { get; set; } = 5;
    [Export] public float LoadDistance { get; set; } = 320.0f;
    [Export] public float LandmarkLoadDistance { get; set; } = 650.0f;
    [Export] public float GroundSink { get; set; } = 0.08f;

    public readonly record struct Placement(SetPiece Piece, int Stage, Vector3 Centre, float Yaw);

    public IReadOnlyList<SetPiece> Pieces => LoadPieces();
    public IReadOnlyDictionary<int, SetPieceNode> Built => _built;

    private List<SetPiece>? _pieces;
    private readonly Dictionary<int, List<Placement>> _plan = new();
    private readonly Dictionary<int, SetPieceNode> _built = new();
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

    private List<SetPiece> LoadPieces()
    {
        if (_pieces != null)
        {
            return _pieces;
        }
        _pieces = new List<SetPiece>();
        if (!DirAccess.DirExistsAbsolute(Folder))
        {
            return _pieces;
        }
        foreach (string file in DirAccess.GetFilesAt(Folder))
        {
            string name = file.Replace(".remap", "");
            if (!name.EndsWith(".tres"))
            {
                continue;
            }
            if (ResourceLoader.Load(Folder + "/" + name) is SetPiece piece)
            {
                if (piece.Problem().Length == 0)
                {
                    _pieces.Add(piece);
                }
                else
                {
                    GD.PushWarning("SetPiecePlacer: " + piece.Problem());
                }
            }
        }
        _pieces.Sort(CompareIds);
        return _pieces;
    }

    private static int CompareIds(SetPiece a, SetPiece b)
    {
        return string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>Drops the plan and every built piece (a new run, or a new mountain).</summary>
    public void Forget()
    {
        _plan.Clear();
        _planned = -1;
        foreach (SetPieceNode node in _built.Values)
        {
            node.QueueFree();
        }
        _built.Clear();
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

    /// <summary>The pieces planned for a stage (planning every stage above it first).</summary>
    public IReadOnlyList<Placement> PlanFor(IRouteQuery route, int stage)
    {
        if (_run == null || stage < 0)
        {
            return System.Array.Empty<Placement>();
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

    private List<Placement> Plan(IRouteQuery route, int stage)
    {
        List<Placement> placed = new();
        if (_run == null || LoadPieces().Count == 0)
        {
            return placed;
        }
        SplitMix64 random = new(SplitMix64.Mix(_run.Seed ^ 0x53455450UL, (ulong)stage));
        float top = route.StartZ;
        if (stage > 0)
        {
            top = route.Station(stage).Z;
        }
        float bottom = route.Station(stage + 1).Z;
        int wayside = WaysidePerStage.X + (int)(random.NextFloat() * (WaysidePerStage.Y - WaysidePerStage.X + 1));
        wayside = Mathf.Min(wayside, WaysidePerStage.Y);
        for (int i = 0; i < wayside; i++)
        {
            TryPlace(route, stage, top, bottom, SetPieceKind.Wayside, random, placed);
        }
        if (random.NextFloat() < LandmarkChance)
        {
            TryPlace(route, stage, top, bottom, SetPieceKind.Landmark, random, placed);
        }
        return placed;
    }

    private void TryPlace(IRouteQuery route, int stage, float top, float bottom, SetPieceKind kind, SplitMix64 random, List<Placement> placed)
    {
        float spawnY = route.RoutePoint(route.StartZ).Y;
        for (int attempt = 0; attempt < TriesPerPiece; attempt++)
        {
            float z = Mathf.Lerp(top - StationGap, bottom + StationGap, random.NextFloat());
            int paths = route.PathCount(stage);
            int path = 0;
            if (kind == SetPieceKind.Wayside)
            {
                path = Mathf.Min((int)(random.NextFloat() * paths), paths - 1);
            }
            float side = 1.0f;
            if (random.NextFloat() < 0.5f)
            {
                side = -1.0f;
            }
            Vector3 along = route.PathPoint(stage, path, z);
            int circle = _run!.Circles.CircleAt(spawnY - along.Y);
            SetPiece? piece = Choose(kind, circle, random);
            if (piece == null)
            {
                return;
            }
            float distance;
            float clearance;
            if (kind == SetPieceKind.Wayside)
            {
                distance = PathClearance + piece.Radius + random.NextFloat() * WaysideSpread;
                clearance = PathClearance + piece.Radius;
            }
            else
            {
                distance = Mathf.Lerp(LandmarkDistance.X, LandmarkDistance.Y, random.NextFloat()) + piece.Radius;
                clearance = LandmarkClearance + piece.Radius;
            }
            float x = along.X + side * distance;
            Vector3 centre = new(x, route.GroundHeight(x, z), z);
            float yaw = random.NextFloat() * Mathf.Tau;
            if (!Fits(route, piece, centre, clearance, placed))
            {
                continue;
            }
            placed.Add(new Placement(piece, stage, centre, yaw));
            return;
        }
    }

    /// <summary>Clear of every path and station, of the other pieces, and on ground even enough.</summary>
    private bool Fits(IRouteQuery route, SetPiece piece, Vector3 centre, float clearance, List<Placement> placed)
    {
        route.NearestPath(centre, out float fromPath);
        if (fromPath < clearance)
        {
            return false;
        }
        int next = route.NextStationIndex(centre.Z + StationGap);
        if (Mathf.Abs(route.Station(next).Z - centre.Z) < StationGap)
        {
            return false;
        }
        foreach (Placement other in placed)
        {
            Vector3 offset = other.Centre - centre;
            if (new Vector2(offset.X, offset.Z).Length() < other.Piece.Radius + piece.Radius + PieceGap)
            {
                return false;
            }
        }
        float low = centre.Y;
        float high = centre.Y;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Tau / 8.0f;
            float x = centre.X + Mathf.Cos(angle) * piece.Radius;
            float z = centre.Z + Mathf.Sin(angle) * piece.Radius;
            float height = route.GroundHeight(x, z);
            low = Mathf.Min(low, height);
            high = Mathf.Max(high, height);
        }
        return high - low <= MaxRise * Mathf.Max(1.0f, piece.Radius / 3.0f);
    }

    private SetPiece? Choose(SetPieceKind kind, int circle, SplitMix64 random)
    {
        float total = 0.0f;
        foreach (SetPiece piece in LoadPieces())
        {
            total += WeightOf(piece, kind, circle);
        }
        if (total <= 0.0f)
        {
            return null;
        }
        float roll = random.NextFloat() * total;
        SetPiece? last = null;
        foreach (SetPiece piece in LoadPieces())
        {
            float weight = WeightOf(piece, kind, circle);
            if (weight <= 0.0f)
            {
                continue;
            }
            last = piece;
            roll -= weight;
            if (roll <= 0.0f)
            {
                return piece;
            }
        }
        return last;
    }

    private static float WeightOf(SetPiece piece, SetPieceKind kind, int circle)
    {
        if (piece.Kind != kind || circle < piece.FirstCircle)
        {
            return 0.0f;
        }
        if (piece.Favours(circle))
        {
            return piece.Weight * 3.0f;
        }
        return piece.Weight;
    }

    private void Stream(IRouteQuery route)
    {
        HashSet<int> wanted = new();
        int first = Mathf.Max(0, route.StageAt(_focus.Z + LandmarkLoadDistance));
        int last = route.StageAt(_focus.Z - LandmarkLoadDistance);
        for (int stage = first; stage <= last; stage++)
        {
            IReadOnlyList<Placement> placements = PlanFor(route, stage);
            for (int i = 0; i < placements.Count; i++)
            {
                Placement placement = placements[i];
                float reach = LoadDistance;
                if (placement.Piece.Kind == SetPieceKind.Landmark)
                {
                    reach = LandmarkLoadDistance;
                }
                if (Mathf.Abs(placement.Centre.Z - _focus.Z) > reach)
                {
                    continue;
                }
                int key = stage * 16 + i;
                wanted.Add(key);
                if (_built.ContainsKey(key))
                {
                    continue;
                }
                SetPieceNode node = new();
                AddChild(node);
                node.Build(placement.Piece, placement.Centre, placement.Yaw, route, GroundSink);
                _built[key] = node;
            }
        }
        List<int> drop = new();
        foreach (int key in _built.Keys)
        {
            if (!wanted.Contains(key))
            {
                drop.Add(key);
            }
        }
        foreach (int key in drop)
        {
            _built[key].QueueFree();
            _built.Remove(key);
        }
    }
}
