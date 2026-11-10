using System;

namespace DownhillRoguelite.Core;

/// <summary>The summit car's music player: commands in (from the page, the car, keys), its state out.</summary>
public static partial class GameEvents
{
    /// <summary>Raised when something asks the music player to play, pause, skip and so on.</summary>
    public static event Action<RadioCommand>? RadioCommanded;

    /// <summary>Raised when the volume is set (0–1).</summary>
    public static event Action<float>? RadioVolumeRequested;

    /// <summary>Raised when the song should jump to a point (0–1 of its length).</summary>
    public static event Action<float>? RadioSeekRequested;

    /// <summary>Raised when the player's state changes, and a few times a second while a song plays.</summary>
    public static event Action<RadioState>? RadioChanged;

    /// <summary>Raised when the radio page should open (E at the car, or its key).</summary>
    public static event Action? RadioPageRequested;

    /// <summary>The music player's last published state.</summary>
    public static RadioState Radio { get; private set; }

    /// <summary>True while a song plays (the ambient music steps aside).</summary>
    public static bool IsRadioPlaying => Radio.Playing;

    /// <summary>Plays the song whose name holds this (once, the first time E is pressed at the car).</summary>
    public static event Action<string>? RadioSongRequested;

    public static void CommandRadio(RadioCommand command) => RadioCommanded?.Invoke(command);

    public static void RequestRadioVolume(float volume) => RadioVolumeRequested?.Invoke(volume);

    public static void RequestRadioSeek(float fraction) => RadioSeekRequested?.Invoke(fraction);

    public static void RequestRadioSong(string name) => RadioSongRequested?.Invoke(name);

    public static void RequestRadioPage() => RadioPageRequested?.Invoke();

    public static void PublishRadio(RadioState state)
    {
        Radio = state;
        RadioChanged?.Invoke(state);
    }
}
