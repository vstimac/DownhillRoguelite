using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The director's rule, measured: everything the carrier does is fluid. For each move a key is pressed
/// at the awkward moment (just before landing, on a bump, while getting up, while a draw is still
/// playing) and the test checks the press was taken, how long the carrier was held, and that nothing
/// slides or stands frozen. The roll's own rules (RollSave: a press outside the window is a miss) are
/// checked to stay as they are: a buffered press never turns a missed roll into a jump.
/// Every line prints its measurement, so the numbers can go into the review log.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/FluidityTests.tscn
/// </summary>
public partial class FluidityTests : Node3D
{
    private const float Frame60 = 1.0f / 60.0f;
    private readonly List<string> _failures = new();
    private PlayerController _body = null!;
    private PlayerRagdoll _ragdoll = null!;
    private Dive _dive = null!;
    private Node3D _world = null!;

    public override async void _Ready()
    {
        try
        {
            _world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
            AddChild(_world);
            _world.GetNode("BlueStep").Free();
            _world.GetNode("OrangeBlock").Free();
            _body = _world.GetNode<PlayerController>("Player");
            _ragdoll = _body.GetNode<PlayerRagdoll>("Ragdoll");
            _dive = _body.GetNode<Dive>("Dive");
            _ragdoll.SafeImpactSpeed = 100.0f;
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-walk-off") >= 0)
            {
                // The get-up as it was: walking does not cut its pose short (to see what the slide check measures).
                _ragdoll.WalkOffRiseSpeed = 999.0f;
            }
            await Place();

            await TestWalkStartStop();
            await TestTurnOnTheSpot();
            await TestSprint();
            await TestCrouch();
            await TestJumpOnFlat();
            await TestHopWithRoll();
            await TestJumpJustBeforeLanding();
            await TestJumpOnABump();
            await TestEarlyRollIsStillAMiss();
            await TestLanding();
            await TestGetUp(withInput: false);
            await TestGetUp(withInput: true);
            await TestEatWhileGettingUp();
            await TestDiveWhileGettingUp();
            await TestSword();
        }
        catch (Exception error)
        {
            _failures.Add("the fluidity tests ran without an exception: " + error);
        }
        foreach (string failure in _failures)
        {
            GD.Print("FAIL  " + failure);
        }
        GD.Print($"FLUIDITY TESTS: {_failures.Count} failures");
        GetTree().Quit(Math.Min(_failures.Count, 1));
    }

    private void Check(bool ok, string what, string detail)
    {
        if (ok)
        {
            GD.Print($"PASS  {what}: {detail}");
        }
        else
        {
            GD.Print($"MISS  {what}: {detail}");
            _failures.Add($"{what}: {detail}");
        }
    }

    // --- Walking, turning, sprinting, crouching ---

    private async Task TestWalkStartStop()
    {
        await Place();
        Vector3 start = _body.GlobalPosition;
        _body.DirectionOverride = Vector3.Forward;
        int moved = await FramesUntil(() => Flat(_body.GlobalPosition - start) > 0.05f, 60);
        await Seconds(1.0f);
        _body.DirectionOverride = Vector3.Zero;
        int stopped = await FramesUntil(() => Flat(_body.Velocity) < 0.1f, 90);
        Check(moved <= 10 && stopped <= 24, "walking starts and stops at once",
            $"moved 5 cm in {moved} frames (max 10), stopped in {stopped} frames (max 24)");
    }

    private async Task TestTurnOnTheSpot()
    {
        await Place();
        _body.DirectionOverride = Vector3.Back * 0.3f;
        float target = Mathf.Atan2(0.0f, -1.0f);
        int turned = await FramesUntil(() => Mathf.Abs(Mathf.AngleDifference(_body.Rotation.Y, target)) < Mathf.DegToRad(20.0f), 120);
        _body.DirectionOverride = Vector3.Zero;
        Check(turned <= 30, "turning round on the spot", $"faced the other way in {turned} frames ({turned * Frame60:0.00} s, max 0.5 s)");
    }

