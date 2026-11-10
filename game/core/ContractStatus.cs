namespace DownhillRoguelite.Core;

/// <summary>
/// Where a cargo contract stands (see docs/roguelite-implementation-status.md). Offered and
/// Accepted are open; Delivered, Failed and Abandoned are final, and a contract reaches one of them
/// only once.
/// </summary>
public enum ContractStatus
{
    /// <summary>Pinned on a station's board; nobody has taken it.</summary>
    Offered,
    /// <summary>Taken: its cargo is the carrier's to bring to the destination.</summary>
    Accepted,
    /// <summary>Handed over at the destination station. Final.</summary>
    Delivered,
    /// <summary>The cargo was lost, or the carrier died with it. Final.</summary>
    Failed,
    /// <summary>Given up, or never finished when the run ended. Final.</summary>
    Abandoned,
}
