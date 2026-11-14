using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// A pack on the carrier. The fit (computed once in rest/body space, plus any hand fit) says where it
/// sits on the back; the imported model keeps its own transform under a fit pivot. While its harness
/// simulates it (BackpackHarness), the pivot is set free of the bone and hangs where the straps hold
/// it; the fit is then only where it starts and what the straps' lengths are measured from.
/// </summary>
public partial class BackpackAttachment : BoneAttachment3D
{
    [Export] public Vector3 LocalOffset { get; set; } = new(0, -0.08f, 0.24f);
    [Export] public Vector3 LocalRotationDegrees { get; set; }
    [Export] public Vector3 ModelScale { get; set; } = Vector3.One;
    [Export] public bool FitToBody { get; set; } = true;
    [Export] public float Clearance { get; set; } = 0.015f;
    /// <summary>Legacy centre offset; changing LocalOffset.Z adds/subtracts from automatic placement.</summary>
    [Export] public float ReferenceBackOffset { get; set; } = 0.24f;
    [Export] public float TorsoWidthShare { get; set; } = 0.35f;
    [Export] public float TorsoSampleHalfHeight { get; set; } = 0.12f;
    public Node3D Pivot { get; private set; } = null!;
    public Node3D Model { get; private set; } = null!;
    public bool MeasuredBody { get; private set; }
    public float BodyBack { get; private set; }
    public Vector3 EffectiveOffset { get; private set; }
    public string Problem { get; private set; } = "";
    public string EquippedItemId { get; set; } = "";
    /// <summary>Unadjusted pack centre in the skeleton's rest/body frame, in metre units.</summary>
    public Transform3D FitAnchor { get; private set; } = Transform3D.Identity;
    private Transform3D _placement = Transform3D.Identity;
    private Transform3D _anchorInBone = Transform3D.Identity;
    private Transform3D _adjustment = Transform3D.Identity;
    private Transform3D _fitted = Transform3D.Identity;

    /// <summary>Counts every change of the fit (the harness starts over from a new fit).</summary>
    public int FitVersion { get; private set; }
    /// <summary>The fit itself: where the pivot sits under the bone when nothing swings it (tests, tools).</summary>
    public Transform3D FitTransform => _fitted;
    /// <summary>The harness moves the pivot (it hangs from the straps, not from the bone).</summary>
    public bool Simulated { get; private set; }
    /// <summary>Where the fit puts the pivot now, in world space (follows the bone).</summary>
    /// <summary>
    /// Where the fit puts the pivot for the skeleton's current pose, read from the bone itself (this node
    /// may still hold the last pose while the skeleton's update is being handled).
    /// </summary>
    public Transform3D FittedPivot
    {
        get
        {
            if (GetParent() is Skeleton3D rig && BoneIdx >= 0 && BoneIdx < rig.GetBoneCount())
            {
                return rig.GlobalTransform * rig.GetBoneGlobalPose(BoneIdx) * _fitted;
            }
            return GlobalTransform * _fitted;
        }
    }

    /// <summary>Sets the pivot where the straps hold the pack (world space), free of the bone.</summary>
    public void Simulate(Transform3D pivot)
    {
        if (!pivot.IsFinite()) { return; }
        if (!Simulated)
        {
            Simulated = true;
            Pivot.TopLevel = true;
        }
        Pivot.GlobalTransform = pivot;
    }

    /// <summary>Back on the bone, exactly at the fit.</summary>
    public void StopSimulating()
    {
        if (!Simulated) { return; }
        Simulated = false;
        Pivot.TopLevel = false;
        Pivot.Transform = _fitted;
    }

