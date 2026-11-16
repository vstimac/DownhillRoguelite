using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The carrier left alone stays standing. Like the environment shots (EnvironmentShots), this
/// teleports him onto the main path at eight depths of seed 1, waits for the ground to stream in,
/// then leaves him with no input for HoldSeconds: he must not fall, walk or slide off. When he
/// does, the log says what did it (a knockdown or launch and its cause, a hard landing, balance
/// torques, the weather, the nearest animal).
/// </summary>
public partial class CarrierStandsTests : Node3D
{
    [Export] public float[] Depths { get; set; } = { 25.0f, 120.0f, 260.0f, 1250.0f, 2350.0f, 3400.0f, 4450.0f, 7600.0f };
    [Export] public float SettleSeconds { get; set; } = 1.5f;
    [Export] public float HoldSeconds { get; set; } = 8.0f;
    [Export] public float MaxWaitSeconds { get; set; } = 25.0f;
    /// <summary>Most the carrier may drift (horizontally) while he stands, in m.</summary>
    [Export] public float MaxDrift { get; set; } = 0.35f;

    private readonly List<string> _events = new();
    private float _clock;
    private BalanceSnapshot _balance;
    private float _peakImbalance;
    private LocomotionSnapshot _locomotion;
    private float _peakInput;
    private int _falls;
    private int _inputEvents;
    private int _mouseMotions;

    public override async void _Ready()
    {
        PlayerController player = GetNode<PlayerController>("World/Player");
        TerrainStreamer streamer = GetNode<TerrainStreamer>("World/Terrain");
        RunState run = GetNode<RunState>("/root/RunState");
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        Node? wildlife = GetNodeOrNull("World/Wildlife");
        Node? beasts = GetNodeOrNull("World/Beasts");
        ProcessMode = ProcessModeEnum.Always;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--depths=", StringComparison.Ordinal))
            {
                List<float> picked = new();
                foreach (string part in arg["--depths=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    picked.Add(float.Parse(part, CultureInfo.InvariantCulture));
                }
                Depths = picked.ToArray();
            }
        }
        await Seconds(1.0f);
        GD.Print($"carrier stands: joypads {Input.GetConnectedJoypads().Count}, window focus {DisplayServer.WindowIsFocused()}");
        if (run.Seed != 1)
        {
            streamer.Reseed(1);
            await Seconds(1.0f);
        }
        if (run.Route == null || streamer.Generator == null)
        {
            GD.Print("FAIL  carrier stands: no route");
            GetTree().Quit(1);
            return;
        }
        GameEvents.KnockdownRequested += OnKnockdown;
        GameEvents.LaunchRequested += OnLaunch;
        GameEvents.RagdollLaunchRequested += OnRagdollLaunch;
        GameEvents.Landed += OnLanded;
        GameEvents.DamageRequested += OnDamage;
        GameEvents.BalanceUpdated += OnBalance;
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.DownedChanged += OnDowned;

        TerrainGenerator generator = streamer.Generator;
        IRouteQuery route = run.Route;
        int failures = 0;
        foreach (float depth in Depths)
        {
            float z = route.StartZ - 5.0f;
            while (generator.DepthAt(route.RoutePoint(z).Y) < depth && z > -60000.0f) { z -= 10.0f; }
            Vector3 at = route.RoutePoint(z);
            player.Teleport(at + Vector3.Up * 0.3f, 0.0f);
            if (clock != null) { clock.SetTimeOfDay(12.0f); clock.Paused = true; }
            TerrainStreamer.Loading = true;
            float waited = 0.0f;
            int stable = 0;
            int lastLoaded = -1;
            while (waited < MaxWaitSeconds)
            {
                await Seconds(0.25f);
                waited += 0.25f;
                player.Teleport(at + Vector3.Up * 0.3f, 0.0f);
                if (streamer.LoadedCount == lastLoaded && streamer.PlantsPendingCount == 0) { stable++; } else { stable = 0; }
                lastLoaded = streamer.LoadedCount;
                if (stable >= 6) { break; }
            }
            TerrainStreamer.Loading = false;
            _events.Clear();
            _clock = 0.0f;
            _peakImbalance = 0.0f;
            _peakInput = 0.0f;
            _inputEvents = 0;
            _mouseMotions = 0;
            int fallsBefore = _falls;
            bool downedAtStart = GameEvents.IsDowned;
            await Seconds(SettleSeconds);
            Vector3 standing = player.GlobalPosition;
            float drift = 0.0f;
            float held = 0.0f;
            float dt = 1.0f / Engine.PhysicsTicksPerSecond;
            while (held < HoldSeconds)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                held += dt;
                Vector3 moved = player.GlobalPosition - standing;
                drift = Mathf.Max(drift, new Vector2(moved.X, moved.Z).Length());
            }
            int falls = _falls - fallsBefore;
            string animal = Nearest(wildlife, player.GlobalPosition);
            string beast = Nearest(beasts, player.GlobalPosition);
            WeatherSnapshot weather = GameEvents.CurrentWeather;
            string detail = string.Format(CultureInfo.InvariantCulture,
                "at {0}, waited {1:0.0}s, drift {2:0.00} m, falls {3}, downed {4} (at start {5}), surface {6}, slope {7:0.0}°, grip {8:0.00}, state {9}, peak input {10:0.00}, peak imbalance {11:0.00}, balance {12}, weather {13} wind {14:0.0} m/s, nearest animal {15}, nearest beast {16}, input events {17} (+{18} mouse moves), physics {19} Hz, frame {20:0.0} ms",
                at, waited, drift, falls, GameEvents.IsDowned, downedAtStart, _locomotion.Surface, _locomotion.SlopeDegrees, _locomotion.Grip,
                _locomotion.State, _peakInput, _peakImbalance, _balance.State, weather.Kind, weather.Wind.Length(), animal, beast,
                _inputEvents, _mouseMotions, Engine.PhysicsTicksPerSecond, Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);
            bool standingStill = falls == 0 && !GameEvents.IsDowned && !downedAtStart && drift <= MaxDrift;
            GD.Print($"{(standingStill ? "PASS" : "FAIL")}  carrier stands at {depth:0} m: {detail}");
            if (!standingStill)
            {
                failures++;
                foreach (string line in _events) { GD.Print("      " + line); }
            }
        }

