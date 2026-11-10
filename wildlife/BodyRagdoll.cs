using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// A ragdoll for any skinned body (an enemy that dies): built when it is needed from the body's bone
/// hit boxes (BoneHitboxes), one rigid box per sizeable part, each joined to its nearest parent part
/// by a cone joint at the bone's head. Bodies are plain, unscaled rigid bodies in the world, so a
/// scaled model is no problem; as the last modifier of the skeleton this writes their poses back
/// over the animation every frame. The parts collide with the world, not with each other.
/// Sink() lets the body go limp into the ground (the corpse sinking away).
/// </summary>
public partial class BodyRagdoll : SkeletonModifier3D
{
    /// <summary>Parts thinner than this (m) are left to follow their parent (fingers, toes, straps).</summary>
    [Export] public float MinThickness { get; set; } = 0.05f;
    [Export] public int MaxParts { get; set; } = 18;
    [Export] public float TotalMass { get; set; } = 80.0f;
    /// <summary>The boxes are this share of the fitted hit boxes (a little inside the skin).</summary>
    [Export] public float ShapeShare { get; set; } = 0.85f;
    [Export] public float SwingDegrees { get; set; } = 50.0f;
    [Export] public float TwistDegrees { get; set; } = 30.0f;
    [Export] public float Friction { get; set; } = 0.9f;
    [Export] public float AngularDamp { get; set; } = 2.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint Layer { get; set; } = 2;
    [Export(PropertyHint.Layers3DPhysics)] public uint WorldMask { get; set; } = 1;
    /// <summary>While sinking: how strongly the ground's pull is damped (slow sinking) and how much gravity still pulls.</summary>
    [Export] public float SinkDamp { get; set; } = 6.0f;
    [Export] public float SinkGravity { get; set; } = 0.25f;

    public int Parts => _parts.Count;
    public bool Sinking { get; private set; }
    /// <summary>The heaviest part (the hips or the chest), for where the body lies.</summary>
    public RigidBody3D? Core { get; private set; }

    private sealed class Part
    {
        public int Bone;
        public RigidBody3D Body = null!;
        /// <summary>From the body's transform to the bone's (world, with the model's scale).</summary>
        public Transform3D ToBone;
    }

    private readonly List<Part> _parts = new();
    private Node3D? _world;

    /// <summary>
    /// Throws <paramref name="skeleton"/>'s body as a ragdoll from its pose of the moment, moving at
    /// <paramref name="velocity"/>, the upper parts pushed a little more by <paramref name="upperPush"/>.
    /// Null if the body has no bone boxes to build it from.
    /// </summary>
    public static BodyRagdoll? Throw(Skeleton3D skeleton, Vector3 velocity, Vector3 upperPush, BodyRagdoll? settings = null)
    {
        BoneHitboxes? boxes = BoneHitboxes.For(skeleton);
        if (boxes == null || !skeleton.IsInsideTree())
        {
            return null;
        }
        BodyRagdoll ragdoll = settings ?? new BodyRagdoll();
        ragdoll.Name = "BodyRagdoll";
        ragdoll.Build(skeleton, boxes, velocity, upperPush);
        if (ragdoll._parts.Count == 0)
        {
            ragdoll.QueueFree();
            return null;
        }
        // Last of the skeleton's modifiers: its pose wins over the animation and the rig follower.
        skeleton.AddChild(ragdoll);
        return ragdoll;
    }

