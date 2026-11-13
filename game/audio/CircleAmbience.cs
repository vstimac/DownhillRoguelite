using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// A quiet bed of sound under the music for each circle of the descent (docs/narrative.md): a high
/// meadow in Limbo, a moaning wind in Lust, cold rain and hail in Gluttony, great weights dragged in
/// Greed, the Styx's marsh in Wrath, burning tombs in Heresy, a dark wood in Violence, the evil pits
/// of Fraud and the frozen lake of Treachery. Reads the circle from RunState and crossfades to the
/// new bed when it changes; a little quieter under shelter. Plays on the "Ambience" bus. The beds are
/// cut from the Sonniss GDC bundle by audio/tools/import_sonniss.py.
/// </summary>
public partial class CircleAmbience : Node
{
    /// <summary>One loop per circle, Limbo first; past the last circle the last one keeps playing.</summary>
    [Export] public string[] Beds { get; set; } =
    {
        "res://audio/sonniss/ambience/circle_0_limbo.ogg",
        "res://audio/sonniss/ambience/circle_1_lust.ogg",
        "res://audio/sonniss/ambience/circle_2_gluttony.ogg",
        "res://audio/sonniss/ambience/circle_3_greed.ogg",
        "res://audio/sonniss/ambience/circle_4_wrath.ogg",
        "res://audio/sonniss/ambience/circle_5_heresy.ogg",
        "res://audio/sonniss/ambience/circle_6_violence.ogg",
        "res://audio/sonniss/ambience/circle_7_fraud.ogg",
        "res://audio/sonniss/ambience/circle_8_treachery.ogg",
    };
    [Export] public StringName Bus { get; set; } = "Ambience";
    [Export] public float VolumeDb { get; set; } = -6.0f;
    /// <summary>Volume multiplier under shelter.</summary>
    [Export] public float ShelterFactor { get; set; } = 0.6f;
    /// <summary>How long a crossfade to the next circle's bed takes, in s.</summary>
    [Export] public float CrossfadeSeconds { get; set; } = 6.0f;
    /// <summary>How often the circle is checked, in s.</summary>
    [Export] public float CheckSeconds { get; set; } = 1.0f;

    /// <summary>The circle whose bed is playing (-1: none yet).</summary>
    public int Circle { get; private set; } = -1;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    private readonly float[] _mix = new float[2];
    private int _current;
    private float _checkTimer;
    private RunState? _run;
    private bool _enabled;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        // Headless runs (tests, smoke) have no real audio output and never release playing streams.
        _enabled = DisplayServer.GetName() != "headless";
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer { Name = "Bed" + i, Bus = Bus, VolumeDb = -80.0f };
            AddChild(_players[i]);
        }
    }

    public override void _ExitTree()
    {
        foreach (AudioStreamPlayer player in _players)
        {
            if (player != null)
            {
                player.Stop();
                player.Stream = null;
            }
        }
    }

    /// <summary>The bed for a circle (the last one beyond the circles), or "" if there are none.</summary>
    public string BedFor(int circle)
    {
        if (Beds.Length == 0)
        {
            return "";
        }
        return Beds[Mathf.Clamp(circle, 0, Beds.Length - 1)];
    }

    /// <summary>Loads a bed and makes it loop (tests use it to check every bed loads).</summary>
    public static AudioStream? LoadBed(string path)
    {
        if (path.Length == 0 || !ResourceLoader.Exists(path))
        {
            return null;
        }
        AudioStream? stream = ResourceLoader.Load<AudioStream>(path);
        if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true;
        }
        else if (stream is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = Mathf.FloorToInt((float)wav.GetLength() * wav.MixRate);
        }
        return stream;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _checkTimer -= dt;
        if (_checkTimer <= 0.0f)
        {
            _checkTimer = CheckSeconds;
            int circle = 0;
            if (_run != null)
            {
                circle = Mathf.Min(_run.Circle, Beds.Length - 1);
            }
            if (circle != Circle)
            {
                SwitchTo(circle);
            }
        }
        float shelter = 1.0f;
        if (GameEvents.InShelter)
        {
            shelter = ShelterFactor;
        }
        float step = dt / Mathf.Max(CrossfadeSeconds, 0.1f);
        for (int i = 0; i < _players.Length; i++)
        {
            float target = 0.0f;
            if (i == _current)
            {
                target = 1.0f;
            }
            _mix[i] = Mathf.MoveToward(_mix[i], target, step);
            AudioStreamPlayer player = _players[i];
            player.VolumeDb = VolumeDb + Mathf.LinearToDb(Mathf.Max(_mix[i] * shelter, 0.0001f));
            if (_mix[i] <= 0.0f && player.Playing)
            {
                player.Stop();
            }
        }
    }

    private void SwitchTo(int circle)
    {
        Circle = circle;
        _current = 1 - _current;
        AudioStreamPlayer player = _players[_current];
        player.Stream = LoadBed(BedFor(circle));
        if (_enabled && player.Stream != null)
        {
            // Start somewhere in the loop, so a bed doesn't always open the same way.
            player.Play((float)GD.RandRange(0.0, player.Stream.GetLength() * 0.9));
        }
    }
}
