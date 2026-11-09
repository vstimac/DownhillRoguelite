using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Fitting worn clothes by keys, run as a game (debug/ClothingFittingPlay.tscn), like the sword fitting
/// scene: it shows the editor fitting scene (debug/ClothingFitting.tscn) and moves its handles from the
/// keyboard instead of with the editor's gizmo. Tab picks a piece, the arrows shift it, Shift turns it,
/// Ctrl stretches it along one axis, C and O change the character and the outfit, and Ctrl+S saves every
/// fit to player/clothing_fit.tres, which the game applies. See docs/clothing-fitting.md.
/// </summary>
public partial class ClothingFittingPlay : Node3D
{
    [Export] public PackedScene? FittingScene { get; set; }
    /// <summary>Outfits on O, each a comma-separated list of item ids (one per slot).</summary>
    [Export] public string[] Outfits { get; set; } =
    {
        "trail_shirt,wool_trousers,leather_gloves,trail_backpack,wool_hat",
        "trail_shirt,oilskin_coat,wool_trousers,hobnail_boots,hard_hat",
        "trail_shirt,hi_vis_vest,wool_trousers,hobnail_boots,fur_cape",
    };
    /// <summary>Starting steps for a shift (m), a turn (degrees) and a size change; [ and ] halve and double them.</summary>
    [Export] public float MoveStep { get; set; } = 0.005f;
    [Export] public float TurnStep { get; set; } = 5.0f;
    [Export] public float ScaleStep { get; set; } = 0.02f;
    [Export] public float MinScale { get; set; } = 0.2f;
    [Export] public float OrbitSpeed { get; set; } = 0.3f;
    [Export] public Vector2 ZoomRange { get; set; } = new(0.3f, 6.0f);
    [Export] public float StartDistance { get; set; } = 1.4f;
    /// <summary>Height of the whole-body view's centre (m), and the marker on the chosen piece.</summary>
    [Export] public float BodyCentre { get; set; } = 0.95f;
    [Export] public float MarkerSize { get; set; } = 0.035f;
    [Export] public Color MarkerColour { get; set; } = new(1.0f, 0.75f, 0.1f, 0.85f);
    /// <summary>Frames to wait for the fitting scene to build before taking over.</summary>
    [Export] public int SetupFrames { get; set; } = 6;

    /// <summary>For tests: the fitting scene, its handles, the chosen one and the last message.</summary>
    public ClothingFitting? Fitting => _fitting;
    public IReadOnlyList<Marker3D> Handles => _handles;
    public Marker3D? Selected => SelectedHandle();
    public bool IsSetUp => _ready;
    public string Message => _message;

    private ClothingFitting? _fitting;
    private readonly List<Marker3D> _handles = new();
    private readonly List<string> _characters = new();
    private int _selected;
    private int _character;
    private int _outfit;
    private Camera3D? _camera;
    private MeshInstance3D? _marker;
    private float _yaw;
    private float _pitch = -10.0f;
    private float _distance;
    private bool _wholeBody;
    private bool _orbiting;
    private Label? _info;
    private string _message = "";
    private int _wait;
    private bool _ready;

