using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Another player's footsteps (co-op), heard from where they walk: a step each stride along the way
/// they move, louder and quicker the faster they go, crunching in falling snow. A child of their
/// RemoteCarrier; silent in headless runs and while they're fallen.
/// </summary>
public partial class RemoteFootsteps : Node3D
{
    [Export(PropertyHint.Dir)] public string Folder { get; set; } = "res://audio/sfx/kenney_impact_sounds";
    [Export] public string GrassPrefix { get; set; } = "footstep_grass";
    [Export] public string SnowPrefix { get; set; } = "footstep_snow";
    [Export] public int Variants { get; set; } = 5;
    [Export] public StringName Bus { get; set; } = "Ambience";
    [Export] public float VolumeDb { get; set; } = -4.0f;
    /// <summary>Heard up to this far, in m.</summary>
    [Export] public float Range { get; set; } = 30.0f;
    /// <summary>A stride's length at a walk and at a run, in m.</summary>
    [Export] public Vector2 Stride { get; set; } = new(0.7f, 1.4f);
    /// <summary>Speed range from walk to run, in m/s (slower than the first is standing still).</summary>
    [Export] public Vector2 SpeedRange { get; set; } = new(0.4f, 6.0f);
    /// <summary>A step at the slowest speed is this share of the loudest.</summary>
    [Export] public float SlowVolume { get; set; } = 0.4f;
    [Export] public float SnowCover { get; set; } = 0.5f;

    public RemoteCarrier? Carrier { get; set; }

    private AudioStream[] _grass = System.Array.Empty<AudioStream>();
    private AudioStream[] _snow = System.Array.Empty<AudioStream>();
    private AudioStreamPlayer3D? _player;
    private readonly RandomNumberGenerator _random = new();
    private float _travelled;
    private bool _enabled;

    public override void _Ready()
    {
        _enabled = DisplayServer.GetName() != "headless";
        if (!_enabled)
        {
            return;
        }
        _random.Randomize();
        _grass = Load(GrassPrefix);
        _snow = Load(SnowPrefix);
        _player = new AudioStreamPlayer3D { Name = "Step", Bus = Bus, MaxDistance = Range, UnitSize = 4.0f, VolumeDb = VolumeDb };
        AddChild(_player);
    }

    private AudioStream[] Load(string prefix)
    {
        System.Collections.Generic.List<AudioStream> sounds = new();
        for (int i = 0; i < Variants; i++)
        {
            string path = $"{Folder}/{prefix}_{i:000}.ogg";
            if (ResourceLoader.Exists(path) && ResourceLoader.Load<AudioStream>(path) is AudioStream stream)
            {
                sounds.Add(stream);
            }
        }
        return sounds.ToArray();
    }

    public override void _Process(double delta)
    {
        if (!_enabled || _player == null || Carrier == null || Carrier.Dead || Carrier.Bleeding)
        {
            return;
        }
        Vector3 velocity = Carrier.Velocity;
        float speed = new Vector2(velocity.X, velocity.Z).Length();
        // A jump to a far state (a teleport) is not walking.
        if (speed < SpeedRange.X || speed > 15.0f)
        {
            _travelled = 0.0f;
            return;
        }
        float pace = Mathf.Clamp((speed - SpeedRange.X) / Mathf.Max(SpeedRange.Y - SpeedRange.X, 0.01f), 0.0f, 1.0f);
        float stride = Mathf.Lerp(Stride.X, Stride.Y, pace);
        _travelled += speed * (float)delta;
        if (_travelled < stride)
        {
            return;
        }
        _travelled = 0.0f;
        AudioStream[] sounds = _grass;
        if (GameEvents.CurrentWeather.Snow > SnowCover && _snow.Length > 0)
        {
            sounds = _snow;
        }
        if (sounds.Length == 0)
        {
            return;
        }
        _player.Stream = sounds[_random.RandiRange(0, sounds.Length - 1)];
        _player.VolumeDb = VolumeDb + Mathf.LinearToDb(Mathf.Lerp(SlowVolume, 1.0f, pace));
        _player.PitchScale = _random.RandfRange(0.9f, 1.1f);
        _player.Play();
    }
}