    /// <summary>The pack's extent in the pivot's frame (metres, as the model is placed).</summary>
    public Aabb PackBounds()
    {
        // A model can name its main body (metadata harness_bounds, in its own space), so the harness
        // and the swing leave out pouches and other parts hung on its sides.
        if (Model.HasMeta("harness_bounds") && Model.GetMeta("harness_bounds").VariantType == Variant.Type.Aabb)
        {
            return Model.Transform * Model.GetMeta("harness_bounds").AsAabb();
        }
        Aabb bounds = ModelFit.Bounds(Model);
        if (Model is MeshInstance3D own && own.Mesh != null)
        {
            if (bounds.Size.LengthSquared() < 0.000001f) { bounds = own.Mesh.GetAabb(); }
            else { bounds = bounds.Merge(own.Mesh.GetAabb()); }
        }
        return Model.Transform * bounds;
    }

    /// <summary>The fitted pivot in the skeleton's rest pose, in the skeleton's space.</summary>
    public Transform3D RestPivot(Skeleton3D rig)
    {
        Transform3D placed = _anchorInBone * _adjustment * _anchorInBone.AffineInverse() * _placement;
        return rig.GetBoneGlobalRest(BoneIdx) * placed;
    }

    /// <summary>Replaces the authored hand fit; automatic refitting preserves it without stacking.</summary>
    public bool ApplyAdjustment(Transform3D local)
    {
        if (!local.IsFinite() || Mathf.Abs(local.Basis.Determinant()) < 0.000001f) { return false; }
        _adjustment = local;
        ApplyPlacement();
        return true;
    }

    private void ApplyPlacement()
    {
        _fitted = _anchorInBone * _adjustment * _anchorInBone.AffineInverse() * _placement;
        FitVersion++;
        // A new fit is a fresh start: the pack goes there at once (the harness hangs it from its
        // straps again on the next skeleton update).
        StopSimulating();
        Pivot.Transform = _fitted;
    }

    public static BackpackAttachment? Create(Skeleton3D rig, Node3D model, string bone,
        Vector3 offset, Vector3 rotation, Vector3 scale, bool fit, float clearance)
    {
        int index = ClothingRig.Bone(rig, bone);
        if (index < 0 || !rig.IsInsideTree())
        {
            model.Free();
            GD.PushWarning("BackpackAttachment: missing attachment bone '" + bone + "'.");
            return null;
        }
        BackpackAttachment attachment = new()
        {
            Name = "BackpackAttachment", BoneName = rig.GetBoneName(index), BoneIdx = index,
            LocalOffset = offset, LocalRotationDegrees = rotation, ModelScale = scale,
            FitToBody = fit, Clearance = clearance,
            Model = model, Pivot = new Node3D { Name = "BackpackPivot" },
        };
        attachment.Pivot.AddChild(model);
        attachment.AddChild(attachment.Pivot);
        if (!attachment.Align(rig))
        {
            GD.PushWarning("BackpackAttachment: " + attachment.Problem);
            attachment.Free();
            return null;
        }
        // The harness (straps, sternum strap, hip belt) comes with every pack; it hangs the pack on them.
        attachment.AddChild(new BackpackHarness { Name = "BackpackHarness" });
        rig.AddChild(attachment);
        return attachment;
    }

    /// <summary>Explicit developer adjustment; not called every frame and never changes inventory.</summary>
    public bool Refit()
    {
        if (GetParent() is not Skeleton3D rig) { return false; }
        return Align(rig);
    }

