using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M10: every stage offers a choice of paths, the same for the same seed;
/// branches leave below a station and rejoin above the next, swinging well away in between; every
/// path is walkable; the hazardous path is shorter and loose underfoot, the supply-rich path longer
/// with far more supplies; at the top of a stage the lookout sign lists every path's estimate;
/// walking onto a branch counts as choosing it and the cairn estimate follows that path.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RouteTests.tscn
/// </summary>
public partial class RouteTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public StationManager? Stations { get; set; }
    [Export] public SupplySpawner? Supplies { get; set; }
    [Export] public int StagesChecked { get; set; } = 30;
    /// <summary>Steepest a path may get along its length, measured over AlongStep, in degrees.</summary>
    [Export] public float MaxAlongSlope { get; set; } = 32.0f;
    [Export] public float AlongStep { get; set; } = 5.0f;
    /// <summary>Steepest tilt across a path's band, in degrees.</summary>
    [Export] public float MaxCrossSlope { get; set; } = 22.0f;

    private readonly List<string> _failures = new();
    private RunState? _run;
    private int _optionsStage = -1;
    private RouteEstimate[] _options = System.Array.Empty<RouteEstimate>();
    private int _chosenStage = -1;
    private RouteKind _chosenKind;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (PlayerBody == null || Streamer?.Generator == null || Stations == null || Supplies == null || _run?.Route == null)
        {
            GD.PushError("RouteTests: PlayerBody, Streamer, Stations, Supplies and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.RouteOptionsUpdated += OnOptions;
        GameEvents.RouteChosen += OnChosen;
        TerrainGenerator generator = Streamer.Generator;
        IRouteQuery route = _run.Route;

        TestChoices(generator);
        TestSplitAndJoin(generator, route);
        TestWalkable(generator, route);
        int stage = FindStageWithBoth(route);
        TestLengths(route);
        TestSurfaces(generator, route, stage);
        TestSupplies(route, stage);
        await TestSign(PlayerBody, route, stage);
        await TestChoice(PlayerBody, route, stage);

        GameEvents.RouteOptionsUpdated -= OnOptions;
        GameEvents.RouteChosen -= OnChosen;
        if (_failures.Count == 0)
        {
            GD.Print("ROUTE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"ROUTE TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnOptions(int stage, RouteEstimate[] options)
    {
        _optionsStage = stage;
        _options = options;
    }

    private void OnChosen(int stage, RouteKind kind)
    {
        _chosenStage = stage;
        _chosenKind = kind;
    }

    private void TestChoices(TerrainGenerator generator)
    {
        TerrainSettings settings = generator.Settings;
        TerrainGenerator same = new(settings, generator.Seed);
        TerrainGenerator other = new(settings, generator.Seed + 77);
        bool identical = true;
        int differ = 0;
        int withChoice = 0;
        int both = 0;
        int hazardous = 0;
        int supply = 0;
        for (int stage = 0; stage < StagesChecked; stage++)
        {
            RouteBranch[] branches = generator.Branches(stage);
            if (!Same(branches, same.Branches(stage)))
            {
                identical = false;
            }
            if (!Same(branches, other.Branches(stage)))
            {
                differ++;
            }
            if (branches.Length > 0)
            {
                withChoice++;
            }
            if (branches.Length > 1)
            {
                both++;
            }
            foreach (RouteBranch branch in branches)
            {
                if (branch.Kind == RouteKind.Hazardous)
                {
                    hazardous++;
                }
                else
                {
                    supply++;
                }
            }
        }
        Check("every stage offers a choice, the same for the same seed",
            identical && differ > StagesChecked / 2 && withChoice == StagesChecked && both > 0 && hazardous > 0 && supply > 0,
            $"{withChoice}/{StagesChecked} stages with a choice, {both} with both, {hazardous} hazardous, {supply} supply-rich, {differ} differ with another seed");
    }

    private static bool Same(RouteBranch[] a, RouteBranch[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    private void TestSplitAndJoin(TerrainGenerator generator, IRouteQuery route)
    {
        bool joined = true;
        float leastSwing = float.MaxValue;
        for (int stage = 0; stage < 8; stage++)
        {
            Vector2 zone = route.BranchZone(stage);
            for (int path = 1; path < route.PathCount(stage); path++)
            {
                float above = Mathf.Abs(route.PathPoint(stage, path, zone.X + 1.0f).X - route.RoutePoint(zone.X + 1.0f).X);
                float below = Mathf.Abs(route.PathPoint(stage, path, zone.Y - 1.0f).X - route.RoutePoint(zone.Y - 1.0f).X);
                if (above > 0.01f || below > 0.01f)
                {
                    joined = false;
                }
                float middle = (zone.X + zone.Y) * 0.5f;
                leastSwing = Mathf.Min(leastSwing, Mathf.Abs(route.PathPoint(stage, path, middle).X - route.RoutePoint(middle).X));
            }
        }
        float top = route.Station(1).Z;
        bool belowStation = route.BranchZone(1).X < top - generator.Settings.ClearingRadius - 10.0f
            && route.BranchZone(1).Y > route.Station(2).Z + generator.Settings.ClearingRadius + 10.0f;
        Check("branches leave below a station, rejoin above the next and swing well away",
            joined && belowStation && leastSwing >= generator.Settings.BranchOffset.X * 0.8f,
            $"rejoin exactly {joined}, clear of stations {belowStation}, least swing at mid-stage {leastSwing:0} m");
    }

    private void TestWalkable(TerrainGenerator generator, IRouteQuery route)
    {
        float worstAlong = 0.0f;
        float worstCross = 0.0f;
        string where = "";
        for (int stage = 0; stage < 6; stage++)
        {
            Vector2 zone = route.BranchZone(stage);
            for (int path = 0; path < route.PathCount(stage); path++)
            {
                RouteKind kind = route.PathKind(stage, path);
                float half = generator.HalfWidth(kind) * 0.8f;
                Vector3 previous = route.PathPoint(stage, path, zone.X);
                for (float z = zone.X - AlongStep; z > zone.Y; z -= AlongStep)
                {
                    Vector3 point = route.PathPoint(stage, path, z);
                    Vector3 step = point - previous;
                    float along = Mathf.RadToDeg(Mathf.Atan2(Mathf.Abs(step.Y), new Vector2(step.X, step.Z).Length()));
                    // Across the band: perpendicular to the path direction.
                    Vector3 across = new Vector3(-step.Z, 0.0f, step.X).Normalized() * half;
                    float left = generator.Height(point.X + across.X, point.Z + across.Z);
                    float right = generator.Height(point.X - across.X, point.Z - across.Z);
                    float cross = Mathf.RadToDeg(Mathf.Atan2(Mathf.Abs(left - right), half * 2.0f));
                    if (along > worstAlong)
                    {
                        worstAlong = along;
                        where = $"{RouteSign.KindName(kind)} stage {stage} at z {z:0}";
                    }
                    worstCross = Mathf.Max(worstCross, cross);
                    previous = point;
                }
            }
        }
        Check("every path is walkable along and across", worstAlong <= MaxAlongSlope && worstCross <= MaxCrossSlope,
            $"steepest along {worstAlong:0.0}° ({where}, max {MaxAlongSlope}°), steepest across {worstCross:0.0}° (max {MaxCrossSlope}°)");
    }

    private int FindStageWithBoth(IRouteQuery route)
    {
        for (int stage = 1; stage < StagesChecked; stage++)
        {
            if (route.PathCount(stage) == 3)
            {
                return stage;
            }
        }
        return 1;
    }

    private static float PathLength(IRouteQuery route, int stage, int path)
    {
        Vector2 zone = route.BranchZone(stage);
        float length = 0.0f;
        Vector3 previous = route.PathPoint(stage, path, zone.X);
        for (float z = zone.X - 2.0f; z >= zone.Y; z -= 2.0f)
        {
            Vector3 point = route.PathPoint(stage, path, z);
            length += point.DistanceTo(previous);
            previous = point;
        }
        return length;
    }

    private void TestLengths(IRouteQuery route)
    {
        int hazardShorter = 0;
        int hazardCount = 0;
        int supplyLonger = 0;
        int supplyCount = 0;
        float hazardRatio = 0.0f;
        float supplyRatio = 0.0f;
        for (int stage = 0; stage < 12; stage++)
        {
            float safe = PathLength(route, stage, 0);
            for (int path = 1; path < route.PathCount(stage); path++)
            {
                float length = PathLength(route, stage, path);
                if (route.PathKind(stage, path) == RouteKind.Hazardous)
                {
                    hazardCount++;
                    hazardRatio += length / safe;
                    if (length < safe)
                    {
                        hazardShorter++;
                    }
                }
                else
                {
                    supplyCount++;
                    supplyRatio += length / safe;
                    if (length > safe * 1.08f)
                    {
                        supplyLonger++;
                    }
                }
            }
        }
        Check("hazardous paths are shorter, supply-rich paths clearly longer",
            hazardCount > 0 && supplyCount > 0 && hazardShorter == hazardCount && supplyLonger == supplyCount,
            $"hazardous shorter {hazardShorter}/{hazardCount} (avg ×{hazardRatio / Mathf.Max(hazardCount, 1):0.00}), supply-rich over 8 % longer {supplyLonger}/{supplyCount} (avg ×{supplyRatio / Mathf.Max(supplyCount, 1):0.00})");
    }

    private void TestSurfaces(TerrainGenerator generator, IRouteQuery route, int stage)
    {
        Vector2 zone = route.BranchZone(stage);
        Dictionary<RouteKind, int[]> counts = new();
        for (int path = 0; path < route.PathCount(stage); path++)
        {
            RouteKind kind = route.PathKind(stage, path);
            int[] tally = new int[System.Enum.GetValues<TerrainSurface>().Length];
            // The middle of the branch, away from the ramps where bands overlap.
            float from = Mathf.Lerp(zone.X, zone.Y, 0.3f);
            float to = Mathf.Lerp(zone.X, zone.Y, 0.7f);
            for (float z = from; z > to; z -= 3.0f)
            {
                Vector3 point = route.PathPoint(stage, path, z);
                tally[(int)generator.SurfaceAt(point.X, point.Z)]++;
            }
            counts[kind] = tally;
        }
        int[] safe = counts[RouteKind.Safe];
        bool safeFirm = safe[(int)TerrainSurface.Scree] == 0 && safe[(int)TerrainSurface.Mud] == 0;
        bool hazardLoose = true;
        string hazardText = "none";
        if (counts.TryGetValue(RouteKind.Hazardous, out int[]? hazard))
        {
            int total = 0;
            foreach (int c in hazard)
            {
                total += c;
            }
            int loose = hazard[(int)TerrainSurface.Scree] + hazard[(int)TerrainSurface.Snow];
            hazardLoose = loose >= total * 0.9f;
            hazardText = $"{loose}/{total} scree or snow";
        }
        Check("the safe path is firm, the hazardous path loose underfoot", safeFirm && hazardLoose,
            $"safe: {safe[(int)TerrainSurface.Scree]} scree, {safe[(int)TerrainSurface.Mud]} mud; hazardous: {hazardText}");
    }

    private void TestSupplies(IRouteQuery route, int stage)
    {
        Vector2 zone = route.BranchZone(stage);
        int paths = route.PathCount(stage);
        int[] counts = new int[paths];
        float cell = Supplies!.CellSize;
        int rowFrom = Mathf.FloorToInt(zone.Y / cell);
        int rowTo = Mathf.FloorToInt(zone.X / cell);
        float middleX = route.RoutePoint((zone.X + zone.Y) * 0.5f).X;
        int columnFrom = Mathf.FloorToInt((middleX - 160.0f) / cell);
        int columnTo = Mathf.FloorToInt((middleX + 160.0f) / cell);
        for (int row = rowFrom; row <= rowTo; row++)
        {
            for (int column = columnFrom; column <= columnTo; column++)
            {
                // Difficulty multiplier 1: count what the world holds before difficulty.
                foreach (SupplySpawner.Slot slot in Supplies.PlanCell(new Vector2I(column, row), _run!.Seed, route, 1.0f))
                {
                    if (slot.Z > zone.X || slot.Z < zone.Y)
                    {
                        continue;
                    }
                    int path = route.NearestPath(new Vector3(slot.X, 0.0f, slot.Z), out float distance);
                    if (distance <= 12.0f)
                    {
                        counts[path]++;
                    }
                }
            }
        }
        string text = "";
        float safe = Mathf.Max(counts[0], 1);
        bool ok = true;
        for (int path = 0; path < paths; path++)
        {
            RouteKind kind = route.PathKind(stage, path);
            text += $"{RouteSign.KindName(kind)} {counts[path]}  ";
            if (kind == RouteKind.SupplyRich && counts[path] < safe * 2.0f)
            {
                ok = false;
            }
            if (kind == RouteKind.Hazardous && counts[path] < safe)
            {
                ok = false;
            }
        }
        Check("supply-rich paths hold far more supplies, hazardous ones more than safe", ok && counts[0] > 0,
            $"items along stage {stage}: {text}");
    }

    private async Task TestSign(PlayerController player, IRouteQuery route, int stage)
    {
        Vector3 top = route.Station(stage);
        await Place(player, top + new Vector3(0.0f, 0.0f, -2.0f), 2.0f);
        RouteSign? sign = null;
        Stations!.Signs.TryGetValue(stage, out sign);
        int paths = route.PathCount(stage);
        bool allValid = _options.Length == paths;
        float safe = 0.0f;
        bool ordered = true;
        foreach (RouteEstimate option in _options)
        {
            if (!option.Valid || option.Station != stage + 1)
            {
                allValid = false;
            }
            if (option.Kind == RouteKind.Safe)
            {
                safe = option.DistanceMeters;
            }
        }
        foreach (RouteEstimate option in _options)
        {
            if (option.Kind == RouteKind.Hazardous && option.DistanceMeters >= safe)
            {
                ordered = false;
            }
            if (option.Kind == RouteKind.SupplyRich && option.DistanceMeters <= safe)
            {
                ordered = false;
            }
        }
        int lines = 0;
        if (sign != null)
        {
            lines = sign.LineCount;
        }
        Check("the lookout sign lists every path's estimate", _optionsStage == stage && allValid && ordered && lines == paths,
            $"stage {_optionsStage}/{stage}, {_options.Length} estimates for {paths} paths, distances in order {ordered}, sign lines {lines}:\n      {sign?.Text.Replace("\n", "\n      ")}");
    }

    private async Task TestChoice(PlayerController player, IRouteQuery route, int stage)
    {
        int target = route.PathCount(stage) - 1;
        RouteKind kind = route.PathKind(stage, target);
        Vector2 zone = route.BranchZone(stage);
        float z = Mathf.Lerp(zone.X, zone.Y, 0.4f);
        _chosenStage = -1;
        await Place(player, route.PathPoint(stage, target, z), 1.5f);
        RouteEstimate estimate = GameEvents.LatestEstimate;
        Check("walking onto a branch chooses it, and the estimate follows that path",
            _chosenStage == stage && _chosenKind == kind && estimate.Kind == kind,
            $"chose {RouteSign.KindName(_chosenKind)} in stage {_chosenStage} (expected {RouteSign.KindName(kind)} in {stage}), estimate along {RouteSign.KindName(estimate.Kind)}");
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < settle; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
