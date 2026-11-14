namespace DownhillRoguelite.Core;

/// <summary>
/// A vault on offer (the Jump Over timing event): whether a low boulder or log is ahead, how far it
/// is, the window of distances where pressing Space vaults it (Near..Far) and the sweet spot, in m.
/// </summary>
/// <remarks>
/// A roll on a fall (RollSave) is <paramref name="Timed"/>: its distances are seconds to impact, and
/// <paramref name="Perfect"/> is how far either side of the sweet spot still counts as perfect.
/// </remarks>
public readonly record struct VaultCue(bool Active, float Distance, float Near, float Far, float Sweet, float Perfect = 0.0f, bool Timed = false)
{
    public static readonly VaultCue None = new(false, 0.0f, 0.0f, 0.0f, 0.0f);

    public bool InWindow => Active && Distance >= Near && Distance <= Far;
}
