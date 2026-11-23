namespace DownhillRoguelite.Player;

/// <summary>
/// A key press the carrier could not act on at that moment (down, getting up, setting the pack down,
/// the feet a frame off the ground), kept a short while and acted on as soon as they can: a press is
/// never simply lost because it came a moment early.
/// </summary>
public struct KeptPress
{
    private float _left;

    /// <summary>A press is waiting.</summary>
    public readonly bool Waiting => _left > 0.0f;

    /// <summary>Keeps the press for <paramref name="seconds"/>.</summary>
    public void Keep(float seconds)
    {
        _left = seconds;
    }

    public void Clear()
    {
        _left = 0.0f;
    }

    /// <summary>
    /// Called every step: true (once, and the press is used up) when a press is waiting and the carrier
    /// can act now; otherwise the press keeps counting down.
    /// </summary>
    public bool Take(float dt, bool canAct)
    {
        if (_left <= 0.0f)
        {
            return false;
        }
        if (canAct)
        {
            _left = 0.0f;
            return true;
        }
        _left -= dt;
        return false;
    }
}
