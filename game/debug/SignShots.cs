using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Photographs each in-world sign kind from close up, and the first tree trunk too, and writes
/// report.txt: each sign's text, whether its labels show, and how far its post sits above or below
/// the ground under it. The carrier walks the route to each kind and the terrain loads round it, so
/// the game's own placement code streams the signs in. Path markers are off in the game (the junction
/// signpost replaced them), so this run switches them on to photograph them.
/// Run it windowed (headless can't render): `-- --shots-directory=C:/somewhere`.
/// </summary>
public partial class SignShots : Node3D
{
    /// <summary>How far from the carrier a sign may stand and still be photographed (m).</summary>
    [Export] public float ReachMetres { get; set; } = 45.0f;
    [Export] public float SettleSeconds { get; set; } = 2.0f;
    [Export] public float MaxWaitSeconds { get; set; } = 25.0f;

    private PlayerController? _player;
    private IRouteQuery? _route;
    private TerrainStreamer? _streamer;
    private StationManager? _stations;
    private Camera3D? _camera;
    private string _directory = "";
    private readonly HashSet<string> _done = new();
    private readonly List<string> _report = new();
    private int _shots;

    public override async void _Ready()
    {
        _directory = ProjectSettings.GlobalizePath("res://../../sign-shots");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { _directory = arg["--shots-directory=".Length..]; }
        }
        Directory.CreateDirectory(_directory);
        await Seconds(3.0f);
        // The loading screen holds the whole tree paused while the mountain loads; the world must run for the streaming.
        GetTree().Paused = false;
        _camera = new Camera3D { Name = "SignCamera", Fov = 60.0f, Far = 300.0f };
        AddChild(_camera);
        _player = GetNodeOrNull<PlayerController>("World/Player");
        _streamer = GetNodeOrNull<TerrainStreamer>("World/Terrain");
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        _route = run?.Route;
        if (_player == null || _streamer == null || _route == null)
        {
            GD.Print("FAIL  sign shots: no carrier, terrain or route");
            GetTree().Quit(1);
            return;
        }
        IRouteQuery route = _route;
        _stations = Find<StationManager>(GetTree().Root);
        if (_stations != null)
        {
            _stations.ShowPathMarkers = true;
        }

        // 1. The first stage with a choice of paths: its route sign at the top, then the path markers and junction signpost.
        int branch = -1;
        for (int stage = 0; stage < 60 && branch < 0; stage++)
        {
            if (route.PathCount(stage) >= 2) { branch = stage; }
        }
        if (branch >= 0)
        {
            Vector2 zone = route.BranchZone(branch);
            float top = branch == 0 ? route.StartZ : route.Station(branch).Z;
            await Visit(route.RoutePoint(top - (_stations?.SignDistance ?? 16.0f)));
            await Capture("route sign", typeof(RouteSign));
            await Visit(route.RoutePoint(zone.X - (_stations?.MarkerDistance ?? 30.0f)));
            await Capture("path markers and junction", typeof(PathMarker), typeof(JunctionSignpost));
            // A cairn shows its estimate to the next station when the carrier stands just above it.
            Node3D? cairn = Nearest(GetTree().Root, typeof(WaypointCairn), _player!.GlobalPosition);
            if (cairn != null)
            {
                await Visit(cairn.GlobalPosition + new Vector3(0.0f, 0.0f, 4.0f));
                await Capture("cairn with estimate", typeof(WaypointCairn));
            }
        }

        // 2. The first station below the start, reached on foot: its name and fire, and the boon stones it offers.
        int first = route.NextStationIndex(route.StartZ);
        await Visit(route.Station(first));
        await Capture("station camp", typeof(StationSite), typeof(BoonStone));

