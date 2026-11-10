using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Helpers for dropping art-pack models into shapes the game already has (an item's cell block,
/// a boulder's collision box): bounds of a model, the scale that fits it, and a cache of the first
/// mesh of a model file so thousands of rocks can share one mesh instead of whole scenes.
/// </summary>
public static class ModelFit
{
    private static readonly Dictionary<string, (Mesh? Mesh, Transform3D Local)> Meshes = new();

    /// <summary>Bounds of every mesh under <paramref name="root"/>, in the root's space.</summary>
    public static Aabb Bounds(Node3D root)
    {
        Aabb bounds = new();
        bool first = true;
        Collect(root, Transform3D.Identity, ref bounds, ref first);
        return bounds;
    }

    private static void Collect(Node node, Transform3D toRoot, ref Aabb bounds, ref bool first)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is not Node3D child3D)
            {
                continue;
            }
            Transform3D transform = toRoot * child3D.Transform;
            if (child is MeshInstance3D mesh && mesh.Mesh != null)
            {
                Aabb box = transform * mesh.Mesh.GetAabb();
                if (first)
                {
                    bounds = box;
                    first = false;
                }
                else
                {
                    bounds = bounds.Merge(box);
                }
            }
            Collect(child, transform, ref bounds, ref first);
        }
    }

    /// <summary>
    /// A transform that fits <paramref name="bounds"/> inside a box of <paramref name="size"/> sitting
    /// on the origin (centred in X and Z, resting on Y = 0); uniform keeps the model's proportions.
    /// </summary>
    public static Transform3D FitOnGround(Aabb bounds, Vector3 size, bool uniform)
    {
        Vector3 extent = new(Mathf.Max(bounds.Size.X, 1e-4f), Mathf.Max(bounds.Size.Y, 1e-4f), Mathf.Max(bounds.Size.Z, 1e-4f));
        Vector3 scale = new(size.X / extent.X, size.Y / extent.Y, size.Z / extent.Z);
        if (uniform)
        {
            float s = Mathf.Min(scale.X, Mathf.Min(scale.Y, scale.Z));
            scale = new Vector3(s, s, s);
        }
        Vector3 centre = bounds.GetCenter();
        Vector3 offset = new(-centre.X * scale.X, -bounds.Position.Y * scale.Y, -centre.Z * scale.Z);
        return new Transform3D(Basis.FromScale(scale), offset);
    }

    /// <summary>A transform that fits bounds into a box centred on the origin (boulders, logs).</summary>
    public static Transform3D FitCentred(Aabb bounds, Vector3 size)
    {
        Vector3 extent = new(Mathf.Max(bounds.Size.X, 1e-4f), Mathf.Max(bounds.Size.Y, 1e-4f), Mathf.Max(bounds.Size.Z, 1e-4f));
        Vector3 scale = new(size.X / extent.X, size.Y / extent.Y, size.Z / extent.Z);
        Vector3 centre = bounds.GetCenter();
        return new Transform3D(Basis.FromScale(scale), -centre * scale);
    }

    /// <summary>The first mesh in a model file and its transform in the file's root (cached); null if it can't load.</summary>
    public static Mesh? FirstMesh(string path, out Transform3D local)
    {
        if (Meshes.TryGetValue(path, out (Mesh? Mesh, Transform3D Local) cached))
        {
            local = cached.Local;
            return cached.Mesh;
        }
        local = Transform3D.Identity;
        Mesh? found = null;
        if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is PackedScene scene)
        {
            Node root = scene.Instantiate();
            found = Find(root, Transform3D.Identity, ref local);
            root.Free();
        }
        Meshes[path] = (found, local);
        return found;
    }

    private static Mesh? Find(Node node, Transform3D toRoot, ref Transform3D local)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is not Node3D child3D)
            {
                continue;
            }
            Transform3D transform = toRoot * child3D.Transform;
            if (child is MeshInstance3D mesh && mesh.Mesh != null)
            {
                local = transform;
                return mesh.Mesh;
            }
            Mesh? deeper = Find(child, transform, ref local);
            if (deeper != null)
            {
                return deeper;
            }
        }
        return null;
    }

    /// <summary>Forgets cached meshes (scene changes, tests).</summary>
    public static void Clear()
    {
        Meshes.Clear();
    }
}