        // A gamepad used for something else (another game, with this one alt-tabbed away) must not
        // walk or throw the carrier. Godot drops gamepad input once the window loses focus with
        // this project setting; a window that never had focus (a capture started with no_focus)
        // still gets it, which is why the shot tools turn the carrier's input off themselves.
        if (!Input.IgnoreJoypadOnUnfocusedApplication)
        {
            GD.Print("FAIL  carrier stands: gamepad input reaches the game while its window is not in front (input_devices/joypads/ignore_joypad_on_unfocused_application)");
            failures++;
        }
        else
        {
            GD.Print("PASS  carrier stands: the game ignores gamepads once its window goes to the background");
        }

        // The shot tools (EnvironmentShots) turn the carrier's input off: a stick pushed and the
        // jump button pressed then move nothing. With his input on, the same input walks or throws
        // him, so the check would notice if the fake input stopped reaching him.
        SetInput(player, false);
        (float blockedDrift, int blockedFalls) = await PadInput(player);
        SetInput(player, true);
        bool blocked = blockedDrift <= MaxDrift && blockedFalls == 0;
        GD.Print($"{(blocked ? "PASS" : "FAIL")}  carrier stands while his input is off and a gamepad is used: drift {blockedDrift:0.00} m, falls {blockedFalls}");
        if (!blocked) { failures++; }
        (float freeDrift, int freeFalls) = await PadInput(player);
        bool reached = freeDrift > 1.0f || freeFalls > 0;
        GD.Print($"{(reached ? "PASS" : "FAIL")}  the same gamepad input moves him with his input on: drift {freeDrift:0.00} m, falls {freeFalls}");
        if (!reached) { failures++; }

