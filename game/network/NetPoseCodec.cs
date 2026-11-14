using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>Bounded key-body pose, sampled at network rate, never all bones or per-frame IK inputs.</summary>
public static class NetPoseCodec
{
    public static readonly string[] Bones =
    {
        "DEF-hips", "DEF-spine.001", "DEF-spine.002", "DEF-spine.003", "DEF-neck", "DEF-head",
        "DEF-shoulder.L", "DEF-upper_arm.L", "DEF-forearm.L", "DEF-hand.L",
        "DEF-shoulder.R", "DEF-upper_arm.R", "DEF-forearm.R", "DEF-hand.R",
        "DEF-thigh.L", "DEF-shin.L", "DEF-foot.L", "DEF-toe.L",
        "DEF-thigh.R", "DEF-shin.R", "DEF-foot.R", "DEF-toe.R",
    };
    public const int Header = 7;
    public const int BoneSize = 7;
    public const int WeaponStart = Header + 22 * BoneSize;
    public const int WeaponSize = 10;
    public const int Size = WeaponStart + WeaponSize * 2;
    public const int Falling = 1;
    public const int SwordVisible = 2;
    public const int ShieldVisible = 4;

    public static int[] Bind(Skeleton3D skeleton)
    {
        int[] indices = new int[Bones.Length];
        for (int i = 0; i < indices.Length; i++) { indices[i] = skeleton.FindBone(Bones[i]); }
        return indices;
    }

    public static bool Capture(Skeleton3D skeleton, Node3D model, MeleeCombat? melee, int[] indices, float[] data, Vector3 input = default, bool sprinting = false)
    {
        if (data.Length != Size || indices.Length != Bones.Length) { return false; }
        for (int i = 0; i < indices.Length; i++) { if (indices[i] < 0) { return false; } }
        Put(data, 0, model.Position);
        Put(data, 3, input); data[6] = 0;
        if (sprinting) { data[6] = 1; }
        for (int i = 0; i < indices.Length; i++)
        {
            Quaternion q = skeleton.GetBoneGlobalPose(indices[i]).Basis.Orthonormalized().GetRotationQuaternion().Normalized();
            Put(data, Header + i * BoneSize, skeleton.GetBoneGlobalPose(indices[i]).Origin);
            Put(data, Header + i * BoneSize + 3, q);
        }
        Transform3D sword = Transform3D.Identity;
        Transform3D shield = Transform3D.Identity;
        if (melee?.Sword != null)
        {
            int anchor = 0;
            if (melee.SwordInHand) { anchor = 13; }
            sword = skeleton.GetBoneGlobalPose(indices[anchor]) * melee.Sword.Transform;
        }
        if (melee?.Shield != null) { shield = skeleton.GetBoneGlobalPose(indices[8]) * melee.Shield.Transform; }
        Put(data, WeaponStart, sword);
        Put(data, WeaponStart + WeaponSize, shield);
        return Valid(data);
    }

    public static bool Valid(float[] data)
    {
        if (data.Length != Size) { return false; }
        foreach (float value in data) { if (!float.IsFinite(value)) { return false; } }
        if (Vector(data, 0).LengthSquared() > 16 || Vector(data, 3).LengthSquared() > 1.01f || (data[6] != 0 && data[6] != 1)) { return false; }
        for (int i = 0; i < Bones.Length; i++)
        {
            float length = Rotation(data, Header + i * BoneSize + 3).LengthSquared();
            if (length < 0.5f || length > 1.5f || Vector(data, Header + i * BoneSize).LengthSquared() > 64) { return false; }
        }
        for (int i = WeaponStart; i < Size; i += WeaponSize)
        {
            if (Vector(data, i).LengthSquared() > 100) { return false; }
            float length = Rotation(data, i + 3).LengthSquared();
            if (length < 0.5f || length > 1.5f) { return false; }
            Vector3 scale = Vector(data, i + 7).Abs();
            if (scale.X < 0.00001f || scale.Y < 0.00001f || scale.Z < 0.00001f
                || scale.X > 20 || scale.Y > 20 || scale.Z > 20) { return false; }
        }
        return true;
    }

    public static Vector3 Vector(float[] data, int start) => new(data[start], data[start + 1], data[start + 2]);
    public static void Interpolate(float[] from, float[] to, float weight, float[] target)
    {
        for (int i = 0; i < Header; i++) { target[i] = Mathf.Lerp(from[i], to[i], weight); }
        for (int i = 0; i < Bones.Length; i++)
        {
            int offset = Header + i * BoneSize;
            Put(target, offset, Vector(from, offset).Lerp(Vector(to, offset), weight));
            Put(target, offset + 3, Rotation(from, offset + 3).Normalized().Slerp(Rotation(to, offset + 3).Normalized(), weight));
        }
        for (int i = WeaponStart; i < Size; i += WeaponSize) { Put(target, i, Transform(from, to, i, weight)); }
    }
    public static Quaternion Rotation(float[] data, int start) => new(data[start], data[start + 1], data[start + 2], data[start + 3]);
    public static Transform3D Transform(float[] from, float[] to, int start, float weight)
    {
        Quaternion rotation = Rotation(from, start + 3).Normalized().Slerp(Rotation(to, start + 3).Normalized(), weight);
        Vector3 scale = Vector(from, start + 7).Lerp(Vector(to, start + 7), weight);
        return new Transform3D(new Basis(rotation).Scaled(scale), Vector(from, start).Lerp(Vector(to, start), weight));
    }
    private static void Put(float[] data, int start, Vector3 value)
    { data[start] = value.X; data[start + 1] = value.Y; data[start + 2] = value.Z; }
    private static void Put(float[] data, int start, Quaternion value)
    { data[start] = value.X; data[start + 1] = value.Y; data[start + 2] = value.Z; data[start + 3] = value.W; }
    private static void Put(float[] data, int start, Transform3D value)
    {
        Put(data, start, value.Origin);
        Put(data, start + 3, value.Basis.Orthonormalized().GetRotationQuaternion().Normalized());
        Put(data, start + 7, value.Basis.Scale);
    }
}
