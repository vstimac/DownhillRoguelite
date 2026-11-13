using DownhillRoguelite.Core;
using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug scene for the šalteruša (narrative/salterusa): her booth on a flat floor, a camera at the
/// counter and a stand-in carrier standing where the camera is. The overlay shows her clip, the counter's
/// state and the lines she has said. Keys: E opens the counter / skips a line, 1–5 pick a choice,
/// B strikes her, A walks away and back (a new greeting), R starts a new run after a death (return lines).
/// Run: godot --path . res://debug/SalterusaLab.tscn
/// </summary>
public partial class SalterusaLab : Node3D
{
    [Export] public Vector3 CameraAt { get; set; } = new(0.9f, 1.65f, 3.6f);
    [Export] public Vector3 LookAtPoint { get; set; } = new(0.0f, 1.35f, 0.0f);
    [Export] public float AwayDistance { get; set; } = 30.0f;

    private Salterusa? _clerk;
    private Label? _overlay;
    private Vector3 _carrier;

    public override void _Ready()
    {
        StaticBody3D floor = new() { CollisionLayer = 1 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 100) }, Position = Vector3.Down * 0.5f });
        floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(100, 1, 100) }, Position = Vector3.Down * 0.5f, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.3f) } });
        AddChild(floor);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50.0f, 30.0f, 0.0f), ShadowEnabled = true });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.55f, 0.6f, 0.65f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
            },
        });
        Camera3D camera = new() { Position = CameraAt, Current = true };
        AddChild(camera);
        camera.LookAt(LookAtPoint);
        _clerk = new Salterusa { PlaceAtStation = false };
        AddChild(_clerk);
        _carrier = new Vector3(CameraAt.X, 0.0f, CameraAt.Z);
        CanvasLayer layer = new();
        _overlay = new Label { Position = new Vector2(12, 12), Modulate = new Color(1, 1, 0.85f) };
        layer.AddChild(_overlay);
        AddChild(layer);
    }

    public override void _PhysicsProcess(double delta)
    {
        GameEvents.PublishLocomotion(default(LocomotionSnapshot) with { Position = _carrier, Forward = Vector3.Forward });
        if (_clerk == null || _overlay == null)
        {
            return;
        }
        string said = string.Join(", ", _clerk.Said);
        _overlay.Text = $"Šalteruša  clip {_clerk.Clip}  counter open {_clerk.Talking}  choices {_clerk.ChoicesShown}  speaking {_clerk.Voice?.CurrentLine}\n"
            + $"returning {_clerk.Returning}  said: {said}\nE counter/skip  1-5 choose  B strike  A away/back  R death + new run";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_clerk == null || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode == Key.E)
        {
            _clerk.Interact(_clerk);
        }
        else if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5)
        {
            GameEvents.RequestDialogueChoice((int)(key.Keycode - Key.Key1));
        }
        else if (key.Keycode == Key.B)
        {
            _clerk.TakeHit(5.0f, Vector3.Zero, "lab");
        }
        else if (key.Keycode == Key.A)
        {
            if (_carrier.Z > AwayDistance * 0.5f)
            {
                _carrier = new Vector3(CameraAt.X, 0.0f, CameraAt.Z);
            }
            else
            {
                _carrier = new Vector3(0.0f, 0.0f, AwayDistance);
            }
        }
        else if (key.Keycode == Key.R)
        {
            GameEvents.PublishCarrierDied("lab");
            GameEvents.PublishRespawnRequest();
        }
    }
}
