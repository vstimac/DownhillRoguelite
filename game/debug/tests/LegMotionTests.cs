using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The carrier's legs at the normal pace and sprinting, measured on the driving rig after foot IK:
/// the bones keep their length, the hips don't bound up and down (the jog clip alone lifts them 24 cm
/// a step), the knees aren't pulled straight by the IK, and the leg meets its target in the same
/// frame (TwoBoneIK3D used to reach for last frame's target; FootIK.SolveLeg).
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/LegMotionTests.tscn
/// Options after --: --no-ik (feet as the clips have them), --trace (one line a frame),
/// --off=ModifierName,... (switch driver modifiers off), --set=Node:Property=value or x,y (a node under the player).
/// </summary>
public partial class LegMotionTests : Node3D
{
    private readonly List<string> _failures = new();
    private PlayerController _body = null!;
    private Skeleton3D _driver = null!;
    private FootIK? _ik;
    private readonly List<Sample> _samples = new();
    private bool _recording;
    private bool _trace;
    private bool _noIk;

    private sealed class Sample
    {
        public float Speed;
        public float ThighL, ShinL, ThighR, ShinR;
        public float HipsY;
        public float KneeL, KneeR;
        public float FootLY, FootRY;
        public float TargetMissL, TargetMissR;
    }

    public override async void _Ready()
    {
        try
        {
            Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
            AddChild(world);
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            _body = world.GetNode<PlayerController>("Player");
            _driver = _body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            _ik = _body.GetNodeOrNull<FootIK>("FootIK");
            string[] args = OS.GetCmdlineUserArgs();
            _trace = Array.IndexOf(args, "--trace") >= 0;
            _noIk = Array.IndexOf(args, "--no-ik") >= 0;
            await Frames(2);
            foreach (string arg in args)
            {
                if (arg.StartsWith("--set="))
                {
                    string[] parts = arg.Substring(6).Split('=', 2);
                    string[] where = parts[0].Split(':');
                    Node target = _body.GetNode(where[0]);
                    string[] numbers = parts[1].Split(',');
                    Variant value = numbers.Length == 2
                        ? new Vector2(float.Parse(numbers[0], CultureInfo.InvariantCulture), float.Parse(numbers[1], CultureInfo.InvariantCulture))
                        : float.Parse(numbers[0], CultureInfo.InvariantCulture);
                    target.Set(where[1], value);
                    GD.Print($"set {parts[0]} = {target.Get(where[1])}");
                }
                if (arg.StartsWith("--off="))
                {
                    foreach (string off in arg.Substring(6).Split(','))
                    {
                        SwitchOff(n => n.Name == off || n.GetType().Name == off);
                    }
                }
            }
            if (_noIk)
            {
                SwitchOff(n => n is FootPlacementModifier || n is FootAlignModifier);
            }
            _driver.SkeletonUpdated += OnDriverUpdated;
            await Frames(30);

            await Measure("normal pace", sprint: false);
            await Measure("sprint", sprint: true);
        }
        catch (Exception error)
        {
            _failures.Add("ran without an exception: " + error);
        }
        foreach (string failure in _failures)
        {
            GD.Print("FAIL  " + failure);
        }
        GD.Print($"LEG MOTION TESTS: {_failures.Count} failures");
        GetTree().Quit(Math.Min(_failures.Count, 1));
    }

    private void SwitchOff(Func<Node, bool> which)
    {
        foreach (Node node in _driver.GetChildren())
        {
            if (node is SkeletonModifier3D modifier && which(node))
            {
                modifier.Active = false;
                GD.Print($"off: {node.Name}");
            }
        }
    }

    private void Check(bool ok, string what, string detail)
    {
        GD.Print($"{(ok ? "PASS" : "MISS")}  {what}: {detail}");
        if (!ok) { _failures.Add($"{what}: {detail}"); }
    }

    private async Task Measure(string name, bool sprint)
    {
        _body.Teleport(new Vector3(0.0f, 0.1f, 6.0f), 0.0f);
        _body.DirectionOverride = Vector3.Zero;
        await Frames(40);
        _body.DirectionOverride = Vector3.Forward;
        if (sprint) { Input.ActionPress("player_sprint"); }
        await Frames(90);
        _samples.Clear();
        _recording = true;
        await Frames(180);
        _recording = false;
        if (sprint) { Input.ActionRelease("player_sprint"); }
        _body.DirectionOverride = Vector3.Zero;
        Report(name);
    }

