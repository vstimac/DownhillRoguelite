using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Fitting the torch by hand (debug/TorchFitting.tscn), like the sword's fitting. The carrier stands
/// on a flat floor in one of his own poses (idle, walk, jog, sprint: frozen, scrub through it, or
/// playing); the keys shift and turn the lit torch in the left hand, or the put-away torch at the
/// belt, and Ctrl+S saves the fit to player/torch_fit.tres, which the game loads (CarrierTorch.Fit).
/// `-- --fit-belt` starts on the belt (renders). U switches the hand fit between upright (the flame never tips) and turning with the hand.
/// </summary>
public partial class TorchFitting : Node3D
{
    private enum Part { Hand, Belt }

    [Export] public PlayerController? PlayerBody { get; set; }
    /// <summary>Poses on keys 1 to 6 (the carrier's own clips).</summary>
    [Export] public string[] Poses { get; set; } = { "Idle", "Walk", "Jog_Fwd", "Sprint", "Crouch_Idle", "Jump" };
    /// <summary>Starting step for a shift (m) and a turn (degrees); [ and ] halve and double them.</summary>
    [Export] public float MoveStep { get; set; } = 0.01f;
    [Export] public float TurnStep { get; set; } = 5.0f;
    [Export] public float ScaleStep { get; set; } = 0.05f;
    [Export] public float ScrubStep { get; set; } = 0.05f;
    [Export] public float OrbitSpeed { get; set; } = 0.3f;
    [Export] public Vector2 ZoomRange { get; set; } = new(0.5f, 6.0f);
    [Export] public string SavePath { get; set; } = TorchFit.DefaultPath;

    public TorchFit CurrentFit => _fit;
    public bool IsSetUp => _ready;
    public CarrierTorch? Torch => _torch;

