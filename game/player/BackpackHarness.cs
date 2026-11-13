using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The pack's harness, drawn as continuous webbing:
/// - two shoulder straps (from the pack's top, over the shoulder, down the chest and under the arm to
///   the pack's lower corner);
/// - a sternum strap with a buckle joining them across the chest;
/// - a hip belt round the waist, from one side of the pack to the other, buckled in front.
/// The body is measured once in its rest pose (its visible skinned meshes, so the clothes it wears
/// count; measured again when they change). Every strap point is held by a bone (or by the pack), and
/// the straps are rebuilt from the final pose on each skeleton update (after the animation, IK and
/// lean), so they follow the shoulders, chest and hips through every move and the pack's swing.
/// Procedural webbing, not cloth: it bends along its path but doesn't fold. BackpackAttachment adds it.
/// </summary>
public partial class BackpackHarness : Node3D
{
    [Export] public bool ShoulderStraps { get; set; } = true;
    [Export] public bool SternumStrap { get; set; } = true;
    [Export] public bool HipBelt { get; set; } = true;
    [Export] public float StrapWidth { get; set; } = 0.045f;
    [Export] public float StrapThickness { get; set; } = 0.01f;
    [Export] public float BeltWidth { get; set; } = 0.05f;
    /// <summary>The gap between the measured body and a strap's inner face, in m.</summary>
    [Export] public float Clearance { get; set; } = 0.012f;
    /// <summary>Where a strap crosses the shoulder: its share of the way from the midline to the shoulder joint.</summary>
    [Export] public float ShoulderShare { get; set; } = 0.58f;
    /// <summary>The sternum strap this far below the shoulder joints, in m.</summary>
    [Export] public float SternumDrop { get; set; } = 0.13f;
    /// <summary>The hip belt this far above the hip joint, in m.</summary>
    [Export] public float BeltRaise { get; set; } = 0.06f;
    /// <summary>How far the strap from the armpit to the pack's lower corner sags, in m (less is tighter).</summary>
    [Export] public float Slack { get; set; } = 0.012f;
    /// <summary>Points added between two strap points, at least (a smoother curve).</summary>
    [Export] public int Smoothing { get; set; } = 3;
    /// <summary>And never further apart than this along a strap, in m.</summary>
    [Export] public float MaxStep { get; set; } = 0.05f;
    [Export] public Color Webbing { get; set; } = new(0.29f, 0.24f, 0.15f);
    [Export] public Color Metal { get; set; } = new(0.45f, 0.45f, 0.43f);
    /// <summary>How often (s) it checks whether what the body wears changed (then measures again).</summary>
    [Export] public float RecheckEvery { get; set; } = 1.0f;

    /// <summary>The body was measured and every strap has its points.</summary>
    public bool Fitted { get; private set; }
    public string Problem { get; private set; } = "";
    /// <summary>Times the body was measured (tests).</summary>
    public int Measures { get; private set; }
    /// <summary>Times the straps were rebuilt from a pose (tests).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>A strap point: held by a bone (Local in the bone's frame) or by the pack (Bone -1, Local in the pivot's frame).</summary>
    private readonly record struct Anchor(int Bone, Vector3 Local);

    private Skeleton3D? _rig;
    private BackpackAttachment? _pack;
    private readonly List<Anchor>[] _paths = { new(), new(), new(), new() };
    private readonly List<Vector3>[] _drawn = { new(), new(), new(), new() };
    private int _hips = -1;
    private int _top = -1;
    private int _beltBuckleAt;
    private ImmediateMesh _mesh = new();
    private MeshInstance3D? _straps;
    private MeshInstance3D? _sternumBuckle;
    private MeshInstance3D? _beltBuckle;
    private StandardMaterial3D? _webbing;
    private ulong _wearing;
    private float _recheck;
    private readonly List<Vector3> _body = new();

    public const int LeftStrap = 0;
    public const int RightStrap = 1;
    public const int Sternum = 2;
    public const int Belt = 3;

