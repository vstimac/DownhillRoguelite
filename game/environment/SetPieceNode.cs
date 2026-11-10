using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A set piece built in the world: each part's model scaled to its height and set on the ground
/// where it stands (so a piece on a slope follows it), with a box to bump into for solid parts.
/// </summary>
public partial class SetPieceNode : Node3D
{
    public SetPiece? Piece { get; private set; }
    public int SolidCount { get; private set; }
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
            if (part.Solid)
            {
                StaticBody3D body = new() { Name = "Solid", Transform = place };
                body.AddChild(holder);
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0.0f, size.Y * 0.5f, 0.0f) });
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
