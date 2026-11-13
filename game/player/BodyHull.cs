using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The body's surface as the straps feel it: for each chosen bone, the outline of the skin (and the
/// clothes worn over it) that bone moves, as a radius around the bone's own axis by height along it
/// and angle around it. It is measured once from the skinned meshes in their rest pose; it then moves
/// with the posed bones, so a strap pushed out of it lies on the body as it is, chest, shoulders and
/// all, through every animation, lean and ragdoll fall. Each vertex counts for the bone that moves it
/// most, so an arm hanging beside the chest is the arm's outline, not the chest's.
/// </summary>
public sealed class BodyHull
{
    /// <summary>Rings along a bone's axis and sectors around it.</summary>
    public const int Rings = 12;
    public const int Sectors = 28;

    private sealed class Part
    {
        public int Bone;
        public float Low;
        public float High;
        public float Widest;
        public readonly float[] Radius = new float[Rings * Sectors];
        public Transform3D Before = Transform3D.Identity;
        public Transform3D Now = Transform3D.Identity;
        public Transform3D At = Transform3D.Identity;
        public Transform3D Inverse = Transform3D.Identity;
        /// <summary>Between poses: the scale, and a sphere around the part (world) for a quick miss.</summary>
        public float Scale = 1.0f;
        public Vector3 Centre;
        public float Reach;
    }

    private readonly List<Part> _parts = new();
    private Skeleton3D? _rig;

    /// <summary>Bones that have an outline (tests).</summary>
    public int Count => _parts.Count;

    /// <summary>Measures the outlines of <paramref name="bones"/> from the skinned meshes.</summary>
    public static BodyHull Measure(Skeleton3D rig, IEnumerable<MeshInstance3D> meshes, IEnumerable<int> bones)
    {
        BodyHull hull = new() { _rig = rig };
        Dictionary<int, List<Vector3>> clouds = new();
        foreach (int bone in bones)
        {
            if (bone >= 0 && !clouds.ContainsKey(bone)) { clouds[bone] = new List<Vector3>(); }
        }
        foreach (MeshInstance3D mesh in meshes)
        {
            Gather(rig, mesh, clouds);
        }
        foreach ((int bone, List<Vector3> cloud) in clouds)
        {
            if (cloud.Count < 24) { continue; }
            hull._parts.Add(Fit(bone, cloud));
        }
        hull.Pose(true);
        return hull;
    }

    /// <summary>Each vertex in the space of the bone that moves it most (its bind pose).</summary>
    private static void Gather(Skeleton3D rig, MeshInstance3D instance, Dictionary<int, List<Vector3>> clouds)
    {
        Skin? skin = instance.Skin;
        if (skin == null || instance.Mesh == null) { return; }
        int binds = skin.GetBindCount();
        int[] bindBone = new int[binds];
        for (int i = 0; i < binds; i++)
        {
            int bone = skin.GetBindBone(i);
            if (bone < 0) { bone = rig.FindBone(skin.GetBindName(i)); }
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
            int[] boneIds = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            float[] weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            if (vertices.Length == 0 || boneIds.Length != weights.Length || boneIds.Length % vertices.Length != 0)
            {
                continue;
            }
            int per = boneIds.Length / vertices.Length;
            for (int v = 0; v < vertices.Length; v++)
            {
                int strongest = -1;
                float most = 0.0f;
                for (int k = 0; k < per; k++)
                {
                    if (weights[v * per + k] > most)
                    {
                        most = weights[v * per + k];
                        strongest = boneIds[v * per + k];
                    }
                }
                if (strongest < 0 || strongest >= binds) { continue; }
                if (!clouds.TryGetValue(bindBone[strongest], out List<Vector3>? cloud)) { continue; }
                cloud.Add(skin.GetBindPose(strongest) * vertices[v]);
            }
        }
    }

