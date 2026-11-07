namespace DownhillRoguelite.Progression;

/// <summary>
/// What a contract asks of the carrier. Only Courier exists so far (Phase 1); Preservation, Burden
/// and Pilgrim come with cargo condition and deadlines.
/// </summary>
public enum ContractKind
{
    /// <summary>Bring the sealed cargo to a later station.</summary>
    Courier,
}