    private async Task TestSprint()
    {
        await Place();
        _body.DirectionOverride = Vector3.Forward;
        await Seconds(1.5f);
        float walking = Flat(_body.Velocity);
        Input.ActionPress("player_sprint");
        int faster = await FramesUntil(() => Flat(_body.Velocity) > walking * 1.15f, 90);
        Input.ActionRelease("player_sprint");
        int slower = await FramesUntil(() => Flat(_body.Velocity) < walking * 1.05f, 120);
        _body.DirectionOverride = Vector3.Zero;
        Check(faster <= 20 && slower <= 45, "sprint picks up and lets go", $"15 % faster in {faster} frames (max 20), back to a walk in {slower} frames (max 45), walking {walking:0.0} m/s");
    }

    private async Task TestCrouch()
    {
        await Place();
        Input.ActionPress("player_brake");
        int down = await FramesUntil(() => _body.Crouching, 30);
        Input.ActionRelease("player_brake");
        int up = await FramesUntil(() => !_body.Crouching, 30);
        Check(down <= 2 && up <= 2, "crouch follows the key", $"down in {down} frames, up in {up} frames (max 2)");
    }

    // --- Jumping and the roll ---

    private async Task TestJumpOnFlat()
    {
        await Place();
        int jumps = _body.Jumps;
        await Tap("player_jump");
        int taken = await FramesUntil(() => _body.Jumps > jumps, 10);
        Check(_body.Jumps == jumps + 1, "Space on flat ground jumps", $"jumped after {taken} frames");
        // A real jump: he lands on his feet and walks on (the ragdoll throw is Ctrl's). JumpTests measures it closely.
        _body.DirectionOverride = Vector3.Forward;
        bool downed = false;
        int walking = taken + await FramesUntil(() =>
        {
            downed |= _ragdoll.Downed;
            return _body.State != LocomotionState.Airborne && Flat(_body.Velocity) > 0.5f;
        }, 120);
        _body.DirectionOverride = Vector3.Zero;
        GD.Print($"MEASURE the Space jump with a key held: walking again {walking * Frame60:0.00} s after the press");
        Check(!downed && walking * Frame60 <= 1.0f, "a jump lands on his feet and he walks on at once",
            $"went down {downed}, walking {walking * Frame60:0.00} s after the press (max 1.0; the old ragdoll hop: 4.2)");
        await WaitUp();
    }


    /// <summary>The skill path on a thrown body (the ragdoll hop, now only an option): Space timed in the roll's window rolls him onto his feet.</summary>
    private async Task TestHopWithRoll()
    {
        _body.RagdollJump = true;
        await Place();
        int rolls = 0;
        void OnRolled() { rolls++; }
        GameEvents.Rolled += OnRolled;
        await Tap("player_jump");
        bool pressed = false;
        int frames = 1;
        for (; frames < 200 && !pressed; frames++)
        {
            await Frames(1);
            VaultCue cue = GameEvents.CurrentVault;
            if (_ragdoll.Downed && cue.InWindow && cue.Distance <= cue.Sweet + 0.02f)
            {
                pressed = true;
                await Tap("player_jump");
            }
        }
        _body.DirectionOverride = Vector3.Forward;
        frames += await FramesUntil(() => rolls > 0 && !_ragdoll.Downed && !_dive.Diving && Flat(_body.Velocity) > 1.0f, 300);
        _body.DirectionOverride = Vector3.Zero;
        GameEvents.Rolled -= OnRolled;
        GD.Print($"MEASURE the Space hop with a timed roll: walking again {frames * Frame60:0.00} s after the hop");
        Check(pressed && rolls == 1 && !GameEvents.IsDowned && frames * Frame60 <= 2.6f, "a hop with a timed roll comes up walking",
            $"pressed in the window {pressed}, rolls {rolls}, walking {frames * Frame60:0.00} s after the hop (max 2.6)");
        await WaitUp();
        _body.RagdollJump = false;
    }
    /// <summary>Space a few frames before the feet touch down (a short drop): the jump comes on landing.</summary>
    private async Task TestJumpJustBeforeLanding()
    {
        await Place();
        int jumps = _body.Jumps;
        _body.Teleport(_body.GlobalPosition + Vector3.Up * 1.0f, 0.0f);
        await FramesUntil(() => !_body.IsOnFloor() && Height() < 0.12f, 90);
        bool airborne = !_body.IsOnFloor();
        await Tap("player_jump");
        int taken = await FramesUntil(() => _body.Jumps > jumps, 30);
        Check(airborne && _body.Jumps == jumps + 1, "Space pressed just before landing is kept for the landing",
            $"airborne at the press {airborne}, jumped {_body.Jumps - jumps} time(s), {taken} frames after the press");
        await WaitUp();
    }

