using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// One fittable part of a worn piece: a skinned garment (or one side of it) or the pieces riding a
/// bone. <see cref="Anchor"/> is the part's centre at rest in the body's frame, one metre per unit,
/// in skeleton space; a fit is a move, turn and scale about it.
/// </summary>
public sealed class ClothingFitPart
{
    private const string OriginalMeta = "clothing_fit_original";

    public string Key { get; }
    public Transform3D Anchor { get; }

    private readonly ClothingMeshFit? _mesh;
    private readonly int _side;
    private readonly BoneAttachment3D? _attachment;
    private readonly Transform3D _rest;

    public ClothingFitPart(string key, Transform3D anchor, ClothingMeshFit mesh, int side)
    {
        Key = key;
        Anchor = anchor;
        _mesh = mesh;
        _side = side;
    }

    public ClothingFitPart(string key, Transform3D anchor, BoneAttachment3D attachment, Transform3D rest)
    {
        Key = key;
        Anchor = anchor;
        _attachment = attachment;
        _rest = rest;
    }

    /// <summary>Applies a fit (body frame, metres), replacing any earlier one.</summary>
    public void Apply(Transform3D local)
    {
        Transform3D correction = Anchor * local * Anchor.AffineInverse();
        if (_mesh != null)
        {
            _mesh.Set(_side, correction);
            return;
        }
        if (_attachment == null || !GodotObject.IsInstanceValid(_attachment))
        {
            return;
        }
        if (_attachment is BackpackAttachment backpack)
        {
            backpack.ApplyAdjustment(local);
            return;
        }
        foreach (Node child in _attachment.GetChildren())
        {
            if (child is Node3D node)
            {
                node.Transform = _rest.AffineInverse() * correction * _rest * Original(node);
            }
        }
    }

    /// <summary>The node's transform before any fit (remembered on first use).</summary>
    public static Transform3D Original(Node3D node)
    {
        if (!node.HasMeta(OriginalMeta))
        {
            node.SetMeta(OriginalMeta, node.Transform);
        }
        return node.GetMeta(OriginalMeta).AsTransform3D();
    }
}
