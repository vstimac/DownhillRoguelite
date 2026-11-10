namespace DownhillRoguelite.Core;

/// <summary>
/// One goal the carrier has right now (published by the objective director, read by the HUD and
/// the journal): what to do (Title), why it matters (Why), how far along it is (Progress), and
/// whether it is finished (Done) or failed. Giver is the traveller who asked for it, if anyone.
/// </summary>
public readonly record struct ObjectiveNote(
    string Id,
    ObjectiveKind Kind,
    string Title,
    string Why,
    string Progress,
    bool Done = false,
    bool Failed = false,
    string Giver = "");
