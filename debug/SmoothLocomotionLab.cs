using System;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Ui;
using Godot;
namespace DownhillRoguelite.Debug;

/// <summary>Short repeatable previews using the real player, imported clips, IK and retargeted outfit.</summary>
public partial class SmoothLocomotionLab : Node3D
{
    [Export] public Vector3 PreviewStart { get; set; } = new(-8, 0.1f, 14);
    [Export] public float WalkSpeed { get; set; } = 1.2f;
    [Export] public float JogSpeed { get; set; } = 3.5f;
    [Export] public float FastSpeed { get; set; } = 6.5f;
    [Export] public float PreviewTime { get; set; } = 3;
    private PlayerController _body = null!;
    private CharacterAnimator _animator = null!;
    private Label _status = null!;
    private float _left;
    private float _demoTime;
    private bool _demo;
    private bool _jumped;
    public override void _Ready()
    {
        ProcessPhysicsPriority = -30;
        _body = GetNode<PlayerController>("World/Player");
        _body.Settings = (LocomotionSettings)_body.Settings.Duplicate();
        _animator = _body.GetNode<CharacterAnimator>("Animator");
        CanvasLayer layer = new(); AddChild(layer);
        ParchmentPanel paper = new() { Title = "Smooth operator", Position = new Vector2(20, 20), Size = new Vector2(300, 480) };
        layer.AddChild(paper);
        VBoxContainer rows = new() { Position = new Vector2(18, 65), Size = new Vector2(264, 395) }; paper.AddChild(rows);
        Button(rows, "Walk preview", () => Preview(WalkSpeed));
        Button(rows, "Jog preview", () => Preview(JogSpeed));
        Button(rows, "Fast run preview", () => Preview(FastSpeed));
        Button(rows, "Replay full demo", StartDemo);
        Button(rows, "Manual controls", Stop);
        Button(rows, "The Carrier", () => Select("default"));
        Button(rows, "Vučić", () => Select("vucic"));
        _status = UiStyle.MakeLabel("", 16); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; rows.AddChild(_status);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ResetPosition();
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--locomotion-demo") >= 0) { StartDemo(); }
    }
    private static void Button(Node parent, string text, Action action)
    {
        InkButton button = new() { Text = text, Paper = true, FontSize = 16, CustomMinimumSize = new Vector2(260, 32) };
        button.Pressed += action; parent.AddChild(button);
    }
    private void Select(string id)
    {
        GetNode<RunState>("/root/RunState").BeginCharacterRun(id);
        ResetPosition();
    }
    private void ResetPosition()
    {
        _body.Teleport(PreviewStart, 0);
        _body.Stamina!.Refill();
        _body.Health!.Refill();
        ChaseCamera camera = _body.GetNode<ChaseCamera>("CameraRig");
        camera.AutoAlignDelay = 600;
        camera.Portrait(4.5f, 65);
    }
    private void Preview(float speed)
    {
        Stop(); ResetPosition(); _body.Settings.StrideSpeed = speed;
        _body.DirectionOverride = Vector3.Forward; _left = PreviewTime;
    }
    private void StartDemo() { Stop(); ResetPosition(); _demo = true; _demoTime = 0; _jumped = false; }
    private void Stop()
    {
        _demo = false; _left = 0; _body.DirectionOverride = null;
        Input.ActionRelease("player_jump"); Input.ActionRelease("player_brake");
        _body.Settings.StrideSpeed = JogSpeed;
    }
    public override void _ExitTree()
    { Input.ActionRelease("player_jump"); Input.ActionRelease("player_brake"); }
    public override void _PhysicsProcess(double delta)
    {
        if (_demo)
        {
            _demoTime += (float)delta;
            Input.ActionRelease("player_jump"); Input.ActionRelease("player_brake");
            _body.DirectionOverride = Vector3.Forward;
            if (_demoTime < 1.2f) { _body.Settings.StrideSpeed = WalkSpeed; }
            else if (_demoTime < 3.4f) { _body.Settings.StrideSpeed = JogSpeed; }
            else if (_demoTime < 4.3f) { _body.Settings.StrideSpeed = FastSpeed; _body.DirectionOverride = Vector3.Right; }
            else if (_demoTime < 6.2f)
            {
                _body.DirectionOverride = Vector3.Zero;
                if (_demoTime >= 5.1f && !_jumped) { Input.ActionPress("player_jump"); _jumped = true; }
            }
            else if (_demoTime < 7.5f) { Input.ActionPress("player_brake"); }
            else { StartDemo(); }
        }
        else if (_left > 0)
        {
            _left -= (float)delta;
            if (_left <= 0) { _body.DirectionOverride = null; }
        }
    }
    public override void _Process(double delta)
    {
        if (_status == null) { return; }
        _status.Text = $"{_body.State} · {_animator.SmoothedSpeed:0.00} m/s\nCadence {_animator.Cadence:0.00} cycles/s\n" +
            $"Takeoff {_animator.TakeoffWeight:0.00} · Landing {_animator.LandingWeight:0.00}\n" +
            "WASD move · Shift sprint\nC brake · Space jump\nEsc releases mouse · F9 capture";
    }
}
