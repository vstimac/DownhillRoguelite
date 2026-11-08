using System.Collections.Generic;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>Clearly labelled low-poly test garments, genuinely skinned to measured joints of the current rig.</summary>
public static class ClothingPrototypes
{
    public static MeshInstance3D? Garment(Skeleton3D rig, ClothingVisual definition)
    {
        if (!ClothingRig.Frame(rig, out Basis frame, out float metres)) { return null; }
        List<Vector3> vertices = new(), normals = new();
        List<Vector2> uv = new();
        List<int> bones = new(), triangles = new();
        List<float> weights = new();
        const int sides = 20;
        void Tube(Vector3 from, Vector3 to, float width, float depth, int first, int last)
        {
            if (first < 0 || last < 0 || from.DistanceSquaredTo(to) < 0.00001f) { return; }
            Vector3 axis = (to - from).Normalized();
            Vector3 right = frame.X;
            if (Mathf.Abs(right.Dot(axis)) > 0.9f) { right = frame.Z; }
            Vector3 back = right.Cross(axis).Normalized();
            right = axis.Cross(back).Normalized();
            int start = vertices.Count;
            for (int ring = 0; ring < 4; ring++)
            {
                float t = ring / 3.0f;
                for (int segment = 0; segment < sides; segment++)
                {
                    float angle = Mathf.Tau * segment / sides;
                    Vector3 normal = (right * Mathf.Cos(angle) + back * Mathf.Sin(angle)).Normalized();
                    vertices.Add(from.Lerp(to, t) + right * Mathf.Cos(angle) * width / metres + back * Mathf.Sin(angle) * depth / metres);
                    normals.Add(normal);
                    uv.Add(new Vector2(segment / (float)sides, t));
                    bones.AddRange(new[] { first, last, 0, 0 });
                    weights.AddRange(new[] { 1.0f - t, t, 0.0f, 0.0f });
                    if (ring < 3)
                    {
                        int a = start + ring * sides + segment;
                        int b = start + ring * sides + (segment + 1) % sides;
                        triangles.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
                    }
                }
            }
        }
        void Limb(string from, string to, float radius, float fraction = 1)
        {
            Tube(ClothingRig.Rest(rig, from), ClothingRig.Rest(rig, from).Lerp(ClothingRig.Rest(rig, to), fraction),
                radius, radius, ClothingRig.Bone(rig, from), ClothingRig.Bone(rig, to));
        }
        float slack = Mathf.Clamp(definition.FitSlack, 0.005f, 0.08f);
        if (definition.Kind == ClothingVisual.VisualKind.PrototypeTrousers)
        {
            Vector3 hips = ClothingRig.Rest(rig, "Hips");
            Tube(hips - frame.Y * 0.13f / metres, hips + frame.Y * 0.09f / metres, 0.18f + slack, 0.12f + slack,
                ClothingRig.Bone(rig, "Hips"), ClothingRig.Bone(rig, "Hips"));
            foreach (string side in new[] { "Left", "Right" })
            {
                Limb(side + "UpLeg", side + "Leg", 0.09f + slack);
                Limb(side + "Leg", side + "Foot", 0.06f + slack);
            }
        }
        else if (definition.Kind == ClothingVisual.VisualKind.PrototypeGloves)
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                int hand = ClothingRig.Bone(rig, side + "Hand");
                if (hand < 0) { continue; }
                Vector3 from = ClothingRig.Rest(rig, side + "Hand");
                Vector3 to = ClothingRig.Rest(rig, side + "HandMiddle1");
                if (ClothingRig.Bone(rig, side + "HandMiddle1") < 0)
                { to = from + (from - ClothingRig.Rest(rig, side + "ForeArm")).Normalized() * 0.085f / metres; }
                Tube(from, to, 0.045f + slack, 0.03f + slack, hand, hand);
            }
        }
        else
        {
            float width = (ClothingRig.Rest(rig, "LeftArm") - ClothingRig.Rest(rig, "RightArm")).Length() * metres * 0.52f + slack;
            width = Mathf.Clamp(width, 0.18f, 0.34f);
            Vector3 hips = ClothingRig.Rest(rig, "Hips");
            Vector3 neck = ClothingRig.Rest(rig, "Neck");
            Tube(hips - frame.Y * 0.045f / metres, neck - frame.Y * 0.04f / metres, width, 0.135f + slack,
                ClothingRig.Bone(rig, "Hips"), ClothingRig.Bone(rig, "Spine2"));
            foreach (string side in new[] { "Left", "Right" })
            {
                if (definition.Kind == ClothingVisual.VisualKind.PrototypeJacket)
                {
                    Limb(side + "Arm", side + "ForeArm", 0.075f + slack);
                    Limb(side + "ForeArm", side + "Hand", 0.06f + slack);
                }
                else { Limb(side + "Arm", side + "ForeArm", 0.075f + slack, 0.65f); }
            }
        }
        if (vertices.Count == 0) { return null; }
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arrays[(int)Mesh.ArrayType.Bones] = bones.ToArray();
        arrays[(int)Mesh.ArrayType.Weights] = weights.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = triangles.ToArray();
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        Skin skin = new();
        for (int i = 0; i < rig.GetBoneCount(); i++) { skin.AddBind(i, rig.GetBoneGlobalRest(i).AffineInverse()); }
        MeshInstance3D garment = new() { Name = "PrototypeGarment", Mesh = mesh, Skin = skin, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided };
        rig.AddChild(garment);
        garment.Skeleton = garment.GetPathTo(rig);
        return garment;
    }

    public static Node3D Backpack()
    {
        Node3D root = new() { Name = "PrototypeBackpack" };
        Color canvas = new(0.36f, 0.29f, 0.18f);
        ItemModels.Part(root, new BoxMesh { Size = new Vector3(0.39f, 0.51f, 0.22f) }, canvas, Vector3.Zero);
        ItemModels.Part(root, new BoxMesh { Size = new Vector3(0.42f, 0.09f, 0.25f) }, canvas.Lightened(0.12f), new Vector3(0, 0.235f, 0));
        ItemModels.Part(root, new BoxMesh { Size = new Vector3(0.27f, 0.19f, 0.07f) }, canvas, new Vector3(0, -0.07f, 0.14f));
        foreach (float x in new[] { -0.13f, 0.13f })
        {
            ItemModels.Part(root, new BoxMesh { Size = new Vector3(0.045f, 0.48f, 0.018f) }, ItemModels.Leather, new Vector3(x, 0, 0.123f));
            ItemModels.Part(root, new BoxMesh { Size = new Vector3(0.055f, 0.065f, 0.025f) }, new Color(0.55f, 0.48f, 0.28f), new Vector3(x, -0.12f, 0.14f));
        }
        return root;
    }
}
