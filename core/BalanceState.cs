namespace DownhillRoguelite.Core;

/// <summary>Balance states, from steady to on the ground.</summary>
public enum BalanceState
{
    /// <summary>Centre of mass comfortably over the feet.</summary>
    Stable,
    /// <summary>Centre of mass near the edge of the support: a warning.</summary>
    Wobbling,
    /// <summary>Centre of mass outside the support: recoverable with quick input for a short window.</summary>
    Stumbling,
    /// <summary>Down. Input is disabled until the carrier gets back up.</summary>
    Fallen,
}
