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
        "DEF-upper_arm.L", "DEF-forearm.L", "DEF-upper_arm.R", "DEF-forearm.R",
        "DEF-thigh.L", "DEF-shin.L", "DEF-thigh.R", "DEF-shin.R",
    };
    /// <summary>For each bone, the bone whose head marks its tip (sets capsule length); empty uses DefaultBoneLength.</summary>
    [Export] public string[] TipBones { get; set; } =
    {
        "DEF-spine.001", "DEF-spine.003", "DEF-neck", "",
        "DEF-forearm.L", "DEF-hand.L", "DEF-forearm.R", "DEF-hand.R",
        "DEF-shin.L", "DEF-foot.L", "DEF-shin.R", "DEF-foot.R",
    };
    [Export] public float[] Radii { get; set; } = { 0.13f, 0.13f, 0.15f, 0.11f, 0.05f, 0.045f, 0.05f, 0.045f, 0.07f, 0.055f, 0.07f, 0.055f };
    /// <summary>Share of body mass per bone (sums to about 1).</summary>
    [Export] public float[] MassShares { get; set; } = { 0.16f, 0.15f, 0.16f, 0.08f, 0.03f, 0.025f, 0.03f, 0.025f, 0.1f, 0.05f, 0.1f, 0.05f };
    /// <summary>How much a hit to each bone hurts compared to the hips.</summary>
    [Export] public float[] DamageMultipliers { get; set; } = { 1.0f, 1.2f, 1.2f, 2.0f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f };
    [Export] public float DefaultBoneLength { get; set; } = 0.2f;
    [Export] public float BodyMass { get; set; } = 70.0f;
    /// <summary>The pack's mass is added to this bone.</summary>
    [Export] public string LoadBone { get; set; } = "DEF-spine.003";
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
        public float Radius;
        public float MassShare;
        public float DamageMultiplier;
        public bool IsRoot;
        public Vector3 PreviousVelocity;
    }

    private readonly List<RagdollBone> _bones = new();
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
        if (Downed)
        {
            GameEvents.PublishDowned(false);
        }
    }

    private void BuildSkeleton(Skeleton3D skeleton)
    {
        _simulator = new PhysicalBoneSimulator3D { Name = "Ragdoll" };
        skeleton.AddChild(_simulator);

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
            // Capsule centred halfway along the bone (bones point along their local +Y); joint at the bone's head.
            Transform3D toHead = new(Basis.Identity, new Vector3(0.0f, -length * 0.5f, 0.0f));
            PhysicalBone3D body = new()
            {
                Name = $"Bone_{BoneNames[i]}",
                BodyOffset = toHead,
                JointOffset = toHead,
                Mass = BodyMass * share,
                CollisionLayer = RagdollLayer,
                CollisionMask = WorldMask,
                Friction = 0.8f,
            };
            // bone_name has no C# accessor in 4.7.
            body.Set("bone_name", BoneNames[i]);
            if (i == 0)
            {
                body.JointType = PhysicalBone3D.JointTypeEnum.None;
            }
            else
            {
                body.JointType = PhysicalBone3D.JointTypeEnum.Cone;
            }
            body.AddChild(new CollisionShape3D
            {
                Shape = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(length, radius * 2.0f + 0.01f) },
            });
            _simulator.AddChild(body);
            if (i > 0)
            {
                body.Set("joint_constraints/swing_span", SwingLimit);
                body.Set("joint_constraints/twist_span", TwistLimit);
            }
            body.GlobalTransform = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone) * toHead.AffineInverse();

            _bones.Add(new RagdollBone
            {
                Body = body,
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
    }

    /// <summary>Goes down as a ragdoll carrying <paramref name="velocity"/>, plus <paramref name="push"/> on the upper body.</summary>
    public void GoDown(Vector3 velocity, Vector3 push)
    {
        if (Downed || _simulator == null)
        {
            return;
        }

        Downed = true;
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
        bool settled = _downTime >= MinDownTime && _settledTime >= SettleTime;
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