    private CarrierTorch? _torch;
    private AnimationPlayer? _animations;
    private TorchFit _fit = TorchFit.Load();
    private Part _part = Part.Hand;
    private int _pose;
    private float _poseTime;
    private bool _playing;
    private Camera3D? _camera;
    private float _yaw = -35.0f;
    private float _pitch = -10.0f;
    private float _distance = 2.6f;
    private bool _orbiting;
    private Label? _info;
    private string _message = "";
    private int _wait = 6;
    private bool _ready;

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 50.0f, Near = 0.02f };
        AddChild(_camera);
        CanvasLayer layer = new() { Layer = 50 };
        AddChild(layer);
        PanelContainer panel = new() { Position = new Vector2(12, 12) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        _info = new Label();
        _info.AddThemeFontSizeOverride("font_size", 12);
        panel.AddChild(_info);
        layer.AddChild(panel);
    }

    public override void _Process(double delta)
    {
        if (PlayerBody == null)
        {
            return;
        }
        if (!_ready)
        {
            _wait--;
            if (_wait > 0)
            {
                return;
            }
            Setup();
        }
        if (_playing && _animations != null)
        {
            _poseTime = (float)_animations.CurrentAnimationPosition;
        }
        UpdateCamera();
        UpdateInfo();
    }

    private void Setup()
    {
        _ready = true;
        PlayerBody!.InputEnabled = false;
        Node? rig = PlayerBody.GetNodeOrNull("CameraRig");
        if (rig != null)
        {
            rig.ProcessMode = ProcessModeEnum.Disabled;
        }
        _camera?.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        // The animator's tree steps aside: the fitting plays the clips straight on the model.
        CharacterAnimator? animator = PlayerBody.GetNodeOrNull<CharacterAnimator>("Animator");
        if (animator != null)
        {
            if (animator.Tree != null)
            {
                animator.Tree.Active = false;
            }
            animator.ProcessMode = ProcessModeEnum.Disabled;
            _animations = animator.Animations;
        }
        _torch = PlayerBody.GetNodeOrNull<CarrierTorch>("Torch");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            // Renders: start on the belt.
            if (arg == "--fit-belt")
            {
                _part = Part.Belt;
            }
            if (arg.StartsWith("--fit-yaw=") && float.TryParse(arg.Substring(10), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float yaw))
            {
                _yaw = yaw;
            }
        }
        if (_torch != null)
        {
            _torch.Fit = _fit;
            _torch.ShowForFitting(_part == Part.Belt);
        }
        ShowPose();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ready || _torch == null)
        {
            return;
        }
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
                _orbiting = button.Pressed;
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Mathf.Clamp(_distance * 0.9f, ZoomRange.X, ZoomRange.Y);
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Mathf.Clamp(_distance * 1.1f, ZoomRange.X, ZoomRange.Y);
            }
            return;
        }
        if (@event is InputEventMouseMotion motion && _orbiting)
        {
            _yaw -= motion.Relative.X * OrbitSpeed;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * OrbitSpeed, -80.0f, 80.0f);
            return;
        }
        if (@event is not InputEventKey key || !key.Pressed)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        if (key.CtrlPressed && key.Keycode == Key.S)
        {
            Save();
            return;
        }
        if (key.CtrlPressed && _part == Part.Hand)
        {
            // The carry pose: Ctrl + Up/Down raises the arm, Left/Right bends the elbow, Page Up/Down tucks it in.
            switch (key.Keycode)
            {
                case Key.Up: _fit.ArmRaise += TurnStep; break;
                case Key.Down: _fit.ArmRaise -= TurnStep; break;
                case Key.Right: _fit.ElbowBend += TurnStep; break;
                case Key.Left: _fit.ElbowBend -= TurnStep; break;
                case Key.Pageup: _fit.ArmTuck += TurnStep; break;
                case Key.Pagedown: _fit.ArmTuck -= TurnStep; break;
            }
            return;
        }
        bool turn = key.ShiftPressed;
        switch (key.Keycode)
        {
            case Key.Tab:
                _part = (Part)(((int)_part + 1) % 2);
                _torch.ShowForFitting(_part == Part.Belt);
                break;
            case Key.Left: Nudge(Vector3.Left, turn); break;
            case Key.Right: Nudge(Vector3.Right, turn); break;
            case Key.Up: Nudge(Vector3.Up, turn); break;
            case Key.Down: Nudge(Vector3.Down, turn); break;
            case Key.Pageup: Nudge(Vector3.Back, turn); break;
            case Key.Pagedown: Nudge(Vector3.Forward, turn); break;
            case Key.Equal:
            case Key.KpAdd:
                _fit.Scale = Mathf.Max(0.1f, _fit.Scale + ScaleStep);
                break;
            case Key.Minus:
            case Key.KpSubtract:
                _fit.Scale = Mathf.Max(0.1f, _fit.Scale - ScaleStep);
                break;
            case Key.Bracketleft:
                MoveStep *= 0.5f;
                TurnStep *= 0.5f;
                break;
            case Key.Bracketright:
                MoveStep *= 2.0f;
                TurnStep *= 2.0f;
                break;
            case Key.U:
                _fit.Upright = !_fit.Upright;
                break;
            case Key.R:
                ResetPart();
                break;
            case Key.Space:
                _playing = !_playing;
                ShowPose();
                break;
            case Key.Comma:
                Scrub(-ScrubStep);
                break;
            case Key.Period:
                Scrub(ScrubStep);
                break;
            default:
                int number = (int)key.Keycode - (int)Key.Key1;
                if (number >= 0 && number < Poses.Length)
                {
                    _pose = number;
                    _poseTime = 0.0f;
                    ShowPose();
                }
                break;
        }
    }

    /// <summary>Shifts (or with Shift turns about) the chosen part along one of its own axes.</summary>
    private void Nudge(Vector3 axis, bool turn)
    {
        if (turn)
        {
            // Left/Right turn about Y (the haft), Up/Down about X, Page Up/Down about Z.
            Vector3 degrees = new Vector3(Mathf.Abs(axis.Y), Mathf.Abs(axis.X), Mathf.Abs(axis.Z)) * (axis.X + axis.Y + axis.Z) * TurnStep;
            Vector3 value = PartTurn() + degrees;
            SetPartTurn(new Vector3(Mathf.Wrap(value.X, -180.0f, 180.0f), Mathf.Wrap(value.Y, -180.0f, 180.0f), Mathf.Wrap(value.Z, -180.0f, 180.0f)));
        }
        else
        {
            SetPartShift(PartShift() + axis * MoveStep);
        }
    }

    private void ResetPart()
    {
        TorchFit plain = new();
        if (_part == Part.Belt)
        {
            _fit.StowPosition = plain.StowPosition;
            _fit.StowRotation = plain.StowRotation;
        }
        else
        {
            _fit.HandPosition = plain.HandPosition;
            _fit.HandRotation = plain.HandRotation;
            _fit.Upright = plain.Upright;
            _fit.ArmRaise = plain.ArmRaise;
            _fit.ArmTuck = plain.ArmTuck;
            _fit.ElbowBend = plain.ElbowBend;
        }
        _fit.Scale = plain.Scale;
    }

    private Vector3 PartShift()
    {
        if (_part == Part.Belt)
        {
            return _fit.StowPosition;
        }
        return _fit.HandPosition;
    }

    private void SetPartShift(Vector3 value)
    {
        if (_part == Part.Belt)
        {
            _fit.StowPosition = value;
        }
        else
        {
            _fit.HandPosition = value;
        }
    }

    private Vector3 PartTurn()
    {
        if (_part == Part.Belt)
        {
            return _fit.StowRotation;
        }
        return _fit.HandRotation;
    }

    private void SetPartTurn(Vector3 value)
    {
        if (_part == Part.Belt)
        {
            _fit.StowRotation = value;
        }
        else
        {
            _fit.HandRotation = value;
        }
    }

    // --- Pose ---

    private void ShowPose()
    {
        if (_animations == null)
        {
            return;
        }
        string clip = Poses[_pose];
        if (!_animations.HasAnimation(clip))
        {
            _message = $"No clip {clip} on this model";
            return;
        }
        _animations.Play(clip, 0.0f);
        _animations.Seek(_poseTime, true);
        if (!_playing)
        {
            _animations.Pause();
        }
    }

    private void Scrub(float step)
    {
        if (_animations == null || !_animations.HasAnimation(Poses[_pose]))
        {
            return;
        }
        float length = (float)_animations.GetAnimation(Poses[_pose]).Length;
        _poseTime = Mathf.Clamp(_poseTime + step, 0.0f, Mathf.Max(length - 0.01f, 0.0f));
        _playing = false;
        ShowPose();
    }

    // --- Saving ---

    private void Save()
    {
        Error result = ResourceSaver.Save(_fit, SavePath);
        if (result == Error.Ok)
        {
            _message = "Saved to " + SavePath;
        }
        else
        {
            _message = "Could not save: " + result;
        }
        GD.Print(_message);
        GD.Print(Describe());
    }

    private string Describe()
    {
        string upright = "turns with the hand";
        if (_fit.Upright)
        {
            upright = "upright";
        }
        return $"Hand  pos {_fit.HandPosition:F3}  rot {_fit.HandRotation:F1}  ({upright})\n"
            + $"Arm   raise {_fit.ArmRaise:F0}°  tuck {_fit.ArmTuck:F0}°  elbow {_fit.ElbowBend:F0}°\n"
            + $"Belt  pos {_fit.StowPosition:F3}  rot {_fit.StowRotation:F1}  on {_fit.StowBone}\n"
            + $"Scale {_fit.Scale:F2}";
    }

    // --- View ---

    private void UpdateCamera()
    {
        if (_camera == null || PlayerBody == null)
        {
            return;
        }
        Vector3 focus = PlayerBody.GlobalPosition + Vector3.Up * 1.0f;
        if (_torch != null && _torch.IsInsideTree())
        {
            focus = _torch.GlobalPosition + Vector3.Up * 0.15f;
        }
        Basis turn = Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0.0f));
        Vector3 from = focus + PlayerBody.GlobalBasis.Orthonormalized() * (turn * Vector3.Forward) * _distance;
        _camera.LookAtFromPosition(from, focus, Vector3.Up);
    }

    private void UpdateInfo()
    {
        if (_info == null)
        {
            return;
        }
        string part = "LIT TORCH IN THE LEFT HAND";
        if (_part == Part.Belt)
        {
            part = "PUT-AWAY TORCH AT THE BELT";
        }
        string state = "frozen";
        if (_playing)
        {
            state = "playing";
        }
        _info.Text = $"Fitting: {part}   (Tab: next part)\nPose: {_pose + 1} {Poses[_pose]} @ {_poseTime:0.00} s ({state})\n\n{Describe()}\n\n"
            + $"Arrows / Page Up, Page Down: shift {MoveStep:0.###} m (Y = along the haft)\n"
            + $"Shift + same keys: turn {TurnStep:0.##}°   [ ]: smaller / bigger steps   + -: size\n"
            + "Ctrl + Up/Down: raise the arm   Ctrl + Left/Right: bend the elbow   Ctrl + Page Up/Down: tuck it in\n"
            + "U: upright / turns with the hand   1-6: pose   , . : scrub   Space: play / freeze   R: reset this part\n"
            + "Right-drag: orbit   Wheel: zoom   Ctrl+S: save\n"
            + _message;
    }
}