    /// <summary>The outline: the outermost vertex in each ring and sector, gaps filled from neighbours.</summary>
    private static Part Fit(int bone, List<Vector3> cloud)
    {
        float[] heights = new float[cloud.Count];
        for (int i = 0; i < cloud.Count; i++) { heights[i] = cloud[i].Y; }
        System.Array.Sort(heights);
        int trim = cloud.Count / 100;
        Part part = new() { Bone = bone, Low = heights[trim], High = heights[cloud.Count - 1 - trim] };
        if (part.High - part.Low < 0.0001f) { part.High = part.Low + 0.0001f; }
        bool[] seen = new bool[Rings * Sectors];
        foreach (Vector3 p in cloud)
        {
            int ring = Mathf.Clamp((int)((p.Y - part.Low) / (part.High - part.Low) * Rings), 0, Rings - 1);
            int sector = Sector(p.X, p.Z);
            int cell = ring * Sectors + sector;
            float radius = Mathf.Sqrt(p.X * p.X + p.Z * p.Z);
            if (radius > part.Radius[cell]) { part.Radius[cell] = radius; }
            seen[cell] = true;
        }
        // Gaps: around each ring first, then from the nearest ring that has something.
        for (int ring = 0; ring < Rings; ring++)
        {
            for (int pass = 0; pass < Sectors; pass++)
            {
                bool changed = false;
                for (int sector = 0; sector < Sectors; sector++)
                {
                    int cell = ring * Sectors + sector;
                    if (seen[cell]) { continue; }
                    int left = ring * Sectors + (sector + Sectors - 1) % Sectors;
                    int right = ring * Sectors + (sector + 1) % Sectors;
                    if (seen[left] || seen[right])
                    {
                        float a = seen[left] ? part.Radius[left] : part.Radius[right];
                        float b = seen[right] ? part.Radius[right] : part.Radius[left];
                        part.Radius[cell] = Mathf.Min(a, b);
                        seen[cell] = true;
                        changed = true;
                    }
                }
                if (!changed) { break; }
            }
        }
        for (int ring = 0; ring < Rings; ring++)
        {
            if (seen[ring * Sectors]) { continue; }
            for (int step = 1; step < Rings; step++)
            {
                int from = -1;
                if (ring - step >= 0 && seen[(ring - step) * Sectors]) { from = ring - step; }
                else if (ring + step < Rings && seen[(ring + step) * Sectors]) { from = ring + step; }
                if (from < 0) { continue; }
                for (int sector = 0; sector < Sectors; sector++)
                {
                    part.Radius[ring * Sectors + sector] = part.Radius[from * Sectors + sector];
                    seen[ring * Sectors + sector] = true;
                }
                break;
            }
        }
        foreach (float radius in part.Radius) { part.Widest = Mathf.Max(part.Widest, radius); }
        return part;
    }

    private static int Sector(float x, float z)
    {
        float angle = Mathf.Atan2(x, z);
        if (angle < 0.0f) { angle += Mathf.Tau; }
        return Mathf.Clamp((int)(angle / Mathf.Tau * Sectors), 0, Sectors - 1);
    }

    /// <summary>The outline's radius at a height and angle (blended between rings and sectors).</summary>
    private static float RadiusAt(Part part, float height, float x, float z)
    {
        float along = Mathf.Clamp((height - part.Low) / (part.High - part.Low) * Rings - 0.5f, 0.0f, Rings - 1.0f);
        int ring = Mathf.Min((int)along, Rings - 2);
        float ringBlend = along - ring;
        float angle = Mathf.Atan2(x, z);
        if (angle < 0.0f) { angle += Mathf.Tau; }
        float around = angle / Mathf.Tau * Sectors - 0.5f;
        if (around < 0.0f) { around += Sectors; }
        int sector = (int)around % Sectors;
        int next = (sector + 1) % Sectors;
        float sectorBlend = around - Mathf.Floor(around);
        float low = Mathf.Lerp(part.Radius[ring * Sectors + sector], part.Radius[ring * Sectors + next], sectorBlend);
        int upper = Mathf.Min(ring + 1, Rings - 1);
        float high = Mathf.Lerp(part.Radius[upper * Sectors + sector], part.Radius[upper * Sectors + next], sectorBlend);
        return Mathf.Lerp(low, high, ringBlend);
    }

    /// <summary>
    /// Reads the bones' posed transforms (after the skeleton's update). With <paramref name="jump"/> the
    /// last pose is forgotten (a teleport), so nothing is swept from where the body was.
    /// </summary>
    public void Pose(bool jump)
    {
        if (_rig == null) { return; }
        Transform3D rig = _rig.GlobalTransform;
        foreach (Part part in _parts)
        {
            part.Before = part.Now;
            part.Now = rig * _rig.GetBoneGlobalPose(part.Bone);
            if (jump) { part.Before = part.Now; }
        }
        Between(1.0f);
    }

    /// <summary>Places the outlines the given share of the way from the last pose to this one (substeps).</summary>
    public void Between(float share)
    {
        foreach (Part part in _parts)
        {
            if (share >= 1.0f)
            {
                part.At = part.Now;
            }
            else
            {
                Vector3 scale = part.Now.Basis.Scale;
                Quaternion from = part.Before.Basis.Orthonormalized().GetRotationQuaternion();
                Quaternion to = part.Now.Basis.Orthonormalized().GetRotationQuaternion();
                Basis turned = new Basis(from.Slerp(to, share)).Scaled(scale);
                part.At = new Transform3D(turned, part.Before.Origin.Lerp(part.Now.Origin, share));
            }
            part.Inverse = part.At.AffineInverse();
            part.Scale = part.At.Basis.Scale.X;
            float half = (part.High - part.Low) * 0.5f;
            part.Centre = part.At * new Vector3(0.0f, part.Low + half, 0.0f);
            part.Reach = Mathf.Sqrt(half * half + part.Widest * part.Widest) * part.Scale;
        }
    }

