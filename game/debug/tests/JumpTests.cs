using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Space is a real jump (since 23 Nov; the ragdoll throw is Ctrl's): he leaves the ground the frame
/// after the press, lands on his feet, keeps his speed and walks on at once, can jump again the
/// moment he lands, clears a low log on the run, jumps lower under a load, and the drawn body has no
/// pop at takeoff or landing. A real drop still goes down (DiveTests).
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/JumpTests.tscn  (-- --trace)
/// </summary>
public partial class JumpTests : Node3D
{
    private const float Frame = 1.0f / 60.0f;
    private readonly List<string> _failures = new();
    private PlayerController _body = null!;
    private PlayerRagdoll _ragdoll = null!;
    private Skeleton3D _driver = null!;
    private bool _trace;
    // The drawn hips above the soles, a sample per drawn frame while recording.
    private readonly List<float> _hips = new();
    private bool _recording;

    public override async void _Ready()
    {
        try
        {
            Node3D world = GD.Load<PackedScene>("res://debug/LearnGodot.tscn").Instantiate<Node3D>();
            AddChild(world);
            world.GetNode("BlueStep").Free();
            world.GetNode("OrangeBlock").Free();
            _body = world.GetNode<PlayerController>("Player");
            _ragdoll = _body.GetNode<PlayerRagdoll>("Ragdoll");
            _driver = _body.GetNode<Skeleton3D>("Model/Rig/Skeleton3D");
            _driver.SkeletonUpdated += OnDrawn;
            _trace = Array.IndexOf(OS.GetCmdlineUserArgs(), "--trace") >= 0;
            GameEvents.PublishLoad(LoadSnapshot.Empty);
            await Frames(30);

            Check(!_body.RagdollJump, "Space is a real jump, not a ragdoll throw", $"RagdollJump {_body.RagdollJump}");
            await StandingJump();
            await RunningJump("normal pace", sprint: false);
            await RunningJump("sprint", sprint: true);
            await JumpsInARow();
            await JumpsInARow(25.0f);
            await OverALog();
            await UnderALoad();
        }
        catch (Exception error)
        {
            _failures.Add("ran without an exception: " + error);
        }
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        foreach (string failure in _failures)
        {
            GD.Print("FAIL  " + failure);
        }
        GD.Print($"JUMP TESTS: {_failures.Count} failures");
        GetTree().Quit(Math.Min(_failures.Count, 1));
    }

    private void Check(bool ok, string what, string detail)
    {
        GD.Print($"{(ok ? "PASS" : "MISS")}  {what}: {detail}");
        if (!ok) { _failures.Add($"{what}: {detail}"); }
    }

    private static string F(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private void OnDrawn()
    {
        if (!_recording) { return; }
        Transform3D world = _driver.GetGlobalTransformInterpolated();
        _hips.Add((world * _driver.GetBoneGlobalPose(_driver.FindBone("DEF-hips")).Origin).Y - world.Origin.Y);
    }

    /// <summary>The biggest change of the drawn hips (above the soles) from one drawn frame to the next.</summary>
    private float HipsPop()
    {
        float most = 0.0f;
        for (int i = 1; i < _hips.Count; i++) { most = Mathf.Max(most, Mathf.Abs(_hips[i] - _hips[i - 1])); }
        return most;
    }

    private async Task Place(float z = 6.0f)
    {
        _body.DirectionOverride = Vector3.Zero;
        Input.ActionRelease("player_sprint");
        RollSave.Reset();
        _body.Teleport(new Vector3(0.0f, 0.1f, z), 0.0f);
        _body.Health?.Refill();
        _body.Stamina?.Refill();
        await Frames(40);
    }

    /// <summary>One jump from the press to the landing: what it did, sampled each physics frame.</summary>
    private sealed class Flight
    {
        public int Takeoff = -1, Landing = -1;
        public float Apex, Start, Distance, SpeedBefore, SlowestAfter = float.MaxValue;
        public bool Downed;
    }

    private async Task<Flight> Jump(int framesAfterLanding)
    {
        Flight flight = new() { Start = _body.GlobalPosition.Y, SpeedBefore = Flat(_body.Velocity) };
        Vector3 from = _body.GlobalPosition;
        int jumps = _body.Jumps;
        _hips.Clear();
        _recording = true;
        await Tap("player_jump");
        for (int frame = 1; frame < 240; frame++)
        {
            await Frames(1);
            flight.Downed |= _ragdoll.Downed;
            flight.Apex = Mathf.Max(flight.Apex, _body.GlobalPosition.Y - flight.Start);
            bool air = _body.State == LocomotionState.Airborne;
            if (flight.Takeoff < 0 && _body.Jumps > jumps) { flight.Takeoff = frame; }
            if (_trace) { GD.Print($"t {frame} air {air} y {F(_body.GlobalPosition.Y - flight.Start)} speed {F(Flat(_body.Velocity))} vy {F(_body.Velocity.Y)}"); }
            if (flight.Takeoff >= 0 && flight.Landing < 0 && !air && frame > flight.Takeoff + 2)
            {
                flight.Landing = frame;
                flight.Distance = Flat(_body.GlobalPosition - from);
            }
            if (flight.Landing >= 0)
            {
                flight.SlowestAfter = Mathf.Min(flight.SlowestAfter, Flat(_body.Velocity));
                if (frame >= flight.Landing + framesAfterLanding) { break; }
            }
        }
        _recording = false;
        return flight;
    }

    private async Task StandingJump()
    {
        await Place();
        Flight jump = await Jump(30);
        float air = (jump.Landing - jump.Takeoff) * Frame;
        Check(jump.Takeoff >= 1 && jump.Takeoff <= 2, "the jump leaves the ground the frame after the press", $"{jump.Takeoff} frames");
        Check(!jump.Downed && jump.Landing > 0, "a standing jump lands on its feet", $"went down {jump.Downed}, landed after {F(air)} s in the air");
        Check(jump.Apex is > 0.4f and < 0.7f, "a standing jump clears about half a metre", $"{F(jump.Apex)} m up (0.4..0.7)");
        if (_trace) { GD.Print("hips " + string.Join(" ", _hips.ConvertAll(h => h.ToString("0.000", CultureInfo.InvariantCulture)))); }
        Check(HipsPop() < 0.04f, "no pop in the drawn hips at takeoff or landing", $"biggest drawn frame step {HipsPop():0.000} m (max 0.04)");

        // Walking again: the press, the flight and the landing, a key held from the press.
        await Place();
        _body.DirectionOverride = Vector3.Forward;
        await Frames(1);
        _body.DirectionOverride = Vector3.Zero;
        await Frames(20);
        int jumps = _body.Jumps;
        await Tap("player_jump");
        _body.DirectionOverride = Vector3.Forward;
        int frames = 1 + await FramesUntil(() => _body.Jumps > jumps && _body.State != LocomotionState.Airborne && Flat(_body.Velocity) > 1.0f, 120);
        _body.DirectionOverride = Vector3.Zero;
        Check(frames * Frame <= 0.9f, "after a jump he walks on at once", $"walking {F(frames * Frame)} s after the press (max 0.9; the old ragdoll hop: 4.2)");
    }

    private async Task RunningJump(string name, bool sprint)
    {
        await Place(12.0f);
        _body.DirectionOverride = Vector3.Forward;
        if (sprint) { Input.ActionPress("player_sprint"); }
        await Frames(90);
        Flight jump = await Jump(30);
        Input.ActionRelease("player_sprint");
        _body.DirectionOverride = Vector3.Zero;
        float kept = jump.SpeedBefore > 0.01f ? jump.SlowestAfter / jump.SpeedBefore : 0.0f;
        Check(!jump.Downed && kept >= 0.95f, $"{name}: a running jump keeps the stride",
            $"{F(jump.SpeedBefore)} m/s before, slowest {F(jump.SlowestAfter)} m/s in the half second after landing ({kept:0%}, min 95%), went down {jump.Downed}");
        Check(jump.Distance >= jump.SpeedBefore * 0.55f, $"{name}: the jump carries him forward",
            $"{F(jump.Distance)} m from press to landing at {F(jump.SpeedBefore)} m/s");
        Check(HipsPop() < 0.045f, $"{name}: no pop in the drawn hips", $"biggest drawn frame step {HipsPop():0.000} m (max 0.045)");
    }

    /// <summary>Space pressed again just before each landing: the next jump comes on the landing frame, five in a row.</summary>
    private async Task JumpsInARow(float load = 0.0f)
    {
        await Place(12.0f);
        if (load > 0.0f) { Publish(load); await Frames(20); }
        _body.DirectionOverride = Vector3.Forward;
        await Frames(60);
        int jumps = _body.Jumps;
        int start = 0, frames = 0;
        bool downed = false;
        for (; frames < 400 && _body.Jumps < jumps + 5; frames++)
        {
            if (frames == 0) { await Tap("player_jump"); start = 0; continue; }
            await Frames(1);
            downed |= _ragdoll.Downed;
            // Falling and close to the ground: press for the next one.
            if (_body.State == LocomotionState.Airborne && _body.Velocity.Y < -1.0f && _body.GlobalPosition.Y < 0.2f)
            {
                await Tap("player_jump");
                frames++;
            }
        }
        _body.DirectionOverride = Vector3.Zero;
        int made = _body.Jumps - jumps;
        float each = (frames - start) * Frame / Math.Max(made, 1);
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        Check(made == 5 && !downed && each <= 0.85f, $"jumps in a row{(load > 0.0f ? $" with {load:0} kg on his back" : "")}: each press just before landing jumps again on landing",
            $"{made} jumps in {F((frames - start) * Frame)} s ({F(each)} s each, max 0.85), went down {downed}");
        await Frames(30);
    }

    /// <summary>A log 0.5 m high across the way (above the 0.35 m step-up): walking stops at it; run at it and jump a step before it, and he is over without stopping.</summary>
    private async Task OverALog()
    {
        StaticBody3D log = new() { Name = "Log", CollisionLayer = 1, CollisionMask = 0 };
        log.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.0f, 0.5f, 0.35f) } });
        AddChild(log);
        log.GlobalPosition = new Vector3(0.0f, 0.25f, 4.0f);
        await Place(12.0f);
        _body.DirectionOverride = Vector3.Forward;
        await Frames(240);
        bool blocked = _body.GlobalPosition.Z > 4.0f;
        _body.DirectionOverride = Vector3.Zero;
        await Place(12.0f);
        _body.DirectionOverride = Vector3.Forward;
        await FramesUntil(() => _body.GlobalPosition.Z < 5.4f, 240);
        Flight jump = await Jump(20);
        await FramesUntil(() => _body.GlobalPosition.Z < 2.5f, 90);
        bool over = _body.GlobalPosition.Z < 3.0f;
        _body.DirectionOverride = Vector3.Zero;
        Check(blocked && over && !jump.Downed, "a running jump clears a 50 cm log (walking stops at it) and runs on",
            $"walking stopped {blocked}, over {over} (z {F(_body.GlobalPosition.Z)}, log at 4.0), went down {jump.Downed}, {F(jump.Apex)} m up");
        log.QueueFree();
        await Frames(2);
    }

    private async Task UnderALoad()
    {
        float Apex(Flight f) => f.Apex;
        await Place();
        float empty = Apex(await Jump(10));
        Publish(20.0f);
        await Place();
        float twenty = Apex(await Jump(10));
        Publish(40.0f);
        await Place();
        float forty = Apex(await Jump(10));
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        GD.Print($"MEASURE jump height: empty {F(empty)} m, 20 kg {F(twenty)} m, 40 kg {F(forty)} m");
        Check(twenty < empty && forty < twenty && twenty >= 0.3f && forty >= 0.15f, "a heavier load jumps lower, but a loaded carrier still jumps (20 kg: 0.3 m or more, 40 kg: 0.15 m or more)",
            $"empty {F(empty)} m, 20 kg {F(twenty)} m, 40 kg {F(forty)} m");
    }

    private static void Publish(float mass)
    {
        GameEvents.PublishLoad(new LoadSnapshot($"Test {mass:0} kg", mass, new Vector3(0.0f, 1.2f, 0.15f), mass * 1.44f, mass * 1.46f,
            Array.Empty<float>(), Array.Empty<Vector3>(), Array.Empty<Vector3>()));
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    private async Task Tap(StringName action)
    {
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
}