    public override void _Ready()
    {
        _distance = StartDistance;
        _wait = SetupFrames;
        if (FittingScene != null)
        {
            _fitting = FittingScene.Instantiate<ClothingFitting>();
            AddChild(_fitting);
        }
        _camera = new Camera3D { Fov = 50.0f, Near = 0.02f };
        AddChild(_camera);
        _camera.MakeCurrent();
        StandardMaterial3D look = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            AlbedoColor = MarkerColour,
        };
        _marker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = MarkerSize, Height = MarkerSize * 2.0f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = look,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
        };
        AddChild(_marker);
        CanvasLayer layer = new() { Layer = 50 };
        AddChild(layer);
        PanelContainer panel = new() { Position = new Vector2(12, 12) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        _info = new Label();
        _info.AddThemeFontSizeOverride("font_size", 15);
        panel.AddChild(_info);
        layer.AddChild(panel);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        if (_fitting == null)
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
        UpdateCamera();
        UpdateInfo();
    }

    private void Setup()
    {
        _ready = true;
        CarrierRoster? roster = CarrierRoster.Load();
        if (roster != null)
        {
            foreach (CarrierCharacter character in roster.Available())
            {
                _characters.Add(character.Id);
            }
        }
        _character = _characters.IndexOf(_fitting!.CharacterId);
        if (_character < 0)
        {
            _character = 0;
        }
        _outfit = 0;
        string worn = string.Join(",", _fitting.Outfit);
        for (int i = 0; i < Outfits.Length; i++)
        {
            if (Outfits[i] == worn)
            {
                _outfit = i;
            }
        }
        Gather();
    }

    /// <summary>Finds the fitting scene's handles again (after it rebuilt).</summary>
    private void Gather()
    {
        _handles.Clear();
        Node? fits = _fitting?.GetNodeOrNull("Fits");
        if (fits != null)
        {
            foreach (Node node in fits.FindChildren("*", "Marker3D", true, false))
            {
                if (node is Marker3D handle && !handle.IsQueuedForDeletion())
                {
                    _handles.Add(handle);
                }
            }
        }
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(_handles.Count - 1, 0));
    }

    private void Rebuild()
    {
        if (_fitting == null)
        {
            return;
        }
        if (_characters.Count > 0)
        {
            _fitting.CharacterId = _characters[_character];
        }
        if (Outfits.Length > 0)
        {
            _fitting.Outfit = Outfits[_outfit].Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        }
        _fitting.Rebuild();
        _selected = 0;
        Gather();
        _message = "";
    }

    private Marker3D? SelectedHandle()
    {
        if (_selected < 0 || _selected >= _handles.Count)
        {
            return null;
        }
        Marker3D handle = _handles[_selected];
        if (!IsInstanceValid(handle))
        {
            return null;
        }
        return handle;
    }

    // --- Keys and mouse ---

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ready)
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
        switch (key.Keycode)
        {
            case Key.Tab:
                Pick(key.ShiftPressed);
                break;
            case Key.Left: Nudge(Vector3.Left, key); break;
            case Key.Right: Nudge(Vector3.Right, key); break;
            case Key.Up: Nudge(Vector3.Up, key); break;
            case Key.Down: Nudge(Vector3.Down, key); break;
            case Key.Pageup: Nudge(Vector3.Back, key); break;
            case Key.Pagedown: Nudge(Vector3.Forward, key); break;
            case Key.Equal:
            case Key.KpAdd:
                Resize(Vector3.One * ScaleStep);
                break;
            case Key.Minus:
            case Key.KpSubtract:
                Resize(Vector3.One * -ScaleStep);
                break;
            case Key.Bracketleft:
                MoveStep *= 0.5f;
                TurnStep *= 0.5f;
                ScaleStep *= 0.5f;
                break;
            case Key.Bracketright:
                MoveStep *= 2.0f;
                TurnStep *= 2.0f;
                ScaleStep *= 2.0f;
                break;
            case Key.R:
                ResetPiece();
                break;
            case Key.C:
                if (_characters.Count > 0)
                {
                    _character = (_character + 1) % _characters.Count;
                    Rebuild();
                }
                break;
            case Key.O:
                if (Outfits.Length > 0)
                {
                    _outfit = (_outfit + 1) % Outfits.Length;
                    Rebuild();
                }
                break;
            case Key.Space:
                _wholeBody = !_wholeBody;
                break;
        }
    }

    private void Pick(bool back)
    {
        if (_handles.Count == 0)
        {
            return;
        }
        int step = 1;
        if (back)
        {
            step = _handles.Count - 1;
        }
        _selected = (_selected + step) % _handles.Count;
    }

    /// <summary>Shifts the piece along one of the body's axes; with Shift turns it about that axis, with Ctrl stretches it.</summary>
    private void Nudge(Vector3 axis, InputEventKey key)
    {
        Marker3D? handle = SelectedHandle();
        if (handle == null)
        {
            return;
        }
        if (key.ShiftPressed)
        {
            // Left/Right turn about Y (up), Up/Down about X (across), Page Up/Down about Z (front to back).
            Vector3 degrees = new(Mathf.Abs(axis.Y), Mathf.Abs(axis.X), Mathf.Abs(axis.Z));
            float sign = axis.X + axis.Y + axis.Z;
            Vector3 turned = handle.RotationDegrees + degrees * sign * TurnStep;
            handle.RotationDegrees = new Vector3(Mathf.Wrap(turned.X, -180.0f, 180.0f), Mathf.Wrap(turned.Y, -180.0f, 180.0f), Mathf.Wrap(turned.Z, -180.0f, 180.0f));
        }
        else if (key.CtrlPressed)
        {
            Vector3 along = new(Mathf.Abs(axis.X), Mathf.Abs(axis.Y), Mathf.Abs(axis.Z));
            float sign = axis.X + axis.Y + axis.Z;
            Resize(along * sign * ScaleStep);
        }
        else
        {
            handle.Position += axis * MoveStep;
        }
    }

    private void Resize(Vector3 change)
    {
        Marker3D? handle = SelectedHandle();
        if (handle == null)
        {
            return;
        }
        Vector3 scale = handle.Scale + change;
        handle.Scale = new Vector3(Mathf.Max(scale.X, MinScale), Mathf.Max(scale.Y, MinScale), Mathf.Max(scale.Z, MinScale));
    }

    private void ResetPiece()
    {
        Marker3D? handle = SelectedHandle();
        if (handle != null)
        {
            handle.Transform = Transform3D.Identity;
        }
    }

    // --- Saving ---

    private void Save()
    {
        if (_fitting == null)
        {
            return;
        }
        _fitting.Save();
        if (ResourceLoader.Exists(ClothingFit.DefaultPath))
        {
            _message = $"Saved {ClothingFit.Load().Entries.Count} fits to {ClothingFit.DefaultPath}";
        }
        else
        {
            _message = "Could not save (see the output)";
        }
        GD.Print(_message);
    }

    // --- View ---

    private void UpdateCamera()
    {
        if (_camera == null || _fitting == null)
        {
            return;
        }
        Vector3 focus = _fitting.GlobalPosition + Vector3.Up * BodyCentre;
        Marker3D? handle = SelectedHandle();
        if (handle != null && handle.IsInsideTree() && !_wholeBody)
        {
            focus = handle.GlobalPosition;
        }
        if (_marker != null)
        {
            _marker.Visible = handle != null && handle.IsInsideTree();
            if (_marker.Visible)
            {
                _marker.GlobalPosition = handle!.GlobalPosition;
            }
        }
        float distance = _distance;
        if (_wholeBody)
        {
            distance = Mathf.Max(_distance, ZoomRange.Y * 0.5f);
        }
        Basis turn = Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0.0f));
        // Orbit round the piece, starting in front of the character (facing +Z, as in the editor scene).
        Vector3 from = focus + turn * Vector3.Back * distance;
        _camera.LookAtFromPosition(from, focus, Vector3.Up);
    }

    private void UpdateInfo()
    {
        if (_info == null || _fitting == null)
        {
            return;
        }
        string piece = "(no pieces: does this character wear the outfit?)";
        string values = "";
        Marker3D? handle = SelectedHandle();
        if (handle != null)
        {
            piece = $"{_selected + 1}/{_handles.Count}  {handle.GetMeta("clothing_fit_key", handle.Name.ToString()).AsString()}";
            values = $"Shift {handle.Position:F3} m   Turn {handle.RotationDegrees:F1}°   Size {handle.Scale:F2}";
        }
        _info.Text = $"Character: {_fitting.CharacterId}   (C: next)\n"
            + $"Outfit {_outfit + 1}/{Outfits.Length}: {string.Join(", ", _fitting.Outfit)}   (O: next)\n"
            + $"Piece: {piece}   (Tab / Shift+Tab)\n{values}\n\n"
            + $"Arrows / Page Up, Page Down: shift {MoveStep:0.####} m (X right, Y up, Z back)\n"
            + $"Shift + same keys: turn {TurnStep:0.##}°   Ctrl + same keys: stretch {ScaleStep:0.###}\n"
            + "+ -: size   [ ]: smaller / bigger steps   R: reset this piece\n"
            + "Right-drag: orbit   Wheel: zoom   Space: whole body / piece   Ctrl+S: save\n"
            + _message;
    }
}