    /// <summary>Space while a bump has the feet off the ground for a frame or two (uneven slopes do this all the time).</summary>
    private async Task TestJumpOnABump()
    {
        await Place();
        int jumps = _body.Jumps;
        // Past the floor snap (0.5 m), so the feet really leave the ground; pressed two frames after.
        _body.Teleport(_body.GlobalPosition + Vector3.Up * 0.56f, 0.0f);
        await Frames(1);
        await Frames(1);
        bool airborne = !_body.IsOnFloor();
        await Tap("player_jump");
        int taken = await FramesUntil(() => _body.Jumps > jumps, 30);
        Check(_body.Jumps == jumps + 1, "Space with the feet a moment off the ground still jumps",
            $"airborne {airborne}, jumped {_body.Jumps - jumps} time(s), {taken} frames after the press");
        await WaitUp();
    }

    /// <summary>The roll stays a skill: Space far too early in a real fall is a miss, and it is not kept as a jump either.</summary>
    private async Task TestEarlyRollIsStillAMiss()
    {
        await Place();
        RollSave.Reset();
        int jumps = _body.Jumps;
        int rolls = 0;
        RollGrade grade = RollGrade.None;
        void OnRolled() { rolls++; }
        void OnResolved(RollGrade g) { grade = g; }
        GameEvents.Rolled += OnRolled;
        RollSave.Resolved += OnResolved;
        _body.Teleport(_body.GlobalPosition + Vector3.Up * 10.0f, 0.0f);
        await FramesUntil(() => GameEvents.CurrentVault.Active && GameEvents.CurrentVault.Distance > GameEvents.CurrentVault.Far + 0.15f, 90);
        bool cued = GameEvents.CurrentVault.Active && !GameEvents.CurrentVault.InWindow;
        await Tap("player_jump");
        await FramesUntil(() => _body.IsOnFloor() || GameEvents.IsDowned, 120);
        await Frames(30);
        GameEvents.Rolled -= OnRolled;
        RollSave.Resolved -= OnResolved;
        Check(cued && grade == RollGrade.Miss && rolls == 0 && _body.Jumps == jumps, "Space before the roll's window is still a miss, and not a jump on landing",
            $"cue shown {cued}, graded {grade}, rolls {rolls}, jumps {_body.Jumps - jumps}, down {GameEvents.IsDowned}");
        await WaitUp();
    }

