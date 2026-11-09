using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Fitting the sword and shield by hand (debug/SwordFitting.tscn). The carrier stands on a flat floor
/// holding a sword pose (frozen, scrub through it); the keys shift and turn the sword in the hand,
/// the sword in the sheath on the hip, or the shield on the arm, and Ctrl+S saves the corrections to
/// player/sword_fit.tres, which the game loads (MeleeCombat.Fit). See docs/sword-fitting.md.
/// </summary>
public partial class SwordFitting : Node3D
{
    private enum Part { Hand, Hip, Shield }

    [Export] public PlayerController? PlayerBody { get; set; }
    /// <summary>Poses on keys 1 to 7 (SwordAnimator clip names).</summary>
    [Export] public string[] Poses { get; set; } = { "idle", "block_idle", "attack_1", "attack_2", "attack_3", "kick", "draw" };
    /// <summary>Starting step for a shift (m) and a turn (degrees); [ and ] halve and double them.</summary>
    [Export] public float MoveStep { get; set; } = 0.01f;
    [Export] public float TurnStep { get; set; } = 5.0f;
    [Export] public float ScaleStep { get; set; } = 0.05f;
    /// <summary>Pose scrub step, in s (comma and period).</summary>
    [Export] public float ScrubStep { get; set; } = 0.05f;
    [Export] public float OrbitSpeed { get; set; } = 0.3f;
    [Export] public Vector2 ZoomRange { get; set; } = new(0.5f, 6.0f);
    /// <summary>Where Ctrl+S saves (the game loads SwordFit.DefaultPath).</summary>
    [Export] public string SavePath { get; set; } = SwordFit.DefaultPath;

    /// <summary>The fit being edited, and whether the scene is set up (tests).</summary>
    public SwordFit CurrentFit => _fit;
    public bool IsSetUp => _ready;
    public string Message => _message;

    private MeleeCombat? _combat;
    private SwordFit _fit = new();
    private Part _part = Part.Hand;
    private int _pose;
    private float _poseTime;
    private bool _playing;
    private Camera3D? _camera;
    private float _yaw = 30.0f;
    private float _pitch = -10.0f;
    private float _distance = 2.2f;
    private bool _orbiting;
    private Label? _info;
    private string _message = "";
    private int _wait = 6;
    private bool _ready;

