namespace DownhillRoguelite.Narrative;

/// <summary>
/// One spoken NPC line from a voice manifest: its permanent id (subtitle, voice file, choice, log and
/// lip sync all use it), priority (1 first phase, 2 second, 3 secret), the event it belongs to, the
/// emotion it's spoken with, the words (text_sr) and where its voice file is expected.
/// </summary>
public readonly record struct VoiceLine(string Id, int Priority, string Event, string Emotion, string Text, string AudioPath);
