using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Imported skins require matching bind joints AND global rest transforms in skeleton space: exactly,
/// or within a tolerance (metres apart, and RestTurnLimit degrees) for garments made for a sister body
/// of the same rig, whose bones then follow this body's.
/// </summary>
public static class ClothingCompatibility
{
    public const float RestTurnLimit = 30.0f;

    public static bool TryBind(MeshInstance3D source, Skeleton3D sourceRig, Skeleton3D target, out Skin skin, out string reason)
    {
        return TryBind(source, sourceRig, target, 0.0f, out skin, out reason);
    }

    public static bool TryBind(MeshInstance3D source, Skeleton3D sourceRig, Skeleton3D target, float tolerance, out Skin skin, out string reason)
    {
        skin = new Skin();
        reason = "";
        if (source.Mesh == null || source.Skin == null || source.Skin.GetBindCount() == 0)
        { reason = "Garment has no mesh or skin binds"; return false; }
        for (int i = 0; i < source.Skin.GetBindCount(); i++)
        {
            StringName name = source.Skin.GetBindName(i);
            int origin = sourceRig.FindBone(name);
            if (name.IsEmpty) { origin = source.Skin.GetBindBone(i); }
            if (origin < 0 || origin >= sourceRig.GetBoneCount()) { reason = "Invalid source bind"; return false; }
            int destination = target.FindBone(sourceRig.GetBoneName(origin));
            if (destination < 0 || !RestsMatch(sourceRig.GetBoneGlobalRest(origin), target.GetBoneGlobalRest(destination), tolerance))
            { reason = "Rig rests/proportions do not match; a fitted variant is required"; return false; }
            skin.AddBind(destination, source.Skin.GetBindPose(i));
        }
        return true;
    }

    private static bool RestsMatch(Transform3D a, Transform3D b, float tolerance)
    {
        if (tolerance <= 0.0f)
        {
            return a.IsEqualApprox(b);
        }
        if (a.Origin.DistanceTo(b.Origin) > tolerance)
        {
            return false;
        }
        Quaternion turn = (a.Basis.GetRotationQuaternion().Inverse() * b.Basis.GetRotationQuaternion()).Normalized();
        // The short way round (a quaternion's angle runs to 360°).
        float degrees = Mathf.RadToDeg(turn.GetAngle());
        if (degrees > 180.0f)
        {
            degrees = 360.0f - degrees;
        }
        return degrees <= RestTurnLimit;
    }
}