        // 3. A wayside site: the first planned one on the first stages.
        WaysidePlacer? wayside = Find<WaysidePlacer>(GetTree().Root);
        for (int stage = 0; stage < 6 && wayside != null && !_done.Contains(nameof(WaysideSite)); stage++)
        {
            foreach (WaysidePlacer.Placement placement in wayside.PlanFor(route, stage))
            {
                await Visit(route.RoutePoint(placement.Position.Z + 4.0f));
                await Capture("wayside site", typeof(WaysideSite));
                break;
            }
        }

        // 4. Boon stones not yet photographed: beside the first one.
        BoonOfferings? boons = Find<BoonOfferings>(GetTree().Root);
        if (boons != null && boons.Stones.Count > 0 && !_done.Contains(nameof(BoonStone)))
        {
            Vector3 stone = boons.Stones[0].GlobalPosition;
            await Visit(new Vector3(stone.X, stone.Y, stone.Z + 3.0f));
            await Capture("boon stone", typeof(BoonStone));
        }

        // 5. A delivery post: accept the first contract offered, then walk to its post.
        ContractDirector? director = Find<ContractDirector>(GetTree().Root);
        if (director != null)
        {
            for (int index = 0; index < 8; index++)
            {
                ContractInstance? offer = director.Offer(index);
                if (offer == null) { continue; }
                ContractResult result = director.Accept(offer);
                _report.Add($"contract accepted at station {index}: {result}");
                break;
            }
        }
        DeliveryPost? post = Find<DeliveryPost>(GetTree().Root);
        if (post != null)
        {
            await Visit(new Vector3(post.GlobalPosition.X, post.GlobalPosition.Y, post.GlobalPosition.Z + 3.0f));
            await Capture("delivery post", typeof(DeliveryPost));
        }

        // 6. A waypoint set on the map: its marker stands where the chart puts it.
        FieldMap? map = Find<FieldMap>(GetTree().Root);
        if (map != null)
        {
            Vector3 spot = route.RoutePoint(route.StartZ - 60.0f);
            map.AddWaypoint(new Vector2(spot.X + 3.0f, spot.Z));
            await Visit(spot);
            await Capture("map waypoint", typeof(WaypointMarker));
        }

        // 7. A tree trunk, from close up.
        Node3D? trunk = FindTree(GetTree().Root);
        if (trunk != null)
        {
            Vector3 at = trunk.GlobalPosition;
            await Shoot(at + new Vector3(3.0f, 1.3f, 3.0f), at + Vector3.Up * 0.3f, Path.Combine(_directory, $"{_shots:D2}_tree_base.png"));
            GD.Print($"SHOT tree base at {at}");
        }