    private void OnDriverUpdated()
    {
        if (!_recording) { return; }
        Sample s = new() { Speed = new Vector2(_body.Velocity.X, _body.Velocity.Z).Length() };
        // The pose was made for the drawn frame, at the skeleton's interpolated place (as FootIK plans).
        Transform3D world = _driver.GetGlobalTransformInterpolated();
        float sole = world.Origin.Y;
        Vector3 P(string bone) => world * _driver.GetBoneGlobalPose(_driver.FindBone(bone)).Origin;
        Vector3 thighL = P("DEF-thigh.L"), shinL = P("DEF-shin.L"), footL = P("DEF-foot.L");
        Vector3 thighR = P("DEF-thigh.R"), shinR = P("DEF-shin.R"), footR = P("DEF-foot.R");
        s.ThighL = thighL.DistanceTo(shinL);
        s.ShinL = shinL.DistanceTo(footL);
        s.ThighR = thighR.DistanceTo(shinR);
        s.ShinR = shinR.DistanceTo(footR);
        s.HipsY = P("DEF-hips").Y - sole;
        s.KneeL = Mathf.RadToDeg((thighL - shinL).AngleTo(footL - shinL));
        s.KneeR = Mathf.RadToDeg((thighR - shinR).AngleTo(footR - shinR));
        s.FootLY = footL.Y - sole;
        s.FootRY = footR.Y - sole;
        if (_ik != null && _ik.Feet.Count == 2 && !_noIk)
        {
            // A reachable target (leg not straight) must be met this frame, not the next.
            FootState l = _ik.Feet[0], r = _ik.Feet[1];
            s.TargetMissL = l.LegWeight > 0.99f && s.KneeL < 170.0f ? l.TargetPosition.DistanceTo(footL) : 0.0f;
            s.TargetMissR = r.LegWeight > 0.99f && s.KneeR < 170.0f ? r.TargetPosition.DistanceTo(footR) : 0.0f;
        }
        _samples.Add(s);
        if (_trace)
        {
            GD.Print(string.Format(CultureInfo.InvariantCulture,
                "t hips {0:0.000} bounce {1:0.000} kneeL {2:0} kneeR {3:0} footL {4:0.000} footR {5:0.000} missL {6:0.000} missR {7:0.000}",
                s.HipsY, _ik?.BounceShift ?? 0.0f, s.KneeL, s.KneeR, s.FootLY, s.FootRY, s.TargetMissL, s.TargetMissR));
        }
    }

    private void Report(string name)
    {
        if (_samples.Count < 60)
        {
            _failures.Add($"{name}: only {_samples.Count} drawn frames");
            return;
        }
        (float lo, float hi, float jump) Range(Func<Sample, float> get)
        {
            float lo = float.MaxValue, hi = float.MinValue, jump = 0.0f;
            for (int i = 0; i < _samples.Count; i++)
            {
                float v = get(_samples[i]);
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
                if (i > 0) { jump = Mathf.Max(jump, Mathf.Abs(v - get(_samples[i - 1]))); }
            }
            return (lo, hi, jump);
        }
        int straight = 0;
        float speed = 0.0f;
        float miss = 0.0f;
        foreach (Sample s in _samples)
        {
            if (s.KneeL > 172.0f || s.KneeR > 172.0f) { straight++; }
            speed += s.Speed;
            miss = Mathf.Max(miss, Mathf.Max(s.TargetMissL, s.TargetMissR));
        }
        speed /= _samples.Count;
        GD.Print($"MEASURE {name} at {speed:0.00} m/s, {_samples.Count} drawn frames");
        foreach ((string label, Func<Sample, float> get) in new (string, Func<Sample, float>)[]
        {
            ("hips height", s => s.HipsY), ("knee L angle", s => s.KneeL), ("knee R angle", s => s.KneeR), ("foot L height", s => s.FootLY),
        })
        {
            (float lo, float hi, float jump) = Range(get);
            GD.Print(string.Format(CultureInfo.InvariantCulture, "  {0} {1:0.000}..{2:0.000} (range {3:0.000}, biggest frame step {4:0.000})", label, lo, hi, hi - lo, jump));
        }

        float stretch = 0.0f;
        foreach (Func<Sample, float> bone in new Func<Sample, float>[] { s => s.ThighL, s => s.ShinL, s => s.ThighR, s => s.ShinR })
        {
            (float lo, float hi, _) = Range(bone);
            stretch = Mathf.Max(stretch, hi - lo);
        }
        (float hipsLo, float hipsHi, float hipsStep) = Range(s => s.HipsY);
        Check(stretch < 0.002f, $"{name}: leg bones keep their length", $"most a bone changed {stretch * 1000.0f:0.0} mm");
        if (_noIk)
        {
            GD.Print($"  (foot IK off: hips up and down {hipsHi - hipsLo:0.000} m, a knee straight in {straight} of {_samples.Count} frames)");
            return;
        }
        Check(hipsHi - hipsLo < 0.14f && hipsStep < 0.04f, $"{name}: the hips don't bound up and down",
            string.Format(CultureInfo.InvariantCulture, "up and down {0:0.000} m (the jog clip alone: 0.24, max 0.14), biggest step {1:0.000} m a frame (max 0.04; the sprinting clip alone: 0.034)", hipsHi - hipsLo, hipsStep));
        Check(straight <= _samples.Count / 5, $"{name}: the IK doesn't pull the knees straight",
            $"a knee locked straight in {straight} of {_samples.Count} frames (max {_samples.Count / 5}; the clips alone: about 10)");
        Check(miss < 0.01f, $"{name}: each foot reaches its target in the same frame",
            string.Format(CultureInfo.InvariantCulture, "furthest a bent leg's ankle was from its target: {0:0.000} m (max 0.01)", miss));
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
