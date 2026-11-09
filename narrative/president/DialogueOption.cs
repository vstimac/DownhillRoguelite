using System;

namespace DownhillRoguelite.Narrative;

/// <summary>A choice the player can make: its text id (player_text.csv), the shown text, and what comes next (null ends the conversation).</summary>
public sealed class DialogueOption
{
    public string Id { get; }
    public string Text { get; }
    public Func<DialogueBeat?> Next { get; }

    public DialogueOption(string id, string text, Func<DialogueBeat?> next)
    {
        Id = id;
        Text = text;
        Next = next;
    }
}
