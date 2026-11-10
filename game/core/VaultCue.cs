namespace DownhillRoguelite.Core;

/// <summary>
/// A vault on offer (the Jump Over timing event): whether a low boulder or log is ahead, how far it
/// is, the window of distances where pressing Space vaults it (Near..Far) and the sweet spot, in m.
/// </summary>
public readonly record struct VaultCue(bool Active, float Distance, float Near, float Far, float Sweet)
{
    public static readonly VaultCue None = new(false, 0.0f, 0.0f, 0.0f, 0.0f);

    public bool InWindow => Active && Distance >= Near && Distance <= Far;
}
