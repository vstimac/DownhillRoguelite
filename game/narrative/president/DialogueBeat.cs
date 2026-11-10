using System;
using System.Collections.Generic;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// One step of a conversation: the NPC says these lines (manifest ids, one after another, each
/// skippable), then Done runs (rewards, state), then the choices show. No choices: the conversation ends.
/// </summary>
public sealed class DialogueBeat
{
    public List<string> Lines { get; } = new();
    public List<DialogueOption> Options { get; } = new();
    public Action? Done { get; set; }

    public DialogueBeat(params string[] lines)
    {
        Lines.AddRange(lines);
    }

    public DialogueBeat Option(DialogueOption option)
    {
        Options.Add(option);
        return this;
    }

    public DialogueBeat Then(Action done)
    {
        Done = done;
        return this;
    }
}
