using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Lock on (Tab): with no enemy near, Tab is left to the pack screen; with enemies about it picks the
/// one in view over a nearer one behind; the camera swings to keep it in view; with the sword drawn
/// the carrier turns to face it and his swing lands on it; without a lock a swing turns him the way
/// the camera looks; Tab again lets go, and the lock breaks by itself when the enemy is gone.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/LockOnTests.tscn
/// </summary>
public partial class LockOnTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        LockOn? lockOn = PlayerBody?.GetNodeOrNull<LockOn>("LockOn");
        MeleeCombat? combat = PlayerBody?.GetNodeOrNull<MeleeCombat>("MeleeCombat");
        if (PlayerBody == null || Streamer == null || lockOn == null || combat == null)
        {
            GD.PushError("LockOnTests: PlayerBody (with LockOn and MeleeCombat) and Streamer are needed.");
            GetTree().Quit(1);
            return;
        }
        PlayerBody.Teleport(Streamer.SpawnPoint + Vector3.Up * 0.1f, 0.0f);
        PlayerBody.Health?.Refill();
        PlayerBody.Stamina?.Refill();
        await Wait(2.0f);

        Check("with no enemy near, Tab is left to the pack screen", !lockOn.Toggle() && GameEvents.LockTarget == null, $"lock {GameEvents.LockTarget?.Name}");

        Vector3 at = PlayerBody.GlobalPosition;
        Vector3 forward = Flat(-PlayerBody.GlobalBasis.Z);
        Vector3 side = Flat(PlayerBody.GlobalBasis.X);
        MeleeDummy ahead = Dummy("Ahead", at + forward * 6.0f + side * 2.0f);
        MeleeDummy behind = Dummy("Behind", at - forward * 4.0f);
        await Wait(0.2f);
        bool locked = lockOn.Toggle();
        Check("Tab picks the enemy in view over a nearer one behind", locked && GameEvents.LockTarget == ahead && lockOn.Target == ahead,
            $"locked {locked}, on {GameEvents.LockTarget?.Name}");

        // Swing the view round: the one behind is the lock now (let go, turn, lock again).
        lockOn.Toggle();
        Check("Tab again lets go", GameEvents.LockTarget == null && lockOn.Target == null, $"lock {GameEvents.LockTarget?.Name}");
        ahead.QueueFree();
        await Wait(0.1f);
        lockOn.Toggle();
        await Wait(1.5f);
        float view = CameraLook().Dot(Flat(behind.GlobalPosition - PlayerBody.GlobalPosition));
        Check("the camera swings round to keep the locked enemy in view", GameEvents.LockTarget == behind && view > 0.95f,
            $"lock {GameEvents.LockTarget?.Name}, camera toward it {view:0.00}");
        float bodyBefore = Flat(-PlayerBody.GlobalBasis.Z).Dot(Flat(behind.GlobalPosition - PlayerBody.GlobalPosition));
        Check("with the sword away he doesn't turn to it", bodyBefore < 0.0f, $"body toward it {bodyBefore:0.00}");

        // Drawn: he turns to face it, and a swing lands.
        behind.GlobalPosition = PlayerBody.GlobalPosition - forward * 1.3f;
        combat.ToggleDraw();
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);
        await Wait(0.6f);
        float facing = Flat(-PlayerBody.GlobalBasis.Z).Dot(Flat(behind.GlobalPosition - PlayerBody.GlobalPosition));
        Check("with the sword drawn he turns to face the locked enemy", facing > 0.95f, $"body toward it {facing:0.00}");
        combat.Attack();
        await Until(() => behind.Blows > 0, 2.0f);
        Check("his swing lands on it", behind.Blows == 1, $"blows {behind.Blows}");
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 4.0f);

        // Gone: the lock breaks by itself.
        behind.QueueFree();
        await Wait(0.2f);
        Check("the lock breaks when the enemy is gone", GameEvents.LockTarget == null && lockOn.Target == null && PlayerBody.FacingOverride == null,
            $"lock {GameEvents.LockTarget != null}, facing held {PlayerBody.FacingOverride.HasValue}");

        // No lock: a swing turns him the way the camera looks. Sword away, lock on to one at his side
        // (the camera turns, he doesn't), let go, draw, swing.
        combat.ToggleDraw();
        await Until(() => combat.State == MeleeCombat.Stance.Sheathed, 3.0f);
        Vector3 right = Flat(PlayerBody.GlobalBasis.X);
        MeleeDummy third = Dummy("Third", PlayerBody.GlobalPosition + right * 1.3f);
        await Wait(0.1f);
        lockOn.Toggle();
        await Wait(1.5f);
        lockOn.Toggle();
        combat.ToggleDraw();
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);
        Vector3 camera = CameraLook();
        float before = Flat(-PlayerBody.GlobalBasis.Z).Dot(camera);
        combat.Attack();
        await Until(() => third.Blows > 0, 2.0f);
        float after = Flat(-PlayerBody.GlobalBasis.Z).Dot(camera);
        Check("without a lock a swing turns him the way the camera looks", before < 0.5f && after > 0.95f && third.Blows == 1,
            $"body toward the view {before:0.00} to {after:0.00}, blows {third.Blows}");
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 4.0f);
        third.QueueFree();

        if (_failures.Count == 0)
        {
            GD.Print("LOCK ON TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("LOCK ON TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    private MeleeDummy Dummy(string name, Vector3 at)
    {
        MeleeDummy dummy = new() { Name = name };
        AddChild(dummy);
        dummy.GlobalPosition = at;
        return dummy;
    }

    private Vector3 CameraLook()
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            return Vector3.Zero;
        }
        return Flat(-camera.GlobalBasis.Z);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0.0f;
        if (v.LengthSquared() < 0.000001f)
        {
            return Vector3.Zero;
        }
        return v.Normalized();
    }

    private async Task Wait(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Until(System.Func<bool> done, float timeout)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < timeout && !done(); elapsed += dt)
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
