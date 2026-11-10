using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Every fall is physical. At startup this adds a physical skeleton (PhysicalBoneSimulator3D with
/// one capsule body per major bone) to the model. The carrier goes down as a ragdoll when balance
/// is lost, on a hard landing, or on death, keeping its momentum; the pack's mass rides on the
/// chest. While down, lost incoming speed into a surface is an impact: the surface hit is found by a ray
/// along the bone's previous motion, and damage = (normal impact speed − safe speed) × damage per m/s ×
/// surface hardness × body-part multiplier × the bone's share of body mass. Once settled (and
/// alive) the ragdoll pose blends back into the animation and the carrier is up.
/// </summary>
public partial class PlayerRagdoll : Node
{
    [Export] public PlayerController? Body { get; set; }
    [Export] public Skeleton3D? Skeleton { get; set; }
    [Export] public CarrierHealth? Health { get; set; }

    [ExportGroup("Bones")]
    /// <summary>Bones that get a physical body; the rest follow their parent.</summary>
    [Export] public string[] BoneNames { get; set; } =
    {
        "DEF-hips", "DEF-spine.001", "DEF-spine.003", "DEF-head",
        "DEF-upper_arm.L", "DEF-forearm.L", "DEF-hand.L", "DEF-upper_arm.R", "DEF-forearm.R", "DEF-hand.R",
        "DEF-thigh.L", "DEF-shin.L", "DEF-foot.L", "DEF-thigh.R", "DEF-shin.R", "DEF-foot.R",
    };
    /// <summary>For each bone, the bone whose head marks its tip (sets capsule length); empty uses DefaultBoneLength.</summary>
    [Export] public string[] TipBones { get; set; } =
    {
        "DEF-spine.001", "DEF-spine.003", "DEF-neck", "",
        "DEF-forearm.L", "DEF-hand.L", "DEF-f_middle.01.L", "DEF-forearm.R", "DEF-hand.R", "DEF-f_middle.01.R",
        "DEF-shin.L", "DEF-foot.L", "DEF-toe.L", "DEF-shin.R", "DEF-foot.R", "DEF-toe.R",
    };
    [Export] public float[] Radii { get; set; } = { 0.13f, 0.13f, 0.15f, 0.11f, 0.05f, 0.045f, 0.04f, 0.05f, 0.045f, 0.04f, 0.07f, 0.055f, 0.045f, 0.07f, 0.055f, 0.045f };
    /// <summary>
    /// Share of body mass per bone, from Plagenhoef et al. (1983): pelvis, abdomen, thorax (with the
    /// shoulders), head and neck, then upper arm, forearm, hand and thigh, shank, foot on each side.
    /// </summary>
    [Export] public float[] MassShares { get; set; } = { 0.1366f, 0.1306f, 0.201f, 0.0826f, 0.0325f, 0.0187f, 0.0065f, 0.0325f, 0.0187f, 0.0065f, 0.105f, 0.0475f, 0.0143f, 0.105f, 0.0475f, 0.0143f };
    /// <summary>How much a hit to each bone hurts compared to the hips.</summary>
    [Export] public float[] DamageMultipliers { get; set; } = { 1.0f, 1.2f, 1.2f, 2.0f, 0.6f, 0.6f, 0.4f, 0.6f, 0.6f, 0.4f, 0.6f, 0.6f, 0.4f, 0.6f, 0.6f, 0.4f };
    /// <summary>
    /// Joints that only bend one way, like a hinge (elbows and knees): the most they bend, in degrees,
    /// positive bending toward the front of the body (elbows), negative toward the back (knees).
    /// 0 makes a ball joint with SwingLimits and TwistLimits instead.
    /// </summary>
    [Export] public float[] HingeBend { get; set; } = { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 145.0f, 0.0f, 0.0f, 145.0f, 0.0f, 0.0f, -140.0f, 0.0f, 0.0f, -140.0f, 0.0f };
    /// <summary>How far a hinge may bend the wrong way (a straight knee or elbow locks slightly past straight), in degrees.</summary>
    [Export] public float HingeOverextension { get; set; } = 5.0f;
    /// <summary>Physics engines disagree on which way a hinge's angle counts; this mirrors the limits (RagdollJointTests checks it).</summary>
    [Export] public bool HingeAngleReversed { get; set; } = true;
    /// <summary>Lays each ball joint's twist axis along its bone (off: the joint frame is the bone's own, twist about its side).</summary>
    [Export] public bool ConeAlongBone { get; set; } = true;
    /// <summary>Ball joints: how far the bone may swing away from its parent (degrees), per bone; the root's entry is unused.</summary>
    [Export] public float[] SwingLimits { get; set; } = { 0.0f, 25.0f, 25.0f, 40.0f, 80.0f, 0.0f, 50.0f, 80.0f, 0.0f, 50.0f, 65.0f, 0.0f, 35.0f, 65.0f, 0.0f, 35.0f };
    /// <summary>Ball joints: how far the bone may twist about its own length (degrees), per bone.</summary>
    [Export] public float[] TwistLimits { get; set; } = { 0.0f, 15.0f, 15.0f, 40.0f, 45.0f, 0.0f, 25.0f, 45.0f, 0.0f, 25.0f, 25.0f, 0.0f, 10.0f, 25.0f, 0.0f, 10.0f };
    /// <summary>The bone pointing from heel to toe; its direction marks the body's front (which way knees and elbows bend).</summary>
    [Export] public string FrontBone { get; set; } = "DEF-foot.L";
    [Export] public float DefaultBoneLength { get; set; } = 0.2f;
    [Export] public float BodyMass { get; set; } = 70.0f;
    /// <summary>The pack's mass is added to this bone.</summary>
    [Export] public string LoadBone { get; set; } = "DEF-spine.003";
    /// <summary>Swing and twist for a ball joint whose bone has no entry in SwingLimits/TwistLimits (degrees).</summary>
    [Export] public float SwingLimit { get; set; } = 45.0f;
    [Export] public float TwistLimit { get; set; } = 20.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint RagdollLayer { get; set; } = 2;
    [Export(PropertyHint.Layers3DPhysics)] public uint WorldMask { get; set; } = 1;

    [ExportGroup("Going down")]
    /// <summary>Landing faster than this puts the carrier down, in m/s (about a 3 m drop).</summary>
    [Export] public float HardLandingSpeed { get; set; } = 7.5f;
    /// <summary>Extra speed given to the chest toward the fall when balance is lost, in m/s.</summary>
    [Export] public float BalanceFallPush { get; set; } = 1.5f;
    /// <summary>Share of a blow's velocity (KnockdownRequested) the whole body takes; the upper body takes all of it.</summary>
    [Export] public float KnockdownBodyShare { get; set; } = 0.45f;

    [ExportGroup("Getting up")]
    [Export] public float MinDownTime { get; set; } = 1.5f;
    /// <summary>After a throw on purpose (the dive, the jump) he may get up this soon once settled.</summary>
    [Export] public float LaunchMinDownTime { get; set; } = 0.6f;
    [Export] public float MaxDownTime { get; set; } = 6.0f;
    /// <summary>The ragdoll counts as settled when every bone is slower than this, in m/s.</summary>
    [Export] public float SettleSpeed { get; set; } = 0.35f;
    [Export] public float SettleTime { get; set; } = 0.8f;
    [Export] public float GetUpBlendTime { get; set; } = 0.6f;

    [ExportGroup("Impacts")]
    /// <summary>Impacts slower than this don't hurt, in m/s.</summary>
    [Export] public float SafeImpactSpeed { get; set; } = 4.0f;
    /// <summary>Damage per m/s above the safe speed, for the whole body on rock.</summary>
    [Export] public float DamagePerSpeed { get; set; } = 8.0f;
    /// <summary>Extra length of the impact ray beyond the bone's radius and last step, in m.</summary>
    [Export] public float ImpactProbeMargin { get; set; } = 0.15f;
    /// <summary>Hardness of geometry without a SurfaceType.</summary>
    [Export] public float DefaultHardness { get; set; } = 1.0f;
    [Export] public float GroundProbeDistance { get; set; } = 3.0f;

    public bool Downed { get; private set; }
    /// <summary>Total damage taken in the current fall.</summary>
    public float FallDamage { get; private set; }

    private sealed class RagdollBone
    {
        public PhysicalBone3D Body = null!;
        public string Name = "";
        public float Radius;
        public float MassShare;
        public float DamageMultiplier;
        public bool IsRoot;
        public Vector3 PreviousVelocity;
    }

    private readonly List<RagdollBone> _bones = new();

    /// <summary>The physical bodies, root (hips) first, in BoneNames order (missing bones skipped).</summary>
    public IReadOnlyList<PhysicalBone3D> Bodies => _bones.ConvertAll(bone => bone.Body);

    /// <summary>The physical body for a bone name, or null.</summary>
    public PhysicalBone3D? BodyOf(string boneName)
    {
        foreach (RagdollBone bone in _bones)
        {
            if (bone.Name == boneName)
            {
                return bone.Body;
            }
        }
        return null;
    }

    /// <summary>The body's front when the ragdoll was built (heel to toe, flat), in world space.</summary>
    public Vector3 BuildFront { get; private set; } = Vector3.Forward;
    private PhysicalBoneSimulator3D? _simulator;
    private PhysicalBone3D? _loadBone;
    private float _loadBoneBaseMass;
    private CollisionShape3D? _bodyShape;
    private bool _gettingUp;
    private float _downTime;
    private float _settledTime;
    private float _blend;
    private int _velocityFramesPending;
    private Vector3 _entryVelocity;
    private Vector3 _entryPush;
    private BalanceState _lastBalanceState = BalanceState.Stable;
    private Godot.Collections.Array<Rid> _probeExclude = new();

    public override void _Ready()
    {
        if (Body == null || Skeleton == null)
        {
            GD.PushError("PlayerRagdoll: Body and Skeleton must be assigned.");
            return;
        }

        // Runs after the controller so following the hips wins over anything it does.
        ProcessPhysicsPriority = 20;
        foreach (Node child in Body.GetChildren())
        {
            if (child is CollisionShape3D shape)
            {
                _bodyShape = shape;
            }
        }
        _probeExclude = new Godot.Collections.Array<Rid> { Body.GetRid() };

        BuildSkeleton(Skeleton);
        Body.Teleported += OnTeleported;
        GameEvents.BalanceUpdated += OnBalanceUpdated;
        GameEvents.Landed += OnLanded;
        GameEvents.LoadChanged += OnLoadChanged;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.KnockdownRequested += OnKnockdownRequested;
        GameEvents.RagdollLaunchRequested += OnRagdollLaunchRequested;
        OnLoadChanged(GameEvents.CurrentLoad);
    }

    public override void _ExitTree()
    {
        if (Body != null)
        {
            Body.Teleported -= OnTeleported;
        }
        GameEvents.BalanceUpdated -= OnBalanceUpdated;
        GameEvents.Landed -= OnLanded;
        GameEvents.LoadChanged -= OnLoadChanged;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.KnockdownRequested -= OnKnockdownRequested;
        GameEvents.RagdollLaunchRequested -= OnRagdollLaunchRequested;
        if (Downed)
        {
            GameEvents.PublishDowned(false);
        }
    }

    private void BuildSkeleton(Skeleton3D skeleton)
    {
        _simulator = new PhysicalBoneSimulator3D { Name = "Ragdoll" };
        skeleton.AddChild(_simulator);
        Vector3 front = FrontOf(skeleton);
        BuildFront = front;
        List<(PhysicalBone3D Body, PhysicalBone3D.JointTypeEnum Type, Godot.Collections.Dictionary Limits)> joints = new();

        for (int i = 0; i < BoneNames.Length; i++)
        {
            int bone = skeleton.FindBone(BoneNames[i]);
            if (bone < 0)
            {
                GD.PushWarning($"PlayerRagdoll: bone {BoneNames[i]} not found.");
                continue;
            }

            float length = DefaultBoneLength;
            if (i < TipBones.Length && TipBones[i] != "")
            {
                int tip = skeleton.FindBone(TipBones[i]);
                if (tip >= 0)
                {
                    length = skeleton.GetBoneGlobalRest(bone).Origin.DistanceTo(skeleton.GetBoneGlobalRest(tip).Origin);
                }
            }

            float radius = ValueAt(Radii, i, 0.05f);
            float share = ValueAt(MassShares, i, 0.05f);
            // Capsule centred halfway along the bone (bones point along their local +Y); joint at the bone's
            // head. The body sits at bone * BodyOffset, so the offset points from the head to the middle.
            Transform3D toMiddle = new(Basis.Identity, new Vector3(0.0f, length * 0.5f, 0.0f));
            Vector3 head = new(0.0f, -length * 0.5f, 0.0f);
            PhysicalBone3D body = new()
            {
                Name = $"Bone_{BoneNames[i]}",
                BodyOffset = toMiddle,
                JointOffset = new Transform3D(Basis.Identity, head),
                Mass = BodyMass * share,
                CollisionLayer = RagdollLayer,
                CollisionMask = WorldMask,
                Friction = 0.8f,
            };
            // bone_name has no C# accessor in 4.7.
            body.Set("bone_name", BoneNames[i]);
            Basis boneBasis = (skeleton.GlobalTransform * skeleton.GetBoneGlobalRest(bone)).Basis.Orthonormalized();
            float hinge = ValueAt(HingeBend, i, 0.0f);
            PhysicalBone3D.JointTypeEnum jointType = PhysicalBone3D.JointTypeEnum.Cone;
            if (i == 0)
            {
                jointType = PhysicalBone3D.JointTypeEnum.None;
            }
            else if (Mathf.Abs(hinge) > 0.01f)
            {
                jointType = PhysicalBone3D.JointTypeEnum.Hinge;
            }
            body.AddChild(new CollisionShape3D
            {
                Shape = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(length, radius * 2.0f + 0.01f) },
            });
            _simulator.AddChild(body);
            // A joint takes its frames (and the middle of its limits) from where both bodies stand when it
            // is made: build it in the rig's rest pose, so place the body first and make the joints once
            // every body is in place (below). Going down moves the bodies to the pose of the moment.
            body.GlobalTransform = skeleton.GlobalTransform * skeleton.GetBoneGlobalRest(bone) * toMiddle;
            Godot.Collections.Dictionary limits = new();
            if (jointType == PhysicalBone3D.JointTypeEnum.Hinge)
            {
                // The hinge turns about its Z axis: lay that across the bone so that turning it the
                // positive way swings the bone toward the side it really bends to (front for elbows,
                // back for knees). The limits then let it bend that way only.
                Vector3 along = boneBasis.Y.Normalized();
                Vector3 bendTo = front;
                if (hinge < 0.0f)
                {
                    bendTo = -front;
                }
                bendTo = (bendTo - along * bendTo.Dot(along)).Normalized();
                Vector3 axis = along.Cross(bendTo).Normalized();
                Vector3 axisLocal = (boneBasis.Inverse() * axis).Normalized();
                body.JointOffset = new Transform3D(JointFrame(axisLocal, Vector3.Up), head);
                // The hinge counts its angle from the pose it was built in: take off the bend it already had.
                float built = 0.0f;
                int parentBone = skeleton.GetBoneParent(bone);
                if (parentBone >= 0)
                {
                    Vector3 parentAlong = (skeleton.GlobalTransform * skeleton.GetBoneGlobalRest(parentBone)).Basis.Y.Normalized();
                    built = Mathf.RadToDeg(Mathf.Atan2(parentAlong.Cross(along).Dot(axis), parentAlong.Dot(along)));
                }
                float lower = -HingeOverextension - built;
                float upper = Mathf.Abs(hinge) - built;
                if (HingeAngleReversed)
                {
                    float mirrored = -upper;
                    upper = -lower;
                    lower = mirrored;
                }
                limits["joint_constraints/angular_limit_enabled"] = true;
                limits["joint_constraints/angular_limit_lower"] = lower;
                limits["joint_constraints/angular_limit_upper"] = upper;
            }
            else if (i > 0)
            {
                // The cone's twist axis is the joint's X: lay it along the bone, so the swing limit is
                // how far the bone leans off its parent and the twist limit how far it turns about itself.
                if (ConeAlongBone)
                {
                    body.JointOffset = new Transform3D(new Basis(Vector3.Back, Mathf.Pi * 0.5f), head);
                }
                limits["joint_constraints/swing_span"] = ValueAt(SwingLimits, i, SwingLimit);
                limits["joint_constraints/twist_span"] = ValueAt(TwistLimits, i, TwistLimit);
            }
            joints.Add((body, jointType, limits));

            _bones.Add(new RagdollBone
            {
                Body = body,
                Name = BoneNames[i],
                Radius = radius,
                MassShare = share,
                DamageMultiplier = ValueAt(DamageMultipliers, i, 1.0f),
                IsRoot = i == 0,
            });
            if (BoneNames[i] == LoadBone)
            {
                _loadBone = body;
                _loadBoneBaseMass = body.Mass;
            }
        }

        // Every body stands in its bone now: make the joints (setting the type builds the joint from
        // the bodies' current places), then set their limits on the live joint.
        foreach ((PhysicalBone3D body, PhysicalBone3D.JointTypeEnum type, Godot.Collections.Dictionary limits) in joints)
        {
            body.JointType = type;
            foreach (Variant key in limits.Keys)
            {
                body.Set(key.AsString(), limits[key]);
            }
        }
    }

    /// <summary>The body's front: FrontBone's direction (heel to toe) laid flat; the skeleton's -Z without it.</summary>
    private Vector3 FrontOf(Skeleton3D skeleton)
    {
        Vector3 fallback = -skeleton.GlobalBasis.Z;
        int foot = skeleton.FindBone(FrontBone);
        if (foot >= 0)
        {
            Vector3 toe = (skeleton.GlobalTransform * skeleton.GetBoneGlobalRest(foot)).Basis.Y;
            toe.Y = 0.0f;
            if (toe.LengthSquared() > 0.01f)
            {
                return toe.Normalized();
            }
        }
        fallback.Y = 0.0f;
        if (fallback.LengthSquared() < 0.01f)
        {
            return Vector3.Forward;
        }
        return fallback.Normalized();
    }

    /// <summary>A right-handed frame whose Z is <paramref name="z"/>, with Y as close to <paramref name="up"/> as it can be.</summary>
    private static Basis JointFrame(Vector3 z, Vector3 up)
    {
        Vector3 x = up.Cross(z);
        if (x.LengthSquared() < 0.0001f)
        {
            x = Vector3.Right.Cross(z);
        }
        x = x.Normalized();
        Vector3 y = z.Cross(x).Normalized();
        return new Basis(x, y, z);
    }

    /// <summary>Goes down as a ragdoll carrying <paramref name="velocity"/>, plus <paramref name="push"/> on the upper body.</summary>
    public void GoDown(Vector3 velocity, Vector3 push)
    {
        if (Downed || _simulator == null)
        {
            return;
        }

        Downed = true;
        _launched = false;
        _gettingUp = false;
        _downTime = 0.0f;
        _settledTime = 0.0f;
        FallDamage = 0.0f;
        _entryVelocity = velocity;
        _entryPush = push;
        _bodyShape?.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);

        _simulator.Influence = 1.0f;
        _simulator.PhysicalBonesStartSimulation();
        // Bodies only accept velocities once simulating; set them on the next physics step.
        _velocityFramesPending = 1;
        foreach (RagdollBone bone in _bones)
        {
            bone.PreviousVelocity = velocity;
        }
        GameEvents.PublishDowned(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Downed || Body == null || _simulator == null || _bones.Count == 0)
        {
            return;
        }

        float dt = (float)delta;
        _downTime += dt;

        if (_velocityFramesPending > 0)
        {
            _velocityFramesPending--;
            foreach (RagdollBone bone in _bones)
            {
                Vector3 velocity = _entryVelocity;
                if (!bone.IsRoot)
                {
                    velocity += _entryPush;
                }
                bone.Body.LinearVelocity = velocity;
                bone.PreviousVelocity = velocity;
            }
            return;
        }

        if (!_gettingUp)
        {
            ApplyImpacts(dt);
        }
        FollowHips();

        if (_gettingUp)
        {
            _blend += dt / GetUpBlendTime;
            _simulator.Influence = Mathf.Clamp(1.0f - _blend, 0.0f, 1.0f);
            if (_blend >= 1.0f)
            {
                StandUp();
            }
            return;
        }

        float fastest = 0.0f;
        foreach (RagdollBone bone in _bones)
        {
            fastest = Mathf.Max(fastest, bone.Body.LinearVelocity.Length());
        }
        if (fastest < SettleSpeed)
        {
            _settledTime += dt;
        }
        else
        {
            _settledTime = 0.0f;
        }

        bool dead = Health != null && Health.Dead;
        float minDown = MinDownTime;
        float settleNeeded = SettleTime;
        if (_launched)
        {
            minDown = LaunchMinDownTime;
            settleNeeded = SettleTime * 0.5f;
        }
        bool settled = _downTime >= minDown && _settledTime >= settleNeeded;
        if (!dead && (settled || _downTime >= MaxDownTime))
        {
            _gettingUp = true;
            _blend = 0.0f;
        }
    }

    /// <summary>Lost normal approach speed causes damage, scaled by hardness, body part and mass share.</summary>
    private void ApplyImpacts(float dt)
    {
        PhysicsDirectSpaceState3D space = Body!.GetWorld3D().DirectSpaceState;
        float damage = 0.0f;
        float worst = 0.0f;
        string source = "";

        foreach (RagdollBone bone in _bones)
        {
            Vector3 velocity = bone.Body.LinearVelocity;
            float impact = (bone.PreviousVelocity - velocity).Length();
            Vector3 travel = bone.PreviousVelocity;
            bone.PreviousVelocity = velocity;
            if (impact <= SafeImpactSpeed || travel.IsZeroApprox())
            {
                continue;
            }

            Vector3 from = bone.Body.GlobalPosition;
            Vector3 to = from + travel.Normalized() * (travel.Length() * dt + bone.Radius + ImpactProbeMargin);
            PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to, WorldMask, _probeExclude);
            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit.Count == 0)
            {
                // A joint yanked the bone, not the world.
                continue;
            }

            impact = ImpactMath.SpeedLost(travel, velocity, hit["normal"].AsVector3());
            if (impact <= SafeImpactSpeed)
            {
                continue;
            }

            float hardness = DefaultHardness;
            string surface = "obstacle";
            SurfaceType? type = SurfaceLookup.Find(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
            if (type != null)
            {
                hardness = type.Hardness;
                surface = type.DisplayName;
            }

            float hurt = (impact - SafeImpactSpeed) * DamagePerSpeed * hardness * bone.DamageMultiplier * bone.MassShare;
            damage += hurt;
            if (hurt > worst)
            {
                worst = hurt;
                source = surface;
            }
        }

        if (damage > 0.0f)
        {
            damage *= (1.0f - GameEvents.CurrentGear.FallProtection) * (1.0f - GameEvents.CurrentGear.PhysicalProtection);
            damage *= GetNodeOrNull<RunState>("/root/RunState")?.Character.ImpactFactor ?? 1;
            FallDamage += damage;
            Health?.TakeDamage(damage, source);
        }
    }

    /// <summary>Keeps the (collision-free) body under the hips so the camera and getting up follow the ragdoll.</summary>
    private void FollowHips()
    {
        Vector3 hips = _bones[0].Body.GlobalPosition;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(hips, hips + Vector3.Down * GroundProbeDistance, WorldMask, _probeExclude);
        Godot.Collections.Dictionary hit = Body!.GetWorld3D().DirectSpaceState.IntersectRay(query);
        float ground = hips.Y - 1.0f;
        if (hit.Count > 0)
        {
            ground = hit["position"].AsVector3().Y;
        }
        Body.GlobalPosition = new Vector3(hips.X, ground, hips.Z);
    }

    private void StandUp()
    {
        _simulator?.PhysicalBonesStopSimulation();
        if (_simulator != null)
        {
            _simulator.Influence = 1.0f;
        }
        _bodyShape?.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
        _gettingUp = false;
        Downed = false;
        GameEvents.PublishDowned(false);
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        if (snapshot.State == BalanceState.Fallen && _lastBalanceState != BalanceState.Fallen && !Downed && Body != null)
        {
            Vector3 toward = snapshot.Right * snapshot.Imbalance.X + snapshot.Forward * snapshot.Imbalance.Y;
            Vector3 push = Vector3.Zero;
            if (!toward.IsZeroApprox())
            {
                push = toward.Normalized() * BalanceFallPush;
            }
            GoDown(Body.Velocity, push);
        }
        _lastBalanceState = snapshot.State;
    }

    /// <summary>A blow: the body takes part of its velocity, the upper body all of it.</summary>
    private void OnKnockdownRequested(Vector3 velocity, string cause)
    {
        if (Body == null)
        {
            return;
        }
        Vector3 body = velocity * KnockdownBodyShare;
        GoDown(Body.Velocity + body, velocity - body);
    }

    /// <summary>A throw on purpose (the dive): the body flies with the given velocity.</summary>
    private void OnRagdollLaunchRequested(Vector3 velocity, Vector3 upperPush, string cause)
    {
        GoDown(velocity, upperPush);
        _launched = Downed;
    }

    /// <summary>True while down from a throw on purpose (dive, jump), which gets up sooner.</summary>
    private bool _launched;

    private void OnLanded(float downwardSpeed)
    {
        if (downwardSpeed >= HardLandingSpeed && Body != null)
        {
            GoDown(new Vector3(Body.Velocity.X, -downwardSpeed, Body.Velocity.Z), Vector3.Zero);
        }
    }

    public override void _Process(double delta)
    {
        // Dying while upright collapses the carrier too.
        if (Health != null && Health.Dead && !Downed && Body != null)
        {
            GoDown(Body.Velocity, Vector3.Zero);
        }
    }

    private void OnLoadChanged(LoadSnapshot load)
    {
        if (_loadBone != null)
        {
            _loadBone.Mass = _loadBoneBaseMass + load.Mass;
        }
    }

    private void OnTeleported()
    {
        if (Downed)
        {
            _blend = 1.0f;
            StandUp();
        }
    }

    private void OnRespawnRequested()
    {
        OnTeleported();
    }

    private static float ValueAt(float[] values, int index, float fallback)
    {
        if (index < values.Length)
        {
            return values[index];
        }
        return fallback;
    }
}