    private bool Align(Skeleton3D rig)
    {
        Problem = "";
        if (!LocalOffset.IsFinite() || !LocalRotationDegrees.IsFinite() || !ModelScale.IsFinite()
            || ModelScale.X <= 0 || ModelScale.Y <= 0 || ModelScale.Z <= 0
            || !float.IsFinite(Clearance) || Clearance < 0 || !Model.Transform.IsFinite()
            || Mathf.Abs(Model.Transform.Basis.Determinant()) < 0.000001f
            || !float.IsFinite(ReferenceBackOffset) || !float.IsFinite(TorsoWidthShare) || TorsoWidthShare <= 0
            || !float.IsFinite(TorsoSampleHalfHeight) || TorsoSampleHalfHeight <= 0)
        { Problem = "invalid offset, rotation, scale or clearance"; return false; }
        if (!ClothingRig.Frame(rig, out Basis frame, out float metres))
        { Problem = "no supported body frame; check hips, head and leg bones"; return false; }
        Vector3 rigScale = rig.GlobalBasis.Scale.Abs();
        if (Mathf.Abs(rigScale.X - metres) > metres * 0.01f || Mathf.Abs(rigScale.Z - metres) > metres * 0.01f)
        { Problem = "non-uniform skeleton scale needs an authored fit"; return false; }
        Aabb bounds = ModelFit.Bounds(Model);
        if (Model is MeshInstance3D own && own.Mesh != null)
        {
            if (bounds.Size.LengthSquared() < 0.000001f) { bounds = own.Mesh.GetAabb(); }
            else { bounds = bounds.Merge(own.Mesh.GetAabb()); }
        }
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || bounds.Size.LengthSquared() < 0.000001f)
        { Problem = "backpack scene has no usable mesh bounds"; return false; }
        int bone = ClothingRig.Bone(rig, BoneName);
        if (bone < 0) { Problem = "attachment bone no longer exists"; return false; }
        Transform3D rest = rig.GetBoneGlobalRest(bone);
        Basis authored = Basis.FromEuler(LocalRotationDegrees * Mathf.Pi / 180) * Basis.FromScale(ModelScale);
        Aabb footprint = new Transform3D(authored, Vector3.Zero) * Model.Transform * bounds;
        Vector3 offset = LocalOffset;
        MeasuredBody = false;
        if (FitToBody && MeasureBack(rig, frame, metres, rest.Origin, LocalOffset.Y, out float back))
        {
            MeasuredBody = true;
            BodyBack = back;
            offset.X -= footprint.GetCenter().X;
            offset.Y -= footprint.GetCenter().Y;
            offset.Z = back + Clearance - footprint.Position.Z + LocalOffset.Z - ReferenceBackOffset;
        }
        EffectiveOffset = offset;
        Transform3D bodyPlacement = new((frame * authored).Scaled(Vector3.One / metres), rest.Origin + frame * (offset / metres));
        _placement = rest.AffineInverse() * bodyPlacement;
        FitAnchor = new Transform3D(frame.Scaled(Vector3.One / metres),
            rest.Origin + frame * ((offset + footprint.GetCenter()) / metres));
        _anchorInBone = rest.AffineInverse() * FitAnchor;
        ApplyPlacement();
        SetMeta("fit_measured_body", MeasuredBody);
        SetMeta("equipped_item", EquippedItemId);
        return true;
    }

    private bool MeasureBack(Skeleton3D rig, Basis frame, float metres, Vector3 anchor, float height, out float back)
    {
        back = float.NegativeInfinity;
        Basis toBody = frame.Transposed();
        float shoulders = ClothingRig.Rest(rig, "LeftArm").DistanceTo(ClothingRig.Rest(rig, "RightArm")) * metres;
        float halfWidth = Mathf.Max(shoulders * TorsoWidthShare, 0.06f);
        int samples = 0;
        foreach (Node node in rig.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh is not ArrayMesh array || mesh.Skin == null
                || mesh.IsQueuedForDeletion() || IsAncestorOf(mesh)) { continue; }
            Transform3D toRig = rig.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
            {
                Godot.Collections.Array arrays = array.SurfaceGetArrays(surface);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 body = toBody * (toRig * vertex - anchor) * metres;
                    if (Mathf.Abs(body.X) > halfWidth || Mathf.Abs(body.Y - height) > TorsoSampleHalfHeight) { continue; }
                    back = Mathf.Max(back, body.Z);
                    samples++;
                }
            }
        }
        return samples >= 8 && float.IsFinite(back);
    }
}