        foreach (Type kind in KindsToFind())
        {
            if (_done.Contains(kind.Name)) { continue; }
            GD.Print($"FAIL  sign shots: {kind.Name} not reached");
            _report.Add($"{kind.Name}: NOT REACHED");
        }
        File.WriteAllText(Path.Combine(_directory, "report.txt"), string.Join("\n", _report) + "\n");
        foreach (string line in _report) { GD.Print("REPORT " + line); }
        GD.Print($"SIGN SHOTS: {_shots} photographs, directory {_directory}");
        GetTree().Quit(0);
    }

    private static Type[] KindsToFind() => new[]
    {
        typeof(JunctionSignpost), typeof(RouteSign), typeof(PathMarker), typeof(WaypointCairn), typeof(StationSite),
        typeof(WaysideSite), typeof(BoonStone), typeof(DeliveryPost), typeof(WaypointMarker),
    };

    /// <summary>Walks the carrier to a spot and waits for the terrain round it to load, as the streamer does in play.</summary>
    private async Task Visit(Vector3 at)
    {
        Vector3 spot = new(at.X, _route!.GroundHeight(at.X, at.Z) + 0.3f, at.Z);
        TerrainStreamer.Loading = true;
        _player!.Teleport(spot, 0.0f);
        // The carrier reports where it is only while it turns or walks (PlayerController), so the stations, cairns and
        // signs round it stream in from a report of its position, as they do while it walks.
        Report(spot);
        int stable = 0;
        int last = -1;
        float waited = 0.0f;
        while (waited < MaxWaitSeconds)
        {
            await Seconds(0.25f);
            waited += 0.25f;
            _player.Teleport(spot, 0.0f);
            Report(spot);
            if (_streamer!.LoadedCount == last && _streamer.PlantsPendingCount == 0) { stable++; } else { stable = 0; }
            last = _streamer.LoadedCount;
            if (stable >= 6) { break; }
        }
        TerrainStreamer.Loading = false;
        await Seconds(SettleSeconds);
        GD.Print($"VISIT carrier {Fmt(_player.GlobalPosition)}, terrain {_streamer!.LoadedCount} chunks, waited {waited:0.0}s");
    }

    private static void Report(Vector3 spot)
    {
        GameEvents.PublishLocomotion(new LocomotionSnapshot(spot, Vector3.Zero, Vector3.Zero, Vector3.Up, 0.0f, Vector3.Zero,
            Vector3.Zero, LocomotionState.Idle, "", 1.0f, Vector3.Forward));
    }

    /// <summary>Photographs each kind of sign within reach of the carrier: the given kinds, or else every kind not yet photographed.</summary>
    private async Task Capture(string why, params Type[] only)
    {
        Vector3 from = _player!.GlobalPosition;
        Type[] kinds = only.Length > 0 ? only : KindsToFind();
        List<(Type Kind, Node3D Node, string Gap, string Labels)> found = new();
        foreach (Type kind in kinds)
        {
            if (only.Length == 0 && _done.Contains(kind.Name)) { continue; }
            Node3D? node = Nearest(GetTree().Root, kind, from);
            if (node == null || node.GlobalPosition.DistanceTo(from) > ReachMetres) { continue; }
            // Looked at, as the carrier sees it: the signs that show their words only when focused (wayside sites,
            // boon stones, delivery posts) are focused for their photo.
            if (node is IInteractable interactable) { interactable.SetFocused(true); }
            // Measured now, while the physics state is current.
            found.Add((kind, node, GroundGap(node.GlobalPosition), LabelTexts(node)));
        }
        foreach ((Type kind, Node3D node, string gap, string labels) in found)
        {
            Vector3 at = node.GlobalPosition;
            string file = Path.Combine(_directory, $"{_shots:D2}_{kind.Name}.png");
            (Vector3 target, Vector3 eye) = Aim(node, kind);
            await Shoot(eye, target, file);
            if (node is IInteractable interactable) { interactable.SetFocused(false); }
            _report.Add($"{kind.Name} ({why}) | photo {Path.GetFileName(file)} | base at {Fmt(at)} | post vs ground {gap} | labels: {labels}");
            _done.Add(kind.Name);
        }
        if (found.Count == 0) { GD.Print($"CAPTURE {why}: nothing of {string.Join(", ", Array.ConvertAll(kinds, k => k.Name))} within {ReachMetres} m of {Fmt(from)}"); }
    }

    /// <summary>
    /// Where the camera looks and where it stands: halfway between the post's foot and the middle of the
    /// sign's words (or 1.1 m up when none show), so the post and the words are both in the picture.
    /// A station is framed wider, so its gate banners and name are in it.
    /// </summary>
    private static (Vector3 Target, Vector3 Eye) Aim(Node3D node, Type kind)
    {
        Vector3 at = node.GlobalPosition;
        Vector3 words = Vector3.Zero;
        int count = 0;
        foreach (Node child in node.FindChildren("*", "Label3D", true, false))
        {
            if (child is Label3D label && label.IsVisibleInTree())
            {
                words += label.GlobalPosition;
                count++;
            }
        }
        Vector3 target = count == 0 ? at + Vector3.Up * 1.1f : (at + words / count) * 0.5f;
        // A station is framed from up the slope, where the carrier comes in past the gate banners.
        if (kind == typeof(StationSite)) { return (target, target + new Vector3(0.0f, 1.0f, 6.0f)); }
        return (target, target + new Vector3(2.6f, 1.2f, 2.6f));
    }

    /// <summary>How far the sign's post base sits above (+) or below (-) the ground under it, by a physics ray.</summary>
    private string GroundGap(Vector3 at)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 2.0f, at + Vector3.Down * 3.0f);
        // The carrier (and its own bodies, which can stand over the spot) is not the ground.
        Godot.Collections.Array<Rid> carrier = new();
        CarrierBodies(_player!, carrier);
        query.Exclude = carrier;
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0)
        {
            return "no ground hit (terrain formula " + Signed(at.Y - _route!.GroundHeight(at.X, at.Z)) + " m)";
        }
        Vector3 ground = (Vector3)hit["position"];
        return Signed(at.Y - ground.Y) + " m";
    }

    private static void CarrierBodies(Node node, Godot.Collections.Array<Rid> bodies)
    {
        if (node is CollisionObject3D body) { bodies.Add(body.GetRid()); }
        foreach (Node child in node.GetChildren()) { CarrierBodies(child, bodies); }
    }

    /// <summary>Each text label under the sign: its words and whether it shows.</summary>
    private static string LabelTexts(Node3D node)
    {
        StringBuilder text = new();
        foreach (Node child in node.FindChildren("*", "Label3D", true, false))
        {
            if (child is not Label3D label) { continue; }
            if (text.Length > 0) { text.Append(" || "); }
            text.Append('"').Append(label.Text.Replace("\n", " / ")).Append('"');
            text.Append(label.IsVisibleInTree() ? "" : " [hidden]");
        }
        return text.Length == 0 ? "none" : text.ToString();
    }

    /// <summary>The node of the kind nearest to a point, or null.</summary>
    private static Node3D? Nearest(Node root, Type kind, Vector3 from)
    {
        Node3D? best = null;
        float bestDistance = float.MaxValue;
        Walk(root);
        return best;

        void Walk(Node current)
        {
            if (current is Node3D found && kind.IsInstanceOfType(found) && found.IsInsideTree())
            {
                float distance = found.GlobalPosition.DistanceTo(from);
                if (distance < bestDistance) { best = found; bestDistance = distance; }
            }
            foreach (Node child in current.GetChildren()) { Walk(child); }
        }
    }

    private static T? Find<T>(Node node) where T : class
    {
        if (node is T found) { return found; }
        foreach (Node child in node.GetChildren())
        {
            T? hit = Find<T>(child);
            if (hit != null) { return hit; }
        }
        return null;
    }

    /// <summary>A standing tree's trunk body (grouped as a tree trunk), or null.</summary>
    private static Node3D? FindTree(Node node)
    {
        // The trunk bodies sit at the chunk's origin; each tree's own collision cylinder is where the tree stands.
        if (node is CollisionShape3D shape && shape.IsInsideTree() && shape.GetParent() is StaticBody3D body
            && body.IsInGroup(DownhillRoguelite.Terrain.TerrainChunk.TreeGroup)) { return shape; }
        foreach (Node child in node.GetChildren())
        {
            Node3D? hit = FindTree(child);
            if (hit != null) { return hit; }
        }
        return null;
    }

    private static string Fmt(Vector3 v) => $"({Signed(v.X)}, {Signed(v.Y)}, {Signed(v.Z)})";

    private static string Signed(float value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private async Task Shoot(Vector3 eye, Vector3 target, string file)
    {
        _camera!.GlobalPosition = eye;
        _camera.LookAt(target, Vector3.Up);
        _camera.MakeCurrent();
        HideHud(GetTree().Root);
        await Seconds(0.5f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(file);
        _shots++;
        GD.Print("Saved " + file + ": " + error.ToString().ToLower(CultureInfo.InvariantCulture));
    }

    /// <summary>Hides the HUD and overlays (canvas layers) so only the world is in the picture.</summary>
    private static void HideHud(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is CanvasLayer layer) { layer.Visible = false; }
            HideHud(child);
        }
    }

    private async Task Seconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
