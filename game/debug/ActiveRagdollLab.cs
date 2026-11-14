using System;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Real-controller sandbox for recovery, grounded IK and physical arm muscles.</summary>
public partial class ActiveRagdollLab : Node3D
{
    [Export] public Vector3 Spawn { get; set; } = new(-5, 0.1f, 5);
    [Export] public float KnockSpeed { get; set; } = 0.8f;
    [Export] public float FallSpeed { get; set; } = 2.0f;
    [Export] public float RampDegrees { get; set; } = 20;
    [Export] public float DemoPeriod { get; set; } = 9;
    private PlayerController _body = null!;
    private BalanceController _balance = null!;
    private ActiveBalancePose _pose = null!;
    private RagdollMuscles _muscles = null!;
    private Label _status = null!;
    private bool _demo;
    private float _demoTime;
    private int _demoStage;
    private bool _oldDebug;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -30;
        _body = GetNode<PlayerController>("World/Player");
        _balance = _body.GetNode<BalanceController>("Balance");
        _pose = _body.GetNode<ActiveBalancePose>("Model/Rig/Skeleton3D/ActiveBalancePose");
        _muscles = _body.GetNode<RagdollMuscles>("RagdollMuscles");
        _body.GetNode<PlayerRagdoll>("Ragdoll").SafeImpactSpeed = 20; // This lab's falls do not kill the preview.
        _oldDebug = DebugDraw3D.Enabled;
        DebugDraw3D.SetEnabled(true);
        AddChild(new FootPlacementDebugOverlay());
        AddRamp();
        CanvasLayer layer = new();
        AddChild(layer);
        ParchmentPanel paper = new() { Title = "Active recovery", Position = new Vector2(18, 18), Size = new Vector2(315, 640) };
        layer.AddChild(paper);
        VBoxContainer rows = new() { Position = new Vector2(16, 65), Size = new Vector2(283, 550) };
        paper.AddChild(rows);
        Button(rows, "Small knock left", () => Knock(-KnockSpeed));
        Button(rows, "Small knock right", () => Knock(KnockSpeed));
        Button(rows, "Physical fall", Fall);
        Button(rows, "Toggle recovery + muscles (A/B)", Toggle);
        Button(rows, "Toggle arm muscles (opt-in)", () => _muscles.Enabled = !_muscles.Enabled);
        Button(rows, "Reset on flat ground", ResetPlayer);
        Button(rows, "Try the slope", () => ResetAt(new Vector3(9, 1.7f, 0)));
        Button(rows, "Replay demonstration", StartDemo);
        Button(rows, "Manual controls", () => { _demo = false; _body.DirectionOverride = null; });
        _status = UiStyle.MakeLabel("", 15);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rows.AddChild(_status);
        ResetPlayer();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--active-balance-demo") >= 0) { StartDemo(); }
    }

    private static void Button(Node parent, string text, Action action)
    {
        InkButton button = new() { Text = text, Paper = true, FontSize = 15, CustomMinimumSize = new Vector2(280, 31) };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void AddRamp()
    {
        StaticBody3D ramp = new() { Name = "RecoveryRamp", Position = new Vector3(9, 1, 0), RotationDegrees = new Vector3(RampDegrees, 0, 0) };
        AddChild(ramp);
        Vector3 size = new(5, 0.4f, 8);
        ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        ramp.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.46f, 0.37f, 0.25f), Roughness = 1 },
        });
    }

    private void Toggle() { _pose.Enabled = !_pose.Enabled; _muscles.Enabled = _pose.Enabled; }
    private void Knock(float speed) { _balance.ApplyImpulse(new Vector2(speed, 0)); }
    private void Fall()
    {
        _body.GetNode<PlayerRagdoll>("Ragdoll").GoDown(-_body.GlobalBasis.Z * FallSpeed, _body.GlobalBasis.X * 0.5f);
    }
    private void ResetPlayer() { ResetAt(Spawn); }
    private void ResetAt(Vector3 position)
    {
        _demo = false;
        _body.DirectionOverride = null;
        _body.Teleport(position, 0);
        _body.Health?.Refill();
        _body.Stamina?.Refill();
        _balance.ResetBalance();
        ChaseCamera camera = _body.GetNode<ChaseCamera>("CameraRig");
        camera.AutoAlignDelay = 600;
        camera.Portrait(4.8f, 65);
    }
    private void StartDemo() { ResetPlayer(); _demo = true; _demoTime = 0; _demoStage = 0; _body.DirectionOverride = Vector3.Zero; }

    public override void _PhysicsProcess(double delta)
    {
        BalanceSnapshot balance = _balance.Snapshot;
        DebugDraw3D.Cross(balance.SupportCenter, Colors.Green, 0.1f);
        DebugDraw3D.Cross(balance.CenterOfMassGround, Colors.Red, 0.1f);
        Vector3 hips = _body.GlobalPosition + Vector3.Up;
        DebugDraw3D.Arrow(hips, hips + _pose.HipShift * 4, Colors.Cyan);
        if (!_demo) { return; }
        _demoTime += (float)delta;
        if (_demoStage == 0 && _demoTime >= 1) { Knock(KnockSpeed); _demoStage++; }
        if (_demoStage == 1 && _demoTime >= 2.5f) { Knock(-KnockSpeed); _demoStage++; }
        if (_demoStage == 2 && _demoTime >= 4) { Fall(); _demoStage++; }
        if (_demoTime >= DemoPeriod) { StartDemo(); }
    }

    public override void _Process(double delta)
    {
        string enabled = "off";
        if (_pose.Enabled) { enabled = "on"; }
        string muscleMode = "off";
        if (_muscles.Enabled) { muscleMode = "on"; }
        _status.Text = $"Recovery {enabled} | {_balance.State}\nEffort {_pose.Effort:0.00} | hips {_pose.HipShift.Length() * 100:0.0} cm\n"
            + $"Arm muscles {muscleMode} | weight {_muscles.Weight:0.00}\nPeak torque {_muscles.PeakTorque:0.00} Nm\n"
            + "WASD move | Shift run\nC brake | Space jump | Ctrl dive\nF3 debug | F10 labs\nGreen: support | red: COM\nCyan: hip correction (4x)\nWalking uses clips + procedural IK.";
    }

    public override void _ExitTree() { DebugDraw3D.SetEnabled(_oldDebug); }
}
