using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Every step the carrier takes is heard and leaves a print: the footstep sounds (Footsteps) and the
/// trail (FootTrail) both go off when FootIK plants a foot, so each gait (braking, the normal pace,
/// the sprint) has to plant a foot about as often as the clip puts one down: twice a cycle.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/FootstepTests.tscn
/// Built things (ruins, carts, station props) carry the surface they're made of, so a step on them
/// sounds like stone or planks. The steps went missing at 144 fps, not at 60: run it at both.
/// Options after --: --set=FootIK:Property=value (try a planting setting).
/// </summary>
public partial class FootstepTests : Node3D
{
    private readonly List<string> _failures = new();
    private PlayerController _body = null!;
    private Skeleton3D _skeleton = null!;
    private readonly List<float> _leftHeights = new();
    private readonly List<float> _rightHeights = new();
    private bool _recording;
    private bool _leftPlanted;
    private bool _rightPlanted;
    private int _plants;
    private float _speed;
    private int _frames;

    public override async void _Ready()
    {
        try
        {
            Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
            AddChild(world);
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            _body = world.GetNode<PlayerController>("Player");
            _skeleton = _body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--set="))
                {
                    string[] parts = arg.Substring(6).Split('=', 2);
                    string[] where = parts[0].Split(':');
                    Node target = _body.GetNode(where[0]);
                    target.Set(where[1], float.Parse(parts[1], CultureInfo.InvariantCulture));
                    GD.Print($"set {parts[0]} = {target.Get(where[1])}");
                }
            }
            Floors();
            GameEvents.FootPlacementUpdated += OnFeet;
            await Frames(30);
            await Measure("braking", "player_brake");
            await Measure("normal pace", null);
            await Measure("sprint", "player_sprint");
            GameEvents.FootPlacementUpdated -= OnFeet;
        }
        catch (Exception error)
        {
            _failures.Add("ran without an exception: " + error);
        }
        foreach (string failure in _failures)
        {
            GD.Print("FAIL  " + failure);
        }
        GD.Print(_failures.Count == 0 ? "FOOTSTEP TESTS: PASS" : $"FOOTSTEP TESTS: {_failures.Count} failures");
        GetTree().Quit(Math.Min(_failures.Count, 1));
    }

    private async Task Measure(string name, string? action)
    {
        _body.Teleport(new Vector3(0.0f, 0.1f, 6.0f), 0.0f);
        _body.DirectionOverride = Vector3.Zero;
        await Frames(40);
        _body.DirectionOverride = Vector3.Forward;
        if (action != null) { Input.ActionPress(action); }
        await Frames(90);
        _plants = 0;
        _leftHeights.Clear();
        _rightHeights.Clear();
        _speed = 0.0f;
        _frames = 0;
        _recording = true;
        await Frames(240);
        _recording = false;
        if (action != null) { Input.ActionRelease(action); }
        _body.DirectionOverride = Vector3.Zero;

        float speed = _speed / Mathf.Max(_frames, 1);
        // The feet that came down: each ankle dropping through the middle of its own rise and fall.
        int expected = Touchdowns(_leftHeights) + Touchdowns(_rightHeights);
        bool ok = _plants >= expected * 0.75f && _plants <= expected * 1.3f + 1.0f;
        GD.Print($"{(ok ? "PASS" : "MISS")}  {name}: {_plants} steps heard for {expected} feet put down at {speed:0.0} m/s");
        if (!ok) { _failures.Add($"{name}: {_plants} steps heard for {expected} feet put down"); }
    }

    /// <summary>Built things sound like what they're made of underfoot (WorldStyle.FloorOf), not like grass.</summary>
    private void Floors()
    {
        (string Model, string Sound)[] cases =
        {
            ("res://art/props/quaternius_medieval_village/Wall_UnevenBrick_Straight.gltf", "Rock"),
            ("res://art/props/quaternius_medieval_village/Prop_Brick2.gltf", "Rock"),
            ("res://art/props/kenney_castle_kit/GLB format/tower-square-base.glb", "Rock"),
            ("res://art/props/kenney_fantasy_town_kit/GLB format/pillar-stone.glb", "Rock"),
            ("res://art/props/kenney_graveyard_kit/GLB format/gravestone-broken.glb", "Rock"),
            ("res://art/props/quaternius_medieval_village/Prop_Wagon.gltf", "Wood"),
            ("res://art/props/quaternius_medieval_village/Prop_Crate.gltf", "Wood"),
            ("res://art/props/kenney_fantasy_town_kit/GLB format/stall-stool.glb", "Wood"),
            ("res://art/props/kenney_nature_kit/GLB format/log_stack.glb", "Wood"),
        };
        foreach ((string model, string sound) in cases)
        {
            if (!ResourceLoader.Exists(model)) { _failures.Add($"{model.GetFile()} is there"); continue; }
            Node3D instance = GD.Load<PackedScene>(model).Instantiate<Node3D>();
            string heard = Environment.WorldStyle.FloorOf(instance, model)?.DisplayName ?? "grass (untagged)";
            instance.Free();
            bool ok = heard == sound;
            GD.Print($"{(ok ? "PASS" : "MISS")}  {model.GetFile()} sounds like {heard} underfoot (want {sound})");
            if (!ok) { _failures.Add($"{model.GetFile()}: {heard}, want {sound}"); }
        }
    }

    private void OnFeet(FootPlacementSnapshot snapshot)
    {
        if (_recording)
        {
            if (snapshot.Left.Down && !_leftPlanted) { _plants++; }
            if (snapshot.Right.Down && !_rightPlanted) { _plants++; }
        }
        _leftPlanted = snapshot.Left.Down;
        _rightPlanted = snapshot.Right.Down;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_recording)
        {
            return;
        }
        _frames++;
        _speed += new Vector2(_body.Velocity.X, _body.Velocity.Z).Length();
        Transform3D world = _skeleton.GlobalTransform;
        _leftHeights.Add((world * _skeleton.GetBoneGlobalPose(_skeleton.FindBone("DEF-foot.L")).Origin).Y - _body.GlobalPosition.Y);
        _rightHeights.Add((world * _skeleton.GetBoneGlobalPose(_skeleton.FindBone("DEF-foot.R")).Origin).Y - _body.GlobalPosition.Y);
    }

    private static int Touchdowns(List<float> heights)
    {
        if (heights.Count < 2)
        {
            return 0;
        }
        float low = float.MaxValue;
        float high = float.MinValue;
        foreach (float h in heights) { low = Mathf.Min(low, h); high = Mathf.Max(high, h); }
        float middle = (low + high) * 0.5f;
        int count = 0;
        for (int i = 1; i < heights.Count; i++)
        {
            if (heights[i - 1] >= middle && heights[i] < middle) { count++; }
        }
        return count;
    }


    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
