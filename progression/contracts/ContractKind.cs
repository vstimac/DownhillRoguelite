namespace DownhillRoguelite.Progression;

/// <summary>
/// What a contract asks of the carrier (roguelite plan, section "Contract types"). The kind is how a
/// job is described and weighed; what really decides its outcome is its definition's numbers
/// (fragility, the condition it must arrive in, how far, how long).
/// </summary>
public enum ContractKind
{
    /// <summary>Bring the sealed cargo to a later station.</summary>
    Courier,
    /// <summary>A fragile thing that must arrive above a condition threshold.</summary>
    Preservation,
    /// <summary>Unusually heavy cargo: a burden on balance and stamina, paid well.</summary>
    Burden,
    /// <summary>A personal object carried across more than one station, with a deadline.</summary>
    Pilgrim,
}
