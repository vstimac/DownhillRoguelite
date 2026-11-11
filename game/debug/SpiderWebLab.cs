using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Spider web lab: a few trunks in a row with a web strung between every close pair, seen at dusk
/// against a dark wood. Walk the stand-in carrier through them with W A S D (or `-- --walk`) to see one tear (the
/// hint and the snare are printed).
/// </summary>
public partial class SpiderWebLab : Node3D
{
    [Export] public float TrunkSpacing { get; set; } = 3.4f;
    [Export] public int TrunkCount { get; set; } = 4;
    [Export] public float WalkSpeed { get; set; } = 2.0f;

    private Vector3 _carrier = new(0.0f, 0.0f, 1.5f);
    private SpiderWebs? _webs;
    private bool _autoWalk;

    public override void _Ready()
    {
        StaticBody3D trees = new() { Name = "Trunks" };
        trees.AddToGroup(TerrainChunk.TreeGroup);
        StandardMaterial3D bark = new() { AlbedoColor = new Color(0.22f, 0.16f, 0.11f), Roughness = 1.0f };
        for (int i = 0; i < TrunkCount; i++)
        {
            Vector3 at = new((i - (TrunkCount - 1) * 0.5f) * TrunkSpacing, 2.0f, 0.0f);
            trees.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.25f, Height = 4.0f }, Position = at });
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.28f, Height = 4.0f, Material = bark }, Position = at });
        }
        AddChild(trees);
        _webs = new SpiderWebs { Name = "Webs", Chance = 1.0f, CircleOverride = 4 };
        AddChild(_webs);
        GameEvents.Snared += OnSnared;
        GameEvents.HintShown += OnHint;
        // `-- --walk`: the stand-in walks straight through the middle web by itself.
        _autoWalk = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--walk") >= 0;
    }

    public override void _ExitTree()
    {
        GameEvents.Snared -= OnSnared;
        GameEvents.HintShown -= OnHint;
    }

    private void OnSnared(float seconds, float speed) => GD.Print($"[SpiderWebLab] snared for {seconds:0.0} s at {speed:0.00} of the stride");

    private void OnHint(string text) => GD.Print($"[SpiderWebLab] hint: {text}");

    public override void _PhysicsProcess(double delta)
    {
        float step = 0.0f;
        if (Input.IsKeyPressed(Key.A))
        {
            step -= 1.0f;
        }
        if (Input.IsKeyPressed(Key.D))
        {
            step += 1.0f;
        }
        if (Input.IsKeyPressed(Key.W) || _autoWalk)
        {
            _carrier.Z -= WalkSpeed * (float)delta;
        }
        if (Input.IsKeyPressed(Key.S))
        {
            _carrier.Z += WalkSpeed * (float)delta;
        }
        _carrier.X += step * WalkSpeed * (float)delta;
        GameEvents.PublishLocomotion(default(LocomotionSnapshot) with { Position = _carrier });
    }
}