    /// <summary>A soft drop (0.6 m): walking on at full stride right after, no stop on landing.</summary>
    private async Task TestLanding()
    {
        await Place();
        _body.DirectionOverride = Vector3.Forward;
        await Seconds(1.5f);
        float walking = Flat(_body.Velocity);
        _body.GlobalPosition += Vector3.Up * 0.6f;
        await FramesUntil(() => !_body.IsOnFloor(), 10);
        await FramesUntil(() => _body.IsOnFloor(), 90);
        int slowest = 0;
        float least = 99.0f;
        for (int i = 0; i < 30; i++)
        {
            await Frames(1);
            float speed = Flat(_body.Velocity);
            if (speed < least)
            {
                least = speed;
                slowest = i;
            }
        }
        _body.DirectionOverride = Vector3.Zero;
        Check(least > walking * 0.6f && !GameEvents.IsDowned, "a soft landing doesn't stop the walk",
            $"slowest {least:0.0} m/s ({slowest} frames after landing) of {walking:0.0} m/s walking, down {GameEvents.IsDowned}");
    }

    // --- Falling down and getting up ---

    /// <summary>
    /// Knocked down on flat ground: how long he lies still after the body has come to rest, and how
    /// soon he walks once up. With a key held he gets up as soon as the fall allows (MinDownTime).
    /// </summary>
    private async Task TestGetUp(bool withInput)
    {
        await Place();
        string how = withInput ? "with a key held" : "with nothing pressed";
        if (withInput)
        {
            _body.DirectionOverride = null;
            Input.ActionPress("player_move_forward");
        }
        _ragdoll.GoDown(_body.GlobalBasis.Z * 2.0f, Vector3.Zero);
        CharacterAnimator animator = _body.GetNode<CharacterAnimator>("Animator");
        int downFrames = 0;
        float lastStill = 0.0f;
        float waited = -1.0f;
        for (int i = 0; i < 600 && _ragdoll.Downed; i++)
        {
            await Frames(1);
            downFrames++;
            if (waited < 0.0f && (_ragdoll.GettingUp || _ragdoll.TurningOver))
            {
                // Lying still beyond what the fall itself demands (its MinDownTime): the wait the player sees.
                float at = downFrames * Frame60;
                waited = at - Mathf.Max(_ragdoll.MinDownTime, at - lastStill);
            }
            lastStill = _ragdoll.StillFor;
        }
        // Up: walking on, and whether the legs stride while the get-up clip still holds him in its pose.
        Vector3 up = _body.GlobalPosition;
        _body.DirectionOverride = Vector3.Forward;
        int walk = await FramesUntil(() => Flat(_body.GlobalPosition - up) > 0.05f, 120);
        int slideFrames = 0;
        for (int i = 0; i < 60; i++)
        {
            if (animator.ActionOn || animator.ActionWeight > 0.3f)
            {
                if (Flat(_body.Velocity) > 1.0f)
                {
                    slideFrames++;
                }
            }
            await Frames(1);
        }
        _body.DirectionOverride = Vector3.Zero;
        Input.ActionRelease("player_move_forward");
        GD.Print($"MEASURE get-up {how}: down {downFrames * Frame60:0.00} s, lay still {waited:0.00} s longer than the fall's minimum, walked 5 cm {walk} frames after, {slideFrames} frames striding in the get-up pose");
        if (withInput)
        {
            Check(waited <= 0.3f, "a key held while down gets him up without lying there longer than the fall",
                $"lay still {waited:0.00} s past the fall's minimum {_ragdoll.MinDownTime:0.0} s (max 0.3), down {downFrames * Frame60:0.00} s");
        }
        Check(walk <= 12 && slideFrames <= 12, $"he walks as soon as he is up, without sliding in the get-up pose ({how})", $"5 cm in {walk} frames (max 12), {slideFrames} frames striding in the get-up pose (max 12)");
        await Seconds(1.0f);
    }

    /// <summary>F pressed while he is getting up: he eats once he is up, not never.</summary>
    private async Task TestEatWhileGettingUp()
    {
        await Place();
        int asked = 0;
        void OnConsume(SupplyNeed need, float deficit) { asked++; }
        GameEvents.ConsumeRequested += OnConsume;
        _ragdoll.GoDown(_body.GlobalBasis.Z * 2.0f, Vector3.Zero);
        await FramesUntil(() => _ragdoll.GettingUp, 600);
        await Tap("player_eat");
        bool during = GameEvents.IsDowned;
        await FramesUntil(() => asked > 0, 90);
        GameEvents.ConsumeRequested -= OnConsume;
        Check(during && asked == 1, "F pressed while getting up eats once he is up", $"pressed while down {during}, asked to eat {asked} time(s)");
        await WaitUp();
    }

