using System;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;
namespace DownhillRoguelite.Debug;

/// <summary>Exercise the actual imported clips, gait blend clocks, takeoff/landing and visible retargeted actor.</summary>
public partial class AnimationSmoothTests : Node3D
{
    private int _failures;
    private PlayerController _body = null!;
    private CharacterAnimator _animator = null!;
    public override async void _Ready()
    {
        _body = GetNode<PlayerController>("World/Player");
        _animator = _body.GetNode<CharacterAnimator>("Animator");
        _body.Settings = (LocomotionSettings)_body.Settings.Duplicate();
        await Frames(30);
        bool clips = true;
        foreach (StringName clip in new StringName[] { "Idle", "Walk", "Jog_Fwd", "Sprint", "Crouch_Idle", "Crouch_Fwd", "Jump", "Jump_Start", "Jump_Land" })
        { clips &= _animator.Animations!.HasAnimation(clip); }
        Check("all nine locomotion clips are available on the driving rig", clips);
        _body.Teleport(new Vector3(-8, 0.1f, 12), 0);
        await Frames(30);
        _body.Settings.StrideSpeed = 2.2f;
        _body.DirectionOverride = Vector3.Forward;
        await Frames(120);
        Check("walk-jog blend keeps the physical target speed", Mathf.Abs(_body.Velocity.Length() - 2.2f) < 0.1f);
        Check("cadence remains finite and moving", float.IsFinite(_animator.Cadence) && _animator.Cadence > 0.2f);
        float phaseError = 0;
        for (int i = 0; i < 240; i++)
        {
            if (i % 40 == 0) { _body.Settings.StrideSpeed = 1.7f + (i % 80) / 40.0f; }
            await Frames(1);
            foreach (string node in new[] { "jog", "sprint", "brake_walk" })
            {
                float gap = Mathf.Abs(Phase("walk") - Phase(node));
                phaseError = Mathf.Max(phaseError, Mathf.Min(gap, 1 - gap));
            }
        }
        Check($"actual gait clocks stay in phase across changing speeds (error {phaseError:0.0000})", phaseError < 0.001f);
        CarrierModel look = _body.GetNode<CarrierModel>("CarrierModel");
        Check("Vučić can use the same locomotion driver", look.ShowCharacter("vucic"));
        await Frames(5);
        if (look.Follower == null) { GetTree().Quit(1); return; }
        RigFollower follower = look.Follower!;
        follower.RecordPose = true;
        _body.Teleport(new Vector3(-8, 0.1f, 12), 0);
        await Frames(90);
        _body.DirectionOverride = Vector3.Right;
        Vector3[] previous = follower.TargetPose;
        float maxStep = 0;
        bool finite = previous.Length > 20;
        float height = follower.GetSkeleton()!.GetBoneGlobalRest(RigFollower.FindBone(follower.GetSkeleton()!, "mixamorig1_Hips")).Origin.Length();
        for (int i = 0; i < 90; i++)
        {
            await Frames(1);
            Vector3[] pose = follower.TargetPose;
            for (int bone = 0; bone < pose.Length; bone++)
            {
                finite &= pose[bone].IsFinite();
                maxStep = Mathf.Max(maxStep, pose[bone].DistanceTo(previous[bone]) / height);
            }
            previous = pose;
        }
        Check($"retargeted actor stays continuous through a moving turn (largest bone step {maxStep:0.000} hip heights)", finite && maxStep < 0.3f);
        follower.RecordPose = false;
        _body.DirectionOverride = Vector3.Zero;
        await Frames(120);
        Check("stopped gait settles to idle without treadmill motion", _animator.Tree!.Get("parameters/to_walk/blend_amount").AsSingle() == 0 && _animator.SmoothedSpeed < 0.01f);
        Input.ActionPress("player_jump");
        await Frames(4);
        Input.ActionRelease("player_jump");
        Check("real jump starts the authored takeoff transition", _body.State == LocomotionState.Airborne && _animator.TakeoffWeight > 0);
        float landingPeak = 0;
        for (int i = 0; i < 100; i++)
        {
            await Frames(1);
            landingPeak = Mathf.Max(landingPeak, _animator.LandingWeight);
        }
        Check("actual ground contact plays the landing asset", _body.IsOnFloor() && landingPeak > 0.5f);
        Check("landing pose blends out instead of sticking", _animator.TakeoffWeight == 0 && _animator.LandingWeight == 0);
        GameEvents.PublishVaulting(true);
        GameEvents.PublishLanded(3);
        await Frames(4);
        Check("dive animations retain pose ownership over landing effects", _animator.LandingWeight == 0 && _animator.TakeoffWeight == 0);
        GameEvents.PublishVaulting(false);
        GameEvents.PublishLanded(3);
        await Frames(5);
        GameEvents.PublishDowned(true);
        await Frames(2);
        Check("ragdoll cancels the authored transition", _animator.LandingWeight == 0 && _animator.TakeoffWeight == 0);
        GameEvents.PublishDowned(false);
        _body.Teleport(new Vector3(-8, 0.1f, 10), 0);
        await Frames(1);
        Check("teleport clears old presentation offsets and transitions", _animator.Visual!.Position.Length() < 0.001f && _animator.LandingWeight == 0 && _animator.TakeoffWeight == 0);
        GetTree().Quit(Math.Min(_failures, 1));
    }
    private float Phase(string node)
    {
        float length = _animator.Tree!.Get($"parameters/{node}/current_length").AsSingle();
        if (length <= 0) { return float.NaN; }
        return _animator.Tree.Get($"parameters/{node}/current_position").AsSingle() / length;
    }
    private async Task Frames(int count)
    { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); } }
    private void Check(string name, bool pass)
    {
        if (pass) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
