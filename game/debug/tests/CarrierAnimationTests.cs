using System;
using System.Collections.Generic;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The carrier's animation, measured headless through the photo tool's states (CarrierAnimShots in
/// check mode): every clip the code names exists, every graph node has a clip, no state leaves him in
/// the rest (T) pose, the action slot really shows its clip (rolls and get-ups were invisible from
/// 10 Nov until this test), the normal pace is a grounded trot with planted feet that don't slide
/// far, holding C crouches him, the pack stays along his back when he bends, and no clip change
/// makes a bone jump in one frame.
/// `--fixed-fps 60 res://debug/tests/CarrierAnimationTests.tscn`
/// </summary>
public partial class CarrierAnimationTests : Node3D
{
    private int _failures;

    private static readonly string[] States =
    {
        "idle", "walk", "run", "sprint", "stop", "crouch", "crouch_walk", "drop_land", "fall_back",
        "roll_timed", "clip_roll", "clip_getup", "sword_draw", "sword_swing",
    };

    public override async void _Ready()
    {
        CarrierAnimShots shots = new() { Name = "Shots", Checking = true };
        AddChild(shots);
        try
        {
            await shots.RunChecks(States);
        }
        catch (Exception error)
        {
            Check("the states run", false, error.ToString());
        }
        CheckClips(shots);
        CheckResults(shots.Results);
        GD.Print($"CARRIER ANIMATION TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private void CheckClips(CarrierAnimShots shots)
    {
        CharacterAnimator animator = shots.CharacterAnimator;
        AnimationPlayer? player = animator.Animations;
        List<string> missing = new();
        void Need(string owner, StringName clip)
        {
            if (player == null || !player.HasAnimation(clip)) { missing.Add($"{owner}: {clip}"); }
        }
        Need("animator idle", animator.IdleClip);
        Need("animator walk", animator.WalkClip);
        Need("animator jog", animator.JogClip);
        Need("animator sprint", animator.SprintClip);
        Need("animator brake idle", animator.BrakeIdleClip);
        Need("animator brake walk", animator.BrakeWalkClip);
        Need("animator air", animator.AirClip);
        Need("animator takeoff", animator.TakeoffClip);
        Need("animator landing", animator.LandingClip);
        Dive dive = shots.Body.GetNode<Dive>("Dive");
        Need("dive push-off", dive.PushOffClip);
        Need("dive flight", dive.FlightClip);
        Need("dive roll", dive.RollClip);
        PlayerRagdoll ragdoll = shots.Body.GetNode<PlayerRagdoll>("Ragdoll");
        Need("get-up", ragdoll.GetUpClip);
        Need("get-up from the back", ragdoll.GetUpClipBack);
        Check("every clip the carrier's code names exists on the driving rig", missing.Count == 0, string.Join(", ", missing));

        // Every clip node of the built graph points at a clip that exists.
        List<string> empty = new();
        if (animator.Tree?.TreeRoot is AnimationNodeBlendTree tree)
        {
            foreach (string node in new[] { "idle", "brake_idle", "walk", "jog", "sprint", "brake_walk", "air", "takeoff", "landing", "action" })
            {
                if (tree.GetNode(node) is not AnimationNodeAnimation clip || player?.HasAnimation(clip.Animation) != true)
                {
                    empty.Add(node);
                }
            }
        }
        else
        {
            empty.Add("no animation tree");
        }
        Check("every node of the animation graph has a clip", empty.Count == 0, string.Join(", ", empty));

        // The sword's clips, when this copy has them (they are kept out of git).
        MeleeCombat? melee = shots.Body.GetNodeOrNull<MeleeCombat>("MeleeCombat");
        SwordAnimator? sword = melee?.Animator;
        if (sword?.Player != null)
        {
            List<string> lost = sword.Clips.Keys.Where(k => !sword.Player.HasAnimation(SwordAnimator.Library + "/" + k)).ToList();
            Check("every sword clip is loaded", lost.Count == 0, string.Join(", ", lost));
        }
        else
        {
            GD.Print("SKIP sword clips: this copy has no sword animations");
        }
    }

    private void CheckResults(Dictionary<string, CarrierAnimShots.Result> results)
    {
        foreach (string state in States)
        {
            if (!results.ContainsKey(state)) { Check($"state {state} was measured", false, "missing"); }
        }
        if (States.Any(s => !results.ContainsKey(s))) { return; }
        CarrierAnimShots.Result R(string name) => results[name];

        int tpose = results.Values.Sum(r => r.TposeFrames);
        Check("no state leaves him in the rest (T) pose", tpose == 0,
            string.Join(", ", results.Values.Where(r => r.TposeFrames > 0).Select(r => $"{r.Name} {r.TposeFrames} frames")));

        HashSet<string> seen = results.Values.SelectMany(r => r.States.Keys).ToHashSet();
        string[] wanted = { nameof(LocomotionState.Idle), nameof(LocomotionState.Striding), nameof(LocomotionState.Braking),
            nameof(LocomotionState.Airborne), nameof(LocomotionState.Fallen) };
        Check("every locomotion state was shown", wanted.All(seen.Contains), "seen " + string.Join(",", seen));

        float idleHips = R("idle").HipsHeight;
        Check("the action slot shows its clip: a roll takes his hips down", R("clip_roll").ActionFrames > 30 && R("clip_roll").HipsMin < idleHips - 0.3f,
            $"lowest roll hips {R("clip_roll").HipsMin:0.00} m, standing {idleHips:0.00} m");
        Check("the action slot shows a get-up from the back", R("clip_getup").HipsHeight < idleHips - 0.15f,
            $"get-up hips {R("clip_getup").HipsHeight:0.00} m, standing {idleHips:0.00} m");
        Check("a landing roll plays the roll", R("roll_timed").ActionFrames > 30, $"{R("roll_timed").ActionFrames} frames");

        CarrierAnimShots.Result run = R("run");
        // The normal pace is the jog clip slowed to fit 3.5 m/s (see CharacterAnimator.WalkToJog); these
        // keep it from getting slower or floatier until a real trot clip arrives.
        Check("the normal pace keeps its cadence", run.StepsPerMinute is > 70 and < 185 && run.AirShare < 0.6f && run.FootMean < 0.16f,
            $"{run.StepsPerMinute:0} steps/min, both feet off the ground {run.AirShare:P0}, lowest foot {run.FootMean:0.000} m up");
        Check("walking feet stay planted", R("walk").DriverSlip < 0.2f && R("walk").FootMean is > -0.03f and < 0.04f,
            $"planted toe {R("walk").DriverSlip:0.00} m/s, lowest foot {R("walk").FootMean:0.000} m");
        Check("running feet don't skate", run.DriverSlip < 0.35f, $"planted toe {run.DriverSlip:0.00} m/s");
        Check("standing, his feet are on the ground (not floating or sunk)", R("idle").FootMean is > -0.02f and < 0.03f, $"{R("idle").FootMean:0.000} m");

        Check("holding C crouches him standing still", R("crouch").HipsHeight < idleHips - 0.15f,
            $"crouch hips {R("crouch").HipsHeight:0.00} m, standing {idleHips:0.00} m");
        Check("holding C crouches him while he moves", R("crouch_walk").HipsHeight < idleHips - 0.12f,
            $"crouch-walk hips {R("crouch_walk").HipsHeight:0.00} m");

        float idleTilt = R("idle").PackTilt;
        if (float.IsFinite(idleTilt))
        {
            Check("the pack stays along his back when he crouches", Mathf.Abs(R("crouch").PackTilt - idleTilt) < 12.0f,
                $"pack turned {R("crouch").PackTilt:0}° from his back crouching, {idleTilt:0}° standing");
        }
        else
        {
            GD.Print("SKIP pack on the back: no pack worn");
        }

        string[] smooth = { "idle", "walk", "run", "sprint", "stop", "crouch", "crouch_walk", "sword_draw", "sword_swing" };
        CarrierAnimShots.Result worst = smooth.Select(R).OrderByDescending(r => r.PopMax).First();
        Check("no bone jerks in one frame (no popping between clips)", worst.PopMax < 0.15f, $"worst {worst.Name}: {worst.PopMax:0.000} m in a frame, {worst.PopWhere}");
    }

    private void Check(string name, bool ok, string detail = "")
    {
        if (!ok) { _failures++; }
        GD.Print($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " (" + detail + ")" : "")}");
    }
}
