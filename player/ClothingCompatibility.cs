using Godot;

namespace DownhillRoguelite.Player;

/// <summary>Imported skins require matching bind joints AND global rest transforms in skeleton space.</summary>
public static class ClothingCompatibility
{
    public static bool TryBind(MeshInstance3D source, Skeleton3D sourceRig, Skeleton3D target, out Skin skin, out string reason)
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
            if (destination < 0 || !sourceRig.GetBoneGlobalRest(origin).IsEqualApprox(target.GetBoneGlobalRest(destination)))
            { reason = "Rig rests/proportions do not match; a fitted variant is required"; return false; }
            skin.AddBind(destination, source.Skin.GetBindPose(i));
        }
        return true;
    }
}
