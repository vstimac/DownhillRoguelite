using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Raises the left arm to carry a lit torch out in front (on the driver skeleton, so the shown
/// character and the torch in its hand follow): the upper arm swings forward by ArmRaise, the forearm
/// bends up by ElbowBend, and the arm tucks in toward the chest by ArmTuck (degrees, from the torch's
/// fit). It eases in when the torch is lit in the hand and out when it's put away or burns out, and
/// gives the arm back to the sword and shield while the sword is drawn. CarrierTorch adds it.
/// </summary>
public partial class TorchArm : SkeletonModifier3D
{
    [Export] public string UpperArmBone { get; set; } = "DEF-upper_arm.L";
    [Export] public string ForearmBone { get; set; } = "DEF-forearm.L";
    /// <summary>How fast it eases in and out (per second).</summary>
    [Export] public float Sharpness { get; set; } = 6.0f;

    public CarrierTorch? Torch { get; set; }
    /// <summary>How far the carry pose is in (0 down, 1 fully raised; tests).</summary>
    public float Weight => _weight;

    private float _weight;
    private int _upper = -1;
    private int _forearm = -1;
    private bool _found;

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton == null || Torch == null)
        {
            return;
        }
        if (!_found)
        {
            _found = true;
            _upper = RigFollower.FindBone(skeleton, UpperArmBone);
            _forearm = RigFollower.FindBone(skeleton, ForearmBone);
        }
        float target = 0.0f;
        if (Torch.Lit && (!GameEvents.SwordDrawn || Torch.Remote || Torch.Fitting))
        {
            target = 1.0f;
        }
        _weight = Mathf.Lerp(_weight, target, 1.0f - Mathf.Exp(-Sharpness * (float)delta));
        if (_weight < 0.001f || _upper < 0 || _forearm < 0)
        {
            return;
        }
        Node3D? body = Torch.GetParentOrNull<Node3D>();
        if (body == null)
        {
            return;
        }
        // The carrier's own axes, in the skeleton's space.
        Basis toSkeleton = skeleton.GlobalTransform.Basis.Orthonormalized().Inverse();
        Vector3 right = (toSkeleton * body.GlobalBasis.X).Normalized();
        Vector3 forward = (toSkeleton * -body.GlobalBasis.Z).Normalized();
        TorchFit fit = Torch.Fit;
        // Upper arm: forward about his right axis, then in toward the chest about his forward axis.
        Quaternion raise = new Quaternion(right, Mathf.DegToRad(fit.ArmRaise) * _weight)
            * new Quaternion(forward, Mathf.DegToRad(fit.ArmTuck) * _weight);
        Transform3D upper = skeleton.GetBoneGlobalPose(_upper);
        skeleton.SetBoneGlobalPose(_upper, new Transform3D(new Basis(raise) * upper.Basis, upper.Origin));
        // Forearm (now moved with the upper arm): bent further up at the elbow.
        Quaternion bend = new(right, Mathf.DegToRad(fit.ElbowBend) * _weight);
        Transform3D forearm = skeleton.GetBoneGlobalPose(_forearm);
        skeleton.SetBoneGlobalPose(_forearm, new Transform3D(new Basis(bend) * forearm.Basis, forearm.Origin));
    }
}
