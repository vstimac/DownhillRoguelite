using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A set piece built in the world: each part's model scaled to its height, drawn in the mountain's
/// palette (WorldStyle) and set on the ground where it stands (so a piece on a slope follows it).
/// Every part that looks solid and stands taller than a knee blocks the carrier, whatever its
/// Solid flag says (only vines and the like are soft): a box round small parts, the model's own
/// shape for big architecture (so an arch's opening stays open and a banner's cloth doesn't block).
/// </summary>
public partial class SetPieceNode : Node3D
{
    public SetPiece? Piece { get; private set; }
    public int SolidCount { get; private set; }
    /// <summary>Parts at least this tall (m) collide with their own shape instead of a box round them.</summary>
    public const float ShapedHeight = 2.5f;
    public int PartCount { get; private set; }
    // Direct callers also retain imported scene wrappers until this built piece is released.
    private readonly List<PackedScene> _modelSources = new();
    private readonly HashSet<string> _modelProblems = new(StringComparer.Ordinal);

    /// <summary>Builds the piece at <paramref name="centre"/> turned by <paramref name="yaw"/> (radians), on the route's ground.</summary>
    public void Build(SetPiece piece, Vector3 centre, float yaw, IRouteQuery route, float sink,
        Func<string, Node3D?>? createModel = null)
    {
        Piece = piece;
        Name = "SetPiece_" + piece.Id;
        Position = centre;
        Basis turn = new(Vector3.Up, yaw);
        foreach (SetPiecePart part in piece.Parts)
        {
            if (part == null)
            {
                continue;
            }
            Node3D? model;
            if (createModel != null) { model = createModel(part.Model); }
            else { model = CreateModel(part.Model); }
            if (model == null) { continue; }
            DownhillRoguelite.Core.SurfaceType? floor = WorldStyle.FloorOf(model, part.Model);
            WorldStyle.Restyle(model, part.Model);
            Node3D holder = new() { Name = "Model" };
            holder.AddChild(model);
            Aabb bounds = ModelFit.Bounds(holder);
            float scale = part.Height / Mathf.Max(bounds.Size.Y, 0.01f);
            Vector3 size = bounds.Size * scale;
            holder.Transform = ModelFit.FitOnGround(bounds, size, true);

            Vector3 offset = turn * new Vector3(part.Offset.X, 0.0f, part.Offset.Z);
            float x = centre.X + offset.X;
            float z = centre.Z + offset.Z;
            float ground = route.GroundHeight(x, z) - sink + part.Offset.Y;
            Basis basis = new Basis(Vector3.Up, yaw + Mathf.DegToRad(part.Yaw))
                * Basis.FromEuler(new Vector3(Mathf.DegToRad(part.Tilt.X), 0.0f, Mathf.DegToRad(part.Tilt.Y)));
            Transform3D place = new(basis, new Vector3(offset.X, ground - centre.Y, offset.Z));
            if (Blocks(part, size.Y))
            {
                StaticBody3D body = new() { Name = "Solid", Transform = place };
                if (floor != null) { body.SetMeta(DownhillRoguelite.Core.SurfaceType.MetaKey, floor); }
                body.AddChild(holder);
                Shape3D? shaped = size.Y >= ShapedHeight || Shaped(part.Model) ? ShapeOf(holder) : null;
                if (shaped != null)
                {
                    body.AddChild(new CollisionShape3D { Shape = shaped });
                }
                else
                {
                    body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0.0f, size.Y * 0.5f, 0.0f) });
                }
                AddChild(body);
                SolidCount++;
            }
            else
            {
                Node3D node = new() { Name = "Part", Transform = place };
                node.AddChild(holder);
                AddChild(node);
            }
            PartCount++;
        }
    }

    /// <summary>A part blocks the carrier when it is marked solid, or looks solid and stands taller than a knee.</summary>
    public static bool Blocks(SetPiecePart part, float height)
    {
        return part.Solid || (height >= WorldStyle.KneeHeight && !Soft(part.Model));
    }

    /// <summary>Plants hanging on a wall: seen, never bumped into.</summary>
    public static bool Soft(string model)
    {
        string file = model.GetFile().ToLowerInvariant();
        return file.Contains("vine") || file.Contains("ivy") || file.Contains("moss");
    }

    /// <summary>Parts whose box would block where nothing is (an arch's opening, a banner's free cloth).</summary>
    private static bool Shaped(string model)
    {
        string file = model.GetFile().ToLowerInvariant();
        return file.Contains("arch") || file.Contains("flag") || file.Contains("banner") || file.Contains("pennant")
            || file.Contains("fence") || file.Contains("stall") || file.Contains("wagon") || file.Contains("cart");
    }

    /// <summary>The model's own triangles (in the part's space, scaled) as one static shape; null when it has none.</summary>
    private static Shape3D? ShapeOf(Node3D holder)
    {
        List<Vector3> faces = new();
        foreach (Node node in holder.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null) { continue; }
            Transform3D toHolder = Transform3D.Identity;
            for (Node? at = mesh; at != null && at != holder; at = at.GetParent())
            {
                if (at is Node3D step) { toHolder = step.Transform * toHolder; }
            }
            Transform3D toPart = holder.Transform * toHolder;
            foreach (Vector3 corner in mesh.Mesh.GetFaces())
            {
                faces.Add(toPart * corner);
            }
        }
        if (faces.Count < 3) { return null; }
        ConcavePolygonShape3D shape = new() { BackfaceCollision = true };
        shape.SetFaces(faces.ToArray());
        return shape;
    }

    private Node3D? CreateModel(string path)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene || !scene.CanInstantiate())
        {
            ReportModel(path, "missing or non-instantiable scene");
            return null;
        }
        _modelSources.Add(scene);
        Node instance = scene.Instantiate();
        if (instance is Node3D model) { return model; }
        instance.Free();
        ReportModel(path, "scene root is not Node3D");
        return null;
    }

    private void ReportModel(string path, string problem)
    {
        if (_modelProblems.Add(path)) { GD.PushWarning($"SetPieceNode {Piece?.Id}: {path}: {problem}."); }
    }
}
