namespace DownhillRoguelite.Progression;

/// <summary>Why a contract action did or didn't happen (ContractResolver, ContractDirector).</summary>
public enum ContractResult
{
    Ok,
    /// <summary>No such contract, or its definition is broken.</summary>
    Invalid,
    /// <summary>It is already taken (taking it twice does nothing).</summary>
    AlreadyAccepted,
    /// <summary>It has ended; nothing more can happen to it.</summary>
    AlreadyResolved,
    /// <summary>The carrier is already working on another contract (one at a time).</summary>
    AnotherActive,
    /// <summary>The cargo doesn't fit in the pack; the offer stays.</summary>
    NoRoom,
    /// <summary>It isn't taken, so it can't be delivered.</summary>
    NotAccepted,
    /// <summary>This isn't its destination station.</summary>
    WrongStation,
    /// <summary>The carrier isn't carrying its cargo (dropped, lost).</summary>
    CargoMissing,
    /// <summary>The world-clock deadline has passed, even if the periodic check has not run yet.</summary>
    Expired,
}
