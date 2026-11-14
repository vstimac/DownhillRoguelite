namespace DownhillRoguelite.Narrative;

/// <summary>
/// One spoken NPC line from a voice manifest: its permanent id (subtitle, voice file, choice, log and
/// lip sync all use it), priority (1 first phase, 2 second, 3 secret), the event it belongs to, the
/// emotion it's spoken with, the words as recorded (text_sr: Croatian or Serbian), where its voice
/// file is expected, and the English translation (text_en, optional) that the subtitle shows when
/// Options → Dialogue text is English.
/// </summary>
public readonly record struct VoiceLine(string Id, int Priority, string Event, string Emotion, string Text, string AudioPath, string TextEn = "")
{
    /// <summary>The words for the subtitle in the chosen dialogue language (the voice never changes).</summary>
    public string Shown => DownhillRoguelite.Core.DialogueText.FromCroatian(Text, TextEn);
}
