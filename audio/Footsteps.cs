using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// A footstep each time a foot plants (FootPlacementUpdated), picked by the surface underfoot
/// (LocomotionUpdated) from audio/footsteps/&lt;surface&gt;_&lt;n&gt;.wav: grass, rock, scree, snow,
/// mud, wood. Falling snow makes every step crunch like snow (except on wood). Louder with speed,
/// a little random in pitch. Sounds come from audio/tools/generate_foley.py.
/// </summary>
public partial class Footsteps : Node
{
    [Export(PropertyHint.Dir)] public string Folder { get; set; } = "res://audio/footsteps";
    /// <summary>Surfaces that use recorded footsteps (Kenney impact sounds) instead of the synthesized ones.</summary>
    [Export] public string[] RecordedSurfaces { get; set; } = { "grass", "snow", "wood", "rock" };
    /// <summary>File prefix for each recorded surface (variants _000 to _004).</summary>
    [Export] public string[] RecordedPrefixes { get; set; } = { "footstep_grass", "footstep_snow", "footstep_wood", "footstep_concrete" };
    [Export(PropertyHint.Dir)] public string RecordedFolder { get; set; } = "res://audio/sfx/kenney_impact_sounds";
    [Export] public int Variants { get; set; } = 4;
    [Export] public StringName Bus { get; set; } = "Ambience";
    [Export] public float VolumeDb { get; set; } = -10.0f;
    /// <summary>Speed range from quietest to loudest step, in m/s.</summary>
    [Export] public Vector2 SpeedRange { get; set; } = new(0.3f, 6.0f);
    /// <summary>Volume of a step at the slowest speed, as a fraction of the loudest.</summary>
    [Export] public float SlowVolume { get; set; } = 0.35f;
    [Export] public Vector2 PitchRange { get; set; } = new(0.9f, 1.1f);
    /// <summary>Falling snow above this makes steps crunch like snow.</summary>
    [Export] public float SnowCover { get; set; } = 0.5f;

    private static readonly string[] Surfaces = { "grass", "rock", "scree", "snow", "mud", "wood" };

    private readonly Dictionary<string, AudioStream[]> _sounds = new();
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[3];
    private readonly RandomNumberGenerator _random = new();
    private int _next;
    private bool _leftPlanted;
    private bool _rightPlanted;
    private string _surface = "grass";
    private float _speed;
    private bool _enabled;

    public override void _Ready()
    {
        _random.Randomize();
        // Headless runs (tests, smoke) have no real audio output and never release playing streams.
        _enabled = DisplayServer.GetName() != "headless";
        foreach (string surface in Surfaces)
        {
            List<AudioStream> variants = new();
            int recorded = System.Array.IndexOf(RecordedSurfaces, surface);
            if (recorded >= 0 && recorded < RecordedPrefixes.Length)
            {
                for (int i = 0; i < 5; i++)
                {
                    string path = $"{RecordedFolder}/{RecordedPrefixes[recorded]}_{i:000}.ogg";
                    if (ResourceLoader.Exists(path) && ResourceLoader.Load<AudioStream>(path) is AudioStream stream)
                    {
                        variants.Add(stream);
                    }
                }
            }
            if (variants.Count == 0)
            {
                for (int i = 0; i < Variants; i++)
                {
                    AudioStream? stream = ResourceLoader.Load<AudioStream>($"{Folder}/{surface}_{i}.wav");
                    if (stream != null)
                    {
                        variants.Add(stream);
                    }
                }
            }
            _sounds[surface] = variants.ToArray();
        }
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = Bus };
            AddChild(_players[i]);
        }
        GameEvents.FootPlacementUpdated += OnFootPlacementUpdated;
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.FootPlacementUpdated -= OnFootPlacementUpdated;
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        foreach (AudioStreamPlayer player in _players)
        {
            player?.Stop();
            if (player != null)
            {
                player.Stream = null;
            }
        }
        _sounds.Clear();
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _surface = snapshot.Surface.ToLowerInvariant();
        _speed = new Vector2(snapshot.Velocity.X, snapshot.Velocity.Z).Length();
    }

    private void OnFootPlacementUpdated(FootPlacementSnapshot snapshot)
    {
        if (snapshot.Left.Planted && !_leftPlanted)
        {
            Step();
        }
        if (snapshot.Right.Planted && !_rightPlanted)
        {
            Step();
        }
        _leftPlanted = snapshot.Left.Planted;
        _rightPlanted = snapshot.Right.Planted;
    }

    private void Step()
    {
        if (!_enabled || _speed < SpeedRange.X)
        {
            return;
        }
        string surface = _surface;
        if (GameEvents.CurrentWeather.Snow >= SnowCover && surface != "wood")
        {
            surface = "snow";
        }
        if (!_sounds.TryGetValue(surface, out AudioStream[]? variants) || variants.Length == 0)
        {
            if (!_sounds.TryGetValue("grass", out variants) || variants.Length == 0)
            {
                return;
            }
        }
        float loudness = Mathf.Lerp(SlowVolume, 1.0f, Mathf.Clamp((_speed - SpeedRange.X) / (SpeedRange.Y - SpeedRange.X), 0.0f, 1.0f));
        AudioStreamPlayer player = _players[_next];
        _next = (_next + 1) % _players.Length;
        player.Stream = variants[_random.RandiRange(0, variants.Length - 1)];
        player.VolumeDb = VolumeDb + Mathf.LinearToDb(loudness);
        player.PitchScale = _random.RandfRange(PitchRange.X, PitchRange.Y);
        player.Play();
    }
}