    private void Build(Skeleton3D skeleton, BoneHitboxes boxes, Vector3 velocity, Vector3 upperPush)
    {
        Transform3D skeletonWorld = skeleton.GlobalTransform;
        // The sizeable parts, biggest first, then in bone order (parents before children).
        List<(int Index, float Volume, Vector3 Size)> candidates = new();
        for (int i = 0; i < boxes.Count; i++)
        {
            Transform3D world = skeletonWorld * skeleton.GetBoneGlobalPose(boxes.BoneAt(i));
            Vector3 scale = world.Basis.Scale;
            Vector3 size = boxes.BoxAt(i).Size * scale * ShapeShare;
            float thinnest = Mathf.Min(size.X, Mathf.Min(size.Y, size.Z));
            if (thinnest < MinThickness)
            {
                continue;
            }
            candidates.Add((i, size.X * size.Y * size.Z, size));
        }
        candidates.Sort((a, b) => b.Volume.CompareTo(a.Volume));
        if (candidates.Count > MaxParts)
        {
            candidates.RemoveRange(MaxParts, candidates.Count - MaxParts);
        }
        candidates.Sort((a, b) => boxes.BoneAt(a.Index).CompareTo(boxes.BoneAt(b.Index)));
        float totalVolume = 0.0f;
        foreach ((int _, float volume, Vector3 _) in candidates)
        {
            totalVolume += volume;
        }

        // The bodies live in the world beside the creature (not moved by it).
        _world = new Node3D { Name = "RagdollBodies", TopLevel = true };
        skeleton.AddChild(_world);
        Dictionary<int, Part> byBone = new();
        float heaviest = 0.0f;
        foreach ((int index, float volume, Vector3 size) in candidates)
        {
            int bone = boxes.BoneAt(index);
            Transform3D world = skeletonWorld * skeleton.GetBoneGlobalPose(bone);
            Vector3 centre = world * boxes.BoxAt(index).GetCenter();
            Transform3D place = new(world.Basis.Orthonormalized(), centre);
            float mass = Mathf.Max(TotalMass * volume / Mathf.Max(totalVolume, 0.0001f), 0.5f);
            RigidBody3D body = new()
            {
                Name = "Part_" + skeleton.GetBoneName(bone),
                Mass = mass,
                CollisionLayer = Layer,
                CollisionMask = WorldMask,
                AngularDamp = AngularDamp,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = Friction, Bounce = 0.05f },
                ContinuousCd = true,
            };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size.Max(new Vector3(0.04f, 0.04f, 0.04f)) } });
            _world.AddChild(body);
            body.GlobalTransform = place;
            Vector3 push = velocity;
            BodyZone zone = boxes.ZoneAt(index);
            if (zone == BodyZone.Head || zone == BodyZone.Neck || zone == BodyZone.Torso || zone == BodyZone.Arm)
            {
                push += upperPush;
            }
            body.LinearVelocity = push;
            Part part = new() { Bone = bone, Body = body, ToBone = place.AffineInverse() * world };
            _parts.Add(part);
            byBone[bone] = part;
            if (mass > heaviest)
            {
                heaviest = mass;
                Core = body;
            }
        }

        // Each part hangs on its nearest parent part, at its bone's head.
        foreach (Part part in _parts)
        {
            int parent = skeleton.GetBoneParent(part.Bone);
            while (parent >= 0 && !byBone.ContainsKey(parent))
            {
                parent = skeleton.GetBoneParent(parent);
            }
            if (parent < 0)
            {
                continue;
            }
            Transform3D head = skeletonWorld * skeleton.GetBoneGlobalPose(part.Bone);
            ConeTwistJoint3D joint = new()
            {
                Name = "Joint_" + skeleton.GetBoneName(part.Bone),
                SwingSpan = Mathf.DegToRad(SwingDegrees),
                TwistSpan = Mathf.DegToRad(TwistDegrees),
            };
            _world.AddChild(joint);
            // The cone's twist axis is its X: lay it along the bone.
            Vector3 along = head.Basis.Y.Normalized();
            Vector3 side = along.Cross(Vector3.Up);
            if (side.LengthSquared() < 0.0001f)
            {
                side = along.Cross(Vector3.Right);
            }
            side = side.Normalized();
            joint.GlobalTransform = new Transform3D(new Basis(along, side.Cross(along).Normalized(), side), head.Origin);
            joint.NodeA = joint.GetPathTo(byBone[parent].Body);
            joint.NodeB = joint.GetPathTo(part.Body);
        }
    }

    /// <summary>The body goes limp into the ground: it stops meeting the world and slowly sinks.</summary>
    public void Sink()
    {
        Sinking = true;
        foreach (Part part in _parts)
        {
            if (!IsInstanceValid(part.Body))
            {
                continue;
            }
            part.Body.CollisionMask = 0;
            part.Body.LinearDamp = SinkDamp;
            part.Body.GravityScale = SinkGravity;
            part.Body.LinearVelocity = Vector3.Zero;
        }
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton == null || _parts.Count == 0)
        {
            return;
        }
        Transform3D toSkeleton = skeleton.GlobalTransform.AffineInverse();
        foreach (Part part in _parts)
        {
            if (!IsInstanceValid(part.Body))
            {
                continue;
            }
            skeleton.SetBoneGlobalPose(part.Bone, toSkeleton * part.Body.GlobalTransform * part.ToBone);
        }
    }
}
