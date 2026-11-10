using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// The summit car's radio: plays the player's own songs (LocalMusicLibrary: user://music or music/
/// beside the game; none ship with it) on the Music bus. Takes commands through GameEvents (play,
/// pause, next, previous, volume, seek, a song by name) and publishes its RadioState on every change
/// and a few times a second while playing. Keeps playing while paused and all the way down the
/// mountain; the next song follows when one ends. Headless runs keep the state but make no sound.
/// </summary>
public partial class RadioPlayer : Node
{
    [Export] public StringName Bus { get; set; } = "Music";
    /// <summary>Starting volume, 0–1.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Volume { get; set; } = 0.8f;
    /// <summary>Loudness at full volume, in dB (the bus's own slider is in the options).</summary>
    [Export] public float FullVolumeDb { get; set; } = 0.0f;
    /// <summary>How often the state is published while a song plays, in seconds.</summary>
    [Export] public float PublishInterval { get; set; } = 0.25f;
    /// <summary>"Previous" past this many seconds into a song starts it again instead, like a real player.</summary>
    [Export] public float RestartAfterSeconds { get; set; } = 3.0f;
    /// <summary>Make no sound (the state still runs); on by itself in headless runs.</summary>
    [Export] public bool Silent { get; set; }

    public IReadOnlyList<string> Tracks => _tracks;
    public int Index => _index;
    public bool Playing => _playing;
    public float Position => _position;
    /// <summary>Extra folders searched (tests); the defaults are LocalMusicLibrary.Folders().</summary>
    public List<string>? FoldersOverride { get; set; }