        GameEvents.KnockdownRequested -= OnKnockdown;
        GameEvents.LaunchRequested -= OnLaunch;
        GameEvents.RagdollLaunchRequested -= OnRagdollLaunch;
        GameEvents.Landed -= OnLanded;
        GameEvents.DamageRequested -= OnDamage;
        GameEvents.BalanceUpdated -= OnBalance;
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.DownedChanged -= OnDowned;
        GD.Print(failures == 0 ? "PASS  carrier stands at every depth and ignores input he should" : $"FAIL  carrier stands: {failures} checks failed");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion) { _mouseMotions++; return; }
        _inputEvents++;
        Note($"input {@event.GetType().Name} device {@event.Device}: {@event.AsText()}");
    }

    private void Note(string what)
    {
        if (_events.Count < 40) { _events.Add($"{_clock:0.00}s {what}"); }
    }

    private void OnKnockdown(Vector3 velocity, string cause) => Note($"knockdown by {cause}, velocity {velocity}");
    private void OnLaunch(Vector3 direction, float height, string cause) => Note($"launch by {cause}, {direction} height {height:0.0}");
    private void OnRagdollLaunch(Vector3 velocity, Vector3 spin, string cause) => Note($"ragdoll launch by {cause}, velocity {velocity}");
    private void OnLanded(float speed) { if (speed > 1.0f) { Note($"landed at {speed:0.0} m/s"); } }
    private void OnDamage(float amount, string cause) => Note($"damage {amount:0.0} from {cause}");

    private void OnDowned(bool downed)
    {
        if (!downed) { Note("up again"); return; }
        _falls++;
        Note($"DOWN: balance {_balance.State} {_balance.StateTime:0.00}s, imbalance {_balance.Imbalance}, gravity {_balance.GravityTorque}, slope {_balance.SlopeTorque}, wind {_balance.WindTorque}, inertial {_balance.InertialTorque}, correction {_balance.CorrectionTorque}; locomotion {_locomotion.State}, velocity {_locomotion.Velocity}, input {_locomotion.InputDirection}, surface {_locomotion.Surface}, slope {_locomotion.SlopeDegrees:0.0}°");
    }

    private void OnBalance(BalanceSnapshot snapshot)
    {
        if (Mathf.Abs(snapshot.LeanInput) > 0.01f && Mathf.Abs(_balance.LeanInput) <= 0.01f) { Note($"lean input {snapshot.LeanInput:0.00}"); }
        if (snapshot.Bracing && !_balance.Bracing) { Note("bracing"); }
        if (snapshot.State != _balance.State) { Note($"balance {_balance.State} -> {snapshot.State}, imbalance {snapshot.Imbalance}, wind torque {snapshot.WindTorque}, slope torque {snapshot.SlopeTorque}"); }
        _balance = snapshot;
        _peakImbalance = Mathf.Max(_peakImbalance, snapshot.ImbalanceMagnitude);
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _clock += 1.0f / Engine.PhysicsTicksPerSecond;
        if (snapshot.State != _locomotion.State) { Note($"locomotion {_locomotion.State} -> {snapshot.State}, velocity {snapshot.Velocity}, input {snapshot.InputDirection}"); }
        _locomotion = snapshot;
        _peakInput = Mathf.Max(_peakInput, snapshot.InputDirection.Length());
    }

    /// <summary>As EnvironmentShots does: the carrier's own input (walk, jump, lean) on or off.</summary>
    private static void SetInput(PlayerController player, bool enabled)
    {
        player.InputEnabled = enabled;
        foreach (Node node in player.FindChildren("*", "", true, false))
        {
            if (node is DownhillRoguelite.Balance.BalanceController balance) { balance.InputEnabled = enabled; }
        }
    }

    /// <summary>Two seconds of the left stick pushed forward and jump pressed twice a second; how far he moved and how often he fell.</summary>
    private async Task<(float Drift, int Falls)> PadInput(PlayerController player)
    {
        int fallsBefore = _falls;
        Vector3 standing = player.GlobalPosition;
        float drift = 0.0f;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float held = 0.0f; held < 2.0f; held += dt)
        {
            Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = -1.0f });
            Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = held % 0.5f < dt });
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Vector3 moved = player.GlobalPosition - standing;
            drift = Mathf.Max(drift, new Vector2(moved.X, moved.Z).Length());
        }
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0.0f });
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = false });
        await Seconds(0.5f);
        return (drift, _falls - fallsBefore);
    }

    private static string Nearest(Node? root, Vector3 from)
    {
        if (root == null) { return "none"; }
        string best = "none";
        float closest = float.MaxValue;
        foreach (Node node in root.FindChildren("*", "CharacterBody3D", true, false))
        {
            if (node is not Node3D body) { continue; }
            float distance = body.GlobalPosition.DistanceTo(from);
            if (distance < closest)
            {
                closest = distance;
                string mode = body is WildlifeAnimal animal ? $" {animal.State}" : "";
                best = string.Format(CultureInfo.InvariantCulture, "{0}{1} at {2:0.0} m", body.Name, mode, distance);
            }
        }
        return best;
    }

    private async Task Seconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    }
}
