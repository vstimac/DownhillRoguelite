namespace DownhillRoguelite.Core;

/// <summary>
/// What the rest of the game may know about a relic event (journal, narrator, telemetry): which
/// relic, its name, the words that go with it (what was seen, or the interpretation when studied),
/// whether any run had seen or studied it before, and for a delivery where, in what condition and
/// for what score.
/// </summary>
public readonly record struct RelicNote(
    string Id,
    string Name,
    string Text,
    bool FirstEver,
    int Station = 0,
    float Condition = 100.0f,
    int Score = 0);
