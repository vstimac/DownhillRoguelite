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
        Button print = new() { Text = "Apply fit / print to Output" };
        print.Pressed += () => { if (Pack() is BackpackAttachment pack) { pack.Refit(); GD.Print($"Backpack {pack.BoneName}: offset {pack.LocalOffset}, rotation {pack.LocalRotationDegrees}, scale {pack.ModelScale}, auto {pack.FitToBody}, gap {pack.Clearance}"); } };
        ControlList.AddChild(print);
        ControlList.AddChild(UiStyle.MakeLabel("Scroll for alignment controls. WASD / Shift / C / Space / Ctrl.\nLive tuning is visual only; copy values to ClothingVisual to save.", 14));
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--no-pack") { HidePack = true; DrawMount = false; continue; }
            if (!arg.StartsWith("--pack-view=", StringComparison.Ordinal)) { continue; }
            Vector3 eye = new(0, 1.3f, 3);
            if (arg.EndsWith("side", StringComparison.Ordinal)) { eye = new Vector3(3, 1.3f, 0); }
            else if (arg.EndsWith("front", StringComparison.Ordinal)) { eye = new Vector3(0, 1.3f, -3); }
            float aim = 1.15f;
            // The whole body, head to feet (reference shots).
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--whole-body") >= 0) { eye = new Vector3(eye.X * 1.7f, 1.0f, eye.Z * 1.7f); aim = 0.9f; }
            Camera3D photo = new() { Position = GetNode<Node3D>("World/Player").GlobalPosition + eye, Fov = 45, Current = true };
            AddChild(photo);
            photo.LookAt(GetNode<Node3D>("World/Player").GlobalPosition + Vector3.Up * aim);
            GetNode<CanvasLayer>("ClothingControls").Visible = false;
        }
    }

    private BackpackAttachment? Pack() => _look.Clothing?.BackpackMount;

    public override void _Process(double delta)
    {
        base._Process(delta);
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

    public override void _PhysicsProcess(double delta)
    {
        if (!DrawMount || Pack() is not BackpackAttachment pack) { return; }
        DebugDraw3D.Cross(pack.GlobalPosition, Colors.Yellow, 0.08f);
        DebugDraw3D.Line(pack.GlobalPosition, pack.Pivot.GlobalPosition, Colors.Cyan);
    }
}
