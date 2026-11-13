using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The pack's harness, and what holds the pack on:
/// - two shoulder straps (from the pack's top, over the shoulder, down the chest and under the arm to
///   the pack's lower corner);
/// - a sternum strap with a buckle joining them across the chest;
/// - a hip belt round the waist, from one side of the pack to the other, buckled in front.
/// The body is measured once in its rest pose (its visible skinned meshes, so the clothes it wears
/// count; measured again when they change): where each strap runs over it, and its surface (BodyHull).
/// The pack is its own body (PackPhysics), not held by a bone. Where the straps hold it is one frame:
/// its top where the shoulder straps hold it on the upper back, its bottom turned towards the hips
/// where the hip belt holds it (so it follows the spine as it bends, and every strap's pull agrees).
/// Its strap points are tied to that frame, firmly at the top and with a little give below, so it lags,
/// swings and settles with every move while the straps and pack stay one piece. Its back panel rests on the mesh's surface. Simulated on each
/// skeleton update (after the animation, IK, lean and ragdoll); the straps are drawn from that pose as
/// flat webbing lying on the body, their ends sewn to the swinging pack. Visual only. BackpackAttachment adds it.
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
    /// <summary>Let the pack swing on its straps; off: it sits at its fit.</summary>
    [Export] public bool Simulate { get; set; } = true;
    [Export] public float PackMass { get; set; } = 8.0f;
    /// <summary>How much the shoulder straps' tops give (compliance, m/N; 0 holds rigidly).</summary>
    [Export] public float ShoulderGive { get; set; } = 0.00001f;
    /// <summary>How much the shoulder straps' lower ends and the hip belt give (more: the pack's bottom swings further).</summary>
    [Export] public float LowerGive { get; set; } = 0.00003f;
    /// <summary>On each start, lay the pack onto the body's surface (moved in and tilted until its back panel's top and bottom touch).</summary>
    [Export] public bool LayOnBody { get; set; } = true;
    /// <summary>The most (degrees) laying it on the body may tilt it from its fit.</summary>
    [Export] public float MaxLayTiltDegrees { get; set; } = 25.0f;
    /// <summary>How far in (m) the straps pull the pack towards the body: its back panel stops it on the body, so it lies snug.</summary>
    [Export] public float Snug { get; set; } = 0.03f;
    /// <summary>Simulation steps per second (each frame is split into this many, at least one).</summary>
    [Export] public float StepsPerSecond { get; set; } = 480.0f;
    [Export] public float PackDamping { get; set; } = 4.0f;
    [Export] public float PackTurnDamping { get; set; } = 10.0f;
    /// <summary>The furthest (m) the pack swings from where its straps hold it (they only stretch so far).</summary>
    [Export] public float MaxSwing { get; set; } = 0.06f;
    /// <summary>The most (degrees) the pack turns from where its straps hold it.</summary>
    [Export] public float MaxTurnDegrees { get; set; } = 10.0f;
    /// <summary>The pack's back panel stays this far off the body, in m.</summary>
    [Export] public float PackClearance { get; set; } = 0.012f;
    /// <summary>The body moving further than this (m) in one update was moved (a teleport): the pack starts over at its fit.</summary>
    [Export] public float TeleportDistance { get; set; } = 1.5f;
    /// <summary>The pack further than this (m) from its fit starts over there (a safety net).</summary>
    [Export] public float ResetDistance { get; set; } = 0.6f;

    /// <summary>The body was measured and every strap has its points.</summary>
    public bool Fitted { get; private set; }
    public string Problem { get; private set; } = "";
    /// <summary>Times the body was measured (tests).</summary>
    public int Measures { get; private set; }
    /// <summary>Times the straps were rebuilt from a pose (tests).</summary>
    public int Rebuilds { get; private set; }
    /// <summary>Times the pack started over at its fit (tests).</summary>
    public int Resets { get; private set; }
    /// <summary>The last simulation step's cost, in microseconds (tests).</summary>
    public long StepMicroseconds { get; private set; }
    /// <summary>How far (m) and how much (radians) the pack has swung from where its straps hold it, at the last update.</summary>
    public float SwingDistance { get; private set; }
    public float SwingAngle { get; private set; }
    /// <summary>Starts the pack over at its fit on the next update, with the current settings (tuning).</summary>
    public void RequestRestart() => _needReset = true;
    /// <summary>The simulation (null until fitted).</summary>
    public PackPhysics? Physics => _physics;
    /// <summary>The body's surface the pack rests on and the straps lie on (measured from the mesh).</summary>
    public BodyHull? Hull => _hull;

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
    private PackPhysics? _physics;
    private BodyHull? _hull;
    private bool _needReset = true;
    private int _fitVersion = -1;
    private ulong _lastFrame;
    private Vector3 _lastHips;
    private float _awaySign = 1.0f;
    /// <summary>The pack's centre of mass in the pivot's frame, and the pivot's scale.</summary>
    private Vector3 _centre;
    private float _scale = 1.0f;
    /// <summary>The ties' strap points (offsets from the centre of mass, pack axes, pulled in by Snug).</summary>
    private readonly List<Vector3> _ties = new();
    private readonly List<Vector3> _held = new();
    // The held frame: the pack's top on the upper spine, its bottom towards the hips (see HeldFrame).
    private Vector3 _topOnSpine;
    private Vector3 _bottomOnHips;
    private Vector3 _awayOnSpine;
    private Vector3 _topInPack;
    private Basis _packAxes = Basis.Identity;
    /// <summary>Where the pack came to rest, in the held frame (on the body, the straps pulling): the swing is around it.</summary>
    private Transform3D _rest = Transform3D.Identity;

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
        _awaySign = awaySign;
        List<int> hullBones = new();
        foreach (string name in new[] { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm" })
        {
            int bone = ClothingRig.Bone(_rig, name);
            if (bone >= 0 && !hullBones.Contains(bone)) { hullBones.Add(bone); }
        }
        _hull = BodyHull.Measure(_rig, BodyMeshes(), hullBones);
        _physics ??= new PackPhysics();
        _needReset = true;
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

    private Vector3 World(Anchor anchor) => World(anchor, _pack!.Pivot.GlobalTransform);

    private Vector3 World(Anchor anchor, Transform3D pivot)
    {
        if (anchor.Bone < 0)
        {
            return pivot * anchor.Local;
        }
        return _rig!.GlobalTransform * (_rig.GetBoneGlobalPose(anchor.Bone) * anchor.Local);
    }

    /// <summary>Simulates the pack and draws the harness for the pose just finished (the skeleton's update).</summary>
    private void Rebuild()
    {
        if (!Fitted || _rig == null || _pack == null || _webbing == null || !IsVisibleInTree())
        {
            return;
        }
        if (!Simulate || _physics == null)
        {
            _pack.StopSimulating();
            DrawStraps();
            return;
        }
        ulong frame = Engine.GetProcessFrames();
        float dt = 0.0f;
        if (frame != _lastFrame)
        {
            dt = Mathf.Min((float)GetProcessDeltaTime(), 0.05f);
        }
        _lastFrame = frame;
        Vector3 hips = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_hips).Origin;
        bool moved = hips.DistanceTo(_lastHips) > TeleportDistance;
        _lastHips = hips;
        if (_needReset || moved || _pack.FitVersion != _fitVersion)
        {
            Restart();
        }
        else
        {
            ulong started = Time.GetTicksUsec();
            _hull?.Pose(false);
            int substeps = Mathf.Clamp(Mathf.CeilToInt(dt * StepsPerSecond), 1, 24);
            Transform3D rest = HeldFrame() * _rest;
            _physics.MaxDistance = MaxSwing;
            _physics.MaxAngle = Mathf.DegToRad(MaxTurnDegrees);
            _physics.Step(dt, substeps, Held(), rest);
            StepMicroseconds = (long)(Time.GetTicksUsec() - started);
            SwingDistance = _physics.Position.DistanceTo(rest.Origin);
            SwingAngle = _physics.Rotation.AngleTo(rest.Basis.GetRotationQuaternion());
            Transform3D hung = HungPivot();
            if (!hung.IsFinite() || SwingDistance > ResetDistance)
            {
                Restart();
            }
            else
            {
                _pack.Simulate(hung);
            }
        }
        DrawStraps();
    }

    /// <summary>The pack's centre of mass's frame at its fit (no scale): where its straps hold it.</summary>
    private Transform3D FitFrame()
    {
        Transform3D pivot = _pack!.FittedPivot;
        return new Transform3D(pivot.Basis.Orthonormalized(), pivot * _centre);
    }

    /// <summary>
    /// Where the straps hold the pack now (its centre of mass's frame, no scale): its top on the upper
    /// spine (the shoulder straps), turned so its bottom points at the hips (the hip belt), its back to
    /// the body.
    /// </summary>
    private Transform3D HeldFrame()
    {
        Transform3D spine = _rig!.GlobalTransform * _rig.GetBoneGlobalPose(_pack!.BoneIdx);
        Transform3D hips = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_hips);
        Vector3 top = spine * _topOnSpine;
        Vector3 down = (hips * _bottomOnHips - top).Normalized();
        Vector3 away = (spine.Basis * _awayOnSpine).Normalized();
        Basis world = Axes(down, away);
        if (!world.IsFinite())
        {
            return new Transform3D(spine.Basis.Orthonormalized() * _packAxes, top - spine.Basis.Orthonormalized() * _packAxes * _topInPack);
        }
        Basis turn = world * _packAxes;
        return new Transform3D(turn, top - turn * _topInPack);
    }

    /// <summary>An orthonormal frame from a down direction and an away direction (columns: across, down, away).</summary>
    private static Basis Axes(Vector3 down, Vector3 away)
    {
        Vector3 out_ = (away - down * away.Dot(down)).Normalized();
        Vector3 across = down.Cross(out_).Normalized();
        return new Basis(across, down, out_);
    }

    /// <summary>Where the straps hold each tie now.</summary>
    private List<Vector3> Held()
    {
        Transform3D frame = HeldFrame();
        _held.Clear();
        foreach (Vector3 local in _ties) { _held.Add(frame * local); }
        return _held;
    }

    /// <summary>
    /// The pack's fit moved in towards the body and tilted (about its across axis) until its back
    /// panel's top and bottom both rest on the body's surface, PackClearance off it: whatever the body,
    /// its clothes or the measured fit, the pack lies on the back.
    /// </summary>
    private Transform3D OnTheBody(Transform3D fit, List<Vector3> panel, ICollection<int> torso)
    {
        float maxTilt = Mathf.DegToRad(MaxLayTiltDegrees);
        for (int pass = 0; pass < 3; pass++)
        {
            Vector3 inward = (fit.Basis * new Vector3(0.0f, 0.0f, -_awaySign)).Normalized();
            float top = float.PositiveInfinity, bottom = float.PositiveInfinity;
            float topY = 0.0f, bottomY = 0.0f;
            int tops = 0, bottoms = 0;
            foreach (Vector3 point in panel)
            {
                float gap = _hull!.GapAlong(fit.Origin + fit.Basis * point, inward, 0.35f, torso);
                if (point.Y >= 0.0f) { top = Mathf.Min(top, gap); topY += point.Y; tops++; }
                else { bottom = Mathf.Min(bottom, gap); bottomY += point.Y; bottoms++; }
            }
            if (tops == 0 || bottoms == 0) { return fit; }
            topY /= tops;
            bottomY /= bottoms;
            // Nothing under one end (off the body): keep its height, just move in by the other.
            if (top >= 0.35f) { top = bottom; }
            if (bottom >= 0.35f) { bottom = top; }
            if (top >= 0.35f) { return fit; }
            float tilt = Mathf.Clamp((top - bottom) / Mathf.Max(topY - bottomY, 0.01f), -maxTilt, maxTilt);
            float move = top - PackClearance - tilt * topY;
            // A turn about the across axis by -awaySign * tilt moves a panel point at height y by tilt * y inwards.
            Basis turn = new Basis(fit.Basis.X.Normalized(), -_awaySign * tilt);
            fit = new Transform3D(turn * fit.Basis, fit.Origin + inward * move);
            if (Mathf.Abs(move) < 0.002f && Mathf.Abs(tilt) < 0.005f) { break; }
        }
        return fit;
    }

    private void AddTie(Vector3 local, float give)
    {
        _ties.Add(local);
        _physics!.Tie(local, give, HeldFrame() * local);
    }

    /// <summary>Where the simulated pack puts the pivot.</summary>
    private Transform3D HungPivot()
    {
        Basis turn = new Basis(_physics!.Rotation).Scaled(Vector3.One * _scale);
        return new Transform3D(turn, _physics.Position - turn * _centre);
    }

    /// <summary>The pack back at its fit, still, tied on by its straps' ends.</summary>
    private void Restart()
    {
        if (_physics == null || _pack == null || _rig == null)
        {
            return;
        }
        Resets++;
        _needReset = false;
        _fitVersion = _pack.FitVersion;
        _lastHips = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_hips).Origin;
        _scale = _pack.FittedPivot.Basis.Scale.X;
        Aabb box = _pack.PackBounds();
        _centre = box.GetCenter();
        _hull?.Pose(true);
        _physics.LinearDamping = PackDamping;
        _physics.AngularDamping = PackTurnDamping;
        _physics.PackClearance = PackClearance;
        List<int> torso = new();
        foreach (string name in new[] { "Hips", "Spine", "Spine1", "Spine2" })
        {
            int bone = ClothingRig.Bone(_rig, name);
            if (bone >= 0) { torso.Add(bone); }
        }
        _physics.PanelBones = torso;
        // The back panel: a grid over its body side (offsets from the centre of mass, pack axes).
        Vector3 halfSize = box.Size * 0.5f;
        float backZ = _centre.Z - _awaySign * halfSize.Z;
        List<Vector3> panel = new();
        foreach (float x in new[] { -0.7f, 0.0f, 0.7f })
        {
            foreach (float y in new[] { -0.8f, -0.27f, 0.27f, 0.8f })
            {
                panel.Add((new Vector3(_centre.X + x * halfSize.X, _centre.Y + y * halfSize.Y, backZ) - _centre) * _scale);
            }
        }
        Transform3D fit = FitFrame();
        if (LayOnBody && _hull != null)
        {
            fit = OnTheBody(fit, panel, torso);
        }
        _physics.Clear(_hull, PackMass, box.Size * _scale, fit);
        _ties.Clear();
        // The held frame, measured at the fit: the pack's top (between the shoulder straps' ends) on
        // the upper spine, its bottom (between the hip belt's ends, else the shoulder straps' lower
        // ends) on the hips, and which way is away from the back.
        Vector3 topEnd = Vector3.Zero;
        Vector3 bottomEnd = Vector3.Zero;
        int tops = 0;
        int bottoms = 0;
        foreach (int strap in new[] { LeftStrap, RightStrap })
        {
            List<Anchor> path = _paths[strap];
            if (!Shown(strap) || path.Count < 2 || path[0].Bone >= 0) { continue; }
            topEnd += path[0].Local;
            tops++;
        }
        int lower = Belt;
        if (!Shown(Belt) || _paths[Belt].Count < 2) { lower = -1; }
        foreach (int strap in new[] { LeftStrap, RightStrap, Belt })
        {
            List<Anchor> path = _paths[strap];
            if (!Shown(strap) || path.Count < 2 || (lower >= 0 && strap != Belt) || (lower < 0 && strap == Belt)) { continue; }
            if (path[0].Bone < 0 && strap == Belt) { bottomEnd += path[0].Local; bottoms++; }
            if (path[^1].Bone < 0) { bottomEnd += path[^1].Local; bottoms++; }
        }
        Vector3 half = box.Size * 0.5f;
        if (tops == 0) { topEnd = _centre + new Vector3(0.0f, half.Y, 0.0f); tops = 1; }
        if (bottoms == 0) { bottomEnd = _centre - new Vector3(0.0f, half.Y, 0.0f); bottoms = 1; }
        Vector3 topRel = (topEnd / tops - _centre) * _scale;
        Vector3 bottomRel = (bottomEnd / bottoms - _centre) * _scale;
        Vector3 awayRel = new(0.0f, 0.0f, _awaySign);
        Transform3D spine = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_pack.BoneIdx);
        Transform3D hips = _rig.GlobalTransform * _rig.GetBoneGlobalPose(_hips);
        _topOnSpine = spine.AffineInverse() * (fit * topRel);
        _bottomOnHips = hips.AffineInverse() * (fit * bottomRel);
        _awayOnSpine = spine.Basis.Inverse() * (fit.Basis * awayRel);
        _topInPack = topRel;
        _packAxes = Axes((bottomRel - topRel).Normalized(), awayRel).Transposed();
        // The straps' ends sewn to the pack, tied to the held frame, pulled a little in towards the
        // body (the straps' tension: the back panel stops the pack on the body, so it lies snug). The
        // shoulder straps' tops hold it, the rest give a little.
        Vector3 pull = new(0.0f, 0.0f, -_awaySign * Snug);
        foreach (int strap in new[] { LeftStrap, RightStrap, Belt })
        {
            List<Anchor> path = _paths[strap];
            if (!Shown(strap) || path.Count < 2) { continue; }
            float first = LowerGive;
            if (strap != Belt) { first = ShoulderGive; }
            if (path[0].Bone < 0) { AddTie((path[0].Local - _centre) * _scale + pull, first); }
            if (path[^1].Bone < 0) { AddTie((path[^1].Local - _centre) * _scale + pull, LowerGive); }
        }
        // The back panel rests on the body.
        foreach (Vector3 point in panel) { _physics.Contact(point); }
        // It comes to rest on the back once (the straps pulling it in, the panel stopping it); the
        // swing is then around that resting place.
        _physics.MaxDistance = MaxSwing;
        _physics.MaxAngle = Mathf.DegToRad(MaxTurnDegrees);
        _physics.Settle(Held(), 1.0f);
        Transform3D held = HeldFrame();
        _rest = held.AffineInverse() * new Transform3D(new Basis(_physics.Rotation), _physics.Position);
        _physics.Step(0.0f, 1, Held(), held * _rest);
        SwingDistance = 0.0f;
        SwingAngle = 0.0f;
        _pack.Simulate(HungPivot());
    }

    /// <summary>The body's outward direction under a strap point (its surface), else from the body's middle line.</summary>
    private Vector3 Surface(Vector3 point, Vector3 along, Vector3 axisFrom, Vector3 axisTo)
    {
        Vector3 normal = Vector3.Zero;
        if (_hull != null && Simulate)
        {
            normal = _hull.NormalNear(point, Clearance + StrapThickness + 0.03f);
            normal -= along * along.Dot(normal);
        }
        if (normal.LengthSquared() < 0.25f)
        {
            return Outward(point, along, axisFrom, axisTo);
        }
        return normal.Normalized();
    }

    /// <summary>The straps along their measured paths over the body, their ends sewn to the pack where it hangs now.</summary>
    private void DrawStraps()
    {
        if (_rig == null || _pack == null || _webbing == null)
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
            Vector3 normal = Surface(points[i], along, axisFrom, axisTo);
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
