using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for Save &amp; Continue: a new run saves nothing until asked; "Save and quit"
/// writes the run (seed, where the carrier stands, health, needs); Continue rebuilds that mountain
/// and puts the carrier back where they stood with their health and needs; death deletes the save.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RunKeeperTests.tscn
/// </summary>
public partial class RunKeeperTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public RunKeeper? Keeper { get; set; }
    /// <summary>How far from the start the carrier is moved before saving (m).</summary>
    [Export] public Vector3 SaveOffset { get; set; } = new(18.0f, 0.0f, -36.0f);
    [Export] public float PlacedWithin { get; set; } = 1.5f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        CarrierNeeds? needs = PlayerBody?.GetNodeOrNull<CarrierNeeds>("Needs");
        if (PlayerBody?.Health == null || Keeper == null || run == null || needs == null)
        {
            GD.PushError("RunKeeperTests: PlayerBody (with Health and Needs), Keeper and RunState are needed.");
            GetTree().Quit(1);
            return;
        }
        RunSave.UseFiles("user://test_run.json", "user://test_run_pack.json");
        RunSave.Delete();
        await WaitSeconds(1.5f);

        GameEvents.PublishRunBegun(false);
        Check("a new run starts with no save", Keeper.Active && !RunSave.Exists, $"active {Keeper.Active}, saved {RunSave.Exists}");

        Vector3 spot = await Ground(PlayerBody.GlobalPosition + SaveOffset);
        PlayerBody.Teleport(spot + Vector3.Up * 0.1f, 0.5f);
        await WaitSeconds(1.0f);
        Vector3 stood = PlayerBody.GlobalPosition;
        PlayerBody.Health.TakeDamage(30.0f, "test");
        needs.SetNeeds(40.0f, 55.0f);
        float health = PlayerBody.Health.Current;
        ulong seed = run.Seed;
        GameEvents.RequestSave();
        RunSave? saved = RunSave.Read(out string problem);
        Check("Save and quit writes the run", saved != null && Keeper.Saves == 1, $"saves {Keeper.Saves}, {problem}");
        if (saved != null)
        {
            Check("the save knows the mountain and the spot", saved.Seed == seed && saved.Where.DistanceTo(stood) < PlacedWithin,
                $"seed {saved.Seed} of {seed}, at {saved.Where} (stood {stood})");
            Check("the save knows health and needs", Mathf.IsEqualApprox(saved.Health, health) && Mathf.IsEqualApprox(saved.Hunger, 40.0f) && Mathf.IsEqualApprox(saved.Thirst, 55.0f),
                $"health {saved.Health:0.0} of {health:0.0}, hunger {saved.Hunger:0}, thirst {saved.Thirst:0}");
        }

        // Somewhere else entirely, as if the game had been closed and opened again.
        run.StartNewRun(seed + 977);
        await WaitSeconds(1.0f);
        PlayerBody.Health.Restore(PlayerBody.Health.Max);
        needs.SetNeeds(100.0f, 100.0f);

        GameEvents.PublishRunBegun(true);
        Check("Continue starts putting the run back", Keeper.Restoring, $"restoring {Keeper.Restoring}");
        float waited = 0.0f;
        while (Keeper.Restoring && waited < Keeper.PlaceTimeout + 2.0f)
        {
            await WaitSeconds(0.25f);
            waited += 0.25f;
        }
        await WaitSeconds(0.5f);
        Vector3 back = PlayerBody.GlobalPosition;
        Vector2 flatBack = new(back.X, back.Z);
        Vector2 flatStood = new(stood.X, stood.Z);
        Check("Continue rebuilds the saved mountain", run.Seed == seed, $"seed {run.Seed} of {seed}");
        Check("Continue puts the carrier back where they stood", flatBack.DistanceTo(flatStood) < PlacedWithin && Mathf.Abs(back.Y - stood.Y) < 2.0f,
            $"at {back}, stood {stood}, after {waited:0.0} s");
        Check("Continue gives back health and needs", Mathf.IsEqualApprox(PlayerBody.Health.Current, health) && Mathf.Abs(needs.Hunger - 40.0f) < 1.0f && Mathf.Abs(needs.Thirst - 55.0f) < 1.0f,
            $"health {PlayerBody.Health.Current:0.0} of {health:0.0}, hunger {needs.Hunger:0.0}, thirst {needs.Thirst:0.0}");

        GameEvents.PublishCarrierDied("test");
        Check("death deletes the save", !RunSave.Exists, $"saved {RunSave.Exists}");
        RunSave.Delete();

        if (_failures.Count == 0)
        {
            GD.Print("RUN KEEPER TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"RUN KEEPER TESTS: FAIL ({string.Join(", ", _failures)})");
            GetTree().Quit(1);
        }
    }

    /// <summary>The ground under a spot, once the terrain there has loaded.</summary>
    private async Task<Vector3> Ground(Vector3 around)
    {
        for (int i = 0; i < 400; i++)
        {
            PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(around + Vector3.Up * 100.0f, around + Vector3.Down * 300.0f, GroundMask,
                new Godot.Collections.Array<Rid> { PlayerBody!.GetRid() });
            Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count > 0)
            {
                return (Vector3)hit["position"];
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return around;
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
