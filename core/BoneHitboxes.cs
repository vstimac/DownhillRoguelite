using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Hit boxes that follow a creature's skinned body: one box per bone, fitted once to the mesh
/// vertices that bone moves most (in the bone's own space, trimmed of the few stray ones), then
/// carried by the bone's pose every time the skeleton updates (after its modifiers, so a model that
/// follows another rig is caught in its real pose). A blade is tested against these boxes, so a
/// blow lands where the body actually is (a deer's long back, a lifted arm), not on a standing capsule.
/// </summary>
public sealed class BoneHitboxes
{
    /// <summary>A bone needs this many vertices to get a box.</summary>
    public const int MinVertices = 12;
    /// <summary>The share of a bone's vertices trimmed off each end of each axis.</summary>
    public const float Trim = 0.03f;

    private static readonly Dictionary<ulong, BoneHitboxes> Cache = new();

    public int Count => _bones.Count;
    /// <summary>Times the boxes have followed the skeleton (its updates since the first query).</summary>
    public int Updates { get; private set; }

    private readonly List<int> _bones = new();
    private readonly List<Aabb> _boxes = new();
    private readonly List<BodyZone> _zones = new();
    private Transform3D[] _world = System.Array.Empty<Transform3D>();
    private readonly Skeleton3D _skeleton;

    private BoneHitboxes(Skeleton3D skeleton)
    {
        _skeleton = skeleton;
    }

    /// <summary>The boxes of this skeleton (built and hooked to its updates the first time); null if it has no skinned mesh.</summary>
    public static BoneHitboxes? For(Skeleton3D? skeleton)
    {
        if (skeleton == null || !GodotObject.IsInstanceValid(skeleton))
        {
            return null;
        }
        ulong id = skeleton.GetInstanceId();
        if (Cache.TryGetValue(id, out BoneHitboxes? known))
        {
            return known;
        }
        BoneHitboxes boxes = new(skeleton);
        boxes.Build();
        if (boxes.Count == 0)
        {
            Cache[id] = boxes;
            return null;
        }
        boxes.Follow();
        skeleton.SkeletonUpdated += boxes.Follow;
        skeleton.TreeExiting += () => Cache.Remove(id);
        Cache[id] = boxes;
        return boxes;
    }

