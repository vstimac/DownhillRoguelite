using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Light the carrier carries: a torch lit from the pack (inventory) burns in the left hand
/// (player/CarrierTorch); night beasts (wildlife/BeastDirector) are slower to attack while it burns.
/// </summary>
public static partial class GameEvents
{
    /// <summary>A torch was lit from the pack: it burns this many seconds (another one adds its time).</summary>
    public static event Action<float>? TorchRequested;
    /// <summary>The carrier's torch caught or went out.</summary>
    public static event Action<bool>? TorchChanged;

    /// <summary>Whether the carrier holds a burning torch now.</summary>
    public static bool CarrierTorchLit { get; private set; }

    public static void RequestTorch(float seconds) => TorchRequested?.Invoke(seconds);

    public static void PublishTorch(bool lit)
    {
        CarrierTorchLit = lit;
        TorchChanged?.Invoke(lit);
    }
}
