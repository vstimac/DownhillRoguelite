namespace DownhillRoguelite.Core;

/// <summary>
/// What a found text said, for whoever records it (journal, lore book, narrator): its id, the
/// heading it was found under, its words, whether this run had read it before, and whether any
/// run ever had.
/// </summary>
public readonly record struct LoreNote(string Id, string Heading, string Text, bool FirstThisRun, bool FirstEver);
