using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the set pieces that dress the mountain: every piece resource is valid and its
/// models exist; stages get several wayside pieces and often a landmark; nothing stands on a path's
/// walking band, near a station or on another piece; the same mountain is dressed the same way;
/// built pieces have all their parts, solid ones to bump into, and none of them blocks a path; the
/// stations carry the new stall, cart and banner.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/SetPieceTests.tscn
/// </summary>
public partial class SetPieceTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public StationManager? Stations { get; set; }
    [Export] public SetPiecePlacer? Placer { get; set; }
    [Export] public int StagesChecked { get; set; } = 24;
    /// <summary>No solid part's centre this close to a path's centre, in m.</summary>
    [Export] public float WalkingBand { get; set; } = 4.0f;

    private readonly List<string> _failures = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (PlayerBody == null || Streamer?.Generator == null || Stations == null || Placer == null || _run?.Route == null)
        {
            GD.PushError("SetPieceTests: PlayerBody, Streamer, Stations, Placer and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        IRouteQuery route = _run.Route;
        TestResources(Placer);
        TestPlan(Placer, route);
        await TestBuilt(PlayerBody, Placer, route);
        await TestModelLifetime(PlayerBody, Placer, route);
        await TestStation(PlayerBody, route);

        if (_failures.Count == 0)
        {
            GD.Print("SET PIECE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("SET PIECE TESTS: FAIL (" + _failures.Count + ")");
            GetTree().Quit(1);
        }
    }

    private void TestResources(SetPiecePlacer placer)
    {
        int files = 0;
        foreach (string file in DirAccess.GetFilesAt(SetPiecePlacer.Folder))
        {
            if (file.Replace(".remap", "").EndsWith(".tres"))
            {
                files++;
            }
        }
        int landmarks = 0;
        foreach (SetPiece piece in placer.Pieces)
        {
            if (piece.Kind == SetPieceKind.Landmark)
            {
                landmarks++;
            }
        }
        Check("every set piece loads with all its models", placer.Pieces.Count == files && files >= 10 && landmarks >= 2,
            placer.Pieces.Count + " valid of " + files + " files, " + landmarks + " landmarks");
    }

    private void TestPlan(SetPiecePlacer placer, IRouteQuery route)
    {
        placer.Forget();
        int wayside = 0;
        int landmarks = 0;
        float closest = float.MaxValue;
        float nearestStation = float.MaxValue;
        bool overlap = false;
        HashSet<string> kinds = new();
        List<string> order = new();
        for (int stage = 0; stage < StagesChecked; stage++)
        {
            IReadOnlyList<SetPiecePlacer.Placement> placements = placer.PlanFor(route, stage);
            for (int i = 0; i < placements.Count; i++)
            {
                SetPiecePlacer.Placement p = placements[i];
                order.Add(p.Piece.Id);
                kinds.Add(p.Piece.Id);
                if (p.Piece.Kind == SetPieceKind.Landmark)
                {
                    landmarks++;
                }
                else
                {
                    wayside++;
                }
                route.NearestPath(p.Centre, out float fromPath);
                closest = Mathf.Min(closest, fromPath - p.Piece.Radius);
                int next = route.NextStationIndex(p.Centre.Z + placer.StationGap);
                nearestStation = Mathf.Min(nearestStation, Mathf.Abs(route.Station(next).Z - p.Centre.Z));
                for (int j = 0; j < i; j++)
                {
                    Vector3 offset = placements[j].Centre - p.Centre;
                    if (new Vector2(offset.X, offset.Z).Length() < placements[j].Piece.Radius + p.Piece.Radius)
                    {
                        overlap = true;
                    }
                }
            }
        }
        Check("stages are dressed with several pieces and some landmarks", wayside >= StagesChecked * 3 / 2 && landmarks >= StagesChecked / 5 && kinds.Count >= 8,
            wayside + " wayside pieces and " + landmarks + " landmarks in " + StagesChecked + " stages, " + kinds.Count + " different pieces");
        Check("pieces keep off the paths, the stations and each other", closest >= placer.PathClearance - 0.01f && nearestStation >= placer.StationGap - 0.01f && !overlap,
            "nearest edge to a path " + closest.ToString("0.0") + " m, nearest to a station " + nearestStation.ToString("0") + " m, overlap " + overlap);
        placer.Forget();
        List<string> again = new();
        for (int stage = 0; stage < StagesChecked; stage++)
        {
            foreach (SetPiecePlacer.Placement p in placer.PlanFor(route, stage))
            {
                again.Add(p.Piece.Id);
            }
        }
        Check("the same mountain is dressed the same way", string.Join(",", order) == string.Join(",", again), order.Count + " pieces");
    }

    private async Task TestBuilt(PlayerController player, SetPiecePlacer placer, IRouteQuery route)
    {
        Vector3 mid = route.RoutePoint(Mathf.Lerp(route.Station(1).Z, route.Station(2).Z, 0.5f));
        await Place(player, mid, 1.0f);
        int built = 0;
        int solids = 0;
        bool complete = true;
        float closestSolid = float.MaxValue;
        foreach (SetPieceNode node in placer.Built.Values)
        {
            built++;
            if (node.Piece == null || node.PartCount != node.Piece.Parts.Count)
            {
                complete = false;
            }
            foreach (Node child in node.GetChildren())
            {
                if (child is StaticBody3D body)
                {
                    solids++;
                    route.NearestPath(body.GlobalPosition, out float fromPath);
                    closestSolid = Mathf.Min(closestSolid, fromPath);
                }
            }
        }
        Check("built pieces have every part and something solid, off the walking band", built >= 3 && complete && solids > 0 && closestSolid >= WalkingBand,
            built + " pieces built, all parts " + complete + ", " + solids + " solid parts, nearest to a path " + closestSolid.ToString("0.0") + " m");
    }

    private async Task TestModelLifetime(PlayerController player, SetPiecePlacer placer, IRouteQuery route)
    {
        Dictionary<string, System.WeakReference<PackedScene>> sources = WatchModelSources(placer);
        placer.SetPhysicsProcess(false);
        placer.Forget();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        bool retained = sources.Count > 0;
        foreach (System.WeakReference<PackedScene> source in sources.Values)
        {
            retained &= source.TryGetTarget(out PackedScene? scene) && GodotObject.IsInstanceValid(scene);
        }
        Check("unloading pieces and managed collection retain their imported scene wrappers", retained,
            sources.Count + " distinct model sources");
        placer.SetPhysicsProcess(true);
        for (int stage = 1; stage <= 3; stage++)
        {
            Vector3 mid = route.RoutePoint(Mathf.Lerp(route.Station(stage).Z, route.Station(stage + 1).Z, 0.5f));
            await Place(player, mid, 0.3f);
            bool complete = placer.Built.Count > 0;
            foreach (SetPieceNode node in placer.Built.Values)
            {
                complete &= node.Piece != null && node.PartCount == node.Piece.Parts.Count;
            }
            Check("models instantiate completely after collection at stage " + stage, complete,
                placer.Built.Count + " rebuilt pieces");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Dictionary<string, System.WeakReference<PackedScene>> WatchModelSources(SetPiecePlacer placer)
    {
        Dictionary<string, System.WeakReference<PackedScene>> sources = new();
        foreach (SetPieceNode node in placer.Built.Values)
        {
            if (node.Piece == null) { continue; }
            foreach (SetPiecePart part in node.Piece.Parts)
            {
                if (!sources.ContainsKey(part.Model) && ResourceLoader.Load(part.Model) is PackedScene scene)
                { sources.Add(part.Model, new System.WeakReference<PackedScene>(scene)); }
            }
        }
        return sources;
    }

    private async Task TestStation(PlayerController player, IRouteQuery route)
    {
        await Place(player, route.Station(1) + new Vector3(0.0f, 0.0f, 3.0f), 0.5f);
        Stations!.Stations.TryGetValue(1, out StationSite? site);
        bool stall = site?.FindChild("stall-green", true, false) != null;
        bool cart = site?.FindChild("cart", true, false) != null;
        bool banner = site?.FindChild("flag-banner-long", true, false) != null;
        Check("stations carry the stall, cart and banner", stall && cart && banner, "stall " + stall + ", cart " + cart + ", banner " + banner);
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
            GD.Print("PASS  " + name + ": " + detail);
        }
        else
        {
            GD.Print("FAIL  " + name + ": " + detail);
            _failures.Add(name);
        }
    }
}
