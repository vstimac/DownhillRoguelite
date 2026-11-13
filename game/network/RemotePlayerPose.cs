using System;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>Final key-body pose on a remote driver. No input, local AI, collisions or physical re-simulation.</summary>
public partial class RemotePlayerPose : SkeletonModifier3D
{
    [Export] public float Delay { get; set; } = 0.12f;
    [Export] public int MaxPacketsPerSecond { get; set; } = 45;
    public Node3D? Model { get; set; }
    public Node3D? WeaponParent { get; set; }
    public MeleeCombat? Template { get; set; }
    public int Received { get; private set; }
    public int LastSequence { get; private set; }
    public bool PhysicalFall { get; private set; }
    public int Flags { get; private set; }
    public int LastReceivedFlags { get; private set; }
    public float[] CurrentPose { get; } = new float[NetPoseCodec.Size];
    public Node3D? SwordVisual => _sword;
    public Node3D? ShieldVisual => _shield;
    private readonly NetPoseFrame[] _frames = new NetPoseFrame[6];
    private int _next;
    private int _count;
    private int[] _indices = Array.Empty<int>();
    private Node3D? _sword;
    private Node3D? _shield;
    private ulong _rateWindow;
    private int _rateCount;

    public override void _Ready()
    {
        for (int i = 0; i < _frames.Length; i++) { _frames[i] = new NetPoseFrame(); }
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton != null) { _indices = NetPoseCodec.Bind(skeleton); }
    }

    public bool Receive(int sequence, int flags, float[] data)
    {
        ulong now = Time.GetTicksMsec();
        if (now - _rateWindow >= 1000) { _rateWindow = now; _rateCount = 0; }
        if (++_rateCount > MaxPacketsPerSecond) { return false; }
        if (sequence <= LastSequence || flags < 0 || flags > 7 || !NetPoseCodec.Valid(data)) { return false; }
        NetPoseFrame slot = _frames[_next];
        Array.Copy(data, slot.Data, data.Length);
        slot.Time = Time.GetTicksMsec() / 1000.0;
        slot.Sequence = sequence;
        slot.Flags = flags;
        _next = (_next + 1) % _frames.Length;
        _count = Math.Min(_count + 1, _frames.Length);
        LastSequence = sequence;
        LastReceivedFlags = flags;
        Received++;
        return true;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D? skeleton = GetSkeleton();
        if (skeleton == null || _count == 0) { return; }
        double at = Time.GetTicksMsec() / 1000.0 - Delay;
        int first = (_next - _count + _frames.Length) % _frames.Length;
        NetPoseFrame from = _frames[first];
        NetPoseFrame to = from;
        for (int i = 1; i < _count; i++)
        {
            NetPoseFrame following = _frames[(first + i) % _frames.Length];
            to = following;
            // Several queued samples may share a millisecond. Keep the latest one at a tied
            // timestamp rather than selecting an older pose from the same receive batch.
            if (following.Time > at) { break; }
            from = following;
        }
        float weight = 1;
        if (to.Time > from.Time) { weight = Mathf.Clamp((float)((at - from.Time) / (to.Time - from.Time)), 0, 1); }
        Flags = from.Flags;
        if (weight >= 0.5f) { Flags = to.Flags; }
        PhysicalFall = (Flags & NetPoseCodec.Falling) != 0;
        NetPoseCodec.Interpolate(from.Data, to.Data, weight, CurrentPose);
        if (Model != null) { Model.Position = NetPoseCodec.Vector(CurrentPose, 0); }
        for (int i = 0; i < _indices.Length; i++)
        {
            int bone = _indices[i];
            if (bone < 0) { continue; }
            int offset = NetPoseCodec.Header + i * NetPoseCodec.BoneSize;
            Quaternion rotation = NetPoseCodec.Rotation(CurrentPose, offset + 3);
            Transform3D pose = skeleton.GetBoneGlobalPose(bone);
            pose.Basis = new Basis(rotation);
            pose.Origin = NetPoseCodec.Vector(CurrentPose, offset);
            skeleton.SetBoneGlobalPose(bone, pose);
        }
        TryWeapons();
        if (_sword != null)
        {
            _sword.Visible = (Flags & NetPoseCodec.SwordVisible) != 0;
            _sword.GlobalTransform = skeleton.GlobalTransform * NetPoseCodec.Transform(from.Data, to.Data, NetPoseCodec.WeaponStart, weight);
        }
        if (_shield != null)
        {
            _shield.Visible = (Flags & NetPoseCodec.ShieldVisible) != 0;
            _shield.GlobalTransform = skeleton.GlobalTransform * NetPoseCodec.Transform(from.Data, to.Data, NetPoseCodec.WeaponStart + NetPoseCodec.WeaponSize, weight);
        }
    }

    private void TryWeapons()
    {
        Template ??= NetSession.Instance?.LocalPlayer()?.GetNodeOrNull<MeleeCombat>("MeleeCombat");
        if (WeaponParent == null || Template == null) { return; }
        if (_sword == null && Template.Sword != null) { _sword = CopyWeapon(Template.Sword); }
        if (_shield == null && Template.Shield != null) { _shield = CopyWeapon(Template.Shield); }
    }
    private Node3D CopyWeapon(Node3D source)
    {
        Node3D visual = (Node3D)source.Duplicate(0);
        visual.Visible = false;
        WeaponParent!.AddChild(visual);
        foreach (Node node in visual.FindChildren("*", "CollisionObject3D", true, false))
        { if (node is CollisionObject3D collision) { collision.CollisionLayer = 0; collision.CollisionMask = 0; } }
        return visual;
    }
}
