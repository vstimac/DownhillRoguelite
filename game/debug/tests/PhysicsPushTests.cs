using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for fluid collisions: a loose crate waits frozen until there is ground under it,
/// then settles; walking into it shoves it along instead of stopping the carrier dead; a parked
/// hand cart gives way when walked into; two characters pushed into each other are eased apart, the
/// lighter one giving way more; a crate that falls far is put back.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/PhysicsPushTests.tscn
/// </summary>
public partial class PhysicsPushTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public StaticBody3D? Floor { get; set; }
    /// <summary>The carrier must keep at least this share of their walking speed while shoving a crate.</summary>
    [Export] public float KeepsSpeed { get; set; } = 0.35f;

    private const string ActionForward = "player_move_forward";
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        if (PlayerBody == null || Floor == null)
        {
            GD.PushError("PhysicsPushTests: PlayerBody and Floor are needed.");
            GetTree().Quit(1);
            return;
        }
        await WaitSeconds(0.5f);
        TestWaitsForGround();
        await TestCrateSettles();
        await TestShoveCrate(PlayerBody);
        await TestShoveCart(PlayerBody);
        await TestSpacing();
        await TestReset();

        if (_failures.Count == 0)
        {
            GD.Print("PHYSICS PUSH TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"PHYSICS PUSH TESTS: FAIL ({string.Join(", ", _failures)})");
            GetTree().Quit(1);
        }
    }

    private LooseProp Crate(Vector3 at, float mass)
    {
        LooseProp crate = new() { Name = "Crate", Mass = mass, Position = at };
        crate.SetBox(new Vector3(0.6f, 0.6f, 0.6f), 1.0f);
        crate.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.6f, 0.6f) }, Position = new Vector3(0.0f, 0.3f, 0.0f) });
        AddChild(crate);
        return crate;
    }

    private void TestWaitsForGround()
    {
        LooseProp floating = Crate(new Vector3(500.0f, 0.0f, 500.0f), 10.0f);
        Check("a loose prop waits frozen where there is no ground yet", floating.Waiting, $"frozen {floating.Waiting}");
        floating.QueueFree();
    }

    private async Task TestCrateSettles()
    {
        LooseProp crate = Crate(new Vector3(-6.0f, 0.3f, 6.0f), 18.0f);
        await WaitSeconds(1.5f);
        Check("a loose prop wakes over ground and settles on it", !crate.Waiting && Mathf.Abs(crate.GlobalPosition.Y) < 0.08f,
            $"frozen {crate.Waiting}, height {crate.GlobalPosition.Y:0.000}");
        // A kick of 2 m/s: friction stops it after about 0.2 m.
        Vector3 before = crate.GlobalPosition;
        crate.ApplyCentralImpulse(Vector3.Right * crate.Mass * 2.0f);
        await WaitSeconds(1.0f);
        float slid = (crate.GlobalPosition - before).Length();
        Check("a kicked crate slides a little and friction stops it", slid > 0.1f && slid < 0.5f && crate.LinearVelocity.Length() < 0.05f,
            $"slid {slid:0.00} m, now {crate.LinearVelocity.Length():0.00} m/s");
        crate.QueueFree();
    }

    /// <summary>The way the carrier walks with forward held (camera-relative, so measured).</summary>
    private async Task<Vector3> WalkDirection(PlayerController player)
    {
        Vector3 start = player.GlobalPosition;
        Input.ActionPress(ActionForward);
        await WaitSeconds(0.8f);
        Input.ActionRelease(ActionForward);
        await WaitSeconds(0.8f);
        Vector3 moved = player.GlobalPosition - start;
        moved.Y = 0.0f;
        if (moved.LengthSquared() < 0.01f)
        {
            return Vector3.Forward;
        }
        return moved.Normalized();
    }

    private async Task<(float Speed, float Moved)> WalkInto(PlayerController player, Node3D thing, float seconds)
    {
        Vector3 start = thing.GlobalPosition;
        Vector3 playerStart = player.GlobalPosition;
        Input.ActionPress(ActionForward);
        await WaitSeconds(seconds);
        Input.ActionRelease(ActionForward);
        float speed = (player.GlobalPosition - playerStart).Length() / seconds;
        await WaitSeconds(0.5f);
        Vector3 moved = thing.GlobalPosition - start;
        moved.Y = 0.0f;
        return (speed, moved.Length());
    }

    private async Task TestShoveCrate(PlayerController player)
    {
        player.Teleport(new Vector3(0.0f, 0.1f, 0.0f), 0.0f);
        await WaitSeconds(0.5f);
        Vector3 forward = await WalkDirection(player);
        player.Teleport(new Vector3(0.0f, 0.1f, 0.0f), 0.0f);
        await WaitSeconds(0.5f);
        Input.ActionPress(ActionForward);
        await WaitSeconds(1.5f);
        Input.ActionRelease(ActionForward);
        float freeSpeed = (player.GlobalPosition - new Vector3(0.0f, 0.1f, 0.0f)).Length() / 1.5f;

        player.Teleport(new Vector3(0.0f, 0.1f, 0.0f), 0.0f);
        await WaitSeconds(0.8f);
        LooseProp crate = Crate(forward * 1.6f, 18.0f);
        await WaitSeconds(0.6f);
        (float speed, float moved) = await WalkInto(player, crate, 2.5f);
        Check("walking into a crate shoves it along", moved > 0.8f, $"crate moved {moved:0.00} m");
        Check("the carrier isn't stopped dead by a crate", speed > freeSpeed * KeepsSpeed, $"{speed:0.00} m/s against {freeSpeed:0.00} m/s free");
        crate.QueueFree();
    }

    private async Task TestShoveCart(PlayerController player)
    {
        player.Teleport(new Vector3(0.0f, 0.1f, 0.0f), 0.0f);
        await WaitSeconds(0.8f);
        Vector3 forward = await WalkDirection(player);
        player.Teleport(new Vector3(0.0f, 0.1f, 0.0f), 0.0f);
        await WaitSeconds(0.5f);
        HandCart cart = new() { Name = "HandCart" };
        AddChild(cart);
        // Parked with its wheels along the walk, so a push rolls it.
        cart.GlobalPosition = forward * 2.4f + Vector3.Up * (cart.WheelRadius + 0.05f);
        cart.LookAt(cart.GlobalPosition - forward, Vector3.Up);
        await WaitSeconds(1.0f);
        (float _, float moved) = await WalkInto(player, cart, 2.5f);
        Check("walking into the parked cart makes it give way", moved > 0.4f, $"cart moved {moved:0.00} m");
        cart.QueueFree();
        await WaitSeconds(0.2f);
    }

    private CharacterBody3D Body(string name, Vector3 at, float radius)
    {
        CharacterBody3D body = new() { Name = name, Position = at, CollisionLayer = 0, CollisionMask = 1 };
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = 1.8f }, Position = new Vector3(0.0f, 0.9f, 0.0f) });
        AddChild(body);
        return body;
    }

    private async Task TestSpacing()
    {
        CharacterBody3D light = Body("Light", new Vector3(20.0f, 0.05f, 0.0f), 0.35f);
        CharacterBody3D heavy = Body("Heavy", new Vector3(20.3f, 0.05f, 0.0f), 0.45f);
        CharacterSpacing.Register(light, 0.35f, 60.0f);
        CharacterSpacing.Register(heavy, 0.45f, 180.0f);
        Vector3 lightStart = light.GlobalPosition;
        Vector3 heavyStart = heavy.GlobalPosition;
        for (int i = 0; i < 60; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            float dt = 1.0f / Engine.PhysicsTicksPerSecond;
            CharacterSpacing.Separate(light, dt);
            CharacterSpacing.Separate(heavy, dt);
        }
        float apart = new Vector2(light.GlobalPosition.X - heavy.GlobalPosition.X, light.GlobalPosition.Z - heavy.GlobalPosition.Z).Length();
        float lightMoved = light.GlobalPosition.DistanceTo(lightStart);
        float heavyMoved = heavy.GlobalPosition.DistanceTo(heavyStart);
        Check("two characters inside each other are eased apart", apart > 0.75f, $"apart {apart:0.00} m (touching at 0,80)");
        Check("the lighter one gives way more", lightMoved > heavyMoved * 1.5f, $"light moved {lightMoved:0.00}, heavy {heavyMoved:0.00}");
        CharacterSpacing.Unregister(light);
        CharacterSpacing.Unregister(heavy);
        light.QueueFree();
        heavy.QueueFree();
    }

    private async Task TestReset()
    {
        LooseProp crate = Crate(new Vector3(-10.0f, 0.3f, -10.0f), 18.0f);
        await WaitSeconds(1.0f);
        Vector3 home = crate.GlobalPosition;
        crate.GlobalPosition = home + Vector3.Down * (crate.ResetDrop + 5.0f);
        crate.LinearVelocity = Vector3.Zero;
        await WaitSeconds(0.2f);
        Check("a prop that falls far is put back", crate.GlobalPosition.DistanceTo(home) < 1.0f, $"{crate.GlobalPosition.DistanceTo(home):0.00} m from home");
        crate.QueueFree();
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
