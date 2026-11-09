using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Puts the hand-made fits (ClothingFit, made in debug/ClothingFitting.tscn) on worn pieces as they
/// are built, and splits a piece into its fittable parts for that scene. A piece is a node holding
/// skinned garments and/or BoneAttachment3Ds; its key is the item id or a base piece's name.
/// </summary>
public static class ClothingFitter
{
    /// <summary>Applies the saved fits of <paramref name="characterId"/> to the parts of a piece.</summary>
    public static void Fit(Skeleton3D? skeleton, string characterId, string key, Node? root)
    {
        if (skeleton == null || root == null || !GodotObject.IsInstanceValid(root))
        {
            return;
        }
        ClothingFit fit = ClothingFit.Load();
        if (!fit.Has(characterId))
        {
            return;
        }
        foreach (ClothingFitPart part in Parts(skeleton, key, root))
        {
            ClothingFitEntry? entry = fit.Find(characterId, part.Key);
            if (entry != null)
            {
                part.Apply(entry.Local);
            }
        }
    }

    /// <summary>
    /// The fittable parts of a piece: each skinned garment (split into "-Left" and "-Right" when its
    /// halves are apart) and each bone attachment; with more than one, each key gets the node's name.
    /// </summary>
    public static List<ClothingFitPart> Parts(Skeleton3D skeleton, string key, Node root)
    {
        List<ClothingFitPart> parts = new();
        if (!ClothingRig.Frame(skeleton, out Basis frame, out float metres))
        {
            return parts;
        }
        Basis anchorBasis = new(frame.X / metres, frame.Y / metres, frame.Z / metres);
        Vector3 midline = ClothingRig.Rest(skeleton, "Hips");
        List<MeshInstance3D> meshes = new();
        List<BoneAttachment3D> attachments = new();
        Collect(root, meshes, attachments);
        bool many = meshes.Count + attachments.Count > 1;
        foreach (MeshInstance3D mesh in meshes)
        {
            ClothingMeshFit? fit = ClothingMeshFit.For(mesh, skeleton, frame, midline);
            if (fit == null)
            {
                continue;
            }
            string name = key;
            if (many)
            {
                name += "-" + mesh.Name;
            }
            if (fit.Split)
            {
                parts.Add(new ClothingFitPart(name + "-Left", new Transform3D(anchorBasis, fit.Bounds(-1).GetCenter()), fit, -1));
                parts.Add(new ClothingFitPart(name + "-Right", new Transform3D(anchorBasis, fit.Bounds(1).GetCenter()), fit, 1));
            }
            else
            {
                parts.Add(new ClothingFitPart(name, new Transform3D(anchorBasis, fit.Bounds(0).GetCenter()), fit, 0));
            }
        }
        foreach (BoneAttachment3D attachment in attachments)
        {
            int bone = skeleton.FindBone(attachment.BoneName);
            if (bone < 0)
            {
                continue;
            }
            Transform3D rest = skeleton.GetBoneGlobalRest(bone);
            Aabb box = new(rest.Origin, Vector3.Zero);
            bool any = false;
            foreach (Node child in attachment.GetChildren())
            {
                if (child is Node3D node)
                {
                    AddBounds(node, rest * ClothingFitPart.Original(node), ref box, ref any);
                }
            }
            string name = key;
            if (many)
            {
                name += "-" + attachment.BoneName;
            }
            parts.Add(new ClothingFitPart(name, new Transform3D(anchorBasis, box.GetCenter()), attachment, rest));
        }
        return parts;
    }

    private static void Collect(Node node, List<MeshInstance3D> meshes, List<BoneAttachment3D> attachments)
    {
        if (node is BoneAttachment3D attachment)
        {
            attachments.Add(attachment);
            return;
        }
        if (node is MeshInstance3D mesh && mesh.Skin != null)
        {
            meshes.Add(mesh);
        }
        foreach (Node child in node.GetChildren())
        {
            Collect(child, meshes, attachments);
        }
    }

    private static void AddBounds(Node3D node, Transform3D transform, ref Aabb box, ref bool any)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            Aabb bounds = transform * mesh.GetAabb();
            if (!any)
            {
                box = bounds;
                any = true;
            }
            else
            {
                box = box.Merge(bounds);
            }
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D next)
            {
                AddBounds(next, transform * next.Transform, ref box, ref any);
            }
        }
    }
}