    /// <summary>
    /// Pushes a point out of the body to <paramref name="clearance"/> (world units) off its surface.
    /// Returns true when it was inside; <paramref name="normal"/> is then the surface's outward
    /// direction there, and <paramref name="bone"/> and <paramref name="local"/> where it now sits on
    /// that bone (for friction).
    /// </summary>
    public bool Collide(ref Vector3 point, float clearance, out Vector3 normal, out int bone, out Vector3 local)
        => Collide(ref point, clearance, null, out normal, out bone, out local);

    /// <summary>As above, against only <paramref name="bones"/>' outlines (null: all of them).</summary>
    public bool Collide(ref Vector3 point, float clearance, ICollection<int>? bones, out Vector3 normal, out int bone, out Vector3 local)
    {
        normal = Vector3.Zero;
        bone = -1;
        local = Vector3.Zero;
        bool hit = false;
        foreach (Part part in _parts)
        {
            if (bones != null && !bones.Contains(part.Bone)) { continue; }
            float reach = part.Reach + clearance;
            if (part.Centre.DistanceSquaredTo(point) > reach * reach) { continue; }
            Vector3 p = part.Inverse * point;
            float gap = clearance / Mathf.Max(part.Scale, 0.0001f);
            if (p.Y < part.Low - gap || p.Y > part.High + gap) { continue; }
            float radius = Mathf.Sqrt(p.X * p.X + p.Z * p.Z);
            if (radius > part.Widest + gap || radius < 0.00001f) { continue; }
            float surface = RadiusAt(part, p.Y, p.X, p.Z);
            if (surface <= 0.0f || radius >= surface + gap) { continue; }
            float k = (surface + gap) / radius;
            Vector3 outside = new(p.X * k, p.Y, p.Z * k);
            point = part.At * outside;
            normal = (part.At.Basis * new Vector3(p.X, 0.0f, p.Z)).Normalized();
            bone = part.Bone;
            local = outside;
            hit = true;
        }
        return hit;
    }

    /// <summary>
    /// The outlines as line segments in the world (debug drawing): a ring at each height and a line
    /// along every other sector, so the measured surface can be checked against the mesh.
    /// </summary>
    public void Outline(System.Action<Vector3, Vector3, int> line)
    {
        for (int index = 0; index < _parts.Count; index++)
        {
            Part part = _parts[index];
            // The pose read in the skeleton's update (modifiers' results only exist there).
            Transform3D posed = part.Now;
            Vector3 Corner(int ring, int sector)
            {
                float angle = (sector + 0.5f) / Sectors * Mathf.Tau;
                float height = part.Low + (ring + 0.5f) / Rings * (part.High - part.Low);
                float radius = part.Radius[ring * Sectors + sector];
                return posed * new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
            }
            for (int ring = 0; ring < Rings; ring++)
            {
                for (int sector = 0; sector < Sectors; sector++)
                {
                    line(Corner(ring, sector), Corner(ring, (sector + 1) % Sectors), index);
                    if (ring + 1 < Rings && sector % 2 == 0) { line(Corner(ring, sector), Corner(ring + 1, sector), index); }
                }
            }
        }
    }

    /// <summary>
    /// How far a point can move along <paramref name="direction"/> (unit, world) before it touches the
    /// body (only <paramref name="bones"/>' outlines; null: all): negative when it starts inside (how far
    /// back it must go), <paramref name="reach"/> when nothing is in the way.
    /// </summary>
    public float GapAlong(Vector3 point, Vector3 direction, float reach, ICollection<int>? bones)
    {
        const float Step = 0.004f;
        Vector3 probe = point;
        if (Collide(ref probe, 0.0f, bones, out _, out _, out _))
        {
            for (float back = Step; back <= reach; back += Step)
            {
                probe = point - direction * back;
                if (!Collide(ref probe, 0.0f, bones, out _, out _, out _)) { return -back; }
            }
            return -reach;
        }
        for (float along = Step; along <= reach; along += Step)
        {
            probe = point + direction * along;
            if (Collide(ref probe, 0.0f, bones, out _, out _, out _)) { return along - Step * 0.5f; }
        }
        return reach;
    }

    /// <summary>The surface's outward direction nearest a point, or zero away from the body.</summary>
    public Vector3 NormalNear(Vector3 point, float reach)
    {
        Vector3 probe = point;
        if (Collide(ref probe, reach, out Vector3 normal, out _, out _)) { return normal; }
        return Vector3.Zero;
    }
}