    private void Build()
    {
        int boneCount = _skeleton.GetBoneCount();
        List<Vector3>[] points = new List<Vector3>[boneCount];
        foreach (Node node in _skeleton.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null || mesh.Skin == null || mesh.GetNodeOrNull(mesh.Skeleton) != _skeleton)
            {
                continue;
            }
            Gather(mesh, points);
        }
        // Meshes beside the skeleton (a glTF puts them as its children, but some imports as siblings).
        Node? parent = _skeleton.GetParent();
        if (parent != null)
        {
            foreach (Node node in parent.GetChildren())
            {
                if (node is MeshInstance3D mesh && mesh.Mesh != null && mesh.Skin != null && mesh.GetNodeOrNull(mesh.Skeleton) == _skeleton)
                {
                    Gather(mesh, points);
                }
            }
        }
        for (int bone = 0; bone < boneCount; bone++)
        {
            List<Vector3>? cloud = points[bone];
            if (cloud == null || cloud.Count < MinVertices)
            {
                continue;
            }
            _bones.Add(bone);
            _boxes.Add(Fit(cloud));
            _zones.Add(ZoneOf(_skeleton.GetBoneName(bone)));
        }
        _world = new Transform3D[_bones.Count];
    }

    /// <summary>Each vertex, in the space of the bone that moves it most.</summary>
    private void Gather(MeshInstance3D instance, List<Vector3>[] points)
    {
        Skin skin = instance.Skin;
        int binds = skin.GetBindCount();
        int[] bindBone = new int[binds];
        for (int i = 0; i < binds; i++)
        {
            int bone = skin.GetBindBone(i);
            if (bone < 0)
            {
                bone = _skeleton.FindBone(skin.GetBindName(i));
            }
            bindBone[i] = bone;
        }
        Mesh mesh = instance.Mesh;
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
            if (arrays.Count <= (int)Mesh.ArrayType.Weights || arrays[(int)Mesh.ArrayType.Bones].VariantType == Variant.Type.Nil)
            {
                continue;
            }
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            float[] weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            if (vertices.Length == 0 || bones.Length != weights.Length || bones.Length % vertices.Length != 0)
            {
                continue;
            }
            int per = bones.Length / vertices.Length;
            for (int v = 0; v < vertices.Length; v++)
            {
                int strongest = -1;
                float most = 0.0f;
                for (int k = 0; k < per; k++)
                {
                    float w = weights[v * per + k];
                    if (w > most)
                    {
                        most = w;
                        strongest = bones[v * per + k];
                    }
                }
                if (strongest < 0 || strongest >= binds || bindBone[strongest] < 0 || bindBone[strongest] >= points.Length)
                {
                    continue;
                }
                int bone = bindBone[strongest];
                points[bone] ??= new List<Vector3>();
                points[bone].Add(skin.GetBindPose(strongest) * vertices[v]);
            }
        }
    }

    /// <summary>The box around a bone's vertices, each axis trimmed of the outermost few.</summary>
    private static Aabb Fit(List<Vector3> cloud)
    {
        float[] xs = new float[cloud.Count];
        float[] ys = new float[cloud.Count];
        float[] zs = new float[cloud.Count];
        for (int i = 0; i < cloud.Count; i++)
        {
            xs[i] = cloud[i].X;
            ys[i] = cloud[i].Y;
            zs[i] = cloud[i].Z;
        }
        System.Array.Sort(xs);
        System.Array.Sort(ys);
        System.Array.Sort(zs);
        int low = Mathf.Clamp((int)(cloud.Count * Trim), 0, cloud.Count - 1);
        int high = Mathf.Clamp(cloud.Count - 1 - low, low, cloud.Count - 1);
        Vector3 min = new(xs[low], ys[low], zs[low]);
        Vector3 max = new(xs[high], ys[high], zs[high]);
        return new Aabb(min, max - min);
    }

    /// <summary>Carries the boxes to where the bones are now (the skeleton's update, after its modifiers).</summary>
    private void Follow()
    {
        if (!GodotObject.IsInstanceValid(_skeleton) || !_skeleton.IsInsideTree())
        {
            return;
        }
        Transform3D skeleton = _skeleton.GlobalTransform;
        for (int i = 0; i < _bones.Count; i++)
        {
            _world[i] = skeleton * _skeleton.GetBoneGlobalPose(_bones[i]);
        }
        Updates++;
    }

    /// <summary>True if the segment from <paramref name="a"/> to <paramref name="b"/>, <paramref name="radius"/> thick, passes through any box.</summary>
    public bool Touches(Vector3 a, Vector3 b, float radius)
    {
        for (int i = 0; i < _bones.Count; i++)
        {
            Transform3D toBone = _world[i].AffineInverse();
            float scale = _world[i].Basis.Scale.X;
            float grow = radius / Mathf.Max(scale, 0.0001f);
            Aabb box = _boxes[i].Grow(grow);
            if (SegmentHits(box, toBone * a, toBone * b))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Like Touches, but says where: the zone of the box the segment meets first (nearest
    /// <paramref name="a"/>) and the spot where it goes in.
    /// </summary>
    public bool Struck(Vector3 a, Vector3 b, float radius, out BodyHit hit)
    {
        hit = new BodyHit(BodyZone.Body, b, "");
        float first = 2.0f;
        for (int i = 0; i < _bones.Count; i++)
        {
            Transform3D toBone = _world[i].AffineInverse();
            float scale = _world[i].Basis.Scale.X;
            float grow = radius / Mathf.Max(scale, 0.0001f);
            Aabb box = _boxes[i].Grow(grow);
            if (SegmentEnters(box, toBone * a, toBone * b, out float enter) && enter < first)
            {
                first = enter;
                hit = new BodyHit(_zones[i], a.Lerp(b, enter), _skeleton.GetBoneName(_bones[i]));
            }
        }
        return first <= 1.0f;
    }

    /// <summary>The part of the body nearest <paramref name="point"/> (a blow that found it by reach, not by the blade).</summary>
    public BodyHit Nearest(Vector3 point)
    {
        BodyHit best = new(BodyZone.Body, point, "");
        float closest = float.MaxValue;
        for (int i = 0; i < _bones.Count; i++)
        {
            Vector3 centre = _world[i] * _boxes[i].GetCenter();
            float distance = centre.DistanceSquaredTo(point);
            if (distance < closest)
            {
                closest = distance;
                best = new BodyHit(_zones[i], centre, _skeleton.GetBoneName(_bones[i]));
            }
        }
        return best;
    }

    /// <summary>
    /// The zone a bone belongs to, read from its name (Mixamo, Quaternius, Tripo and Blender rigs
    /// name them alike): head; neck; spine, chest, hips are the torso; shoulders, arms, hands; legs, feet.
    /// </summary>
    public static BodyZone ZoneOf(string bone)
    {
        string name = bone.ToLowerInvariant();
        if (name.Contains("head") || name.Contains("jaw") || name.Contains("eye") || name.Contains("skull"))
        {
            return BodyZone.Head;
        }
        if (name.Contains("neck"))
        {
            return BodyZone.Neck;
        }
        if (name.Contains("thigh") || name.Contains("leg") || name.Contains("knee") || name.Contains("calf") || name.Contains("shin")
            || name.Contains("foot") || name.Contains("toe") || name.Contains("ankle"))
        {
            return BodyZone.Leg;
        }
        if (name.Contains("shoulder") || name.Contains("clavicle") || name.Contains("arm") || name.Contains("elbow")
            || name.Contains("hand") || name.Contains("wrist") || name.Contains("finger") || name.Contains("thumb")
            || name.Contains("index") || name.Contains("middle") || name.Contains("ring") || name.Contains("pinky"))
        {
            return BodyZone.Arm;
        }
        if (name.Contains("spine") || name.Contains("chest") || name.Contains("hip") || name.Contains("pelvis")
            || name.Contains("torso") || name.Contains("body") || name.Contains("root"))
        {
            return BodyZone.Torso;
        }
        return BodyZone.Body;
    }

    /// <summary>
    /// Whether any part of the body is within a blow's reach: some box's near side no further than
    /// <paramref name="reach"/> from <paramref name="feet"/> (flat), inside the cone around
    /// <paramref name="facing"/> (its cosine), and between <paramref name="below"/> and
    /// <paramref name="above"/> metres of the feet's height.
    /// </summary>
    public bool InReach(Vector3 feet, Vector3 facing, float reach, float cosLimit, float below, float above)
    {
        for (int i = 0; i < _bones.Count; i++)
        {
            Vector3 centre = _world[i] * _boxes[i].GetCenter();
            float rise = centre.Y - feet.Y;
            if (rise < -below || rise > above)
            {
                continue;
            }
            Vector3 half = _boxes[i].Size * 0.5f * _world[i].Basis.Scale.X;
            float size = Mathf.Min(half.X, Mathf.Min(half.Y, half.Z));
            Vector3 to = centre - feet;
            to.Y = 0.0f;
            float distance = to.Length();
            if (distance - size > reach)
            {
                continue;
            }
            if (distance < 0.05f || (to / distance).Dot(facing) >= cosLimit)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The middle of the boxes now (world space), e.g. to aim at.</summary>
    public Vector3 Centre()
    {
        if (_bones.Count == 0)
        {
            return _skeleton.GlobalPosition;
        }
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < _bones.Count; i++)
        {
            sum += _world[i] * _boxes[i].GetCenter();
        }
        return sum / _bones.Count;
    }

    /// <summary>Slab test: does the segment cross the box?</summary>
    private static bool SegmentHits(Aabb box, Vector3 a, Vector3 b)
    {
        return SegmentEnters(box, a, b, out float _);
    }

    /// <summary>Slab test with where the segment goes in (0 at <paramref name="a"/>, 1 at <paramref name="b"/>).</summary>
    private static bool SegmentEnters(Aabb box, Vector3 a, Vector3 b, out float enter)
    {
        Vector3 d = b - a;
        enter = 0.0f;
        float leave = 1.0f;
        Vector3 min = box.Position;
        Vector3 max = box.End;
        for (int axis = 0; axis < 3; axis++)
        {
            float start = a[axis];
            float step = d[axis];
            if (Mathf.Abs(step) < 0.000001f)
            {
                if (start < min[axis] || start > max[axis])
                {
                    return false;
                }
                continue;
            }
            float t1 = (min[axis] - start) / step;
            float t2 = (max[axis] - start) / step;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            enter = Mathf.Max(enter, t1);
            leave = Mathf.Min(leave, t2);
            if (enter > leave)
            {
                return false;
            }
        }
        return true;
    }
}
