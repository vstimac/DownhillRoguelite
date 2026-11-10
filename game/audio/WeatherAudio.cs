using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// Weather you can hear: a wind loop that grows and rises in pitch with the wind speed (gusts
/// included) and a rain loop as loud as the rain, both a little muffled under shelter. Reads
/// GameEvents.CurrentWeather; plays on the "Ambience" bus. Loops come from
/// audio/tools/generate_weather_sounds.py.
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

    private AudioStreamPlayer? _wind;
    private AudioStreamPlayer? _rain;
    private float _windMix;
    private float _rainMix;

    public override void _Ready()
    {
        _wind = AddLoop(WindLoop);
        _rain = AddLoop(RainLoop);
    }

    public override void _ExitTree()
    {
        foreach (AudioStreamPlayer? player in new[] { _wind, _rain })
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
    }
}