    /// <summary>The strap's points in world space as last drawn (empty if it isn't drawn).</summary>
    public IReadOnlyList<Vector3> Drawn(int strap) => _drawn[strap];

    public override void _Ready()
    {
        TopLevel = true;
        Transform = Transform3D.Identity;
        _pack = GetParent() as BackpackAttachment;
        _rig = _pack?.GetParent() as Skeleton3D;
        if (_pack == null || _rig == null)
        {
            Problem = "needs a BackpackAttachment on a skeleton";
            return;
        }
        _webbing = new StandardMaterial3D { AlbedoColor = Webbing, Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _straps = new MeshInstance3D { Name = "Straps", Mesh = _mesh };
        AddChild(_straps);
        StandardMaterial3D metal = new() { AlbedoColor = Metal, Metallic = 0.6f, Roughness = 0.45f };
        _sternumBuckle = new MeshInstance3D { Name = "SternumBuckle", Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.045f, 0.012f) }, MaterialOverride = metal, Visible = false };
        _beltBuckle = new MeshInstance3D { Name = "BeltBuckle", Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.055f, 0.014f) }, MaterialOverride = metal, Visible = false };
        AddChild(_sternumBuckle);
        AddChild(_beltBuckle);
        Measure();
        _rig.SkeletonUpdated += Rebuild;
    }

    public override void _ExitTree()
    {
        if (_rig != null && IsInstanceValid(_rig))
        {
            _rig.SkeletonUpdated -= Rebuild;
        }
    }

    public override void _Process(double delta)
    {
        _recheck -= (float)delta;
        if (_recheck > 0.0f || _rig == null)
        {
            return;
        }
        _recheck = RecheckEvery;
        if (Wearing() != _wearing)
        {
            Measure();
        }
    }

    /// <summary>Which skinned meshes show on the body now (a change means other clothes).</summary>
    private ulong Wearing()
    {
        ulong sum = 0;
        if (_rig == null)
        {
            return sum;
        }
        foreach (MeshInstance3D mesh in BodyMeshes())
        {
            sum = (sum * 31UL) + mesh.GetInstanceId();
        }
        return sum;
    }

    private IEnumerable<MeshInstance3D> BodyMeshes()
    {
        if (_rig == null || _pack == null)
        {
            yield break;
        }
        foreach (Node node in _rig.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Mesh is ArrayMesh && mesh.Skin != null && mesh.IsVisibleInTree()
                && !mesh.IsQueuedForDeletion() && !_pack.IsAncestorOf(mesh))
            {
                yield return mesh;
            }
        }
    }

    /// <summary>Measures the body in its rest pose and sets every strap point.</summary>
    public void Measure()
    {
        Fitted = false;
        foreach (List<Anchor> path in _paths) { path.Clear(); }
        if (_rig == null || _pack == null)
        {
            return;
        }
        _wearing = Wearing();
        Measures++;
        if (!ClothingRig.Frame(_rig, out Basis frame, out float metres))
        {
            Problem = "no body frame (hips, head and legs)";
            return;
        }
        _hips = ClothingRig.Bone(_rig, "Hips");
        int spine1 = First("Spine1", "Spine");
        int spine2 = First("Spine2", "Spine1");
        int leftArm = ClothingRig.Bone(_rig, "LeftArm");
        int rightArm = ClothingRig.Bone(_rig, "RightArm");
        int leftShoulder = First("LeftShoulder", "Spine2");
        int rightShoulder = First("RightShoulder", "Spine2");
        _top = First("Neck", "Spine2");
        if (_hips < 0 || spine1 < 0 || spine2 < 0 || leftArm < 0 || rightArm < 0 || leftShoulder < 0 || rightShoulder < 0)
        {
            Problem = "missing hips, spine, shoulder or arm bones";
            return;
        }
        Vector3 origin = _rig.GetBoneGlobalRest(_hips).Origin;
        Basis toBody = frame.Transposed();
        SampleBody(toBody, origin, metres);

        Vector3 Body(Vector3 rigPoint) => toBody * (rigPoint - origin) * metres;
        Anchor At(int bone, Vector3 body)
        {
            Vector3 rigPoint = origin + frame * (body / metres);
            return new Anchor(bone, _rig!.GetBoneGlobalRest(bone).AffineInverse() * rigPoint);
        }

        Vector3 armLeft = Body(_rig.GetBoneGlobalRest(leftArm).Origin);
        Vector3 armRight = Body(_rig.GetBoneGlobalRest(rightArm).Origin);
        float shoulderX = (Mathf.Abs(armLeft.X) + Mathf.Abs(armRight.X)) * 0.5f;
        float shoulderY = (armLeft.Y + armRight.Y) * 0.5f;
        float shoulderZ = (armLeft.Z + armRight.Z) * 0.5f;
        float gap = Clearance + StrapThickness * 0.5f;

        // The pack's box, read in the body's directions (right, up, away from the back).
        Transform3D pivot = _pack.RestPivot(_rig);
        Aabb box = _pack.PackBounds();
        Vector3 centre = box.GetCenter();
        Vector3 half = box.Size * 0.5f;
        Vector3 bodyCentre = Body(pivot * centre);
        float rightSign = Sign(Body(pivot * (centre + new Vector3(half.X, 0, 0))).X - bodyCentre.X);
        float upSign = Sign(Body(pivot * (centre + new Vector3(0, half.Y, 0))).Y - bodyCentre.Y);
        float awaySign = Sign(Body(pivot * (centre + new Vector3(0, 0, half.Z))).Z - bodyCentre.Z);
        Anchor Pack(float side, float up, float away)
        {
            return new Anchor(-1, centre + new Vector3(side * rightSign * half.X, up * upSign * half.Y, away * awaySign * half.Z));
        }

        float sternumY = shoulderY - SternumDrop;
        float sternumZ = float.PositiveInfinity;
        for (int strap = 0; strap < 2; strap++)
        {
            float side = -1.0f;
            int shoulderBone = leftShoulder;
            if (strap == RightStrap)
            {
                side = 1.0f;
                shoulderBone = rightShoulder;
            }
            float x = side * shoulderX * ShoulderShare;
            float top = Top(x, shoulderZ, shoulderY, shoulderY + 0.06f);
            float front = Front(x, top - 0.06f, shoulderZ - 0.07f);
            float back = Back(x, top - 0.06f, shoulderZ + 0.07f);
            float chestX = x * 1.08f;
            float chest = Front(chestX, sternumY, shoulderZ - 0.1f);
            sternumZ = Mathf.Min(sternumZ, chest);
            float ribY = sternumY - 0.1f;
            float ribX = side * shoulderX * 0.82f;
            float rib = Front(ribX, ribY, chest + 0.03f);
            float sideY = ribY - 0.05f;
            float width = HalfWidth(sideY, shoulderX * 0.95f, shoulderX * 0.8f);
            float middle = (Front(side * width * 0.9f, sideY, rib + 0.04f) + Back(side * width * 0.9f, sideY, back)) * 0.5f;
            List<Anchor> path = _paths[strap];
            path.Add(Pack(side * 0.42f, 0.9f, -0.7f));
            path.Add(At(shoulderBone, new Vector3(x, top - 0.05f, back + gap)));
            path.Add(At(shoulderBone, new Vector3(x, top + gap, (front + back) * 0.5f)));
            path.Add(At(shoulderBone, new Vector3(x * 1.02f, top - 0.07f, front - gap)));
            path.Add(At(spine2, new Vector3(chestX, sternumY, chest - gap)));
            path.Add(At(spine1, new Vector3(ribX, ribY, rib - gap)));
            path.Add(At(spine1, new Vector3(side * (width + gap), sideY, middle)));
            path.Add(Pack(side * 0.97f, -0.6f, -0.5f));
        }

        // Across the chest, over both straps.
        float across = shoulderX * ShoulderShare * 1.08f;
        float lie = sternumZ - gap - StrapThickness;
        _paths[Sternum].Add(At(spine2, new Vector3(-across, sternumY, lie)));
        _paths[Sternum].Add(At(spine2, new Vector3(0.0f, sternumY, lie - 0.004f)));
        _paths[Sternum].Add(At(spine2, new Vector3(across, sternumY, lie)));

        // Round the waist: from the pack's right side, round the front, to its left side.
        float beltY = BeltRaise;
        float waist = HalfWidth(beltY, 0.35f, 0.16f) + gap;
        float belly = Front(0.0f, beltY, -0.12f, 0.08f);
        float rear = Back(0.0f, beltY, 0.12f, 0.08f);
        float mid = (belly + rear) * 0.5f;
        float depth = (rear - belly) * 0.5f + gap;
        List<Anchor> belt = _paths[Belt];
        belt.Add(Pack(0.97f, -0.85f, -0.6f));
        const int steps = 14;
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(-0.45f, Mathf.Pi + 0.45f, (float)i / steps);
            belt.Add(At(_hips, new Vector3(waist * Mathf.Cos(angle), beltY, mid - depth * Mathf.Sin(angle))));
        }
        belt.Add(Pack(-0.97f, -0.85f, -0.6f));
        _beltBuckleAt = 1 + steps / 2;
        Problem = "";
        Fitted = true;
    }

    private int First(string name, string fallback)
    {
        if (_rig == null)
        {
            return -1;
        }
        int bone = ClothingRig.Bone(_rig, name);
        if (bone < 0)
        {
            bone = ClothingRig.Bone(_rig, fallback);
        }
        return bone;
    }

    private static float Sign(float value)
    {
        if (value < 0.0f)
        {
            return -1.0f;
        }
        return 1.0f;
    }

    private void SampleBody(Basis toBody, Vector3 origin, float metres)
    {
        _body.Clear();
        if (_rig == null)
        {
            return;
        }
        List<(Transform3D ToRig, Vector3[] Vertices)> parts = new();
        int total = 0;
        foreach (MeshInstance3D mesh in BodyMeshes())
        {
            ArrayMesh array = (ArrayMesh)mesh.Mesh;
            Transform3D toRig = _rig.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
            {
                Vector3[] vertices = array.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                parts.Add((toRig, vertices));
                total += vertices.Length;
            }
        }
        int stride = Mathf.Max(1, total / 40000);
        foreach ((Transform3D toRig, Vector3[] vertices) in parts)
        {
            for (int i = 0; i < vertices.Length; i += stride)
            {
                _body.Add(toBody * (toRig * vertices[i] - origin) * metres);
            }
        }
    }

    /// <summary>The body's front (its least Z) near (x, y); the fallback if nothing is there.</summary>
    private float Front(float x, float y, float fallback, float reach = 0.035f)
    {
        float found = float.PositiveInfinity;
        foreach (Vector3 p in _body)
        {
            if (Mathf.Abs(p.X - x) < reach && Mathf.Abs(p.Y - y) < reach)
            {
                found = Mathf.Min(found, p.Z);
            }
        }
        if (float.IsFinite(found)) { return found; }
        return fallback;
    }

    /// <summary>The body's back (its greatest Z) near (x, y).</summary>
    private float Back(float x, float y, float fallback, float reach = 0.035f)
    {
        float found = float.NegativeInfinity;
        foreach (Vector3 p in _body)
        {
            if (Mathf.Abs(p.X - x) < reach && Mathf.Abs(p.Y - y) < reach)
            {
                found = Mathf.Max(found, p.Z);
            }
        }
        if (float.IsFinite(found)) { return found; }
        return fallback;
    }

    /// <summary>The top of the shoulder at x (between the joint's height and a little above; not the head).</summary>
    private float Top(float x, float z, float shoulderY, float fallback)
    {
        float found = float.NegativeInfinity;
        foreach (Vector3 p in _body)
        {
            if (Mathf.Abs(p.X - x) < 0.03f && Mathf.Abs(p.Z - z) < 0.1f && p.Y > shoulderY - 0.05f && p.Y < shoulderY + 0.16f)
            {
                found = Mathf.Max(found, p.Y);
            }
        }
        if (float.IsFinite(found)) { return found; }
        return fallback;
    }

    /// <summary>The torso's half width at y (inside limit, so arms hanging beside it don't count).</summary>
    private float HalfWidth(float y, float limit, float fallback)
    {
        float found = 0.0f;
        foreach (Vector3 p in _body)
        {
            if (Mathf.Abs(p.Y - y) < 0.035f && Mathf.Abs(p.X) < limit)
            {
                found = Mathf.Max(found, Mathf.Abs(p.X));
            }
        }
        if (found > 0.0f) { return found; }
        return fallback;
    }

    private Vector3 World(Anchor anchor)
    {
        if (anchor.Bone < 0)
        {
            return _pack!.Pivot.GlobalTransform * anchor.Local;
        }
        return _rig!.GlobalTransform * (_rig.GetBoneGlobalPose(anchor.Bone) * anchor.Local);
    }

    /// <summary>Rebuilds the webbing from the pose just finished (the skeleton's update).</summary>
    private void Rebuild()
    {
        if (!Fitted || _rig == null || _pack == null || _webbing == null || !IsVisibleInTree())
        {
            return;
        }
        Rebuilds++;
        Vector3 axisFrom = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_hips).Origin;
        Vector3 axisTo = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_top).Origin;
        _mesh.ClearSurfaces();
        bool any = false;
        for (int strap = 0; strap < _paths.Length; strap++)
        {
            _drawn[strap].Clear();
            if (!Shown(strap) || _paths[strap].Count < 2)
            {
                continue;
            }
            List<Vector3> points = new();
            foreach (Anchor anchor in _paths[strap])
            {
                points.Add(World(anchor));
            }
            if (strap == LeftStrap || strap == RightStrap)
            {
                // A little give between the armpit and the pack's lower corner.
                Vector3 last = points[^1];
                Vector3 before = points[^2];
                points.Insert(points.Count - 1, (last + before) * 0.5f + Vector3.Down * Slack);
            }
            List<Vector3> smooth = Smooth(points);
            if (!any)
            {
                _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _webbing);
                any = true;
            }
            float width = StrapWidth;
            if (strap == Belt)
            {
                width = BeltWidth;
            }
            Ribbon(smooth, width, axisFrom, axisTo);
            _drawn[strap].AddRange(smooth);
        }
        if (any)
        {
            _mesh.SurfaceEnd();
        }
        PlaceBuckle(_sternumBuckle, Sternum, 1, axisFrom, axisTo);
        PlaceBuckle(_beltBuckle, Belt, _beltBuckleAt, axisFrom, axisTo);
    }

    private bool Shown(int strap)
    {
        if (strap == Sternum)
        {
            return SternumStrap && ShoulderStraps;
        }
        if (strap == Belt)
        {
            return HipBelt;
        }
        return ShoulderStraps;
    }

    private void PlaceBuckle(MeshInstance3D? buckle, int strap, int index, Vector3 axisFrom, Vector3 axisTo)
    {
        if (buckle == null)
        {
            return;
        }
        buckle.Visible = Shown(strap) && _paths[strap].Count > index;
        if (!buckle.Visible)
        {
            return;
        }
        Vector3 at = World(_paths[strap][index]);
        Vector3 along = (World(_paths[strap][index + 1]) - World(_paths[strap][index - 1])).Normalized();
        Vector3 outward = Outward(at, along, axisFrom, axisTo);
        Vector3 up = outward.Cross(along).Normalized();
        if (!along.IsFinite() || along.LengthSquared() < 0.5f || up.LengthSquared() < 0.5f)
        {
            buckle.Visible = false;
            return;
        }
        buckle.GlobalTransform = new Transform3D(new Basis(along, up, outward), at + outward * (StrapThickness * 0.9f));
    }

    /// <summary>Catmull-Rom through the points (passes through every one).</summary>
    private List<Vector3> Smooth(List<Vector3> points)
    {
        List<Vector3> result = new();
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 p0 = points[Mathf.Max(0, i - 1)];
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];
            Vector3 p3 = points[Mathf.Min(points.Count - 1, i + 2)];
            int steps = Mathf.Max(Mathf.Max(1, Smoothing), Mathf.CeilToInt(p1.DistanceTo(p2) / Mathf.Max(MaxStep, 0.01f)));
            steps = Mathf.Min(steps, 40);
            for (int k = 0; k < steps; k++)
            {
                float t = (float)k / steps;
                float t2 = t * t;
                float t3 = t2 * t;
                result.Add(0.5f * ((2.0f * p1) + (p2 - p0) * t + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 + (3.0f * p1 - p0 - 3.0f * p2 + p3) * t3));
            }
        }
        result.Add(points[^1]);
        return result;
    }

    /// <summary>From the body's middle line (hips to neck) out through the point, square to the strap.</summary>
    private static Vector3 Outward(Vector3 point, Vector3 along, Vector3 axisFrom, Vector3 axisTo)
    {
        Vector3 axis = axisTo - axisFrom;
        float share = 0.0f;
        if (axis.LengthSquared() > 0.000001f)
        {
            share = Mathf.Clamp((point - axisFrom).Dot(axis) / axis.LengthSquared(), -0.5f, 1.5f);
        }
        Vector3 outward = point - (axisFrom + axis * share);
        outward -= along * along.Dot(outward);
        return outward.Normalized();
    }

    /// <summary>A flat strap of the given width and StrapThickness along the points.</summary>
    private void Ribbon(List<Vector3> points, float width, Vector3 axisFrom, Vector3 axisTo)
    {
        int count = points.Count;
        Vector3[] across = new Vector3[count];
        Vector3[] outward = new Vector3[count];
        Vector3 lastOut = Vector3.Up;
        for (int i = 0; i < count; i++)
        {
            Vector3 along = (points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).Normalized();
            Vector3 normal = Outward(points[i], along, axisFrom, axisTo);
            if (!normal.IsFinite() || normal.LengthSquared() < 0.5f)
            {
                normal = lastOut;
            }
            lastOut = normal;
            Vector3 side = along.Cross(normal).Normalized();
            if (!side.IsFinite() || side.LengthSquared() < 0.5f)
            {
                side = Vector3.Right;
            }
            across[i] = side * (width * 0.5f);
            outward[i] = normal * (StrapThickness * 0.5f);
        }
        for (int i = 0; i < count - 1; i++)
        {
            Quad(points[i] + across[i] + outward[i], points[i] - across[i] + outward[i], points[i + 1] - across[i + 1] + outward[i + 1], points[i + 1] + across[i + 1] + outward[i + 1], outward[i].Normalized());
            Quad(points[i] - across[i] - outward[i], points[i] + across[i] - outward[i], points[i + 1] + across[i + 1] - outward[i + 1], points[i + 1] - across[i + 1] - outward[i + 1], -outward[i].Normalized());
            Quad(points[i] + across[i] - outward[i], points[i] + across[i] + outward[i], points[i + 1] + across[i + 1] + outward[i + 1], points[i + 1] + across[i + 1] - outward[i + 1], across[i].Normalized());
            Quad(points[i] - across[i] + outward[i], points[i] - across[i] - outward[i], points[i + 1] - across[i + 1] - outward[i + 1], points[i + 1] - across[i + 1] + outward[i + 1], -across[i].Normalized());
        }
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        _mesh.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(b);
        _mesh.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(c);
        _mesh.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(c);
        _mesh.SurfaceSetNormal(normal);
        _mesh.SurfaceAddVertex(d);
    }
}
