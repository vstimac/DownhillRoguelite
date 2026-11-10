using System.Threading.Tasks;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Controlled support changes that expose stale floor contact in idle wildlife.</summary>
public partial class WildlifePhysicsTests : Node3D
{
    private int _failures;
    private WildlifeAnimal? _animal;

    public override async void _Ready()
    {
        AddChild(Platform(-0.5f, new Vector3(20.0f, 1.0f, 20.0f)));
        StaticBody3D ledge = Platform(0.5f, new Vector3(4.0f, 1.0f, 4.0f));
        AddChild(ledge);
        WildlifeSpecies species = new()
        {
            DisplayName = "Support test animal",
            BodySize = new Vector3(0.4f, 0.8f, 0.4f),
            FleeDistance = 0.0f,
            WatchDistance = 0.0f,
        };
        WildlifeTable table = new() { WanderInterval = new Vector2(1000.0f, 1000.0f) };
        _animal = new WildlifeAnimal();
        AddChild(_animal);
        _animal.Setup(species, table, 42);
        _animal.Position = new Vector3(0.0f, 1.05f, 0.0f);
        await TickSeconds(0.5f);
        Check("idle animal settles on its ledge", _animal.HasGround && Mathf.Abs(_animal.Position.Y - 1.0f) < 0.02f);

        ledge.QueueFree();
        await TickSeconds(0.8f);
        Check("removing support lets a grazing animal fall", _animal.HasGround && Mathf.Abs(_animal.Position.Y) < 0.02f);

        // Streaming, debugging or a moving obstacle can change the transform without walk input.
        _animal.Position = new Vector3(0.0f, 1.0f, 0.0f);
        _animal.Velocity = Vector3.Zero;
        await TickSeconds(0.1f);
        Check("an idle body cannot retain floor contact in mid-air", !_animal.HasGround && _animal.Velocity.Y < 0.0f);
        await TickSeconds(0.8f);
        Check("after displacement the animal lands on the real floor", _animal.HasGround && Mathf.Abs(_animal.Position.Y) < 0.02f);

        float settled = _animal.Position.Y;
        await TickSeconds(2.0f);
        Check("grounded grazing stays still", _animal.HasGround && Mathf.Abs(_animal.Position.Y) < 0.02f
            && Mathf.Abs(_animal.Position.Y - settled) < 0.001f);

        _animal.QueueFree();
        StaticBody3D slope = Platform(0.0f, new Vector3(4.0f, 0.2f, 4.0f));
        slope.Position = new Vector3(7.0f, 0.0f, 0.0f);
        slope.Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.25f);
        AddChild(slope);
        species.BodySize = new Vector3(0.6f, 0.8f, 0.6f);
        _animal = new WildlifeAnimal();
        AddChild(_animal);
        _animal.Setup(species, table, 42);
        _animal.Position = new Vector3(7.0f, 0.8f, 0.0f);
        await TickSeconds(1.0f);
        Vector3 normal = slope.GlobalBasis.Y;
        Vector3 surface = slope.GlobalPosition + normal * 0.1f;
        float beneath = surface.Y - normal.X * (_animal.Position.X - surface.X) / normal.Y;
        float expectedClearance = 0.3f * (1.0f / normal.Y - 1.0f);
        float actualClearance = _animal.Position.Y - beneath;
        Check("a capsule on 45-degree ground has its analytical vertical clearance",
            _animal.HasGround && actualClearance > 0.08f && Mathf.Abs(actualClearance - expectedClearance) < 0.01f);
        float planeGap = (_animal.Position + Vector3.Up * 0.3f - surface).Dot(normal) - 0.3f;
        Check("capsule contact is measured against the supporting plane", Mathf.Abs(planeGap) < 0.01f);
        GD.Print($"WILDLIFE PHYSICS TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private static StaticBody3D Platform(float height, Vector3 size)
    {
        StaticBody3D platform = new() { Position = new Vector3(0.0f, height, 0.0f) };
        platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return platform;
    }

    private async Task TickSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _animal!.Tick(dt, new Vector3(1000.0f, 0.0f, 1000.0f), false, GetWorld3D().DirectSpaceState);
        }
    }

    private void Check(string name, bool passed)
    {
        if (passed)
        {
            GD.Print("PASS  " + name);
        }
        else
        {
            GD.Print("FAIL  " + name + $" (y={_animal!.Position.Y:0.000}, ground={_animal.HasGround}, velocity={_animal.Velocity})");
            _failures++;
        }
    }
}
