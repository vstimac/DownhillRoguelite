using System;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Network;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Native skeleton/physics regression: an observer must reproduce the final body, including a fall.</summary>
public partial class NetPoseTests : Node3D
{
    private int _failures;
    private int _sequence;
    private float _rotationError;
    private float _positionError;
    private int _samples;
    private bool _fell;
    private float _weaponError;
    /// <summary>The owner's stream stops (as if nothing arrived), to see what this machine does alone.</summary>
    private bool _streamPaused;
    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception e) { Check("pose checks complete", false, e.ToString()); }
        GD.Print($"NET POSE TESTS: {_failures} failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private async Task Run()
    {
        Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
        AddChild(world);
        world.GetNode("BlueStep").Free(); world.GetNode("OrangeBlock").Free();
        PlayerController player = world.GetNode<PlayerController>("Player");
        player.DirectionOverride = Vector3.Zero;
        player.Teleport(new Vector3(0, 0.1f, 3), 0);
        await Frames(40);
        Skeleton3D source = player.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
        Node3D model = player.GetNode<Node3D>("Model");
        Node3D look = player.GetNode<Node3D>("CarrierModel");
        MeleeCombat melee = player.GetNode<MeleeCombat>("MeleeCombat");
        PlayerRagdoll ragdoll = player.GetNode<PlayerRagdoll>("Ragdoll");
        RemoteCarrier remote = new() { Name = "Observer", InterpolationDelay = 0 };
        remote.UseModelPlacement(model.Transform, look.Transform);
        world.AddChild(remote);
        RemotePlayerPose pose = remote.Pose!;
        pose.Template = melee; pose.MaxPacketsPerSecond = 100000;
        Skeleton3D target = remote.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
        int[] indices = NetPoseCodec.Bind(source);
        float[] data = new float[NetPoseCodec.Size];
        Vector3[] positions = new Vector3[indices.Length];
        void Capture()
        {
            if (_streamPaused || !NetPoseCodec.Capture(source, model, melee, indices, data)) { return; }
            for (int i = 0; i < indices.Length; i++) { positions[i] = source.GetBoneGlobalPose(indices[i]).Origin; }
            int flags = NetPoseCodec.SwordVisible | NetPoseCodec.ShieldVisible;
            if (ragdoll.Downed) { flags |= NetPoseCodec.Falling; }
            if (look is CarrierModel carrier && carrier.PackSetDown?.Off == true) { flags |= NetPoseCodec.PackOff; }
            pose.Receive(++_sequence, flags, data);
        }
        void Compare()
        {
            if (_sequence == 0) { return; }
            _samples++;
            _fell |= pose.PhysicalFall;
            for (int i = 0; i < indices.Length; i++)
            {
                Transform3D actual = target.GetBoneGlobalPose(target.FindBone(NetPoseCodec.Bones[i]));
                Quaternion expected = NetPoseCodec.Rotation(data, NetPoseCodec.Header + i * NetPoseCodec.BoneSize + 3).Normalized();
                _rotationError = Mathf.Max(_rotationError, expected.AngleTo(actual.Basis.Orthonormalized().GetRotationQuaternion()));
                _positionError = Mathf.Max(_positionError, actual.Origin.DistanceTo(positions[i]));
            }
            if (pose.SwordVisual != null)
            {
                Transform3D expected = target.GlobalTransform * NetPoseCodec.Transform(data, data, NetPoseCodec.WeaponStart, 1);
                _weaponError = Mathf.Max(_weaponError, expected.Origin.DistanceTo(pose.SwordVisual.GlobalPosition));
            }
        }
        source.SkeletonUpdated += Capture; target.SkeletonUpdated += Compare;
        await Frames(35);
        Check("standing remote reproduces final key bones", _samples > 20 && _rotationError < 0.002f && _positionError < 0.015f, Error());
        BalanceController balance = player.GetNode<BalanceController>("Balance");
        balance.SetPhysicsProcess(false);
        GameEvents.PublishBalance(balance.Snapshot with { State = BalanceState.Wobbling, Imbalance = new Vector2(1.2f, 0),
            SupportCenter = player.GlobalPosition, CenterOfMassGround = player.GlobalPosition + Vector3.Right * 0.2f, Right = Vector3.Right, Forward = Vector3.Forward });
        Reset(); await Frames(60);
        Check("observer reproduces balance recovery and IK", _rotationError < 0.002f && _positionError < 0.015f, Error());
        melee.SetDrawnNow(true); await Frames(10);
        Check("combat scenario starts a real swing", melee.Attack() && melee.State == MeleeCombat.Stance.Attacking);
        Reset(); await Frames(35);
        Check("observer reproduces combat overlay bones", _rotationError < 0.002f && _positionError < 0.015f, Error());
        Check("remote weapons follow transmitted mounts", pose.SwordVisual != null && pose.ShieldVisual != null && _weaponError < 0.0001f);
        Check("remote body never participates in local collisions", remote.CollisionLayer == 0 && remote.CollisionMask == 0);
        ragdoll.SafeImpactSpeed = 100;
        ragdoll.GoDown(Vector3.Forward * 2, Vector3.Zero);
        Reset(); await Frames(90);
        Check("observer sees the actual physical fall", _fell && _rotationError < 0.002f && _positionError < 0.025f, Error());
        Reset(); await Frames(420);
        Check("observer follows recovery to standing", !ragdoll.Downed && !pose.PhysicalFall && _rotationError < 0.002f && _positionError < 0.025f, Error());
        await PackSetDown(look as CarrierModel, remote);
        source.SkeletonUpdated -= Capture; target.SkeletonUpdated -= Compare;
        float[] valid = (float[])data.Clone();
        Check("old and duplicate poses are rejected", !pose.Receive(_sequence, 0, data) && !pose.Receive(_sequence - 1, 0, data));
        Check("truncated poses and unknown flags are rejected", !pose.Receive(++_sequence, 0, new float[2]) && !pose.Receive(++_sequence, NetPoseCodec.AllFlags + 1, data));
        data[0] = float.NaN;
        Check("nonfinite poses cannot reach the skeleton", !pose.Receive(++_sequence, 0, data));
        data[0] = 500;
        Check("unbounded offsets are rejected", !pose.Receive(++_sequence, 0, data));
        RemotePlayerPose limited = new() { Active = false, MaxPacketsPerSecond = 1 };
        target.AddChild(limited);
        Check("pose flooding is bounded", limited.Receive(1, 0, valid) && !limited.Receive(2, 0, valid));
        limited.QueueFree();
        world.QueueFree(); await Frames(2);
    }
    /// <summary>
    /// The pack set down to be repacked: the owner's pack comes off on their screen and, through the
    /// pose stream, on the observer's; the observer's own packing never moves the remote carrier's pack.
    /// </summary>
    private async Task PackSetDown(CarrierModel? local, RemoteCarrier remote)
    {
        PackSetDown? mine = local?.PackSetDown;
        PackSetDown? theirs = remote.Look?.PackSetDown;
        Check("both carriers have a pack that can be set down", mine != null && theirs != null && theirs.Remote && mine != null && !mine.Remote,
            $"local {mine != null}, remote {theirs != null}");
        if (mine == null || theirs == null)
        {
            return;
        }
        // With nothing arriving from the owner, this machine's own packing leaves their pack on their back.
        _streamPaused = true;
        GameEvents.PublishPackingMode(true);
        GameEvents.PublishPackOpen(true);
        await Frames(30);
        bool remoteStillWorn = mine.Off && !theirs.Off;
        _streamPaused = false;
        await Frames(60);
        BackpackAttachment? mount = remote.Look?.Clothing?.BackpackMount;
        Check("the owner's pack set down shows on the observer's screen, standing on the ground",
            mine.Off && theirs.Off && theirs.Way >= 0.99f && theirs.Copy != null && mount != null && !mount.Visible
                && Mathf.Abs(theirs.Copy.GlobalPosition.Y - mine.Copy!.GlobalPosition.Y) < 0.3f,
            $"local off {mine.Off}, remote off {theirs.Off} way {theirs.Way:0.00}, remote copy y {theirs.Copy?.GlobalPosition.Y:0.00} vs local {mine.Copy?.GlobalPosition.Y:0.00}");
        Check("the pack moves only from the owner's stream, not from this machine's own packing", remoteStillWorn);
        GameEvents.PublishPackOpen(false);
        GameEvents.PublishPackingMode(false);
        await Frames(60);
        Check("shouldered again, the pack is back on the observer's copy of them too",
            !mine.Off && !theirs.Off && theirs.Copy == null && mount != null && mount.Visible, $"remote off {theirs.Off}, way {theirs.Way:0.00}");
    }

    private void Reset() { _rotationError = 0; _positionError = 0; _weaponError = 0; _samples = 0; }
    private string Error() => $"samples {_samples}, angle {_rotationError:0.000000}, position {_positionError:0.000000}";
    private async Task Frames(int n) { for (int i = 0; i < n; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private void Check(string name, bool ok, string details = "") { if (!ok) { _failures++; } string result = "PASS"; if (!ok) { result = "FAIL"; } GD.Print($"{result} {name} {details}"); }
}
