using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Voiced NPC lines and dialogue choices (the restsite president, docs/restsite-president.md).
/// One id per line everywhere: subtitle, choice, voice file, debug log, lip sync.
/// </summary>
public static partial class GameEvents
{
    /// <summary>An NPC is about to say a line: npc id, line id, the subtitle and where its voice file is expected.</summary>
    public static event Action<string, string, string, string>? VoiceLineRequested;
    /// <summary>A line started: npc id, line id, whether a voice file plays (false: subtitles only).</summary>
    public static event Action<string, string, bool>? VoiceLineStarted;
    /// <summary>A line ended: npc id, line id, whether it was skipped.</summary>
    public static event Action<string, string, bool>? VoiceLineCompleted;
    /// <summary>The player asks to skip the line being spoken.</summary>
    public static event Action? VoiceSkipRequested;

    public static void PublishVoiceLineRequested(string npcId, string lineId, string subtitle, string expectedAudioPath)
        => VoiceLineRequested?.Invoke(npcId, lineId, subtitle, expectedAudioPath);

    public static void PublishVoiceLineStarted(string npcId, string lineId, bool voiced) => VoiceLineStarted?.Invoke(npcId, lineId, voiced);

    public static void PublishVoiceLineCompleted(string npcId, string lineId, bool skipped) => VoiceLineCompleted?.Invoke(npcId, lineId, skipped);

    public static void RequestVoiceSkip() => VoiceSkipRequested?.Invoke();

    /// <summary>Dialogue choices on screen: who is talked to (name) and the choices' texts; empty closes them.</summary>
    public static event Action<string, string[]>? DialogueChoicesShown;
    /// <summary>The player picked a choice (index into the last shown list).</summary>
    public static event Action<int>? DialogueChoiceRequested;
    /// <summary>A dialogue opened (true) or closed (false). Movement is never blocked by it.</summary>
    public static event Action<bool>? DialogueChanged;
    public static bool InDialogue { get; private set; }

    public static void PublishDialogueChoices(string speaker, string[] choices) => DialogueChoicesShown?.Invoke(speaker, choices);

    public static void RequestDialogueChoice(int index) => DialogueChoiceRequested?.Invoke(index);

    public static void PublishDialogue(bool open)
    {
        InDialogue = open;
        DialogueChanged?.Invoke(open);
    }
}
