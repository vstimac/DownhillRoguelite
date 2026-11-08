using Godot;

namespace DownhillRoguelite.Player;

/// <summary>Semantic joints on the two rigs actually shipped here; not a promise about arbitrary GLBs.</summary>
public static class ClothingRig
{
    public static int Bone(Skeleton3D rig, string name)
    {
        int found = RigFollower.FindBone(rig, name);
        if (found >= 0) { return found; }
        string alias = name switch
        {
            "Hips" => "hips", "Spine" => "spine.001", "Spine1" => "spine.002", "Spine2" => "spine.003",
            "Neck" => "neck", "Head" => "head", "LeftUpLeg" => "thigh.L", "RightUpLeg" => "thigh.R",
            "LeftLeg" => "shin.L", "RightLeg" => "shin.R", "LeftFoot" => "foot.L", "RightFoot" => "foot.R",
            "LeftArm" => "upper_arm.L", "RightArm" => "upper_arm.R", "LeftForeArm" => "forearm.L", "RightForeArm" => "forearm.R",
            "LeftHand" => "hand.L", "RightHand" => "hand.R", "LeftHandMiddle1" => "f_middle.01.L", "RightHandMiddle1" => "f_middle.01.R",
            _ => name,
        };
        return rig.FindBone("DEF-" + alias);
    }

    public static Vector3 Rest(Skeleton3D rig, string name)
    {
        int bone = Bone(rig, name);
        if (bone < 0) { return Vector3.Zero; }
        return rig.GetBoneGlobalRest(bone).Origin;
    }

    public static bool Frame(Skeleton3D rig, out Basis frame, out float metres)
    {
        frame = Basis.Identity;
        metres = rig.GlobalBasis.Scale.Abs().Y;
        if (metres < 0.00001f || Bone(rig, "Hips") < 0 || Bone(rig, "Head") < 0) { return false; }
        Vector3 up = (Rest(rig, "Head") - Rest(rig, "Hips")).Normalized();
        Vector3 right = (Rest(rig, "RightUpLeg") - Rest(rig, "LeftUpLeg")).Normalized();
        Vector3 back = right.Cross(up).Normalized();
        if (up.LengthSquared() < 0.9f || back.LengthSquared() < 0.9f) { return false; }
        frame = new Basis(up.Cross(back).Normalized(), up, back);
        return true;
    }
}
