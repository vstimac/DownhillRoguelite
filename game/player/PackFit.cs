using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The pack's placement as set by hand in the editor (res://player/PackFit.tscn): a scene with the body in its
/// rest pose and the pack under a node named BackpackPivot. Its place is read once, relative to the skeleton,
/// so the saved scene works on any body with the same skeleton. The straps are only drawn: the pack stays
/// where it was placed.
/// </summary>
public static class PackFit
{
    public const string ScenePath = "res://player/PackFit.tscn";

    private static bool _read;
    private static bool _found;
    private static Transform3D _skeletonPivot = Transform3D.Identity;

    /// <summary>The placed pack's transform in the skeleton's space, if the scene exists and holds one.</summary>
    public static bool TryGet(out Transform3D skeletonPivot)
    {
        if (!_read)
        {
            _read = true;
            _found = Read(out _skeletonPivot);
        }
        skeletonPivot = _skeletonPivot;
        return _found;
    }

    private static bool Read(out Transform3D skeletonPivot)
    {
        skeletonPivot = Transform3D.Identity;
        if (!ResourceLoader.Exists(ScenePath) || ResourceLoader.Load(ScenePath) is not PackedScene scene)
        {
            return false;
        }
        Node? root = scene.Instantiate();
        if (root == null)
        {
            return false;
        }
        try
        {
            Node3D? pivot = root.GetNodeOrNull<Node3D>("BackpackPivot");
            Skeleton3D? skeleton = root.FindChild("Skeleton3D", true, false) as Skeleton3D;
            if (pivot == null || skeleton == null)
            {
                GD.PushWarning("PackFit: the scene needs a BackpackPivot and a Skeleton3D.");
                return false;
            }
            // The chain of local transforms up to the scene's root (the scene is not in the tree).
            skeletonPivot = Within(skeleton, root).AffineInverse() * Within(pivot, root);
            return skeletonPivot.IsFinite();
        }
        finally
        {
            root.Free();
        }
    }

    private static Transform3D Within(Node3D node, Node root)
    {
        Transform3D result = Transform3D.Identity;
        Node? current = node;
        while (current is Node3D link && current != root)
        {
            result = link.Transform * result;
            current = link.GetParent();
        }
        return result;
    }
}
