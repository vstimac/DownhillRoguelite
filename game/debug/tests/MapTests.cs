using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M13: the chart fills in only what the carrier has seen; it is drawn north
/// up and a point on the sheet maps back to the same place in the world; the held map sits in
/// front of the camera, takes the mouse and slows walking without changing the estimates; a
/// waypoint stands in the world on the ground, shows its distance, the oldest gives way when the
/// map is full and right-click removes one; drawing needs paper and a pencil, and wears both out;
/// the compass needle points north whichever way the carrier faces; a new mountain clears the map.
/// The seen ground is painted into a few tile textures; held up, the sheet stays put (the arrow moves
/// across it), doesn't redraw while nothing changes, and re-centres once when the arrow nears its edge.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/MapTests.tscn
/// </summary>
public partial class MapTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public FieldMap? Map { get; set; }

    private readonly List<string> _failures = new();
    private RunState? _run;

    public override async void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (PlayerBody == null || Streamer?.Generator == null || Map == null || _run?.Route == null)
        {
            GD.PushError("MapTests: PlayerBody, Streamer, Map and RunState.Route are needed.");
            GetTree().Quit(1);
            return;
        }
        _run.SummaryDelay = 0.5f;
        await Place(PlayerBody, Streamer.SpawnPoint, 1.0f);

        TestReveal(Map, Streamer.Generator);
        TestNorthUp(Map);
        TestTiles(Map);
        await TestHolding(PlayerBody, Map);
        await TestStillSheet(PlayerBody, Map);
        await TestWaypoints(Map, Streamer.Generator);
        await TestDrawing(Map);
        await TestCompass(PlayerBody, Map);
        await TestNewMountain(Map);

        if (_failures.Count == 0)
        {
            GD.Print("MAP TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"MAP TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestReveal(FieldMap map, TerrainGenerator generator)
    {
        MapChart chart = map.Chart;
        Vector2 here = map.Centre;
        bool near = chart.IsSeen(here.X, here.Y) && chart.IsSeen(here.X + 30.0f, here.Y - 30.0f);
        bool far = !chart.IsSeen(here.X + 400.0f, here.Y - 400.0f);
        Vector2I cell = chart.CellAt(here.X, here.Y);
        Vector2 middle = chart.CellCentre(cell);
        bool height = Mathf.Abs(chart.Seen[cell] - generator.Height(middle.X, middle.Y)) < 0.01f;
        // Well inside what the carrier sees even at dawn: nothing new to reveal.
        int again = chart.Reveal(here, 15.0f, generator.Height);
        Check("the chart fills in only what the carrier has seen", near && far && height && again == 0,
            $"{chart.Seen.Count} cells seen, near {near}, 400 m away unseen {far}, heights kept {height}, revealing again adds {again}");
    }

    private void TestNorthUp(FieldMap map)
    {
        Vector2 north = map.Centre + new Vector2(0.0f, -50.0f);
        Vector2 size = new(map.TextureSize, map.TextureSize);
        float metresPerPixel = map.MetresAcross / map.TextureSize;
        Vector2 pixel = MapCanvas.ToPixel(north, map.Centre, metresPerPixel, size);
        Vector2 back = map.PixelToWorld(pixel);
        Check("the chart is north up, and a spot on the sheet is the same spot in the world",
            pixel.Y < size.Y * 0.5f && Mathf.Abs(pixel.X - size.X * 0.5f) < 0.5f && back.DistanceTo(north) < 0.01f,
            $"50 m north is {size.Y * 0.5f - pixel.Y:0} px above the centre; round trip off by {back.DistanceTo(north):0.000} m");
    }

    private async Task TestHolding(PlayerController player, FieldMap map)
    {
        float walking = player.WalkingSpeed(0.3f);
        float closedSpeed = await WalkSpeed(player);
        map.SetOpen(true);
        await WaitSeconds(0.6f);
        bool open = GameEvents.IsMapOpen && Input.MouseMode == Input.MouseModeEnum.Visible;
        Camera3D? camera = GetViewport().GetCamera3D();
        Vector2 middle = GetViewport().GetVisibleRect().Size * 0.5f;
        bool onSheet = map.SheetPixel(middle, out Vector2 pixel);
        bool central = onSheet && pixel.DistanceTo(new Vector2(map.TextureSize, map.TextureSize) * 0.5f) < map.TextureSize * 0.25f;
        float openSpeed = await WalkSpeed(player);
        bool estimateSame = Mathf.IsEqualApprox(player.WalkingSpeed(0.3f), walking);
        Check("the map is held in front of the camera, takes the mouse and slows walking (not the estimates)",
            open && camera != null && central && openSpeed < closedSpeed * 0.6f && estimateSame,
            $"open {open}, screen centre on the sheet at {pixel.X:0},{pixel.Y:0}, walking {closedSpeed:0.00} → {openSpeed:0.00} m/s, estimate speed unchanged {estimateSame}");
    }

    private async Task<float> WalkSpeed(PlayerController player)
    {
        await Place(player, _run!.Route!.RoutePoint(Streamer!.SpawnPoint.Z - 40.0f), 0.4f);
        Input.ActionPress("player_move_forward");
        await WaitSeconds(1.5f);
        float speed = new Vector2(player.Velocity.X, player.Velocity.Z).Length();
        Input.ActionRelease("player_move_forward");
        await WaitSeconds(0.3f);
        return speed;
    }

    private async Task TestWaypoints(FieldMap map, TerrainGenerator generator)
    {
        Vector2 spot = map.Centre + new Vector2(30.0f, -60.0f);
        map.AddWaypoint(spot);
        await WaitSeconds(0.2f);
        WaypointMarker? marker = null;
        if (map.Markers.Count > 0)
        {
            marker = map.Markers[^1];
        }
        float ground = generator.Height(spot.X, spot.Y);
        bool onGround = marker != null && Mathf.Abs(marker.GlobalPosition.Y - ground) < 0.1f
            && new Vector2(marker.GlobalPosition.X, marker.GlobalPosition.Z).DistanceTo(spot) < 0.01f;
        string label = marker?.LabelText ?? "";
        for (int i = 0; i < map.MaxWaypoints + 2; i++)
        {
            map.AddWaypoint(map.Centre + new Vector2(i * 10.0f, 20.0f));
        }
        int full = map.Chart.Waypoints.Count;
        bool oldestGone = map.Chart.Waypoints[0] != spot;
        map.RemoveWaypointAt(map.Chart.Waypoints[0]);
        await WaitSeconds(0.1f);
        Check("a waypoint stands on the ground in the world; the oldest gives way; right-click removes one",
            onGround && label.Contains(" m") && full == map.MaxWaypoints && oldestGone && map.Chart.Waypoints.Count == map.MaxWaypoints - 1 && map.Markers.Count == map.MaxWaypoints - 1,
            $"marker on the ground {onGround}, label \"{label}\", {full} kept of {map.MaxWaypoints + 3} set, {map.Chart.Waypoints.Count} after removing one, {map.Markers.Count} markers");
    }

    private async Task TestDrawing(FieldMap map)
    {
        bool blocked = !map.CanDraw;
        GameEvents.PublishItemsGranted(new[] { "res://inventory/items/paper.tres", "res://inventory/items/pencil.tres" });
        await WaitSeconds(0.1f);
        bool ready = map.CanDraw;
        int strokes = map.Chart.Drawings.Count;
        map.BeginDrawing(map.Centre);
        float drawn = 0.0f;
        while (drawn < map.PencilMetres + 10.0f)
        {
            map.Draw(map.Centre + new Vector2(Mathf.Sin(drawn) * 40.0f, drawn * 0.01f));
            drawn += 40.0f;
        }
        await WaitSeconds(0.1f);
        bool pencilGone = !HasItem("pencil") && HasItem("paper");
        for (int i = 0; i < map.StrokesPerPaper; i++)
        {
            map.BeginDrawing(map.Centre);
        }
        await WaitSeconds(0.1f);
        bool paperGone = !HasItem("paper");
        Check("drawing needs paper and a pencil, and wears both out",
            blocked && ready && map.Chart.Drawings.Count > strokes && pencilGone && paperGone && !map.CanDraw,
            $"no tools: can't draw {blocked}; with them: {ready}; pencil used up after {map.PencilMetres:0} m {pencilGone}; paper used up after {map.StrokesPerPaper} lines {paperGone}");
    }

    private async Task TestCompass(PlayerController player, FieldMap map)
    {
        Vector3 at = _run!.Route!.RoutePoint(Streamer!.SpawnPoint.Z - 40.0f);
        player.Teleport(at + Vector3.Up * 0.1f, 0.0f);
        await WaitSeconds(0.5f);
        float facingNorth = map.Needle!.Rotation.Z;
        // Yaw -90°: the carrier (and the camera behind it) faces +X, east; north is then on the left.
        player.Teleport(at + Vector3.Up * 0.1f, -Mathf.Pi * 0.5f);
        await WaitSeconds(0.5f);
        float facingEast = map.Needle.Rotation.Z;
        Check("the compass needle points north whichever way the carrier faces",
            Mathf.Abs(facingNorth) < 0.2f && Mathf.Abs(Mathf.Abs(facingEast) - Mathf.Pi * 0.5f) < 0.2f,
            $"needle {Mathf.RadToDeg(facingNorth):0}° facing north, {Mathf.RadToDeg(facingEast):0}° facing east");
    }

    /// <summary>The seen ground is painted into a few tile textures, not redrawn cell by cell.</summary>
    private void TestTiles(FieldMap map)
    {
        int tiles = map.Tiles.TileCount;
        int seen = map.Chart.Seen.Count;
        float cellsPerTile = map.Tiles.CellsPerTile * map.Tiles.CellsPerTile;
        bool few = tiles > 0 && tiles <= Mathf.CeilToInt(seen / cellsPerTile) + 8;
        Check("the seen ground is painted once into a few tiles", few && map.Chart.TakeFresh().Count == 0,
            seen + " cells seen, painted into " + tiles + " tiles of " + map.Tiles.CellsPerTile + "×" + map.Tiles.CellsPerTile + " cells");
    }

    /// <summary>Held up, the sheet doesn't scroll or redraw while nothing changes; it re-centres once near its edge.</summary>
    private async Task TestStillSheet(PlayerController player, FieldMap map)
    {
        Vector3 spot = _run!.Route!.RoutePoint(Streamer!.SpawnPoint.Z - 40.0f);
        await Place(player, spot, 0.6f);
        map.SetOpen(true);
        await WaitSeconds(0.5f);
        int before = map.RedrawCount;
        await WaitSeconds(1.0f);
        int idle = map.RedrawCount - before;
        Vector2 anchored = map.Centre;
        await Place(player, spot + new Vector3(0.0f, 0.0f, -25.0f), 0.5f);
        bool stayed = map.Centre.IsEqualApprox(anchored);
        bool redrew = map.RedrawCount > before + idle;
        float metres = map.MetresAcross;
        await Place(player, spot + new Vector3(0.0f, 0.0f, -metres * 0.45f), 0.5f);
        bool recentred = map.Centre.DistanceTo(map.CarrierPoint) < 1.0f;
        // Left raised, as the earlier test left it: the compass check reads the held sheet.
        Check("held up, the sheet stays put and only redraws when something changes; near its edge it re-centres",
            idle <= 1 && stayed && redrew && recentred,
            idle + " redraws in a second standing still, a 25 m walk keeps the sheet put " + stayed + " (arrow redrawn " + redrew
            + "), near the edge it re-centres " + recentred);
    }

    private async Task TestNewMountain(FieldMap map)
    {
        bool hadSome = map.Chart.Waypoints.Count > 0 && map.Chart.Drawings.Count > 0;
        _run!.StartNewRun();
        await WaitSeconds(0.6f);
        bool cleared = map.Chart.Waypoints.Count == 0 && map.Chart.Drawings.Count == 0 && map.Markers.Count == 0 && !map.IsOpen && !GameEvents.IsMapOpen;
        bool fresh = map.Chart.Seen.Count < 400 && map.Tiles.TileCount <= 4;
        Check("a new mountain clears the map", hadSome && cleared && fresh,
            $"waypoints and drawings before {hadSome}, cleared {cleared}, {map.Chart.Seen.Count} cells seen on the new mountain");
    }

    private static bool HasItem(string id)
    {
        foreach (string carried in GameEvents.CarriedItems)
        {
            if (carried == id)
            {
                return true;
            }
        }
        return false;
    }

    private async Task Place(PlayerController player, Vector3 position, float settle)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        await WaitSeconds(settle);
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
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