    public override void _Ready()
    {
        if (ResourceLoader.Exists(SwordFit.DefaultPath))
        {
            SwordFit? saved = ResourceLoader.Load<SwordFit>(SwordFit.DefaultPath, null, ResourceLoader.CacheMode.Ignore);
            if (saved != null)
            {
                _fit = (SwordFit)saved.Duplicate();
            }
        }
        _camera = new Camera3D { Fov = 50.0f, Near = 0.02f };
        AddChild(_camera);
        CanvasLayer layer = new() { Layer = 50 };
        AddChild(layer);
        PanelContainer panel = new() { Position = new Vector2(12, 12) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        _info = new Label();
        _info.AddThemeFontSizeOverride("font_size", 15);
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
        if (_playing && _combat?.Animator?.Player != null)
        {
            _poseTime = (float)_combat.Animator.Player.CurrentAnimationPosition;
        }
        // Only the shield's own fitting shows the shield (it hides the sword hand otherwise).
        if (_combat?.Shield != null)
        {
            _combat.Shield.Visible = _part == Part.Shield;
        }
        UpdateCamera();
        UpdateInfo();
    }

    private void Setup()
    {
        _ready = true;
        _combat = PlayerBody!.GetNodeOrNull<MeleeCombat>("MeleeCombat");
        PlayerBody.InputEnabled = false;
        // The fitting camera, not the chase camera, and a free mouse.
        Node? rig = PlayerBody.GetNodeOrNull("CameraRig");
        if (rig != null)
        {
            rig.ProcessMode = ProcessModeEnum.Disabled;
        }
        _camera?.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (_combat != null)
        {
            _combat.Fit = _fit;
            _combat.SetDrawnNow(true);
            _combat.ApplyFit();
        }
        ShowPose();
    }

    // --- Keys and mouse ---

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ready || _combat == null)
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
        bool turn = key.ShiftPressed;
        switch (key.Keycode)
        {
            case Key.Tab:
                _part = (Part)(((int)_part + 1) % 3);
                _combat.SetDrawnNow(_part != Part.Hip);
                ShowPose();
                break;
            case Key.Left: Nudge(Vector3.Left, turn); break;
            case Key.Right: Nudge(Vector3.Right, turn); break;
            case Key.Up: Nudge(Vector3.Up, turn); break;
            case Key.Down: Nudge(Vector3.Down, turn); break;
            case Key.Pageup: Nudge(Vector3.Back, turn); break;
            case Key.Pagedown: Nudge(Vector3.Forward, turn); break;
            case Key.Equal:
            case Key.KpAdd:
                Resize(ScaleStep);
                break;
            case Key.Minus:
            case Key.KpSubtract:
                Resize(-ScaleStep);
                break;
            case Key.Bracketleft:
                MoveStep *= 0.5f;
                TurnStep *= 0.5f;
                break;
            case Key.Bracketright:
                MoveStep *= 2.0f;
                TurnStep *= 2.0f;
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
            Vector3 degrees = new(Mathf.Abs(axis.Y), Mathf.Abs(axis.X), Mathf.Abs(axis.Z));
            float sign = axis.X + axis.Y + axis.Z;
            // Left/Right turn about Y (the blade), Up/Down about X, Page Up/Down about Z.
            degrees *= sign * TurnStep;
            SetPartTurn(PartTurn() + degrees);
        }
        else
        {
            SetPartShift(PartShift() + axis * MoveStep);
        }
        _combat?.ApplyFit();
    }

    private void Resize(float step)
    {
        if (_part == Part.Shield)
        {
            _fit.ShieldScale = Mathf.Max(0.1f, _fit.ShieldScale + step);
        }
        else
        {
            _fit.SwordScale = Mathf.Max(0.1f, _fit.SwordScale + step);
        }
        _combat?.ApplyFit();
    }

    private void ResetPart()
    {
        SetPartShift(Vector3.Zero);
        SetPartTurn(Vector3.Zero);
        if (_part == Part.Shield)
        {
            _fit.ShieldScale = 1.0f;
        }
        else
        {
            _fit.SwordScale = 1.0f;
        }
        _combat?.ApplyFit();
    }

    private Vector3 PartShift()
    {
        if (_part == Part.Hip)
        {
            return _fit.HipPosition;
        }
        if (_part == Part.Shield)
        {
            return _fit.ShieldPosition;
        }
        return _fit.HandPosition;
    }

    private void SetPartShift(Vector3 value)
    {
        if (_part == Part.Hip)
        {
            _fit.HipPosition = value;
        }
        else if (_part == Part.Shield)
        {
            _fit.ShieldPosition = value;
        }
        else
        {
            _fit.HandPosition = value;
        }
    }

    private Vector3 PartTurn()
    {
        if (_part == Part.Hip)
        {
            return _fit.HipRotation;
        }
        if (_part == Part.Shield)
        {
            return _fit.ShieldRotation;
        }
        return _fit.HandRotation;
    }

    private void SetPartTurn(Vector3 value)
    {
        value = new Vector3(Mathf.Wrap(value.X, -180.0f, 180.0f), Mathf.Wrap(value.Y, -180.0f, 180.0f), Mathf.Wrap(value.Z, -180.0f, 180.0f));
        if (_part == Part.Hip)
        {
            _fit.HipRotation = value;
        }
        else if (_part == Part.Shield)
        {
            _fit.ShieldRotation = value;
        }
        else
        {
            _fit.HandRotation = value;
        }
    }

    // --- Pose ---

    /// <summary>Holds the chosen pose (frozen at the scrub time, or playing); on the hip, the plain walk pose.</summary>
    private void ShowPose()
    {
        AnimationPlayer? player = _combat?.Animator?.Player;
        if (player == null)
        {
            return;
        }
        if (_part == Part.Hip)
        {
            return;
        }
        _combat!.Animator!.Play(Poses[_pose], 0.0f, 1.0f);
        player.Seek(_poseTime, true);
        if (!_playing)
        {
            player.Pause();
        }
    }

    private void Scrub(float step)
    {
        AnimationPlayer? player = _combat?.Animator?.Player;
        if (player == null || _part == Part.Hip)
        {
            return;
        }
        float length = _combat!.Animator!.Length(Poses[_pose]);
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
        return $"Hand   pos {_fit.HandPosition:F3}  rot {_fit.HandRotation:F1}\n"
            + $"Hip    pos {_fit.HipPosition:F3}  rot {_fit.HipRotation:F1}\n"
            + $"Shield pos {_fit.ShieldPosition:F3}  rot {_fit.ShieldRotation:F1}\n"
            + $"Sword scale {_fit.SwordScale:F2}   Shield scale {_fit.ShieldScale:F2}";
    }

    // --- View ---

    private void UpdateCamera()
    {
        if (_camera == null || PlayerBody == null)
        {
            return;
        }
        Vector3 focus = PlayerBody.GlobalPosition + Vector3.Up * 1.0f;
        if (_combat?.Sword != null && _combat.Sword.IsInsideTree())
        {
            focus = _combat.Sword.GlobalPosition;
        }
        if (_part == Part.Shield && _combat?.Shield != null && _combat.Shield.IsInsideTree())
        {
            focus = _combat.Shield.GlobalPosition;
        }
        Basis turn = Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0.0f));
        // Orbit round the part, starting in front of the carrier (his facing is -Z).
        Vector3 from = focus + PlayerBody.GlobalBasis.Orthonormalized() * (turn * Vector3.Forward) * _distance;
        _camera.LookAtFromPosition(from, focus, Vector3.Up);
    }

    private void UpdateInfo()
    {
        if (_info == null)
        {
            return;
        }
        string part = "SWORD IN HAND";
        if (_part == Part.Hip)
        {
            part = "SWORD ON HIP";
        }
        else if (_part == Part.Shield)
        {
            part = "SHIELD";
        }
        string pose = "(walk pose)";
        if (_part != Part.Hip)
        {
            string state = "frozen";
            if (_playing)
            {
                state = "playing";
            }
            pose = $"{_pose + 1} {Poses[_pose]} @ {_poseTime:0.00} s ({state})";
        }
        _info.Text = $"Fitting: {part}   (Tab: next part)\nPose: {pose}\n\n{Describe()}\n\n"
            + $"Arrows / Page Up, Page Down: shift {MoveStep:0.###} m (X, Y = along the blade, Z)\n"
            + $"Shift + same keys: turn {TurnStep:0.##}°   [ ]: smaller / bigger steps   + -: size\n"
            + "1-7: pose   , . : scrub   Space: play / freeze   R: reset this part\n"
            + "Right-drag: orbit   Wheel: zoom   Ctrl+S: save\n"
            + _message;
    }
}
