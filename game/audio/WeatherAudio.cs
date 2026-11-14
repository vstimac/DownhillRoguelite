using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// Weather you can hear: a wind loop that grows and rises in pitch with the wind speed (gusts
/// included) and a rain loop as loud as the rain, both a little muffled under shelter, and in heavy
/// rain a roll of thunder now and then. Reads GameEvents.CurrentWeather; plays on the "Ambience" bus.
/// The wind, rain and thunder are cut from the Sonniss GDC bundle (audio/tools/import_sonniss.py); the
/// older synthesized loops (audio/tools/generate_weather_sounds.py) can still be set as WindLoop/RainLoop.
/// </summary>
public partial class WeatherAudio : Node
{
    [Export] public AudioStream? WindLoop { get; set; }
    [Export] public AudioStream? RainLoop { get; set; }
    [Export] public StringName Bus { get; set; } = "Ambience";
    /// <summary>Wind speed where the wind starts to be heard and where it is at full volume, in m/s.</summary>
    [Export] public Vector2 WindRange { get; set; } = new(1.0f, 16.0f);
    [Export] public float WindVolumeDb { get; set; } = -6.0f;
    [Export] public float RainVolumeDb { get; set; } = -8.0f;
    /// <summary>Wind pitch at calm and at full wind.</summary>
    [Export] public Vector2 WindPitch { get; set; } = new(0.8f, 1.15f);
    /// <summary>Volume multiplier under shelter.</summary>
    [Export] public float ShelterFactor { get; set; } = 0.5f;
    /// <summary>How fast the volume follows the weather (per second).</summary>
    [Export] public float Response { get; set; } = 2.0f;
    /// <summary>Thunder rolls (picked at random), heard only in rain heavier than ThunderRain.</summary>
    [Export] public string[] ThunderSounds { get; set; } = { "res://audio/sonniss/weather/thunder_0.ogg", "res://audio/sonniss/weather/thunder_1.ogg" };
    [Export] public float ThunderRain { get; set; } = 0.6f;
    /// <summary>Seconds between thunder rolls, at random in this range.</summary>
    [Export] public Vector2 ThunderInterval { get; set; } = new(25.0f, 70.0f);
    [Export] public float ThunderVolumeDb { get; set; } = -6.0f;

    private AudioStreamPlayer? _wind;
    private AudioStreamPlayer? _rain;
    private AudioStreamPlayer? _thunder;
    private readonly RandomNumberGenerator _random = new();
    private float _thunderTimer;
    private float _windMix;
    private float _rainMix;

    public override void _Ready()
    {
        _wind = AddLoop(WindLoop);
        _rain = AddLoop(RainLoop);
        _random.Randomize();
        _thunderTimer = _random.RandfRange(ThunderInterval.X, ThunderInterval.Y);
        _thunder = new AudioStreamPlayer { Name = "Thunder", Bus = Bus };
        AddChild(_thunder);
    }

    public override void _ExitTree()
    {
        foreach (AudioStreamPlayer? player in new[] { _wind, _rain, _thunder })
        {
            if (player != null)
            {
                player.Stop();
                player.Stream = null;
            }
        }
        WindLoop = null;
        RainLoop = null;
    }

    private AudioStreamPlayer AddLoop(AudioStream? stream)
    {
        if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = Mathf.FloorToInt((float)wav.GetLength() * wav.MixRate);
        }
        else if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true;
        }
        AudioStreamPlayer player = new() { Stream = stream, Bus = Bus, VolumeDb = -80.0f };
        AddChild(player);
        // Headless runs (tests, smoke) have no real audio output and never release playing streams.
        if (stream != null && DisplayServer.GetName() != "headless")
        {
            player.Play();
        }
        return player;
    }

    public override void _Process(double delta)
    {
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        float shelter = 1.0f;
        if (GameEvents.InShelter)
        {
            shelter = ShelterFactor;
        }
        float windTarget = Mathf.Clamp((weather.WindSpeed - WindRange.X) / (WindRange.Y - WindRange.X), 0.0f, 1.0f);
        float rainTarget = Mathf.Clamp(weather.Rain, 0.0f, 1.0f);
        float step = Response * (float)delta;
        _windMix = Mathf.MoveToward(_windMix, windTarget * shelter, step);
        _rainMix = Mathf.MoveToward(_rainMix, rainTarget * shelter, step);

        if (_wind != null)
        {
            _wind.VolumeDb = WindVolumeDb + Mathf.LinearToDb(Mathf.Max(_windMix, 0.0001f));
            _wind.PitchScale = Mathf.Lerp(WindPitch.X, WindPitch.Y, _windMix);
        }
        if (_rain != null)
        {
            _rain.VolumeDb = RainVolumeDb + Mathf.LinearToDb(Mathf.Max(_rainMix, 0.0001f));
        }
        UpdateThunder(weather.Rain, shelter, (float)delta);
    }

    /// <summary>In heavy rain, a roll of thunder every so often (quieter under shelter).</summary>
    private void UpdateThunder(float rain, float shelter, float delta)
    {
        if (rain < ThunderRain || ThunderSounds.Length == 0)
        {
            return;
        }
        _thunderTimer -= delta;
        if (_thunderTimer > 0.0f)
        {
            return;
        }
        _thunderTimer = _random.RandfRange(ThunderInterval.X, ThunderInterval.Y);
        string path = ThunderSounds[_random.RandiRange(0, ThunderSounds.Length - 1)];
        if (_thunder == null || DisplayServer.GetName() == "headless" || !ResourceLoader.Exists(path))
        {
            return;
        }
        _thunder.Stream = ResourceLoader.Load<AudioStream>(path);
        _thunder.VolumeDb = ThunderVolumeDb + Mathf.LinearToDb(Mathf.Max(shelter, 0.0001f));
        _thunder.PitchScale = _random.RandfRange(0.85f, 1.05f);
        _thunder.Play();
    }
}
