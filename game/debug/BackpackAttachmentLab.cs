using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>The existing equipment workshop with event-driven pack alignment controls.</summary>
public partial class BackpackAttachmentLab : ClothingLab
{
    private CarrierModel _look = null!;
    private Label _details = null!;
    private BackpackAttachment? _shown;
    private readonly List<SpinBox> _values = new();
    private CheckButton _automatic = null!;
    private bool _sync;
    private float _refresh;
    [Export] public bool DrawMount { get; set; } = true;
    /// <summary>`-- --no-pack`: hides the pack (and the mount marks), for reference shots of the bare carrier.</summary>
    [Export] public bool HidePack { get; set; }

    public override void _Ready()
    {
        base._Ready();
        _look = GetNode<CarrierModel>("World/Player/CarrierModel");
        // `-- --gauge-demo`: the balance dial over the carrier, fed a wobble that sweeps from side to side.
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--gauge-demo") >= 0)
        {
            CanvasLayer layer = new() { Name = "GaugeDemo" };
            AddChild(layer);
            layer.AddChild(new BalanceGauge { Name = "BalanceGauge" });
            GetNode<Node>("World/Player/Balance").ProcessMode = ProcessModeEnum.Disabled;
            _gaugeDemo = true;
        }
        _details = UiStyle.MakeLabel("", 14);
        _details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ControlList.AddChild(_details); ControlList.MoveChild(_details, 0);
        CarrierRoster? roster = CarrierRoster.Load();
        if (roster != null)
        {
            foreach (CarrierCharacter character in roster.Available())
            {
                Button choose = new() { Text = character.DisplayName };
                choose.Pressed += () => _look.ShowCharacter(character.Id);
                ControlList.AddChild(choose);
            }
        }
        _automatic = new CheckButton { Text = "Fit against measured back" };
        _automatic.Toggled += value => { if (!_sync && Pack() is BackpackAttachment pack) { pack.FitToBody = value; pack.Refit(); } };
        ControlList.AddChild(_automatic);
        foreach (string group in new[] { "Offset (m)", "Rotation (deg)", "Scale" })
        {
            ControlList.AddChild(UiStyle.MakeLabel(group, 16));
            for (int axis = 0; axis < 3; axis++)
            {
                int component = axis;
                string property = group;
                HBoxContainer row = new(); ControlList.AddChild(row);
                row.AddChild(UiStyle.MakeLabel(new[] { "X", "Y", "Z" }[axis], 15));
                SpinBox input = new() { MinValue = -1, MaxValue = 1, Step = 0.005, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                if (group == "Rotation (deg)") { input.MinValue = -180; input.MaxValue = 180; input.Step = 1; }
                if (group == "Scale") { input.MinValue = 0.1; input.MaxValue = 3; input.Step = 0.05; }
                input.ValueChanged += value => {
                    if (_sync || Pack() is not BackpackAttachment pack) { return; }
                    Vector3 vector = pack.LocalOffset;
                    if (property == "Rotation (deg)") { vector = pack.LocalRotationDegrees; }
                    else if (property == "Scale") { vector = pack.ModelScale; }
                    vector[component] = (float)value;
                    if (property == "Rotation (deg)") { pack.LocalRotationDegrees = vector; }
                    else if (property == "Scale") { pack.ModelScale = vector; }
                    else { pack.LocalOffset = vector; }
                    pack.Refit();
                };
                row.AddChild(input); _values.Add(input);
            }
        }
        AddStrapPhysicsControls();
        Button print = new() { Text = "Apply fit / print to Output" };
        print.Pressed += () => { if (Pack() is BackpackAttachment pack) { pack.Refit(); GD.Print($"Backpack {pack.BoneName}: offset {pack.LocalOffset}, rotation {pack.LocalRotationDegrees}, scale {pack.ModelScale}, auto {pack.FitToBody}, gap {pack.Clearance}"); } };
        ControlList.AddChild(print);
        ControlList.AddChild(UiStyle.MakeLabel("Scroll for alignment controls. WASD / Shift / C / Space / Ctrl.\nLive tuning is visual only; copy values to ClothingVisual to save.", 14));
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--no-pack") { HidePack = true; DrawMount = false; continue; }
            // `-- --pack-load=25`: the pack holds 25 kg (its swing and pull on the balance follow).
            if (arg.StartsWith("--pack-load=", StringComparison.Ordinal) && float.TryParse(arg.Substring(12), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float load))
            {
                CallDeferred(MethodName.SetLoad, load);
                continue;
            }
            if (!arg.StartsWith("--pack-view=", StringComparison.Ordinal)) { continue; }
            Vector3 eye = new(0, 1.3f, 3);
            if (arg.EndsWith("side", StringComparison.Ordinal)) { eye = new Vector3(3, 1.3f, 0); }
            else if (arg.EndsWith("front", StringComparison.Ordinal)) { eye = new Vector3(0, 1.3f, -3); }
            float aim = 1.15f;
            // The whole body, head to feet (reference shots).
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--whole-body") >= 0) { eye = new Vector3(eye.X * 1.7f, 1.0f, eye.Z * 1.7f); aim = 0.9f; }
            Node3D player = GetNode<Node3D>("World/Player");
            Camera3D photo = new() { Position = player.GlobalPosition + eye, Fov = 45, Current = true };
            AddChild(photo);
            photo.LookAt(player.GlobalPosition + Vector3.Up * aim);
            // `-- --pack-walk` / `--pack-sprint`: the carrier walks (runs) forward, the camera along with it (the pack's swing in motion).
            bool sprint = Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-sprint") >= 0;
            if (sprint || Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-walk") >= 0)
            {
                photo.Reparent(player, true);
                Input.ActionPress("player_move_forward");
                if (sprint) { Input.ActionPress("player_sprint"); }
            }
            GetNode<CanvasLayer>("ClothingControls").Visible = false;
        }
    }

    private BackpackAttachment? Pack() => _look.Clothing?.BackpackMount;

    private void SetLoad(float kg)
    {
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-fixed") >= 0 && Harness() is BackpackHarness fixedHarness) { fixedHarness.FollowLoad = false; }
        if (Harness() is BackpackHarness harness)
        {
            harness.LoadOverride = kg;
            return;
        }
        // The pack is fitted a moment after start: try again shortly.
        if (++_loadTries < 100) { GetTree().CreateTimer(0.05).Timeout += () => SetLoad(kg); }
    }
    private int _loadTries;
    private static int LogEvery() { if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-log-all") >= 0) { return 1; } return 10; }
    private BackpackHarness? Harness() => Pack()?.GetNodeOrNull<BackpackHarness>("BackpackHarness");

    private bool _showHull;
    private bool _showTies = true;

    /// <summary>
    /// Live tuning of the pack's strap physics: every change starts the pack over at its fit with the
    /// new values (copy the printed values into BackpackHarness's exports to keep them).
    /// </summary>
    private void AddStrapPhysicsControls()
    {
        ControlList.AddChild(UiStyle.MakeLabel("Strap physics", 16));
        CheckButton simulate = new() { Text = "Swing on straps", ButtonPressed = true };
        simulate.Toggled += value => { if (Harness() is BackpackHarness harness) { harness.Simulate = value; harness.RequestRestart(); } };
        ControlList.AddChild(simulate);
        CheckButton hull = new() { Text = "Show body hull (mesh collider)" };
        hull.Toggled += value => _showHull = value;
        ControlList.AddChild(hull);
        CheckButton ties = new() { Text = "Show strap ties (pack > body)", ButtonPressed = true };
        ties.Toggled += value => _showTies = value;
        ControlList.AddChild(ties);
        Tune("Load in the pack (kg; -1: the real one)", -1, 60, 1, h => h.LoadOverride, (h, v) => h.LoadOverride = v);
        Tune("Pack mass (kg, load off)", 1, 30, 0.5, h => h.PackMass, (h, v) => h.PackMass = v);
        Tune("Shoulder give (x1e-5)", 0, 200, 0.5, h => h.ShoulderGive * 100000f, (h, v) => h.ShoulderGive = v / 100000f);
        Tune("Lower give (x1e-5)", 0, 500, 1, h => h.LowerGive * 100000f, (h, v) => h.LowerGive = v / 100000f);
        Tune("Snug pull (m)", 0, 0.15, 0.005, h => h.Snug, (h, v) => h.Snug = v);
        Tune("Panel clearance (m)", 0, 0.05, 0.002, h => h.PackClearance, (h, v) => h.PackClearance = v);
        Tune("Damping", 0, 20, 0.25, h => h.PackDamping, (h, v) => h.PackDamping = v);
        Tune("Turn damping", 0, 40, 0.5, h => h.PackTurnDamping, (h, v) => h.PackTurnDamping = v);
        Tune("Steps per second", 60, 2000, 20, h => h.StepsPerSecond, (h, v) => h.StepsPerSecond = v);
        Tune("Max swing (m)", 0, 0.3, 0.005, h => h.MaxSwing, (h, v) => h.MaxSwing = v);
        Tune("Max turn (deg)", 0, 45, 0.5, h => h.MaxTurnDegrees, (h, v) => h.MaxTurnDegrees = v);
        Button restart = new() { Text = "Restart pack at its fit" };
        restart.Pressed += () => Harness()?.RequestRestart();
        ControlList.AddChild(restart);
        Button print = new() { Text = "Print strap physics to Output" };
        print.Pressed += () =>
        {
            if (Harness() is not BackpackHarness h) { return; }
            GD.Print($"BackpackHarness: PackMass {h.PackMass}, ShoulderGive {h.ShoulderGive}, LowerGive {h.LowerGive}, Snug {h.Snug}, PackClearance {h.PackClearance}, PackDamping {h.PackDamping}, PackTurnDamping {h.PackTurnDamping}, StepsPerSecond {h.StepsPerSecond}, MaxSwing {h.MaxSwing}, MaxTurnDegrees {h.MaxTurnDegrees}");
        };
        ControlList.AddChild(print);
    }

    private void Tune(string title, double min, double max, double step, Func<BackpackHarness, float> read, Action<BackpackHarness, float> write)
    {
        HBoxContainer row = new();
        ControlList.AddChild(row);
        row.AddChild(UiStyle.MakeLabel(title, 14));
        SpinBox input = new() { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        // Picks up the harness's current value once it exists.
        input.Ready += () => { if (Harness() is BackpackHarness h) { input.SetValueNoSignal(read(h)); } };
        input.FocusEntered += () => { if (Harness() is BackpackHarness h) { input.SetValueNoSignal(read(h)); } };
        input.ValueChanged += value =>
        {
            if (Harness() is not BackpackHarness h) { return; }
            write(h, (float)value);
            h.RequestRestart();
        };
        row.AddChild(input);
    }

    private int _logFrame;

    public override void _Process(double delta)
    {
        base._Process(delta);
        // `-- --pack-log`: the hanging pack's state every 10 frames (tuning the strap simulation).
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-log") >= 0 && Pack() is BackpackAttachment logged && ++_logFrame % LogEvery() == 0
            && logged.GetNodeOrNull<BackpackHarness>("BackpackHarness") is BackpackHarness harness && harness.Physics is PackPhysics physics)
        {
            GD.Print($"pack f{_logFrame} at={GetNode<Node3D>("World/Player").GlobalPosition} lift={harness.Lift:0.000} at#{physics.DeepestContact} bone={physics.DeepestBone}:{(logged.GetParent() as Skeleton3D)?.GetBoneName(Math.Max(physics.DeepestBone, 0))} packbone={(logged.GetParent() as Skeleton3D)?.GetBoneName(logged.BoneIdx)} swing={harness.SwingDistance:0.000} m / {Mathf.RadToDeg(harness.SwingAngle):0.0} deg panel inside={physics.DeepestInside:0.000} widest tie={physics.WidestTie:0.000} us={harness.StepMicroseconds} ties={physics.Ties} resets={harness.Resets} load={harness.LoadMass():0} kg share={harness.LoadShare:0.00} mass={physics.Mass:0.0} sway={Core.GameEvents.PackSway.Length():0.000}");
        }
        if (HidePack && Pack() is BackpackAttachment hidden) { hidden.Visible = false; }
        _refresh -= (float)delta;
        if (_refresh > 0 || _details == null) { return; }
        _refresh = 0.2f;
        BackpackAttachment? pack = Pack();
        if (pack == null) { _details.Text = "No compatible backpack attachment."; return; }
        _details.Text = $"Bone: {pack.BoneName}\nItem: {pack.EquippedItemId}\nMeasured body: {pack.MeasuredBody}\nBody back: {pack.BodyBack:0.000} m / gap {pack.Clearance:0.000} m\nApplied offset: {pack.EffectiveOffset}\nSkeleton > attachment > pivot > model\n{pack.Problem}";
        if (ReferenceEquals(_shown, pack)) { return; }
        _shown = pack; _sync = true;
        _automatic.SetPressedNoSignal(pack.FitToBody);
        Vector3[] values = { pack.LocalOffset, pack.LocalRotationDegrees, pack.ModelScale };
        for (int i = 0; i < 9; i++) { _values[i].SetValueNoSignal(values[i / 3][i % 3]); }
        _sync = false;
    }

    private bool _gaugeDemo;
    private float _demoTime;

    public override void _PhysicsProcess(double delta)
    {
        if (_gaugeDemo)
        {
            _demoTime += (float)delta;
            float roll = 1.35f * Mathf.Sin(_demoTime * 1.3f);
            BalanceState state = BalanceState.Wobbling;
            if (Mathf.Abs(roll) >= 1.0f) { state = BalanceState.Stumbling; }
            GameEvents.PublishBalance(new BalanceSnapshot(state, 0.0f, Vector2.Zero, new Vector2(roll, 0.0f), 0.0f, false, Vector3.Forward, Vector3.Right,
                Vector3.Zero, new Vector2(0.12f, 0.15f), Vector3.Zero, 1.0f, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, "demo", 30.0f));
        }
        // `-- --pack-hull`: the body surface the straps and the pack collide with (measured from the
        // skinned meshes' vertices, one outline per bone, coloured by bone). Add Godot's own
        // `--debug-collisions` to see the physics capsules too: the straps never touch those.
        if ((_showHull || Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-hull") >= 0) && Harness()?.Hull is BodyHull hull)
        {
            if (!DebugDraw3D.Enabled) { DebugDraw3D.SetEnabled(true); }
            Color[] colours = { Colors.Magenta, Colors.Cyan, Colors.Yellow, Colors.Orange, Colors.Lime, Colors.White };
            hull.Outline((from, to, part) => DebugDraw3D.Line(from, to, colours[part % colours.Length]));
        }
        // Each strap point on the pack (white cross) and where its strap holds it on the body (red), joined.
        if (_showTies && Harness()?.Physics is PackPhysics physics && Harness()!.Simulate)
        {
            if (!DebugDraw3D.Enabled) { DebugDraw3D.SetEnabled(true); }
            for (int i = 0; i < physics.Ties; i++)
            {
                DebugDraw3D.Cross(physics.TiePoint(i), Colors.White, 0.03f);
                DebugDraw3D.Cross(physics.TieHeld(i), Colors.Red, 0.03f);
                DebugDraw3D.Line(physics.TiePoint(i), physics.TieHeld(i), Colors.Orange);
            }
        }
        if (!DrawMount || Pack() is not BackpackAttachment pack) { return; }
        DebugDraw3D.Cross(pack.GlobalPosition, Colors.Yellow, 0.08f);
        DebugDraw3D.Line(pack.GlobalPosition, pack.Pivot.GlobalPosition, Colors.Cyan);
    }
}