    /// <summary>Ctrl pressed while getting up: the dive comes when he is up (the dive keeps a press a short while).</summary>
    private async Task TestDiveWhileGettingUp()
    {
        await Place();
        int dives = _dive.Dives;
        _ragdoll.GoDown(_body.GlobalBasis.Z * 2.0f, Vector3.Zero);
        await FramesUntil(() => _ragdoll.GettingUp, 600);
        await Tap("player_dive");
        bool during = GameEvents.IsDowned;
        int taken = await FramesUntil(() => _dive.Dives > dives, 90);
        Check(during && _dive.Dives == dives + 1, "Ctrl pressed while getting up dives once he is up", $"pressed while down {during}, dived {_dive.Dives - dives} time(s), {taken} frames after the press");
        await WaitUp();
    }

    // --- The sword ---

    private async Task TestSword()
    {
        await Place();
        MeleeCombat combat = _body.GetNode<MeleeCombat>("MeleeCombat");
        await Seconds(0.5f);
        combat.ToggleDraw();
        if (combat.State == MeleeCombat.Stance.Sheathed)
        {
            GD.Print("SKIP  the sword: no sword clips in this copy");
            return;
        }
        // Attack pressed while the draw still plays: the swing follows the draw.
        await Frames(3);
        combat.Attack();
        int swung = await FramesUntil(() => combat.State == MeleeCombat.Stance.Attacking, 90);
        Check(combat.State == MeleeCombat.Stance.Attacking, "attack pressed while drawing swings right after the draw", $"swing started {swung} frames after the press");
        // Kick pressed during the swing: the kick follows it.
        await Frames(4);
        combat.Kick();
        int kicked = await FramesUntil(() => combat.State == MeleeCombat.Stance.Kicking, 120);
        Check(combat.State == MeleeCombat.Stance.Kicking, "kick pressed during a swing kicks after it", $"kick started {kicked} frames after the press");
        await FramesUntil(() => combat.State == MeleeCombat.Stance.Ready, 120);
        combat.ToggleDraw();
        await Seconds(1.5f);
    }

    // --- Helpers ---

    private async Task Place()
    {
        _body.DirectionOverride = Vector3.Zero;
        RollSave.Reset();
        GameEvents.PublishRollPrimed(false);
        _body.Teleport(new Vector3(0.0f, 0.1f, 3.0f), 0.0f);
        _body.Health?.Refill();
        _body.Stamina?.Refill();
        await Seconds(0.6f);
        await WaitUp();
    }

    private async Task WaitUp()
    {
        for (int i = 0; i < 900 && (_ragdoll.Downed || _ragdoll.Rising || _dive.Diving); i++)
        {
            await Frames(1);
        }
        _body.Health?.Refill();
        _body.Stamina?.Refill();
        await Seconds(0.3f);
    }

    private float Height()
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(_body.GlobalPosition + Vector3.Up, _body.GlobalPosition + Vector3.Down * 10.0f);
        query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
        Godot.Collections.Dictionary hit = _body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? 99.0f : _body.GlobalPosition.Y - hit["position"].AsVector3().Y;
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    private async Task Tap(StringName action)
    {
        // As the keyboard does: an event (for the keys read in _UnhandledInput) that also sets the action's state.
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    private async Task<int> FramesUntil(Func<bool> done, int most)
    {
        int frames = 0;
        while (!done() && frames < most)
        {
            await Frames(1);
            frames++;
        }
        return frames;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Seconds(float seconds)
    {
        await Frames(Mathf.CeilToInt(seconds * 60.0f));
    }
}
