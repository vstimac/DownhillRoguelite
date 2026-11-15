using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Map;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Photographs the first of each in-world sign kind and the first tree trunk, from close up, so the text,
/// the posts and the roots can be checked. Run it windowed (headless can't render):
/// `-- --shots-directory=C:/somewhere`. Stops after MaxWaitSeconds if a kind never shows up.
/// </summary>
public partial class SignShots : Node3D
{
    [Export] public float MaxWaitSeconds { get; set; } = 40.0f;

    public override async void _Ready()
    {
        string directory = ProjectSettings.GlobalizePath("res://../../sign-shots");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { directory = arg["--shots-directory=".Length..]; }
        }
        Directory.CreateDirectory(directory);
        await Seconds(3.0f);
        Camera3D camera = new() { Name = "SignCamera", Fov = 60.0f, Far = 300.0f };
        AddChild(camera);

        List<(string Name, Node3D Node)> targets = new();
        Type[] kinds = { typeof(JunctionSignpost), typeof(RouteSign), typeof(WaypointCairn), typeof(PathMarker), typeof(StationSite), typeof(WaysideSite) };
        float waited = 0.0f;
        while (waited < MaxWaitSeconds)
        {
            targets.Clear();
            foreach (Type kind in kinds)
            {
                Node3D? first = FindFirst(GetTree().Root, kind);
                if (first != null) { targets.Add((kind.Name, first)); }
            }
            if (targets.Count == kinds.Length) { break; }
            await Seconds(0.5f);
            waited += 0.5f;
        }
        int index = 0;
        foreach ((string name, Node3D node) in targets)
        {
            Vector3 at = node.GlobalPosition;
            Vector3 eye = at + new Vector3(2.6f, 1.5f, 2.6f);
            await Shoot(camera, eye, at + Vector3.Up * 1.1f, Path.Combine(directory, $"{index:D2}_{name}.png"));
            GD.Print($"SHOT {name} at {at}");
            index++;
        }
        Node3D? trunk = FindTree(GetTree().Root);
        if (trunk != null)
        {
            Vector3 at = trunk.GlobalPosition;
            await Shoot(camera, at + new Vector3(3.0f, 1.3f, 3.0f), at + Vector3.Up * 0.3f, Path.Combine(directory, $"{index:D2}_tree_base.png"));
            GD.Print($"SHOT tree base at {at}");
        }
        else
        {
            GD.Print("FAIL  sign shots: no tree trunk loaded");
        }
        GD.Print($"SIGN SHOTS: {index} signs, tree {(trunk != null ? "yes" : "no")}, directory {directory}");
        GetTree().Quit(0);
    }

    /// <summary>The first node of the kind in the tree (depth first), or null.</summary>
    private static Node3D? FindFirst(Node node, Type kind)
    {
        if (node is Node3D found && kind.IsInstanceOfType(found) && found.IsInsideTree()) { return found; }
        foreach (Node child in node.GetChildren())
        {
            Node3D? hit = FindFirst(child, kind);
            if (hit != null) { return hit; }
        }
        return null;
    }

    /// <summary>A standing tree's trunk body (grouped as a tree trunk), or null.</summary>
    private static Node3D? FindTree(Node node)
    {
        // (the trunk body is grouped; its shapes are checked below)
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

    private async Task Shoot(Camera3D camera, Vector3 eye, Vector3 target, string file)
    {
        camera.GlobalPosition = eye;
        camera.LookAt(target, Vector3.Up);
        camera.MakeCurrent();
        HideHud(GetTree().Root);
        await Seconds(0.5f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(file);
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
