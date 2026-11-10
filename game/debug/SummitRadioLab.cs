using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug scene for the summit's Reno 4 (environment/SummitRadio): the start of a mountain with the
/// car, the bench and the skeleton, a camera framing them and, after a moment, the radio on (he
/// nods along). Keys: 1–3 move the camera round, Space plays or pauses, N next, B previous,
/// K opens nothing here (no HUD). Run: godot --path . res://debug/SummitRadioLab.tscn [-- --view=2]
/// </summary>
public partial class SummitRadioLab : Node3D
{
    [Export] public SummitRadio? Summit { get; set; }
    [Export] public Camera3D? Camera { get; set; }
    /// <summary>Camera spots around the car (car frame: right, up, forward), in m.</summary>
    [Export] public Vector3[] Views { get; set; } = { new(6.5f, 2.6f, 4.5f), new(4.2f, 1.3f, 0.6f), new(2.2f, 1.4f, -3.2f) };
    /// <summary>Seconds before the radio starts.</summary>
    [Export] public float PlayAfter { get; set; } = 1.0f;

    private int _view;
    private bool _framed;
    private float _timer;
    private bool _played;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=") && int.TryParse(arg.Substring("--view=".Length), out int view))
            {
                _view = Mathf.Max(0, view - 1);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (Summit == null || Camera == null || !Summit.Placed)
        {
            return;
        }
        if (!_framed)
        {
            Frame();
            _framed = true;
        }
        _timer += (float)delta;
        if (!_played && _timer >= PlayAfter)
        {
            _played = true;
            GameEvents.RequestRadioSong(Summit.SongName);
        }
    }

    private void Frame()
    {
        if (Summit?.Car == null || Camera == null || Views.Length == 0)
        {
            return;
        }
        Transform3D car = Summit.Car.GlobalTransform;
        Vector3 view = Views[_view % Views.Length];
        Vector3 at = car.Origin + car.Basis.X.Normalized() * view.X + Vector3.Up * view.Y + car.Basis.Z.Normalized() * view.Z;
        Vector3 target = car.Origin + Vector3.Up * 0.8f;
        if (Summit.Skeleton != null)
        {
            target = (target + Summit.Skeleton.GlobalPosition + Vector3.Up * 0.6f) * 0.5f;
        }
        Camera.LookAtFromPosition(at, target, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key3)
        {
            _view = (int)(key.Keycode - Key.Key1);
            Frame();
        }
        else if (key.Keycode == Key.Space)
        {
            GameEvents.CommandRadio(RadioCommand.Toggle);
        }
        else if (key.Keycode == Key.N)
        {
            GameEvents.CommandRadio(RadioCommand.Next);
        }
        else if (key.Keycode == Key.B)
        {
            GameEvents.CommandRadio(RadioCommand.Previous);
        }
    }
}