    private readonly List<string> _tracks = new();
    private AudioStreamPlayer? _player;
    private AudioStream? _stream;
    private int _index = -1;
    private int _loadedIndex = -1;
    private bool _playing;
    private bool _started;
    private float _position;
    private float _length;
    private float _publishTimer;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (DisplayServer.GetName() == "headless")
        {
            Silent = true;
        }
        _player = new AudioStreamPlayer { Name = "Speaker", Bus = Bus };
        AddChild(_player);
        _player.Finished += OnFinished;
        GameEvents.RadioCommanded += OnCommand;
        GameEvents.RadioVolumeRequested += SetVolume;
        GameEvents.RadioSeekRequested += Seek;
        GameEvents.RadioSongRequested += PlaySong;
        LocalMusicLibrary.EnsureFolder();
        Rescan();
        ApplyVolume();
        Publish();
    }

    public override void _ExitTree()
    {
        GameEvents.RadioCommanded -= OnCommand;
        GameEvents.RadioVolumeRequested -= SetVolume;
        GameEvents.RadioSeekRequested -= Seek;
        GameEvents.RadioSongRequested -= PlaySong;
        if (_player != null)
        {
            _player.Stop();
            _player.Stream = null;
        }
        _stream = null;
        if (GameEvents.Radio.Playing)
        {
            GameEvents.PublishRadio(GameEvents.Radio with { Playing = false });
        }
    }

    /// <summary>Looks for songs again, keeping the current one if it's still there.</summary>
    public void Rescan()
    {
        string current = "";
        if (_index >= 0 && _index < _tracks.Count)
        {
            current = _tracks[_index];
        }
        bool wasLoaded = _index >= 0 && _loadedIndex == _index;
        _tracks.Clear();
        _tracks.AddRange(LocalMusicLibrary.Scan(FoldersOverride ?? LocalMusicLibrary.Folders()));
        _index = _tracks.IndexOf(current);
        _loadedIndex = -1;
        if (wasLoaded)
        {
            _loadedIndex = _index;
        }
        if (_index < 0)
        {
            _loadedIndex = -1;
            if (_tracks.Count > 0)
            {
                _index = 0;
            }
            else
            {
                StopNow();
            }
        }
        Publish();
    }

    private void OnCommand(RadioCommand command)
    {
        switch (command)
        {
            case RadioCommand.Play:
                Play();
                break;
            case RadioCommand.Pause:
                Pause();
                break;
            case RadioCommand.Toggle:
                if (_playing)
                {
                    Pause();
                }
                else
                {
                    Play();
                }
                break;
            case RadioCommand.Next:
                Step(1);
                break;
            case RadioCommand.Previous:
                if (_position > RestartAfterSeconds)
                {
                    Seek(0.0f);
                }
                else
                {
                    Step(-1);
                }
                break;
            case RadioCommand.Stop:
                StopNow();
                Publish();
                break;
            case RadioCommand.Rescan:
                Rescan();
                break;
        }
    }

    public void Play()
    {
        if (_tracks.Count == 0)
        {
            Rescan();
        }
        if (_tracks.Count == 0)
        {
            Publish();
            return;
        }
        if (_index < 0)
        {
            _index = 0;
        }
        if (_loadedIndex != _index && !LoadCurrent())
        {
            return;
        }
        _started = true;
        _playing = true;
        if (!Silent && _player != null)
        {
            if (_player.Playing && _player.StreamPaused)
            {
                _player.StreamPaused = false;
            }
            else if (!_player.Playing)
            {
                _player.StreamPaused = false;
                _player.Play(_position);
            }
        }
        Publish();
    }

    public void Pause()
    {
        if (!_playing)
        {
            return;
        }
        _playing = false;
        if (_player != null && _player.Playing)
        {
            _player.StreamPaused = true;
        }
        Publish();
    }

    /// <summary>Moves to the next (1) or previous (-1) song, wrapping round, and plays it if music was on.</summary>
    public void Step(int direction)
    {
        if (_tracks.Count == 0)
        {
            Publish();
            return;
        }
        bool keepPlaying = _playing;
        _index = (_index + direction + _tracks.Count) % _tracks.Count;
        StopNow();
        if (keepPlaying)
        {
            Play();
        }
        else
        {
            LoadCurrent();
            Publish();
        }
    }

    /// <summary>Plays the first song whose name holds <paramref name="name"/> (the car's song), else the first.</summary>
    public void PlaySong(string name)
    {
        if (_tracks.Count == 0)
        {
            Rescan();
        }
        int found = -1;
        for (int i = 0; i < _tracks.Count; i++)
        {
            if (LocalMusicLibrary.TrackName(_tracks[i]).Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                found = i;
                break;
            }
        }
        if (found >= 0 && found != _index)
        {
            _index = found;
            StopNow();
        }
        Play();
    }

    public void SetVolume(float volume)
    {
        Volume = Mathf.Clamp(volume, 0.0f, 1.0f);
        ApplyVolume();
        Publish();
    }

    /// <summary>Jumps to a point in the song (0–1 of its length).</summary>
    public void Seek(float fraction)
    {
        if (_length <= 0.0f)
        {
            return;
        }
        _position = Mathf.Clamp(fraction, 0.0f, 1.0f) * _length;
        if (!Silent && _player != null && _player.Playing)
        {
            _player.Seek(_position);
        }
        Publish();
    }

    private void ApplyVolume()
    {
        if (_player == null)
        {
            return;
        }
        if (Volume <= 0.001f)
        {
            _player.VolumeDb = -80.0f;
        }
        else
        {
            // Squared, so the slider's middle sounds like the middle.
            _player.VolumeDb = FullVolumeDb + Mathf.LinearToDb(Volume * Volume);
        }
    }

    private bool LoadCurrent()
    {
        if (_index < 0 || _index >= _tracks.Count)
        {
            return false;
        }
        _stream = LocalMusicLibrary.Load(_tracks[_index]);
        _loadedIndex = _index;
        _position = 0.0f;
        _length = 0.0f;
        if (_stream == null)
        {
            GD.PushWarning($"Radio: could not read {_tracks[_index]}");
            return false;
        }
        _length = (float)_stream.GetLength();
        if (_player != null)
        {
            _player.Stream = _stream;
        }
        return true;
    }

    private void StopNow()
    {
        _playing = false;
        _position = 0.0f;
        _loadedIndex = -1;
        if (_player != null)
        {
            _player.Stop();
            _player.StreamPaused = false;
            _player.Stream = null;
        }
        _stream = null;
    }

    private void OnFinished()
    {
        if (_playing)
        {
            Step(1);
        }
    }

    public override void _Process(double delta)
    {
        if (!_playing)
        {
            return;
        }
        if (!Silent && _player != null && _player.Playing)
        {
            _position = (float)_player.GetPlaybackPosition();
        }
        else if (Silent)
        {
            _position += (float)delta;
            if (_length > 0.0f && _position >= _length)
            {
                Step(1);
                return;
            }
        }
        _publishTimer -= (float)delta;
        if (_publishTimer <= 0.0f)
        {
            Publish();
        }
    }

    private void Publish()
    {
        _publishTimer = PublishInterval;
        string name = "";
        if (_index >= 0 && _index < _tracks.Count)
        {
            name = LocalMusicLibrary.TrackName(_tracks[_index]);
        }
        GameEvents.PublishRadio(new RadioState(
            _tracks.Count > 0, _playing, _started, _index, _tracks.Count, name,
            _position, _length, Volume, LocalMusicLibrary.ShownFolder()));
    }
}
